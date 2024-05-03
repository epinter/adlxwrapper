using System;
using System.Runtime.InteropServices;

internal static class AtiAdlxx
{
    private const string DllName = "atiadlxx.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Main_Control_Create(ADL_Main_Memory_AllocDelegate callback, int connectedAdapters, out IntPtr context);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, ref int numAdapters);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr adapterInfo, int size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Adapter_ID_Get(IntPtr context, int adapterIndex, out int adapterId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ADLStatus ADL2_Adapter_Active_Get(IntPtr context, int adapterIndex, out int status);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr hModule);

    public static ADL_Main_Memory_AllocDelegate Main_Memory_Alloc = Marshal.AllocHGlobal;
    public delegate IntPtr ADL_Main_Memory_AllocDelegate(int size);

    public static bool Method_Exists(string name)
    {
        IntPtr library = LoadLibrary(DllName);
        if (library != IntPtr.Zero)
        {
            bool exists = false;
            if (GetProcAddress(library, name) != IntPtr.Zero)
                exists = true;

            FreeLibrary(library);
            return exists;
        }

        return false;
    }

    public static ADLStatus ADL2_Main_Control_Create(IntPtr context, int enumConnectedAdapters)
    {
        if (Method_Exists(nameof(ADL2_Main_Control_Create)))
            return ADL2_Main_Control_Create(Main_Memory_Alloc, enumConnectedAdapters, out context);

        return ADLStatus.ADL_ERR;
    }

    public static ADLStatus ADL2_Adapter_AdapterInfo_Get(ref IntPtr context, ADLAdapterInfo[] info)
    {
        int elementSize = Marshal.SizeOf(typeof(ADLAdapterInfo));
        int size = info.Length * elementSize;
        IntPtr ptr = Marshal.AllocHGlobal(size);
        ADLStatus result = ADL2_Adapter_AdapterInfo_Get(context, ptr, size);
        for (int i = 0; i < info.Length; i++)
            info[i] = (ADLAdapterInfo)Marshal.PtrToStructure((IntPtr)((long)ptr + (i * elementSize)), typeof(ADLAdapterInfo));

        Marshal.FreeHGlobal(ptr);
        return result;
    }

    public const int ADL_MAX_PATH = 256;

    public enum ADLStatus
    {
        ADL_OK_WAIT = 4,
        ADL_OK_RESTART = 3,
        ADL_OK_MODE_CHANGE = 2,
        ADL_OK_WARNING = 1,
        ADL_OK = 0,
        ADL_ERR = -1,
        ADL_ERR_NOT_INIT = -2,
        ADL_ERR_INVALID_PARAM = -3,
        ADL_ERR_INVALID_PARAM_SIZE = -4,
        ADL_ERR_INVALID_ADL_IDX = -5,
        ADL_ERR_INVALID_CONTROLLER_IDX = -6,
        ADL_ERR_INVALID_DIPLAY_IDX = -7,
        ADL_ERR_NOT_SUPPORTED = -8,
        ADL_ERR_NULL_POINTER = -9,
        ADL_ERR_DISABLED_ADAPTER = -10,
        ADL_ERR_INVALID_CALLBACK = -11,
        ADL_ERR_RESOURCE_CONFLICT = -12,
        ADL_ERR_SET_INCOMPLETE = -20,
        ADL_ERR_NO_XDISPLAY = -21
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ADLAdapterInfo
    {
        public int Size;
        public int AdapterIndex;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string UDID;

        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string AdapterName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DisplayName;

        public int Present;
        public int Exist;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DriverPath;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string DriverPathExt;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)]
        public string PNPString;

        public int OSDisplayIndex;
    }

}
