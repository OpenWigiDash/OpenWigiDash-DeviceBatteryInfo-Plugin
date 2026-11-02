using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace OpenWigiDash.DeviceBatteryInfo.GHub;

/// <summary>
/// Conexão com o G HUB (lghub_agent) pelo websocket local. Uma só por plugin, aberta enquanto houver widget na tela.
/// </summary>
/// <remarks>
/// O que evita o "desconectado para sempre" do plugin do Stream Deck:
/// <list type="bullet">
/// <item>a lista e as baterias são pedidas de novo a cada minuto, e não só quando chega um evento;</item>
/// <item>quando o relógio pula (o PC dormiu) ou a tela do WigiDash acorda, pede na hora;</item>
/// <item>uma leitura de um id desconhecido (o G HUB troca os ids depois do standby) pede a lista de novo;</item>
/// <item>se o G HUB fecha, o socket antigo é descartado e as tentativas ficam mais espaçadas (até 1 min).</item>
/// </list>
/// </remarks>
public sealed class GHubClient : IBatterySource, IAsyncDisposable
{
    public static GHubClient Shared { get; } = new(TimeProvider.System, log: DiagnosticLog.Default);

    internal static readonly Uri DefaultEndpoint = new("ws://127.0.0.1:9010");
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] DefaultBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private readonly Uri _endpoint;
    private readonly TimeSpan[] _backoff;
    private readonly TimeProvider _time;
    private readonly GHubState _state;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _lifetime = new();
    private ILogger _logger = NullLogger.Instance;
    private int _users;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private ClientWebSocket? _socket;
    private SourceState _sourceState = SourceState.Connecting;
    private int _refreshQueued;

    private readonly DiagnosticLog _log;

    internal GHubClient(TimeProvider time, Uri? endpoint = null, TimeSpan[]? backoff = null, DiagnosticLog? log = null)
    {
        _log = log ?? new DiagnosticLog(null);
        _endpoint = endpoint ?? DefaultEndpoint;
        _backoff = backoff ?? DefaultBackoff;
        _time = time;
        _state = new GHubState(time);
    }

    public string Id => "ghub";
    public string DisplayName => "Logitech G HUB";
    public string ShortName => "G HUB";
    public SourceState State => _sourceState;
    public IReadOnlyList<BatteryDevice> Devices => _state.Devices;
    public event Action? Changed;

    /// <summary>Por que a última tentativa de conexão falhou (tipo e mensagem), para a tela de opções; <c>null</c> se conectou.</summary>
    public string? LastError { get; private set; }

    public void UseLogger(ILoggerFactory factory) => _logger = factory.CreateLogger("OpenWigiDash.DeviceBatteryInfo.GHub");

    /// <summary>Um widget passou a usar a conexão. A primeira abre.</summary>
    public void Acquire()
    {
        lock (_lifetime)
        {
            if (_users++ > 0)
                return;
            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _log.Write("Primeiro widget: abrindo a conexão com o G HUB.");
            _loop = Task.Run(() => GuardedRunAsync(token));
        }
    }

    /// <summary>Um widget saiu. O último fecha a conexão.</summary>
    public async Task ReleaseAsync()
    {
        Task? loop;
        lock (_lifetime)
        {
            if (_users == 0 || --_users > 0)
                return;
            _log.Write("Último widget saiu: fechando a conexão com o G HUB.");
            _stop?.Cancel();
            loop = _loop;
            _loop = null;
        }
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifetime)
            _users = Math.Min(_users, 1);
        await ReleaseAsync().ConfigureAwait(false);
    }

    /// <summary>Pede lista e baterias de novo. Pedidos seguidos se juntam.</summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
                Volatile.Write(ref _refreshQueued, 0);
                await SendAsync("GET", GHubProtocol.DevicesList, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Sem conexão agora: o laço pede a lista quando conectar.
            }
        });
    }

    /// <summary>O laço nunca pode morrer calado: se algo escapar, registra e recomeça.</summary>
    private async Task GuardedRunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                LastError = Describe(ex);
                _log.Write("Laço do G HUB caiu; recomeçando em 5 s.", ex);
                _logger.LogError(ex, "Laço do G HUB caiu.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _time, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        _log.Write("Laço do G HUB encerrado.");
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("json");
            socket.Options.SetRequestHeader("Origin", "file://");
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(_endpoint, connectTimeout.Token).ConfigureAwait(false);
                }
                _socket = socket;
                failures = 0;
                LastError = null;
                _log.Write($"Conectado ao G HUB em {_endpoint} (subprotocolo {socket.SubProtocol}).");
                SetSourceState(SourceState.Connected);
                _logger.LogInformation("Conectado ao G HUB em {Endpoint}.", _endpoint);
                await SendAsync("SUBSCRIBE", GHubProtocol.DeviceStateChanged, stop).ConfigureAwait(false);
                await SendAsync("SUBSCRIBE", GHubProtocol.BatteryStateChanged, stop).ConfigureAwait(false);
                await SendAsync("GET", GHubProtocol.DevicesList, stop).ConfigureAwait(false);

                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var refresher = RefreshLoopAsync(session.Token);
                try
                {
                    await ReceiveLoopAsync(socket, stop).ConfigureAwait(false);
                }
                finally
                {
                    session.Cancel();
                    await refresher.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
            {
                // G HUB fechado ou reiniciando. Só registra na primeira falha seguida, para não encher o log.
                LastError = Describe(ex);
                _log.Write($"G HUB indisponível (tentativa {failures + 1}): {LastError}");
                if (failures == 0)
                    _logger.LogInformation("G HUB indisponível: {Message}", LastError);
            }
            catch (Exception ex)
            {
                LastError = Describe(ex);
                _log.Write("Erro na conexão com o G HUB.", ex);
                _logger.LogWarning(ex, "Erro na conexão com o G HUB.");
            }
            finally
            {
                _socket = null;
                await CloseQuietlyAsync(socket).ConfigureAwait(false);
            }

            if (stop.IsCancellationRequested)
                break;
            SetSourceState(SourceState.Unavailable);
            if (_state.MarkAllDisconnected())
                RaiseChanged();
            var delay = _backoff[Math.Min(failures++, _backoff.Length - 1)];
            try
            {
                await Task.Delay(delay, _time, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        SetSourceState(SourceState.Connecting);
    }

    /// <summary>Lista a cada minuto; se o relógio pulou bem mais que isso, o PC dormiu e a lista sai na hora.</summary>
    private async Task RefreshLoopAsync(CancellationToken token)
    {
        try
        {
            var last = _time.GetUtcNow();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), _time);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var now = _time.GetUtcNow();
                if (now - last < RefreshInterval)
                    continue;
                last = now;
                await SendAsync("GET", GHubProtocol.DevicesList, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken stop)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, stop).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            await HandleAsync(text, stop).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(string text, CancellationToken stop)
    {
        if (GHubProtocol.Parse(text) is not { } message)
        {
            _log.Write("Mensagem que não é JSON: " + Shorten(text));
            return;
        }
        if (message.Path != GHubProtocol.DevicesList)
            _log.Write($"<- {message.Verb} {message.Path} {message.ResultCode} {(message.Payload is { } p ? Shorten(p.GetRawText()) : "")}");
        var update = _state.Apply(message);
        if (message.Path == GHubProtocol.DevicesList)
            _log.Write($"<- lista: {string.Join(", ", _state.Devices.Select(d => $"{d.Name} [{d.Key}] {(d.Connected ? "ligado" : "desligado")}"))}");
        if (update.Changed)
            RaiseChanged();
        foreach (var rawId in update.BatteryRequests)
            await SendAsync("GET", GHubProtocol.BatteryState(rawId), stop).ConfigureAwait(false);
        if (update.NeedsList)
            Refresh();
    }

    private async Task SendAsync(string verb, string path, CancellationToken token)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            return;
        var bytes = Encoding.UTF8.GetBytes(GHubProtocol.Request(verb, path));
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, token).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";

    private static string Describe(Exception ex)
    {
        var text = $"{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException is { } inner)
            text += $" ({inner.GetType().Name}: {inner.Message})";
        return text;
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private void SetSourceState(SourceState state)
    {
        if (_sourceState == state)
            return;
        _sourceState = state;
        RaiseChanged();
    }

    /// <summary>Avisa os widgets fora do laço: um handler lento (tela, opções) não pode travar a conexão.</summary>
    private void RaiseChanged()
    {
        if (Changed is not { } handlers)
            return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                handlers.Invoke();
            }
            catch (Exception ex)
            {
                _log.Write("Erro ao avisar os widgets.", ex);
                _logger.LogWarning(ex, "Erro ao avisar os widgets.");
            }
        });
    }
}
