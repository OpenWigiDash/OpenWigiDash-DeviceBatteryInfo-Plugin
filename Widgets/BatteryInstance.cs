using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Settings;

namespace OpenWigiDash.DeviceBatteryInfo.Widgets;

/// <summary>
/// Um tile de bateria na tela, ligado a um aparelho de uma fonte (G HUB, e depois Bluetooth e Xbox).
/// </summary>
/// <remarks>
/// O aparelho fica gravado pela chave estável (<see cref="DeviceBinding"/>), e só muda quando a pessoa escolhe outro
/// nas opções. Se ele não está na lista, o tile diz "não encontrado"; nunca mostra outro aparelho no lugar.
/// </remarks>
public sealed class BatteryInstance : IWidgetInstance
{
    internal const string DeviceKey = "device";
    internal const string TitleKey = "title";
    internal const string ShowTitleKey = "showTitle";
    internal const string BackgroundKey = "background";
    internal const string LowLevelKey = "lowLevel";
    internal const string BadgeKey = "badge";

    internal const int DefaultLowLevel = 20;

    private readonly IWidgetContext _context;
    private readonly IBatterySource _source;
    private readonly SourceBadge _badge;
    private int _released;

    private BatteryInstance(IWidgetContext context, IBatterySource source, SourceBadge badge)
    {
        _context = context;
        _source = source;
        _badge = badge;
    }

    internal static async ValueTask<IWidgetInstance> CreateAsync(IWidgetContext context, IBatterySource source, SourceBadge badge,
        CancellationToken cancellationToken)
    {
        var instance = new BatteryInstance(context, source, badge);
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        source.Changed += instance.OnSourceChanged;
        source.Acquire();
        return instance;
    }

    public IBatterySource Source => _source;

    /// <summary>Aparelho escolhido, ou <c>null</c>.</summary>
    public DeviceBinding? Binding { get; private set; }

    /// <summary>Texto de cima; vazio = o nome do aparelho.</summary>
    public string Title { get; private set; } = "";

    /// <summary>Mostra o texto de cima (título ou nome do aparelho).</summary>
    public bool ShowTitle { get; private set; } = true;

    /// <summary>Cor de fundo ("#RRGGBB"), ou <c>null</c> para o padrão.</summary>
    public string? Background { get; private set; }

    /// <summary>Abaixo disso (inclusive) a pilha fica vermelha.</summary>
    public int LowLevel { get; private set; } = DefaultLowLevel;

    /// <summary>Mostra o selo da fonte (o "G" do G HUB) no canto.</summary>
    public bool ShowBadge { get; private set; } = true;

    /// <summary>O aparelho escolhido, como a fonte o vê agora; <c>null</c> se não está na lista.</summary>
    public BatteryDevice? Current => DeviceResolver.Find(Binding, _source.Devices);

    public event Action? Changed;

    public Type? SettingsComponent => typeof(BatterySettings);

