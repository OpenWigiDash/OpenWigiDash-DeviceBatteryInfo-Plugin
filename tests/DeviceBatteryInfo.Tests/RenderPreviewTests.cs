using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Widgets;

namespace DeviceBatteryInfo.Tests;

/// <summary>
/// Salva PNGs dos tiles para olhar, só quando pedido: <c>$env:RENDER_OUT="C:\pasta"; dotnet test --filter RenderPreview</c>.
/// </summary>
public class RenderPreviewTests
{
    [Fact]
    public void RenderPreview_SavesTiles()
    {
        var output = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrEmpty(output) || !OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(output);
        var badges = new (string Name, SourceBadge Badge)[]
        {
            ("ghub", GHubBatteryWidget.Badge), ("bluetooth", BluetoothBatteryWidget.Badge), ("xbox", XboxBatteryWidget.Badge),
        };
        foreach (var (name, badge) in badges)
        foreach (var size in new[] { WidgetSize.Mini, WidgetSize.Small })
        foreach (var (state, percent) in new[] { (BatteryTileState.Online, 98), (BatteryTileState.Online, 40), (BatteryTileState.Asleep, 40) })
        {
            var pixels = TileFrame.Default.ContentSize(size.CellSpan);
            var frame = BatteryRenderer.Render(pixels, new WidgetTheme(),
                new BatteryVisual("Galaxy A55 5G", percent, false, state) { Badge = badge });
            Save(frame, Path.Combine(output, $"{name}-{size}-{state}-{percent}.png".ToLowerInvariant()));
        }
    }

    private static void Save(WidgetFrame frame, string path)
    {
        var size = frame.Size;
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = frame.Pixels.ToArray();
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(bytes, y * frame.Stride, data.Scan0 + y * data.Stride, size.Width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
