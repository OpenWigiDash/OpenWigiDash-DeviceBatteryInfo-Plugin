using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace DeviceBatteryInfo.Tests;

public class GHubStateTests
{
    private readonly GHubState _state = new(TimeProvider.System);

    private BatteryDevice Device(string key) => _state.Devices.Single(d => d.Key == key);

    [Fact]
    public void List_RegistersOnlyDevicesWithBattery_AndAsksTheirBattery()
    {
        var update = _state.Apply(Fixtures.Message("devices-list.json"));

        Assert.True(update.Changed);
        Assert.Equal(["dev00000006", "dev00000007"], update.BatteryRequests);
        Assert.Equal([Fixtures.KeyboardKey, Fixtures.MouseKey], _state.Devices.Select(d => d.Key));
        Assert.All(_state.Devices, d => Assert.Null(d.Percentage));
    }

    [Fact]
    public void BatteryResponse_FillsTheDevice()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        var update = _state.Apply(Fixtures.Message("battery-keyboard.json"));

        Assert.True(update.Changed);
        var keyboard = Device(Fixtures.KeyboardKey);
        Assert.Equal(55, keyboard.Percentage);
        Assert.True(keyboard.Connected);
        Assert.NotNull(keyboard.UpdatedAt);
    }

    [Fact]
    public void NoSuchPath_ForTheWebcam_ChangesNothing()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        Assert.Equal(GHubUpdate.None, _state.Apply(Fixtures.Message("battery-no-such-path.json")));
    }

    [Fact]
    public void Standby_KeepsLastReading_AndWakeComesBackOnline()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000007", 54));

        // Dormiu.
        Assert.True(_state.Apply(Fixtures.DeviceState("dev00000007", "NOT_CONNECTED")).Changed);
        var asleep = Device(Fixtures.MouseKey);
        Assert.False(asleep.Connected);
        Assert.Equal(54, asleep.Percentage);

        // Acordou com o mesmo id: pede a bateria dele.
        var woke = _state.Apply(Fixtures.DeviceState("dev00000007", "ACTIVE"));
        Assert.Equal(["dev00000007"], woke.BatteryRequests);
        Assert.True(_state.Apply(Fixtures.Battery("dev00000007", 54, path: GHubProtocol.BatteryState("dev00000007"))).Changed);
        Assert.True(Device(Fixtures.MouseKey).Connected);
    }

    [Fact]
    public void BatteryEventWithTheSamePercentage_StillWakesTheDevice()
    {
        // O bug do Stream Deck: o aparelho volta com o mesmo percentual e o tile não sai do "desconectado".
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000006", 55));
        _state.Apply(Fixtures.DeviceState("dev00000006", "NOT_CONNECTED"));

        Assert.True(_state.Apply(Fixtures.Battery("dev00000006", 55)).Changed);
        Assert.True(Device(Fixtures.KeyboardKey).Connected);
    }

    [Fact]
    public void NewSessionIds_AfterStandby_KeepEachDeviceOnItsOwnKey()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000006", 55));
        _state.Apply(Fixtures.Battery("dev00000007", 54));

        // O G HUB voltou com ids novos (e trocados de ordem). Uma leitura de id desconhecido pede a lista.
        var unknown = _state.Apply(Fixtures.Battery("dev00000012", 40));
        Assert.True(unknown.NeedsList);
        Assert.False(unknown.Changed);

        var relisted = _state.Apply(Fixtures.ListWithIds(keyboardId: "dev00000013", mouseId: "dev00000012"));
        Assert.Equal(["dev00000013", "dev00000012"], relisted.BatteryRequests);
        Assert.Equal(2, _state.Devices.Count);

        _state.Apply(Fixtures.Battery("dev00000012", 40));
        Assert.Equal(40, Device(Fixtures.MouseKey).Percentage);
        Assert.Equal(55, Device(Fixtures.KeyboardKey).Percentage);
    }

    [Fact]
    public void DeviceActiveWithUnknownId_AsksTheList()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        var update = _state.Apply(Fixtures.DeviceState("dev00000099", "ACTIVE"));
        Assert.True(update.NeedsList);
    }

    [Fact]
    public void DeviceNotConnectedInTheList_StaysKnownButDisconnected()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000007", 54));

        var mouseAsleep = GHubProtocol.Parse(Fixtures.Read("devices-list.json").Replace(
            "\"id\": \"dev00000007\", \"pid\": 16553, \"isSubdevice\": false, \"parentId\": \"\", \"state\": \"ACTIVE\"",
            "\"id\": \"dev00000007\", \"pid\": 16553, \"isSubdevice\": false, \"parentId\": \"\", \"state\": \"NOT_CONNECTED\""));
        // O mesmo aparelho com state NOT_CONNECTED na lista: desligado, sem pedir bateria.
        var update = _state.Apply(mouseAsleep!);
        Assert.DoesNotContain("dev00000007", update.BatteryRequests);
        Assert.False(Device(Fixtures.MouseKey).Connected);
        Assert.Equal(54, Device(Fixtures.MouseKey).Percentage);
    }

    [Fact]
    public void ListWithoutADevice_MarksItDisconnected()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000007", 54));
        _state.ApplyDeviceList([new GHubDeviceInfo("dev00000006", Fixtures.KeyboardKey, "G715", DeviceKind.Keyboard, true, true)]);

        Assert.False(Device(Fixtures.MouseKey).Connected);
        Assert.Equal(54, Device(Fixtures.MouseKey).Percentage);
    }

    [Fact]
    public void GHubClosed_MarksAllDisconnected()
    {
        _state.Apply(Fixtures.Message("devices-list.json"));
        _state.Apply(Fixtures.Battery("dev00000006", 55, charging: true));

        Assert.True(_state.MarkAllDisconnected());
        var keyboard = Device(Fixtures.KeyboardKey);
        Assert.False(keyboard.Connected);
        Assert.False(keyboard.Charging);
        Assert.Equal(55, keyboard.Percentage);

        // Depois de fechar, ids antigos não valem mais.
        Assert.True(_state.Apply(Fixtures.Battery("dev00000006", 56)).NeedsList);
    }
}
