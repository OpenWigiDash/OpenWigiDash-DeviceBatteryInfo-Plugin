using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace OpenWigiDash.DeviceBatteryInfo.Bluetooth;

/// <summary>
/// Bateria dos aparelhos Bluetooth pareados, a mesma que o Windows mostra em Configurações &gt; Bluetooth.
/// </summary>
/// <remarks>
/// O Windows não avisa quando a bateria muda, então a lista é lida de novo a cada 30 s (é barato: só lê
/// propriedades do Gerenciador de Dispositivos, nada vai até o aparelho), e na hora quando a tela acorda ou a pessoa
/// toca no tile. O Windows guarda a última leitura de um aparelho desligado; ela aparece como "dormindo".
/// </remarks>
public sealed class BluetoothSource : IBatterySource, IAsyncDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public static BluetoothSource Shared { get; } = new(new CfgMgrBluetoothReader(), TimeProvider.System,
        new DiagnosticLog(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenWigiDash", "WidgetData", "DeviceBatteryInfo", "bluetooth.log")));

    private readonly IBluetoothNodeReader _reader;
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

    internal BluetoothSource(IBluetoothNodeReader reader, TimeProvider time, DiagnosticLog? log = null)
    {
        _reader = reader;
        _time = time;
        _log = log ?? new DiagnosticLog(null);
    }

    public string Id => "bluetooth";
    public string DisplayName => "Bluetooth do Windows";
    public string ShortName => "Bluetooth";
    public SourceState State => _state;
    public string? LastError { get; private set; }
    public IReadOnlyList<BatteryDevice> Devices => _devices;
    public event Action? Changed;

    public string OfflineText => "Sem Bluetooth";
    public string OfflineShortText => "Sem BT";
    public string UnavailableMessage => "Não deu para ler os aparelhos Bluetooth do Windows. O tile tenta de novo sozinho.";
    public string BindingHelp => "Fica gravado pelo endereço Bluetooth: não troca de aparelho quando ele desliga ou o PC reinicia.";
    public string? LogPath => _log.Path;

    public void UseLogger(ILoggerFactory factory) => _logger = factory.CreateLogger("OpenWigiDash.DeviceBatteryInfo.Bluetooth");

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
                var nodes = _reader.Read();
                var devices = BluetoothInventory.Build(nodes, _time.GetUtcNow());
                var snapshot = string.Join(";", devices.Select(d => $"{d.Key}={d.Name}|{d.Percentage}|{d.Connected}"));
                // Nada mudou: não troca a lista, para não redesenhar à toa.
                if (snapshot != _lastSnapshot)
                {
                    _devices = devices;
                    _lastSnapshot = snapshot;
                    changed = true;
                    _log.Write("Aparelhos: " + (devices.Count == 0 ? "nenhum com bateria" : string.Join(", ",
                        devices.Select(d => $"{d.Name} {d.Percentage}%{(d.Connected ? "" : " (desligado)")}"))));
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
                    _log.Write("Falha ao ler os aparelhos Bluetooth.", ex);
                    _logger.LogWarning(ex, "Falha ao ler os aparelhos Bluetooth.");
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
