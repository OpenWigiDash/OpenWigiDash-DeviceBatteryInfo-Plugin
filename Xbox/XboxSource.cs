using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace OpenWigiDash.DeviceBatteryInfo.Xbox;

/// <summary>
/// Bateria dos controles Xbox no adaptador sem fio ou no cabo, pelo Windows.Gaming.Input.
/// </summary>
/// <remarks>
/// Lê de novo a cada 30 s, e na hora quando a tela acorda ou a pessoa toca no tile. O controle fica gravado pelo id
/// fixo que o Windows dá a ele, nunca pela posição 1 a 4 do XInput (que muda conforme a ordem em que ligam).
/// Controle desligado continua na lista como "dormindo", com a última leitura.
/// </remarks>
public sealed class XboxSource : IBatterySource, IAsyncDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan FirstListDelay = TimeSpan.FromSeconds(2);

    public static XboxSource Shared { get; } = new(new WgiControllerReader(), TimeProvider.System,
        new DiagnosticLog(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenWigiDash", "WidgetData", "DeviceBatteryInfo", "xbox.log")));

    private readonly IXboxControllerReader _reader;
    private readonly XboxInventory _inventory = new();
    private readonly TimeProvider _time;
    private readonly DiagnosticLog _log;
    private readonly object _lifetime = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private ILogger _logger = NullLogger.Instance;
    private int _users;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private int _refreshQueued;
    private IReadOnlyList<BatteryDevice> _devices = [];
    private SourceState _state = SourceState.Connecting;
    private string? _lastSnapshot;

    internal XboxSource(IXboxControllerReader reader, TimeProvider time, DiagnosticLog? log = null)
    {
        _reader = reader;
        _time = time;
        _log = log ?? new DiagnosticLog(null);
    }

    public string Id => "xbox";
    public string DisplayName => "controles Xbox do Windows";
    public string ShortName => "Xbox";
    public SourceState State => _state;
    public string? LastError { get; private set; }
    public IReadOnlyList<BatteryDevice> Devices => _devices;
    public event Action? Changed;

    public string OfflineText => "Sem leitura";
    public string OfflineShortText => "Erro";
    public string UnavailableMessage => "Não deu para ler os controles Xbox do Windows. O tile tenta de novo sozinho.";
    public string BindingHelp => "Fica gravado pelo id fixo do controle: não troca de controle quando ele desliga ou outro liga antes.";
    public string? LogPath => _log.Path;

    public void UseLogger(ILoggerFactory factory) => _logger = factory.CreateLogger("OpenWigiDash.DeviceBatteryInfo.Xbox");

    public void Acquire()
    {
        lock (_lifetime)
        {
            if (_users++ > 0)
                return;
            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _loop = Task.Run(() => PollAsync(token));
        }
    }

    public async Task ReleaseAsync()
    {
        Task? loop;
        lock (_lifetime)
        {
            if (_users == 0 || --_users > 0)
                return;
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

    /// <summary>Lê de novo agora. Pedidos seguidos se juntam.</summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), _time).ConfigureAwait(false);
            Volatile.Write(ref _refreshQueued, 0);
            await ScanAsync().ConfigureAwait(false);
        });
    }

    private async Task PollAsync(CancellationToken stop)
    {
        await ScanAsync().ConfigureAwait(false);
        // O Windows só enche a lista de controles um pouco depois do primeiro acesso.
        await Task.Delay(FirstListDelay, _time, stop).ConfigureAwait(false);
        await ScanAsync().ConfigureAwait(false);
        using var timer = new PeriodicTimer(PollInterval, _time);
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            await ScanAsync().ConfigureAwait(false);
    }

    /// <summary>Uma leitura completa. Nunca lança: erro vira <see cref="SourceState.Unavailable"/>.</summary>
    internal async Task ScanAsync()
    {
        await _scanLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var changed = false;
            try
            {
                var devices = _inventory.Update(_reader.Read(), _time.GetUtcNow());
                var snapshot = string.Join(";", devices.Select(d => $"{d.Key}={d.Name}|{d.Percentage}|{d.Charging}|{d.Connected}"));
                // Nada mudou: não troca a lista, para não redesenhar à toa.
                if (snapshot != _lastSnapshot)
                {
                    _devices = devices;
                    _lastSnapshot = snapshot;
                    changed = true;
                    _log.Write("Controles: " + (devices.Count == 0 ? "nenhum" : string.Join(", ",
                        devices.Select(d => $"{d.Name} {d.Percentage}%{(d.Charging ? " carregando" : "")}{(d.Connected ? "" : " (desligado)")}"))));
                }
                if (_state != SourceState.Connected)
                {
                    _state = SourceState.Connected;
                    LastError = null;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                if (_state != SourceState.Unavailable)
                {
                    _log.Write("Falha ao ler os controles Xbox.", ex);
                    _logger.LogWarning(ex, "Falha ao ler os controles Xbox.");
                    _state = SourceState.Unavailable;
                    changed = true;
                }
            }
            if (changed)
                RaiseChanged();
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            ThreadPool.QueueUserWorkItem(static h =>
            {
                try
                {
                    h();
                }
                catch (Exception)
                {
                    // Um widget com problema não pode parar os outros.
                }
            }, handler, preferLocal: false);
        }
    }
}