    internal async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = _context.Settings;
        Binding = await settings.GetJsonAsync<DeviceBinding?>(DeviceKey, null, cancellationToken).ConfigureAwait(false);
        if (Binding is not null && Binding.Source != _source.Id)
            Binding = null; // gravado por outro widget (não deve acontecer); melhor pedir de novo do que mostrar errado
        Title = await settings.GetAsync(TitleKey, cancellationToken).ConfigureAwait(false) ?? "";
        if (bool.TryParse(await settings.GetAsync(ShowTitleKey, cancellationToken).ConfigureAwait(false), out var showTitle))
            ShowTitle = showTitle;
        Background = TileColor.Normalize(await settings.GetAsync(BackgroundKey, cancellationToken).ConfigureAwait(false));
        if (int.TryParse(await settings.GetAsync(LowLevelKey, cancellationToken).ConfigureAwait(false), out var low))
            LowLevel = Math.Clamp(low, 0, 100);
        if (bool.TryParse(await settings.GetAsync(BadgeKey, cancellationToken).ConfigureAwait(false), out var badge))
            ShowBadge = badge;
    }

    public async Task SetDeviceAsync(BatteryDevice? device)
    {
        Binding = device is null ? null : new DeviceBinding(_source.Id, device.Key, device.Name);
        if (Binding is null)
            await _context.Settings.RemoveAsync(DeviceKey).ConfigureAwait(false);
        else
            await _context.Settings.SetJsonAsync(DeviceKey, Binding).ConfigureAwait(false);
        NotifyChanged();
    }

    public Task SetTitleAsync(string? title) => SaveAsync(TitleKey, Title = title?.Trim() ?? "");

    public Task SetShowTitleAsync(bool value) => SaveAsync(ShowTitleKey, (ShowTitle = value).ToString());

    public Task SetLowLevelAsync(int value) => SaveAsync(LowLevelKey, (LowLevel = Math.Clamp(value, 0, 100)).ToString());

    public Task SetShowBadgeAsync(bool value) => SaveAsync(BadgeKey, (ShowBadge = value).ToString());

    public async Task SetBackgroundAsync(string? hex)
    {
        Background = TileColor.Normalize(hex);
        if (Background is null)
            await _context.Settings.RemoveAsync(BackgroundKey).ConfigureAwait(false);
        else
            await _context.Settings.SetAsync(BackgroundKey, Background).ConfigureAwait(false);
        NotifyChanged();
    }

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken)
    {
        if (reason == RenderReason.Resumed)
            _source.Refresh();
        return ValueTask.FromResult(BatteryRenderer.Render(_context.PixelSize, _context.Theme, CurrentVisual()));
    }

    /// <summary>O que o tile mostra agora.</summary>
    internal BatteryVisual CurrentVisual()
    {
        var compact = _context.PixelSize.Height < 100;
        var device = Current;
        var title = !ShowTitle ? null
            : !string.IsNullOrWhiteSpace(Title) ? Title
            : device?.Name ?? Binding?.Name;

        BatteryVisual visual;
        if (Binding is null)
            visual = new BatteryVisual(title, null, false, BatteryTileState.NotConfigured, compact ? "--" : "Escolha o aparelho");
        else if (_source.State == SourceState.Unavailable)
            visual = new BatteryVisual(title, device?.Percentage, false, BatteryTileState.SourceOffline,
                compact ? _source.OfflineShortText : _source.OfflineText);
        else if (device is null)
            visual = new BatteryVisual(title, null, false,
                _source.State == SourceState.Connecting ? BatteryTileState.Asleep : BatteryTileState.NotFound,
                _source.State == SourceState.Connecting ? (compact ? "..." : "Conectando") : compact ? "Sumiu" : "Não encontrado");
        else if (!device.Connected)
            visual = new BatteryVisual(title, device.Percentage, false, BatteryTileState.Asleep);
        else
            visual = new BatteryVisual(title, device.Percentage, device.Charging, BatteryTileState.Online);

        return visual with
        {
            LowLevel = LowLevel,
            Background = TileColor.Parse(Background),
            Badge = ShowBadge ? _badge : null,
        };
    }

    public ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        // Toque pede as leituras de novo: um jeito de "acordar" o tile sem esperar o próximo minuto.
        if (touch.Kind == TouchKind.Tap)
            _source.Refresh();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        _source.Refresh();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _source.Changed -= OnSourceChanged;
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            try
            {
                await _source.ReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.LogWarning(ex, "Falha ao fechar a conexão da fonte de bateria.");
            }
        }
    }

    private void OnSourceChanged() => NotifyChanged();

    private void NotifyChanged()
    {
        Changed?.Invoke();
        _context.RequestRender();
    }

    private async Task SaveAsync(string key, string value)
    {
        await _context.Settings.SetAsync(key, value).ConfigureAwait(false);
        NotifyChanged();
    }
}
