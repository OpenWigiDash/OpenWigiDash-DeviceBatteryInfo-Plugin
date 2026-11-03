using Microsoft.Extensions.Time.Testing;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.Bluetooth;

namespace DeviceBatteryInfo.Tests;

public class BluetoothSourceTests
{
    private sealed class FakeReader : IBluetoothNodeReader
    {
        public IReadOnlyList<BluetoothNode> Nodes { get; set; } = [];
        public Exception? Error { get; set; }
        public int Reads { get; private set; }

        public IReadOnlyList<BluetoothNode> Read()
        {
            Reads++;
            if (Error is not null)
                throw Error;
            return Nodes;
        }
    }

    private static BluetoothNode Mouse(int battery, bool disconnected = false) =>
        new(@"BTHLE\DEV_AABBCCDDEEFF\8&1&0&AABBCCDDEEFF", "Mouse BT", battery, disconnected);

    [Fact]
    public async Task Scan_ListsDevicesAndConnects()
    {
        var reader = new FakeReader { Nodes = [Mouse(70)] };
        var source = new BluetoothSource(reader, new FakeTimeProvider());

        await source.ScanAsync();

        Assert.Equal(SourceState.Connected, source.State);
        var device = Assert.Single(source.Devices);
        Assert.Equal("bt:aabbccddeeff", device.Key);
        Assert.Equal(70, device.Percentage);
    }

    [Fact]
    public async Task Scan_RaisesChangedOnlyWhenSomethingChanges()
    {
        var reader = new FakeReader { Nodes = [Mouse(70)] };
        var source = new BluetoothSource(reader, new FakeTimeProvider());
        var changes = 0;
        var signal = new SemaphoreSlim(0);
        source.Changed += () => { Interlocked.Increment(ref changes); signal.Release(); };

        await source.ScanAsync();
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        await source.ScanAsync();
        reader.Nodes = [Mouse(69)];
        await source.ScanAsync();
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        reader.Nodes = [Mouse(69, disconnected: true)];
        await source.ScanAsync();
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        await Task.Delay(100);
        Assert.Equal(3, Volatile.Read(ref changes));
        Assert.False(source.Devices[0].Connected);
    }

    [Fact]
    public async Task Scan_ErrorMakesTheSourceUnavailable_AndRecovers()
    {
        var reader = new FakeReader { Error = new DllNotFoundException("cfgmgr32.dll") };
        var source = new BluetoothSource(reader, new FakeTimeProvider());

        await source.ScanAsync();
        Assert.Equal(SourceState.Unavailable, source.State);
        Assert.Contains("cfgmgr32", source.LastError);

        reader.Error = null;
        reader.Nodes = [Mouse(50)];
        await source.ScanAsync();
        Assert.Equal(SourceState.Connected, source.State);
        Assert.Null(source.LastError);
        Assert.Single(source.Devices);
    }

    [Fact]
    public async Task Acquire_ReadsRightAway_AndPollsEvery30Seconds()
    {
        var reader = new FakeReader { Nodes = [Mouse(70)] };
        var time = new FakeTimeProvider();
        var source = new BluetoothSource(reader, time);

        source.Acquire();
        await WaitFor(() => reader.Reads >= 1);
        time.Advance(BluetoothSource.PollInterval);
        await WaitFor(() => reader.Reads >= 2);
        await source.ReleaseAsync();
        var reads = reader.Reads;
        time.Advance(BluetoothSource.PollInterval * 3);
        await Task.Delay(100);

        Assert.Equal(reads, reader.Reads); // o último widget saiu: parou de ler
    }

    [Fact]
    public void Texts_AreAboutBluetooth()
    {
        IBatterySource source = new BluetoothSource(new FakeReader(), new FakeTimeProvider());

        Assert.Equal("bluetooth", source.Id);
        Assert.Equal("Sem Bluetooth", source.OfflineText);
        Assert.Contains("endereço Bluetooth", source.BindingHelp);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
