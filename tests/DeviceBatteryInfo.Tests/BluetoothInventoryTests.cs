using OpenWigiDash.DeviceBatteryInfo.Bluetooth;

namespace DeviceBatteryInfo.Tests;

public class BluetoothInventoryTests
{
    // Formato real (PC do Victor), com endereços trocados: um celular pelo Vincular ao Celular e um mouse LE desligado.
    private const string Phone = "3C0A7A000001";
    private const string Mouse = "D905CE000002";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 13, 0, 0, TimeSpan.Zero);

    private static List<BluetoothNode> RealLikeNodes() =>
    [
        new($@"BTHENUM\DEV_{Phone}\8&38742C08&0&BLUETOOTHDEVICE_{Phone}", "Galaxy A55 5G", null, false),
        new($@"BTHLE\DEV_{Phone}\8&138EAB69&0&{Phone}", "Galaxy A55 5G", null, true),
        new($@"BTHENUM\{{0000111F-0000-1000-8000-00805F9B34FB}}_VID&00010075_PID&0100\8&38742C08&0&{Phone}_C00000000",
            "Galaxy A55 5G Hands-Free HF", 40, false),
        new($@"BTHENUM\{{0000110A-0000-1000-8000-00805F9B34FB}}_VID&00010075_PID&0100\8&38742C08&0&{Phone}_C00000000",
            null, null, false),
        new($@"BTHLEDEVICE\{{0000FCF1-0000-1000-8000-00805F9B34FB}}_{Phone}\9&15D62D7E&0&0099", null, null, false),
        new($@"BTHLE\DEV_{Mouse}\8&138EAB69&0&{Mouse}", "Pebble M350s", 95, true),
        new($@"BTHLEDEVICE\{{0000180F-0000-1000-8000-00805F9B34FB}}_DEV_VID&02046D_PID&B036_REV&0007_{Mouse}\9&1F1C0A21&0&001B",
            null, null, false),
    ];

    [Theory]
    [InlineData(@"BTHENUM\DEV_3C0A7A000001\8&38742C08&0&BLUETOOTHDEVICE_3C0A7A000001", "3c0a7a000001")]
    [InlineData(@"BTHLE\DEV_D905CE000002\8&138EAB69&0&D905CE000002", "d905ce000002")]
    [InlineData(@"BTHENUM\{0000111F-0000-1000-8000-00805F9B34FB}_VID&00010075_PID&0100\8&38742C08&0&3C0A7A000001_C00000000", "3c0a7a000001")]
    [InlineData(@"BTHLEDEVICE\{0000FCF1-0000-1000-8000-00805F9B34FB}_3C0A7A000001\9&15D62D7E&0&0099", "3c0a7a000001")]
    [InlineData(@"BTHLEDEVICE\{0000180F-0000-1000-8000-00805F9B34FB}_DEV_VID&02046D_PID&B036_REV&0007_D905CE000002\9&1F1C0A21&0&001B", "d905ce000002")]
    [InlineData(@"BTH\MS_BTHLE\7&2d99a338&1&3", null)]
    [InlineData(@"BTHENUM\{00001132-0000-1000-8000-00805F9B34FB}_LOCALMFG&0000\7&2d99a338&1&000000000000_00000000", null)]
    public void AddressOf_ReadsTheBluetoothAddress(string instanceId, string? expected) =>
        Assert.Equal(expected, BluetoothInventory.AddressOf(instanceId));

    [Fact]
    public void Build_OneDevicePerAddress_OnlyWithBattery()
    {
        var devices = BluetoothInventory.Build(RealLikeNodes(), Now);

        Assert.Equal(2, devices.Count);
        var phone = Assert.Single(devices, d => d.Key == "bt:3c0a7a000001");
        Assert.Equal("Galaxy A55 5G", phone.Name);
        Assert.Equal(40, phone.Percentage);
        Assert.True(phone.Connected); // o nó LE parado não derruba o clássico conectado

        var mouse = Assert.Single(devices, d => d.Key == "bt:d905ce000002");
        Assert.Equal("Pebble M350s", mouse.Name);
        Assert.Equal(95, mouse.Percentage);
        Assert.False(mouse.Connected); // leitura que o Windows guardou: mostra como dormindo
    }

    [Fact]
    public void Build_PrefersTheReadingOfAConnectedNode()
    {
        var nodes = new List<BluetoothNode>
        {
            new($@"BTHLE\DEV_{Phone}\8&1&0&{Phone}", "Fone", 10, true),
            new($@"BTHENUM\DEV_{Phone}\8&1&0&BLUETOOTHDEVICE_{Phone}", "Fone", 80, false),
        };

        var device = Assert.Single(BluetoothInventory.Build(nodes, Now));

        Assert.Equal(80, device.Percentage);
    }

    [Fact]
    public void Build_WithoutDeviceNode_UsesTheProfileNameWithoutSuffix()
    {
        var nodes = new List<BluetoothNode>
        {
            new($@"BTHENUM\{{0000111F-0000-1000-8000-00805F9B34FB}}_VID&1\8&1&0&{Phone}_C00000000", "Meu Fone Hands-Free HF", 55, false),
        };

        var device = Assert.Single(BluetoothInventory.Build(nodes, Now));

        Assert.Equal("Meu Fone", device.Name);
        Assert.True(device.Connected);
    }

    [Fact]
    public void Build_KeyDoesNotDependOnTheNodeOrInstanceSuffix()
    {
        var first = BluetoothInventory.Build(RealLikeNodes(), Now);
        var reordered = RealLikeNodes();
        reordered.Reverse();
        var second = BluetoothInventory.Build(reordered, Now.AddHours(1));

        Assert.Equal(first.Select(d => d.Key), second.Select(d => d.Key));
    }
}
