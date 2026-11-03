using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.Bluetooth;
using Xunit.Abstractions;

namespace DeviceBatteryInfo.Tests;

/// <summary>
/// Contra o Bluetooth de verdade, só quando pedido: <c>$env:BT_LIVE=1; dotnet test --filter BluetoothLive --logger "console;verbosity=detailed"</c>.
/// Fora disso passa sem fazer nada (CI não tem Bluetooth).
/// </summary>
public class BluetoothLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BluetoothLive_ReadsTheRealDevices()
    {
        if (Environment.GetEnvironmentVariable("BT_LIVE") != "1")
            return;
        var source = new BluetoothSource(new CfgMgrBluetoothReader(), TimeProvider.System);
        await source.ScanAsync();

        output.WriteLine($"Estado: {source.State}; erro: {source.LastError ?? "(nenhum)"}");
        foreach (var d in source.Devices)
            output.WriteLine($"{d.Key} | {d.Name} | {d.Percentage}% | ligado={d.Connected}");
        Assert.True(source.State == SourceState.Connected, source.LastError);
    }
}
