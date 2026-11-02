using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;
using Xunit.Abstractions;

namespace DeviceBatteryInfo.Tests;

/// <summary>
/// Contra o G HUB de verdade, só quando pedido: <c>$env:GHUB_LIVE=1; dotnet test --filter GHubLive --logger "console;verbosity=detailed"</c>.
/// Fora disso passa sem fazer nada (CI não tem G HUB).
/// </summary>
public class GHubLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GHubLive_ReadsTheRealDevices()
    {
        if (Environment.GetEnvironmentVariable("GHUB_LIVE") != "1")
            return;
        var client = new GHubClient(TimeProvider.System);
        client.Acquire();
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && !(client.State == SourceState.Connected && client.Devices.Any(d => d.Percentage is not null)))
                await Task.Delay(200);

            output.WriteLine($"Estado: {client.State}; erro: {client.LastError ?? "(nenhum)"}");
            foreach (var d in client.Devices)
                output.WriteLine($"{d.Key} | {d.Name} | {d.Kind} | {d.Percentage}% | carregando={d.Charging} | ligado={d.Connected}");
            Assert.True(client.State == SourceState.Connected, client.LastError);
        }
        finally
        {
            await client.ReleaseAsync();
        }
    }
}
