using OpenWigiDash.DeviceBatteryInfo.Xbox;

namespace DeviceBatteryInfo.Tests;

public class XboxInventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    // O que o Windows.Gaming.Input devolveu no PC do Victor: Xbox Series no adaptador, pilhas AA, "médio".
    private static XboxControllerReading Controller(string id = "{wgi/nrid/aaaa}\0", int? status = 1, int? remaining = 700,
        int? full = 1000, string name = "Xbox One Game Controller") =>
        new(id, name, 0x045E, 0x0B12, true, status, remaining, full);

    [Fact]
    public void Update_ReadsPercentageAndStableKey()
    {
        var device = Assert.Single(new XboxInventory().Update([Controller()], Now));

        Assert.StartsWith("xbox:", device.Key);
        Assert.Equal(XboxInventory.KeyOf("{wgi/nrid/aaaa}"), device.Key); // o \0 do fim não muda a chave
        Assert.Equal("Xbox One Game Controller", device.Name);
        Assert.Equal(70, device.Percentage);
        Assert.False(device.Charging);
        Assert.True(device.Connected);
    }

    [Fact]
    public void Update_ChargingStatus()
    {
        var device = Assert.Single(new XboxInventory().Update([Controller(status: 3, remaining: 400)], Now));

        Assert.True(device.Charging);
        Assert.Equal(40, device.Percentage);
    }

    [Fact]
    public void Update_ControllerThatLeft_StaysAsDisconnectedWithLastReading()
    {
        var inventory = new XboxInventory();
        inventory.Update([Controller()], Now);

        var device = Assert.Single(inventory.Update([], Now.AddMinutes(1)));

        Assert.False(device.Connected);
        Assert.Equal(70, device.Percentage);
        Assert.Equal(Now, device.UpdatedAt);
    }

    [Fact]
    public void Update_WiredWithoutBattery_HasNoPercentage()
    {
        var device = Assert.Single(new XboxInventory().Update([Controller(status: 0, remaining: null, full: null)], Now));

        Assert.Null(device.Percentage);
        Assert.True(device.Connected);
    }

    [Fact]
    public void Update_TwoEqualControllers_GetNumberedNamesAndDifferentKeys()
    {
        var devices = new XboxInventory().Update([Controller("{a}"), Controller("{b}", remaining: 300)], Now);

        Assert.Equal(2, devices.Count);
        Assert.NotEqual(devices[0].Key, devices[1].Key);
        Assert.Contains(devices, d => d.Name == "Xbox One Game Controller");
        Assert.Contains(devices, d => d.Name == "Xbox One Game Controller #2");
    }

    [Fact]
    public void Update_OrderOfTheList_DoesNotChangeWhichKeyHasWhichReading()
    {
        var first = new XboxInventory().Update([Controller("{a}", remaining: 900), Controller("{b}", remaining: 100)], Now);
        var second = new XboxInventory().Update([Controller("{b}", remaining: 100), Controller("{a}", remaining: 900)], Now);

        foreach (var device in first)
            Assert.Equal(device.Percentage, second.Single(d => d.Key == device.Key).Percentage);
    }
}
