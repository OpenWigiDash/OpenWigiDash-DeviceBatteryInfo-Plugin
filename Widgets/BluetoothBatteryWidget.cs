using System.Drawing;
using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Bluetooth;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Widgets;

[assembly: OpenWigiWidget(typeof(BluetoothBatteryWidget))]

namespace OpenWigiDash.DeviceBatteryInfo.Widgets;

/// <summary>
/// Bateria de um aparelho Bluetooth pareado (fone, mouse, teclado, celular, controle), a mesma que o Windows mostra
/// em Configurações &gt; Bluetooth. Mesmo desenho de tile do G HUB Battery.
/// </summary>
public sealed class BluetoothBatteryWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("b3e7d2c4-5a19-4f86-9c0b-7e2a4d1f8c35");

    internal static readonly SourceBadge Badge = new("BT", Color.FromArgb(0x00, 0x82, 0xFC)) { Image = Logo.Load("bluetooth-logo.png") };

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "Bluetooth Battery",
        Version = typeof(BluetoothBatteryWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = GHubBatteryWidget.Website,
        Description = "Bateria de um aparelho Bluetooth pareado (fone, mouse, teclado, celular), a mesma que o Windows mostra.",
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        BluetoothSource.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(BatteryRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new BatteryVisual(null, 60, false, BatteryTileState.Online) { Badge = Badge }));

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        BatteryInstance.CreateAsync(context, BluetoothSource.Shared, Badge, cancellationToken);
}
