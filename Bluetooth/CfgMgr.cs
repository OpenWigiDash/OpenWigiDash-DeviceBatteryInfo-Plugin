using System.Runtime.InteropServices;

namespace OpenWigiDash.DeviceBatteryInfo.Bluetooth;

/// <summary>Lê os nós Bluetooth do Gerenciador de Dispositivos.</summary>
internal interface IBluetoothNodeReader
{
    IReadOnlyList<BluetoothNode> Read();
}

/// <summary>
/// Leitura pela cfgmgr32 (a mesma base do Gerenciador de Dispositivos e do Get-PnpDevice): rápida, sem pedir nada
/// ao aparelho e sem precisar de WinRT. A bateria é a DEVPKEY_Bluetooth_Battery, que o Windows preenche com o que o
/// aparelho informa (serviço de bateria LE ou o indicador do viva-voz).
/// </summary>
internal sealed class CfgMgrBluetoothReader : IBluetoothNodeReader
{
    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE"];

    private const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint CR_SUCCESS = 0x00;
    private const uint CR_BUFFER_SMALL = 0x1A;
    private const uint DN_DEVICE_DISCONNECTED = 0x02000000;

    private static readonly DEVPROPKEY BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DEVPROPKEY FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DEVPROPKEY DeviceDescKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);

    public IReadOnlyList<BluetoothNode> Read()
    {
        var nodes = new List<BluetoothNode>();
        foreach (var enumerator in Enumerators)
        {
            foreach (var id in DeviceIds(enumerator))
            {
                if (CM_Locate_DevNodeW(out var devInst, id, 0) != CR_SUCCESS)
                    continue;
                var disconnected = CM_Get_DevNode_Status(out var status, out _, devInst, 0) == CR_SUCCESS
                                   && (status & DN_DEVICE_DISCONNECTED) != 0;
                var battery = ReadByte(devInst, BatteryKey);
                // Só o nome dos nós que importam: o do aparelho e o que tem bateria.
                string? name = null;
                if (battery is not null || BluetoothInventory.IsDeviceNode(id))
                    name = ReadString(devInst, FriendlyNameKey) ?? ReadString(devInst, DeviceDescKey);
                nodes.Add(new BluetoothNode(id, name, battery, disconnected));
            }
        }
        return nodes;
    }

    private static List<string> DeviceIds(string enumerator)
    {
        const uint flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != CR_SUCCESS || length <= 1)
                return [];
            var buffer = new char[length];
            var result = CM_Get_Device_ID_ListW(enumerator, buffer, length, flags);
            if (result == CR_BUFFER_SMALL)
                continue; // um aparelho apareceu entre as duas chamadas
            if (result != CR_SUCCESS)
                return [];
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }

    private static int? ReadByte(uint devInst, DEVPROPKEY key)
    {
        var buffer = new byte[4];
        var size = (uint)buffer.Length;
        var k = key;
        if (CM_Get_DevNode_PropertyW(devInst, ref k, out _, buffer, ref size, 0) != CR_SUCCESS || size < 1)
            return null;
        return buffer[0];
    }

    private static string? ReadString(uint devInst, DEVPROPKEY key)
    {
        var k = key;
        uint size = 0;
        var result = CM_Get_DevNode_PropertyW(devInst, ref k, out _, null, ref size, 0);
        if (result != CR_BUFFER_SMALL || size < 2)
            return null;
        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref k, out _, buffer, ref size, 0) != CR_SUCCESS)
            return null;
        var text = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DEVPROPKEY(Guid fmtid, uint pid)
    {
        public readonly Guid Fmtid = fmtid;
        public readonly uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint propertyType,
        [Out] byte[]? buffer, ref uint bufferSize, uint flags);
}
