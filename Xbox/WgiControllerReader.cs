using System.Runtime.InteropServices;

namespace OpenWigiDash.DeviceBatteryInfo.Xbox;

/// <summary>Um controle como o Windows.Gaming.Input o vê agora.</summary>
/// <param name="Id">NonRoamableId: fixo para o controle neste PC.</param>
/// <param name="Status">BatteryStatus: 0 sem bateria, 1 descarregando, 2 parado, 3 carregando; <c>null</c> sem relatório.</param>
internal sealed record XboxControllerReading(
    string Id,
    string Name,
    ushort VendorId,
    ushort ProductId,
    bool Wireless,
    int? Status,
    int? RemainingMilliwattHours,
    int? FullChargeMilliwattHours);

internal interface IXboxControllerReader
{
    IReadOnlyList<XboxControllerReading> Read();
}

/// <summary>
/// Windows.Gaming.Input chamado direto pelas interfaces COM do WinRT, sem a projeção do C#/WinRT (que levaria
/// ~25 MB de Microsoft.Windows.SDK.NET.dll para dentro do plugin e não descarrega bem). Só lê: lista de controles,
/// nome, id fixo e o relatório de bateria.
/// </summary>
internal sealed unsafe class WgiControllerReader : IXboxControllerReader
{
    private static readonly Guid IID_IRawGameControllerStatics = new("EB8D0792-E95A-4B19-AFC7-0A59F8BF759E");
    private static readonly Guid IID_IRawGameController2 = new("43C0C035-BB73-4756-A787-3ED6BEA617BD");
    private static readonly Guid IID_IGameController = new("1BAF6522-5F64-42C5-8267-B9FE2215BFBD");
    private static readonly Guid IID_IGameControllerBatteryInfo = new("DCECC681-3963-4DA6-955D-553F3B6F6161");
    private static readonly Guid IID_IReferenceInt32 = new("548CEFBD-BC8A-5FA0-8DF2-957440FC8BF4");

    private readonly object _lock = new();
    private IntPtr _statics;

    public IReadOnlyList<XboxControllerReading> Read()
    {
        lock (_lock)
        {
            // A fábrica fica viva: o Windows só enche a lista de controles depois do primeiro acesso.
            if (_statics == IntPtr.Zero)
                _statics = ActivationFactory("Windows.Gaming.Input.RawGameController", IID_IRawGameControllerStatics);

            IntPtr vector;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_statics, 10))(_statics, &vector)); // get_RawGameControllers
            try
            {
                uint count;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(vector, 7))(vector, &count)); // get_Size
                var list = new List<XboxControllerReading>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr controller;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(vector, 6))(vector, i, &controller)); // GetAt
                    try
                    {
                        if (ReadController(controller) is { } reading)
                            list.Add(reading);
                    }
                    finally
                    {
                        Marshal.Release(controller);
                    }
                }
                return list;
            }
            finally
            {
                Marshal.Release(vector);
            }
        }
    }

    private static XboxControllerReading? ReadController(IntPtr controller)
    {
        // IRawGameController: get_HardwareProductId (9), get_HardwareVendorId (10)
        ushort pid, vid;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, ushort*, int>)Slot(controller, 9))(controller, &pid));
        Check(((delegate* unmanaged[Stdcall]<IntPtr, ushort*, int>)Slot(controller, 10))(controller, &vid));

        string id, name;
        var raw2 = QueryInterface(controller, IID_IRawGameController2);
        try
        {
            IntPtr h;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(raw2, 7))(raw2, &h)); // get_NonRoamableId
            id = TakeString(h);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(raw2, 8))(raw2, &h)); // get_DisplayName
            name = TakeString(h);
        }
        finally
        {
            Marshal.Release(raw2);
        }
        if (string.IsNullOrEmpty(id))
            return null;

        var wireless = false;
        var game = QueryInterface(controller, IID_IGameController);
        try
        {
            byte value;
            if (((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Slot(game, 13))(game, &value) >= 0) // get_IsWireless
                wireless = value != 0;
        }
        finally
        {
            Marshal.Release(game);
        }

        int? status = null, remaining = null, full = null;
        var battery = QueryInterface(controller, IID_IGameControllerBatteryInfo);
        try
        {
            IntPtr report;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(battery, 6))(battery, &report) >= 0 // TryGetBatteryReport
                && report != IntPtr.Zero)
            {
                try
                {
                    // IBatteryReport: FullCharge (8), Remaining (9), Status (10)
                    full = ReadNullableInt(report, 8);
                    remaining = ReadNullableInt(report, 9);
                    int s;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(report, 10))(report, &s) >= 0)
                        status = s;
                }
                finally
                {
                    Marshal.Release(report);
                }
            }
        }
        finally
        {
            Marshal.Release(battery);
        }

        return new XboxControllerReading(id, name, vid, pid, wireless, status, remaining, full);
    }

    private static int? ReadNullableInt(IntPtr report, int slot)
    {
        IntPtr reference;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(report, slot))(report, &reference) < 0 || reference == IntPtr.Zero)
            return null;
        try
        {
            var typed = QueryInterface(reference, IID_IReferenceInt32);
            try
            {
                int value;
                return ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(typed, 6))(typed, &value) >= 0 ? value : null; // get_Value
            }
            finally
            {
                Marshal.Release(typed);
            }
        }
        finally
        {
            Marshal.Release(reference);
        }
    }

    private static IntPtr Slot(IntPtr instance, int index) => (*(IntPtr**)instance)[index];

    private static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        Check(Marshal.QueryInterface(instance, in iid, out var result));
        return result;
    }

    private static IntPtr ActivationFactory(string className, Guid iid)
    {
        Check(WindowsCreateString(className, (uint)className.Length, out var hstring));
        try
        {
            Check(RoGetActivationFactory(hstring, in iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static string TakeString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero)
            return "";
        try
        {
            var buffer = WindowsGetStringRawBuffer(hstring, out var length);
            return new string((char*)buffer, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, uint length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);
}
