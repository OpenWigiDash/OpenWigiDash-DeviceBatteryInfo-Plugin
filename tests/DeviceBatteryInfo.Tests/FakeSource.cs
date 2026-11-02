using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace DeviceBatteryInfo.Tests;

/// <summary>Fonte de bateria em memória.</summary>
internal sealed class FakeSource : IBatterySource
{
    public string Id => "ghub";
    public string DisplayName => "Logitech G HUB";
    public string ShortName => "G HUB";
    public SourceState State { get; set; } = SourceState.Connected;
    public string? LastError { get; set; }
    public IReadOnlyList<BatteryDevice> Devices { get; set; } = [];
    public event Action? Changed;
    public int Refreshes { get; private set; }
    public int Users { get; private set; }

    public void Refresh() => Refreshes++;
    public void Acquire() => Users++;

    public Task ReleaseAsync()
    {
        Users--;
        return Task.CompletedTask;
    }

    public void Raise() => Changed?.Invoke();
}
