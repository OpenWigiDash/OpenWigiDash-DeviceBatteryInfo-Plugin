using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Rendering;

namespace DeviceBatteryInfo.Tests;

public class BatteryRendererTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(54, 3)]
    [InlineData(57, 3)]
    [InlineData(80, 4)]
    [InlineData(81, 5)]
    [InlineData(100, 5)]
    public void LitSegments(int percentage, int expected) => Assert.Equal(expected, BatteryRenderer.LitSegments(percentage));

    [Fact]
    public void Accent_ByState()
    {
        Assert.Equal(BatteryRenderer.NormalColor, BatteryRenderer.AccentFor(new(null, 54, false, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.LowColor, BatteryRenderer.AccentFor(new(null, 20, false, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.NormalColor, BatteryRenderer.AccentFor(new(null, 84, false, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.HighColor, BatteryRenderer.AccentFor(new(null, 85, false, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.HighColor, BatteryRenderer.AccentFor(new(null, 98, false, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.ChargingColor, BatteryRenderer.AccentFor(new(null, 10, true, BatteryTileState.Online)));
        Assert.Equal(BatteryRenderer.DimColor, BatteryRenderer.AccentFor(new(null, 54, false, BatteryTileState.Asleep)));
    }

    [Fact]
    public void BottomText()
    {
        Assert.Equal("54%", BatteryRenderer.BottomText(new(null, 54, false, BatteryTileState.Online)));
        Assert.Equal("--%", BatteryRenderer.BottomText(new(null, null, false, BatteryTileState.Asleep)));
        Assert.Equal("G HUB fechado", BatteryRenderer.BottomText(new(null, 54, false, BatteryTileState.SourceOffline, "G HUB fechado")));
    }

    [Theory]
    [InlineData(BatteryTileState.Online, true)]
    [InlineData(BatteryTileState.Online, false)]
    [InlineData(BatteryTileState.Asleep, false)]
    [InlineData(BatteryTileState.NotFound, false)]
    [InlineData(BatteryTileState.SourceOffline, false)]
    [InlineData(BatteryTileState.NotConfigured, false)]
    public void Render_FillsTheContentArea(BatteryTileState state, bool charging)
    {
        if (!OperatingSystem.IsWindows())
            return; // System.Drawing (GDI+) só no Windows
        foreach (var size in new[] { WidgetSize.Mini, WidgetSize.Small })
        {
            var pixels = TileFrame.Default.ContentSize(size.CellSpan);
            var frame = BatteryRenderer.Render(pixels, new WidgetTheme(),
                new BatteryVisual("Mouse", 54, charging, state) { Badge = new SourceBadge("G", System.Drawing.Color.DeepSkyBlue) });
            Assert.Equal(pixels, frame.Size);
        }
    }
}
