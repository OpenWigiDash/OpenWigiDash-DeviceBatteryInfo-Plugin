using OpenWigiDash.DeviceBatteryInfo.Battery;

namespace DeviceBatteryInfo.Tests;

public class DeviceResolverTests
{
    private static BatteryDevice Dev(string key, string name) => new(key, name, DeviceKind.Mouse, 50, false, true, null);

    private static readonly IReadOnlyList<BatteryDevice> Devices =
        [Dev("ghub:keyboard.g715.0.1", "G715"), Dev("ghub:mouse.prox2.1.2", "PRO X 2")];

    [Fact]
    public void ByKey() =>
        Assert.Equal("PRO X 2", DeviceResolver.Find(new("ghub", "ghub:mouse.prox2.1.2", "x"), Devices)!.Name);

    [Fact]
    public void NoBinding_IsNotTheFirstDevice()
    {
        // O "G Hub Device" caía em devices[0] (o teclado) quando o widget não tinha aparelho gravado.
        Assert.Null(DeviceResolver.Find(null, Devices));
        Assert.Null(DeviceResolver.Find(new("ghub", "", ""), Devices));
    }

    [Fact]
    public void UnknownKey_IsNotFound_NeverAnotherDevice() =>
        Assert.Null(DeviceResolver.Find(new("ghub", "ghub:headset.pro.0.9", "PRO X Headset"), Devices));

    [Fact]
    public void KeyChanged_FoundByUniqueName() =>
        Assert.Equal("ghub:keyboard.g715.0.1", DeviceResolver.Find(new("ghub", "ghub:g715-old", "g715"), Devices)!.Key);

    [Fact]
    public void KeyChanged_TwoWithTheSameName_IsNotFound()
    {
        IReadOnlyList<BatteryDevice> twins = [Dev("a", "G502"), Dev("b", "G502")];
        Assert.Null(DeviceResolver.Find(new("ghub", "c", "G502"), twins));
    }
}
