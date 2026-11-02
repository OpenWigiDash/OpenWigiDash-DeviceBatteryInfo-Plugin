using System.Drawing;
using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.GHub;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Widgets;

[assembly: OpenWigiWidget(typeof(GHubBatteryWidget))]

namespace OpenWigiDash.DeviceBatteryInfo.Widgets;

/// <summary>
/// Bateria de um aparelho Logitech (mouse, teclado, headset) lida do G HUB. Substitui o "G Hub Device" do Stream Deck.
/// Cada fonte de bateria tem o seu widget (G HUB agora; Bluetooth e Xbox depois), com o mesmo desenho de tile.
/// </summary>
public sealed class GHubBatteryWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("6f1c2a9e-8d43-4b7a-b5e2-3c9d0a7f4e61");

    public const string Website = "https://github.com/OpenWigiDash/OpenWigiDash-DeviceBatteryInfo-Plugin";

    /// <summary>Selo do G HUB no canto do tile (azul-ciano da Logitech G).</summary>
    internal static readonly SourceBadge Badge = new("G", Color.FromArgb(0x00, 0xB8, 0xFC));

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "G HUB Battery",
        Version = typeof(GHubBatteryWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = Website,
        Description = "Bateria de um mouse, teclado ou headset Logitech, lida do G HUB.",
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        GHubClient.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(BatteryRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new BatteryVisual("Mouse", 75, false, BatteryTileState.Online) { Badge = Badge }));

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        BatteryInstance.CreateAsync(context, GHubClient.Shared, Badge, cancellationToken);
}
