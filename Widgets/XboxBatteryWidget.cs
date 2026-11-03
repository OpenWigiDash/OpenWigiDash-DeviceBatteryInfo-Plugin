using System.Drawing;
using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Xbox;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Widgets;

[assembly: OpenWigiWidget(typeof(XboxBatteryWidget))]

namespace OpenWigiDash.DeviceBatteryInfo.Widgets;

/// <summary>
/// Bateria de um controle Xbox no adaptador sem fio ou no cabo, pelo Windows.Gaming.Input. Pareado por Bluetooth, o
/// controle aparece no Bluetooth Battery. Mesmo desenho de tile do G HUB Battery.
/// </summary>
public sealed class XboxBatteryWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("4d8a1f60-2c7e-4b93-a5d1-9e6f3b0c7a28");

    internal static readonly SourceBadge Badge = new("X", Color.FromArgb(0x10, 0x7C, 0x10)) { Image = Logo.Load("gamepad-logo.png") };

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "Xbox Battery",
        Version = typeof(XboxBatteryWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = GHubBatteryWidget.Website,
        Description = "Bateria de um controle Xbox no adaptador sem fio ou no cabo.",
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        XboxSource.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(BatteryRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new BatteryVisual(null, 70, false, BatteryTileState.Online) { Badge = Badge }));

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        BatteryInstance.CreateAsync(context, XboxSource.Shared, Badge, cancellationToken);
}
