using System.Text.Json;
using OpenWigiDash.DeviceBatteryInfo.Battery;
using OpenWigiDash.DeviceBatteryInfo.GHub;

namespace DeviceBatteryInfo.Tests;

public class GHubProtocolTests
{
    [Fact]
    public void Request_IsTheJsonTheAgentExpects()
    {
        using var doc = JsonDocument.Parse(GHubProtocol.Request("GET", "/devices/list"));
        Assert.Equal("", doc.RootElement.GetProperty("msgId").GetString());
        Assert.Equal("GET", doc.RootElement.GetProperty("verb").GetString());
        Assert.Equal("/devices/list", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void DeviceList_UsesTheSignatureAsStableKey()
    {
        var message = Fixtures.Message("devices-list.json");
        Assert.Equal("SUCCESS", message.ResultCode);
        var devices = GHubProtocol.ParseDeviceList(message.Payload!.Value);

        Assert.Equal(3, devices.Count);
        var keyboard = devices[0];
        Assert.Equal("dev00000006", keyboard.RawId);
        Assert.Equal(Fixtures.KeyboardKey, keyboard.Key);
        Assert.Equal("G715", keyboard.Name);
        Assert.Equal(DeviceKind.Keyboard, keyboard.Kind);
        Assert.True(keyboard.HasBattery);
        Assert.True(keyboard.Active);

        Assert.Equal(Fixtures.MouseKey, devices[1].Key);
        Assert.Equal("PRO X SUPERLIGHT 2", devices[1].Name);
        Assert.Equal(DeviceKind.Mouse, devices[1].Kind);

        Assert.False(devices[2].HasBattery); // webcam
    }

    [Fact]
    public void StableKey_WithoutSignature_FallsBackToModelAndUnit()
    {
        using var doc = JsonDocument.Parse("""{"id":"dev1","deviceBaseModel":"g502","deviceUnitId":"ABC","displayName":"G502"}""");
        Assert.Equal("ghub:g502.abc", GHubProtocol.ParseDevice(doc.RootElement)!.Key);

        using var nameOnly = JsonDocument.Parse("""{"id":"dev1","displayName":"Headset X"}""");
        Assert.Equal("ghub:headset x", GHubProtocol.ParseDevice(nameOnly.RootElement)!.Key);
    }

    [Fact]
    public void DeviceList_TwoDevicesWithTheSameKey_GetSuffixes()
    {
        using var doc = JsonDocument.Parse("""
            {"deviceInfos":[{"id":"dev1","displayName":"Mouse"},{"id":"dev2","displayName":"Mouse"},{"displayName":"no id"}]}
            """);
        var devices = GHubProtocol.ParseDeviceList(doc.RootElement);
        Assert.Equal(["ghub:mouse", "ghub:mouse#2"], devices.Select(d => d.Key));
    }

    [Fact]
    public void Battery_ParsesPercentageAndCharging()
    {
        var message = Fixtures.Message("battery-keyboard.json");
        Assert.Equal("dev00000006", GHubProtocol.BatteryPathId(message.Path));
        var battery = GHubProtocol.ParseBattery(message.Payload!.Value);
        Assert.Equal(new GHubBattery("dev00000006", 55, false), battery);
    }

    [Fact]
    public void NoSuchPath_HasNoPayload()
    {
        var message = Fixtures.Message("battery-no-such-path.json");
        Assert.Equal("NO_SUCH_PATH", message.ResultCode);
        Assert.Null(message.Payload);
        Assert.Equal("dev00000008", GHubProtocol.BatteryPathId(message.Path));
    }

    [Theory]
    [InlineData("/battery/state/changed", null)]
    [InlineData("/devices/list", null)]
    [InlineData("/battery/dev00000012/state", "dev00000012")]
    public void BatteryPathId(string path, string? expected) => Assert.Equal(expected, GHubProtocol.BatteryPathId(path));

    [Fact]
    public void Parse_IgnoresGarbage()
    {
        Assert.Null(GHubProtocol.Parse("not json"));
        Assert.Null(GHubProtocol.Parse("[1,2]"));
        var options = GHubProtocol.Parse("""{"msgId":"","verb":"OPTIONS","path":"/","origin":"backend"}""");
        Assert.Equal("OPTIONS", options!.Verb);
        Assert.Null(options.Payload);
    }

    [Theory]
    [InlineData("ACTIVE", true)]
    [InlineData("active", true)]
    [InlineData("NOT_CONNECTED", false)]
    [InlineData("ABSENT", false)]
    public void IsActive(string state, bool expected) => Assert.Equal(expected, GHubProtocol.IsActive(state));
}
