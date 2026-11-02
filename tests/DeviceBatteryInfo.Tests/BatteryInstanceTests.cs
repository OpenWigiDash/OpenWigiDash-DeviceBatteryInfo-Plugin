using OpenWigi.Sdk;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.Rendering;
using OpenWigiDash.DeviceBatteryInfo.Widgets;

namespace DeviceBatteryInfo.Tests;

public class BatteryInstanceTests
{
    private static readonly BatteryDevice Mouse = new("ghub:mouse.1", "PRO X 2", DeviceKind.Mouse, 54, false, true, DateTimeOffset.UtcNow);
    private static readonly BatteryDevice Keyboard = new("ghub:keyboard.1", "G715", DeviceKind.Keyboard, 57, false, true, DateTimeOffset.UtcNow);

    private readonly FakeSource _source = new() { Devices = [Keyboard, Mouse] };
    private readonly FakeContext _context = new(WidgetSize.Small);

    private async Task<BatteryInstance> CreateAsync(FakeContext? context = null) =>
        (BatteryInstance)await BatteryInstance.CreateAsync(context ?? _context, _source, GHubBatteryWidget.Badge, default);

    [Fact]
    public async Task NewWidget_AsksForADevice_InsteadOfShowingTheFirst()
    {
        var instance = await CreateAsync();
        var visual = instance.CurrentVisual();
        Assert.Equal(BatteryTileState.NotConfigured, visual.State);
        Assert.Null(visual.Percentage);
        Assert.Null(instance.Current);
    }

    [Fact]
    public async Task ChosenDevice_IsSaved_AndSurvivesReload()
    {
        var instance = await CreateAsync();
        await instance.SetDeviceAsync(Mouse);

        var visual = instance.CurrentVisual();
        Assert.Equal(BatteryTileState.Online, visual.State);
        Assert.Equal(54, visual.Percentage);
        Assert.Equal("PRO X 2", visual.Title);

        // Outra instância com as mesmas configurações (o manager reabriu): o mesmo mouse, mesmo com a lista em outra ordem.
        _source.Devices = [Mouse, Keyboard];
        var reloaded = await CreateAsync(_context);
        Assert.Equal(Mouse.Key, reloaded.Current!.Key);
    }

    [Fact]
    public async Task Asleep_ShowsLastReadingDimmed()
    {
        var instance = await CreateAsync();
        await instance.SetDeviceAsync(Mouse);
        _source.Devices = [Keyboard, Mouse with { Connected = false }];

        var visual = instance.CurrentVisual();
        Assert.Equal(BatteryTileState.Asleep, visual.State);
        Assert.Equal(54, visual.Percentage);
    }

    [Fact]
    public async Task MissingDevice_IsNotFound_NotAnotherDevice()
    {
        var instance = await CreateAsync();
        await instance.SetDeviceAsync(Mouse);
        _source.Devices = [Keyboard];

        var visual = instance.CurrentVisual();
        Assert.Equal(BatteryTileState.NotFound, visual.State);
        Assert.Null(visual.Percentage);
        Assert.Equal("PRO X 2", visual.Title); // o nome gravado
    }

    [Fact]
    public async Task SourceClosed_SaysSo()
    {
        var instance = await CreateAsync();
        await instance.SetDeviceAsync(Mouse);
        _source.State = SourceState.Unavailable;

        var visual = instance.CurrentVisual();
        Assert.Equal(BatteryTileState.SourceOffline, visual.State);
        Assert.Equal("G HUB fechado", visual.Detail);
    }

    [Fact]
    public async Task Charging_AndTitleAndOptions()
    {
        var instance = await CreateAsync();
        await instance.SetDeviceAsync(Mouse);
        _source.Devices = [Mouse with { Charging = true }];
        await instance.SetTitleAsync("Mouse");
        await instance.SetLowLevelAsync(30);
        await instance.SetShowBadgeAsync(false);
        await instance.SetBackgroundAsync("#1e3a8a");

        var visual = instance.CurrentVisual();
        Assert.True(visual.Charging);
        Assert.Equal("Mouse", visual.Title);
        Assert.Equal(30, visual.LowLevel);
        Assert.Null(visual.Badge);
        Assert.Equal(System.Drawing.Color.FromArgb(0x1E, 0x3A, 0x8A), visual.Background);

        await instance.SetShowTitleAsync(false);
        Assert.Null(instance.CurrentVisual().Title);
    }

    [Fact]
    public async Task SourceChange_RequestsRender_AndWakeRefreshes()
    {
        var instance = await CreateAsync();
        var before = _context.Renders;
        _source.Raise();
        Assert.True(_context.Renders > before);

        await instance.OnWakeAsync(default);
        await instance.OnTouchAsync(new TouchEvent(TouchKind.Tap, 1, 1, TimeSpan.Zero), default);
        Assert.Equal(2, _source.Refreshes);
    }

    [Fact]
    public async Task Dispose_ReleasesTheSourceOnce()
    {
        var instance = await CreateAsync();
        Assert.Equal(1, _source.Users);
        await instance.DisposeAsync();
        await instance.DisposeAsync();
        Assert.Equal(0, _source.Users);
    }
}
