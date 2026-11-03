using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.Xbox;
using Xunit.Abstractions;

namespace DeviceBatteryInfo.Tests;

/// <summary>
/// Contra os controles de verdade, só quando pedido: <c>$env:XBOX_LIVE=1; dotnet test --filter XboxLive --logger "console;verbosity=detailed"</c>.
/// </summary>
public class XboxLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task XboxLive_ReadsTheRealControllers()
    {
        if (Environment.GetEnvironmentVariable("XBOX_LIVE") != "1")
            return;
        var reader = new WgiControllerReader();
        reader.Read(); // o Windows enche a lista depois do primeiro acesso
        await Task.Delay(2000);
        foreach (var r in reader.Read())
            output.WriteLine($"{r.Name} | {r.VendorId:X4}/{r.ProductId:X4} | sem fio={r.Wireless} | status={r.Status} | {r.RemainingMilliwattHours}/{r.FullChargeMilliwattHours} | chave={XboxInventory.KeyOf(r.Id)}");

        var source = new XboxSource(reader, TimeProvider.System);
        await source.ScanAsync();
        output.WriteLine($"Estado: {source.State}; erro: {source.LastError ?? "(nenhum)"}");
        foreach (var d in source.Devices)
            output.WriteLine($"{d.Key} | {d.Name} | {d.Percentage}% | carregando={d.Charging} | ligado={d.Connected}");
        Assert.True(source.State == SourceState.Connected, source.LastError);
    }
}
