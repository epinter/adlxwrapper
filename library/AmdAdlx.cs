//  Copyright (C) Emerson Pinter.

//  This Source Code Form is subject to the terms of the Mozilla Public
//  License, v. 2.0. If a copy of the MPL was not distributed with this
//  file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace ADLXWrapper;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

public static class AmdAdlx
{
    private const string DllName = "amdadlx64.dll";
    private static ILogger _logger = null;
    private static IntPtr adlxSystemStructPtr = IntPtr.Zero;
    private static IntPtr adlMappingStructPtr = IntPtr.Zero;
    private static IADLXSystem systemInstance;
    private static IADLMapping mappingInstance;
    private static ulong adlxFullVersion = 0;
    private static bool adlxInitialized = false;
    private static bool mappingAdl = false;

    private delegate void ADLX_ADL_Main_Memory_Free(ref IntPtr buffer);
    private static ADLX_ADL_Main_Memory_Free Main_Memory_Free_Delegate = Main_Memory_Free;

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ADLXResult ADLXInitializeWithCallerAdl(ref ulong adlxVersion, out IntPtr adlxSystem, out IntPtr adlMapping, IntPtr context, ADLX_ADL_Main_Memory_Free callback);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ADLXResult ADLXInitialize(ref ulong adlxVersion, out IntPtr adlxSystem);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ADLXResult ADLXQueryFullVersion(ref ulong fullVersion);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ADLXResult ADLXTerminate();

    public static void Initialize(ILogger logger = null)
    {
        _logger = logger;
        ADLXQueryFullVersion(ref adlxFullVersion);
        if (ADLXInitialize(ref adlxFullVersion, out adlxSystemStructPtr) == ADLXResult.ADLX_OK)
        {
            adlxInitialized = true;
        }
    }

    public static void InitializeFromAdl(IntPtr adlContext, ILogger logger = null)
    {
        _logger = logger;
        ADLXQueryFullVersion(ref adlxFullVersion);
        ADLXResult status;
        if ((status = ADLXInitializeWithCallerAdl(ref adlxFullVersion, out adlxSystemStructPtr, out adlMappingStructPtr, adlContext, Main_Memory_Free_Delegate)) == ADLXResult.ADLX_OK)
        {
            adlxInitialized = true;
            mappingAdl = true;
        }
        else
        {
            LogDebug("ADLX initialization status = {0}", status);
        }
    }

    public static ADLXResult Terminate()
    {
        adlxSystemStructPtr = IntPtr.Zero;
        adlMappingStructPtr = IntPtr.Zero;
        systemInstance = null;
        mappingInstance = null;
        adlxFullVersion = 0;
        adlxInitialized = false;
        mappingAdl = false;
        _logger = null;
        return ADLXTerminate();
    }

    public static bool IsAdlxInitialized()
    {
        return adlxInitialized;
    }

    public static bool IsMappingAdl()
    {
        return mappingAdl;
    }

    public static bool IsSucceeded(ADLXResult result)
    {
        return result == ADLXResult.ADLX_OK || result == ADLXResult.ADLX_ALREADY_ENABLED || result == ADLXResult.ADLX_ALREADY_INITIALIZED;
    }

    public static bool IsFailed(ADLXResult result)
    {
        return !IsSucceeded(result);
    }

    public static IADLXSystem GetSystemServices()
    {
        if (!adlxInitialized)
        {
            throw new InvalidOperationException("ADLX initialzation failed");
        }
        systemInstance ??= new ADLXSystem(adlxSystemStructPtr);
        return systemInstance;
    }

    public static IADLMapping GetAdlMapping()
    {
        if (!adlxInitialized || !mappingAdl)
        {
            throw new InvalidOperationException("ADLX initialzation failed");
        }
        mappingInstance ??= new ADLMapping(adlMappingStructPtr);
        return mappingInstance;
    }

    public static void Main_Memory_Free(ref IntPtr buffer)
    {
        LogDebug("----Main_Memory_Free: buffer-address={0}", buffer);
        if (buffer != IntPtr.Zero)
            Marshal.FreeHGlobal(buffer);
        LogDebug("----Main_Memory_Free: freed buffer-address={0}", buffer);
    }

    private static void GetVtblPointer<T>(IntPtr interfacePtr, out T vtblStruct) where T : struct
    {
        ADLXVtblPtr iadlxPtr = (ADLXVtblPtr)Marshal.PtrToStructure(interfacePtr, typeof(ADLXVtblPtr));
        vtblStruct = (T)Marshal.PtrToStructure(iadlxPtr.ptr, typeof(T));
    }

    private static void LogDebug(string message, params object[] args)
    {
#pragma warning disable CA2254 // Template should be a static expression
        _logger?.LogDebug(message, args);
#pragma warning restore CA2254 // Template should be a static expression
    }

    /*************************************************************************
       ADLMapping, class to map information between ADL and ADLX
    *************************************************************************/
    public interface IADLMapping
    {
        ADLXResult GetADLXGPUFromAdlAdapterIndex(int adlAdapterIndex, out IADLXGPU adlxGpu);
        GPU GetADLXGPUFromAdlAdapterIndex(int adlAdapterIndex);
    }

    private class ADLMapping : IADLMapping
    {
        private readonly IntPtr _ptr;
        private readonly ADLMappingVtbl vtbl;

        internal ADLMapping(IntPtr ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult GetADLXGPUFromAdlAdapterIndex(int adlAdapterIndex, out IADLXGPU adlxGpu)
        {
            ADLXResult status = vtbl.GetADLXGPUFromAdlAdapterIndex(_ptr, adlAdapterIndex, out IntPtr adlxGpuPtr);
            adlxGpu = new ADLXGPU(adlxGpuPtr);
            return status;
        }

        public GPU GetADLXGPUFromAdlAdapterIndex(int adlAdapterIndex)
        {
            ADLXResult status = GetADLXGPUFromAdlAdapterIndex(adlAdapterIndex, out IADLXGPU adlxGpu);
            GPU gpu = null;

            if (status == ADLXResult.ADLX_OK)
            {
                using (adlxGpu)
                {
                    gpu = new()
                    {
                        Name = adlxGpu.Name(),
                        VendorId = adlxGpu.VendorId(),
                        IsExternal = adlxGpu.IsExternal(),
                        DriverPath = adlxGpu.DriverPath(),
                        PNPString = adlxGpu.PNPString(),
                        HasDesktops = adlxGpu.HasDesktops(),
                        VRAMType = adlxGpu.VRAMType(),
                        Type = adlxGpu.Type(),
                        DeviceId = adlxGpu.DeviceId(),
                        RevisionId = adlxGpu.RevisionId(),
                        SubSystemId = adlxGpu.SubSystemId(),
                        SubSystemVendorId = adlxGpu.SubSystemVendorId(),
                        UniqueId = adlxGpu.UniqueId(),
                        BIOSInfo = adlxGpu.BIOSInfo(),
                        ASICFamilyType = adlxGpu.ASICFamilyType()
                    };
                }
            }
            else
                LogDebug("GetADLXGPUFromAdlAdapterIndex failed: status = {0}", status);

            return gpu;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLMappingVtbl
        {
            // //Gets the IADLXGPU object corresponding to a GPU with given bus, device and function number
            public IntPtr GetADLXGPUFromBdf;
            // //Gets the IADLXGPU object corresponding to a GPU with given ADL adapter index
            public ADLMappingDelegates.GetADLXGPUFromAdlAdapterIndex GetADLXGPUFromAdlAdapterIndex;
            // //Gets the bus, device and function number corresponding to the given IADLXGPU
            public IntPtr BdfFromADLXGPU;
            // //Gets the ADL Adapter index corresponding to the given IADLXGPU
            public IntPtr AdlAdapterIndexFromADLXGPU;
            // //Gets the display object corresponding to the give ADL ids
            public IntPtr GetADLXDisplayFromADLIds;
            // //Gets ADL ids corresponding to the display object
            public IntPtr ADLIdsFromADLXDisplay;
            // //Gets the desktop object corresponding to the give ADL ids
            public IntPtr GetADLXDesktopFromADLIds;
            // //Gets ADL ids corresponding to the desktop object
            public IntPtr ADLIdsFromADLXDesktop;
        }

        protected static class ADLMappingDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetADLXGPUFromAdlAdapterIndex(IntPtr adlMapping, int adlAdapterIndex, out IntPtr ppGPU);
        }
    }

    /*************************************************************************
       ADLXSystem, main interface of ADLX library
    *************************************************************************/
    public interface IADLXSystem
    {
        ADLXResult TotalSystemRAM(out uint ramMB);
        IADLXGPUList GetGPUs();
        IADLXPerformanceMonitoringServices GetPerformanceMonitoringServices();
        IADLXGPUTuningServices GetGPUTuningServices();
        IADLXGPU GetADLXGPUByUniqueId(int uniqueId);
        uint GetTotalSystemRAM();
        List<GPU> GetGPUList();
    }

    private class ADLXSystem : IADLXSystem
    {
        private readonly IntPtr _ptr;
        private readonly ADLXSystemVtbl vtbl;

        internal ADLXSystem(IntPtr ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public IADLXGPUList GetGPUs()
        {
            ADLXResult status = vtbl.GetGPUs(_ptr, out IntPtr gpuListPtr);
            return status == ADLXResult.ADLX_OK ? new ADLXGPUList(gpuListPtr) : null;
        }

        public IADLXGPU GetADLXGPUByUniqueId(int uniqueId)
        {
            using (IADLXGPUList adlxGPUList = GetSystemServices().GetGPUs())
            {
                for (int i = 0; i < adlxGPUList.Size(); i++)
                {
                    if (adlxGPUList.At_GPUList((uint)i, out IADLXGPU adlxGpu) == ADLXResult.ADLX_OK)
                    {
                        if (adlxGpu.UniqueId() == uniqueId)
                        {
                            LogDebug("__returning ADLXGPU pointer");
                            return adlxGpu;
                        }
                        else
                        {
                            adlxGpu.Dispose();
                        }
                    }
                }
            }

            return null;
        }

        public List<GPU> GetGPUList()
        {
            List<GPU> gpuList = [];

            using (IADLXGPUList adlxGPUList = GetGPUs())
            {
                for (int i = 0; i < adlxGPUList.Size(); i++)
                {
                    IADLXGPU adlxGpu;
                    if (adlxGPUList.At_GPUList((uint)i, out adlxGpu) == ADLXResult.ADLX_OK)
                    {
                        using (adlxGpu)
                        {
                            LogDebug(String.Format("GPU: Name='{0}'; VendorId='{1}'; IsExternal='{2}'; DriverPath='{3}'; PNPString='{4}'; HasDesktops='{5}'; VRAMType='{6}'; Type='{7}'; " +
                                            "DeviceId='{8}'; RevisionId='{9}'; SubSystemId='{10}'; SubSystemVendorId='{11}'; UniqueId='{12}'; BIOSInfo='{13}'; ASICFamilyType='{14}';",
                                adlxGpu.Name(),
                                adlxGpu.VendorId(),
                                adlxGpu.IsExternal(),
                                adlxGpu.DriverPath(),
                                adlxGpu.PNPString(),
                                adlxGpu.HasDesktops(),
                                adlxGpu.VRAMType(),
                                (GPUType)adlxGpu.Type(),
                                adlxGpu.DeviceId(),
                                adlxGpu.RevisionId(),
                                adlxGpu.SubSystemId(),
                                adlxGpu.SubSystemVendorId(),
                                adlxGpu.UniqueId(),
                                adlxGpu.BIOSInfo(),
                                (ASICFamilyType)adlxGpu.ASICFamilyType()
                            ));
                            GPU gpu = new()
                            {
                                Name = adlxGpu.Name(),
                                VendorId = adlxGpu.VendorId(),
                                IsExternal = adlxGpu.IsExternal(),
                                DriverPath = adlxGpu.DriverPath(),
                                PNPString = adlxGpu.PNPString(),
                                HasDesktops = adlxGpu.HasDesktops(),
                                VRAMType = adlxGpu.VRAMType(),
                                Type = adlxGpu.Type(),
                                DeviceId = adlxGpu.DeviceId(),
                                RevisionId = adlxGpu.RevisionId(),
                                SubSystemId = adlxGpu.SubSystemId(),
                                SubSystemVendorId = adlxGpu.SubSystemVendorId(),
                                UniqueId = adlxGpu.UniqueId(),
                                BIOSInfo = adlxGpu.BIOSInfo(),
                                ASICFamilyType = adlxGpu.ASICFamilyType()
                            };
                            gpuList.Add(gpu);
                        }
                    }
                }
            }

            return gpuList;
        }

        public uint GetTotalSystemRAM()
        {
            TotalSystemRAM(out uint ramMB);
            return ramMB;
        }

        public ADLXResult TotalSystemRAM(out uint ramMB)
        {
            return vtbl.TotalSystemRAM(_ptr, out ramMB);
        }

        public IADLXPerformanceMonitoringServices GetPerformanceMonitoringServices()
        {
            ADLXResult status = vtbl.GetPerformanceMonitoringServices(_ptr, out IntPtr performanceMonitoringServices);
            return status == ADLXResult.ADLX_OK ? new ADLXPerformanceMonitoringServices(performanceMonitoringServices) : null;
        }

        public IADLXGPUTuningServices GetGPUTuningServices()
        {
            ADLXResult status = vtbl.GetGPUTuningServices(_ptr, out IntPtr gpuTuningServices);
            return status == ADLXResult.ADLX_OK ? new ADLXGPUTuningServices(gpuTuningServices) : null;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXSystemVtbl
        {
            public IntPtr GetHybridGraphicsType;
            public ADLXSystemDelegates.GetGPUs GetGPUs;
            public ADLXSystemDelegates.QueryInterface QueryInterface;
            public IntPtr GetDisplaysServices;
            public IntPtr GetDesktopsServices;
            public IntPtr GetGPUsChangedHandling;
            public IntPtr EnableLog;
            public IntPtr Get3DSettingsServices;
            public ADLXSystemDelegates.GetGPUTuningServices GetGPUTuningServices;
            public ADLXSystemDelegates.GetPerformanceMonitoringServices GetPerformanceMonitoringServices;
            public ADLXSystemDelegates.TotalSystemRAM TotalSystemRAM;
            public IntPtr GetI2C;
        }

        protected static class ADLXSystemDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult TotalSystemRAM(IntPtr adlxSystem, out uint ramMB);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUs(IntPtr adlxSystem, out IntPtr gpus);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult QueryInterface(IntPtr adlxSystem, [MarshalAs(UnmanagedType.LPWStr)] string interfaceId, out IntPtr ppInterface);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetPerformanceMonitoringServices(IntPtr adlxSystem, out IntPtr gpus);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUTuningServices(IntPtr iadlxSystem, out IntPtr ppGPUTuningServices);
        }
    }

    /*************************************************************************
       Abstract class for lists
    *************************************************************************/
    public interface IADLXList : IADLXInterface
    {
        uint Size();
        bool Empty();
        uint Begin();
        uint End();
        ADLXResult At(uint location, out IADLXInterface ppItem);
        ADLXResult Clear();
        ADLXResult Remove_Back();
        ADLXResult Add_Back(IADLXInterface ppItem);
    }

    private abstract class ADLXList : ADLXInterface, IADLXList
    {
        private readonly IntPtr _ptr;
        private readonly ADLXListVtbl vtbl;

        protected ADLXList(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public uint Size()
        {
            return vtbl.Size(_ptr);
        }

        public bool Empty()
        {
            return vtbl.Empty(_ptr);
        }

        public uint Begin()
        {
            return vtbl.Begin(_ptr);
        }

        public uint End()
        {
            return vtbl.End(_ptr);
        }

        public ADLXResult At(uint location, out IADLXInterface ppItem)
        {
            ADLXResult status = vtbl.At(_ptr, location, out IntPtr ptr);

            if (status == ADLXResult.ADLX_OK && ptr != IntPtr.Zero)
            {
                ppItem = new ADLXInterface(ptr);
                return ADLXResult.ADLX_OK;
            }
            else
            {
                ppItem = null;
            }

            return status;
        }

        public ADLXResult Add_Back(IADLXInterface ppItem)
        {
            IntPtr ptrItem = ppItem.ToPointer();

            if (ptrItem != IntPtr.Zero)
            {
                return vtbl.Add_Back(_ptr, ptrItem);
            }

            return ADLXResult.ADLX_INVALID_ARGS;
        }

        public ADLXResult Clear()
        {
            return vtbl.Clear(_ptr);
        }

        public ADLXResult Remove_Back()
        {
            return vtbl.Remove_Back(_ptr);
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ICollections.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXListVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            //IADLXList
            public ADLXListDelegates.Size Size;
            public ADLXListDelegates.Empty Empty;
            public ADLXListDelegates.Begin Begin;
            public ADLXListDelegates.End End;
            public ADLXListDelegates.At At;
            public ADLXListDelegates.Clear Clear;
            public ADLXListDelegates.Remove_Back Remove_Back;
            public ADLXListDelegates.Add_Back Add_Back;
        }

        protected static class ADLXListDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate uint Size(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate bool Empty(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate uint Begin(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate uint End(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Clear(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Remove_Back(IntPtr adlxList);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult At(IntPtr adlxList, uint location, out IntPtr ppItem);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Add_Back(IntPtr adlxList, IntPtr ppItem);
        }
    }

    /*************************************************************************
       ADLXGPU
    *************************************************************************/
    public interface IADLXGPU : IADLXInterface
    {
        ADLXResult Name(out string name);
        ADLXResult TotalVRAM(out uint vramMB);
        ADLXResult VendorId(out string vendorId);
        ADLXResult IsExternal(out bool external);
        ADLXResult DriverPath(out string driverPath);
        ADLXResult PNPString(out string pnpString);
        ADLXResult HasDesktops(out bool hasDesktops);
        ADLXResult VRAMType(out string vramType);
        ADLXResult DeviceId(out string deviceId);
        ADLXResult RevisionId(out string revisionId);
        ADLXResult SubSystemId(out string subSystemId);
        ADLXResult SubSystemVendorId(out string subSystemVendorId);
        ADLXResult UniqueId(out int uniqueId);
        ADLXResult ASICFamilyType(out ASICFamilyType asicFamilyType);
        ADLXResult Type(out GPUType type);
        ADLXResult BIOSInfo(out string partNumber, out string version, out string date);
        string Name();
        uint TotalVRAM();
        string VendorId();
        ASICFamilyType ASICFamilyType();
        GPUType Type();
        BIOSInfo BIOSInfo();
        bool IsExternal();
        string DriverPath();
        string PNPString();
        bool HasDesktops();
        string VRAMType();
        string DeviceId();
        string RevisionId();
        string SubSystemId();
        string SubSystemVendorId();
        int UniqueId();
    }

    private class ADLXGPU : ADLXInterface, IADLXGPU
    {
        private readonly IntPtr _ptr;
        private readonly ADLXGPUVtbl vtbl;

        internal ADLXGPU(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult Name(out string name)
        {
            ADLXResult status = vtbl.Name(_ptr, out IntPtr namePtr);
            name = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string Name()
        {
            Name(out string name);
            return name;
        }

        public ADLXResult TotalVRAM(out uint vramMB)
        {
            ADLXResult status = vtbl.TotalVRAM(_ptr, out vramMB);
            return status;
        }

        public uint TotalVRAM()
        {
            TotalVRAM(out uint vramMB);
            return vramMB;
        }

        public ADLXResult VendorId(out string vendorId)
        {
            ADLXResult status = vtbl.VendorId(_ptr, out IntPtr namePtr);
            vendorId = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string VendorId()
        {
            VendorId(out string vendorId);
            return vendorId;
        }

        public ADLXResult IsExternal(out bool external)
        {
            return vtbl.IsExternal(_ptr, out external);
        }

        public bool IsExternal()
        {
            IsExternal(out bool external);
            return external;
        }

        public ADLXResult DriverPath(out string driverPath)
        {
            ADLXResult status = vtbl.DriverPath(_ptr, out IntPtr namePtr);
            driverPath = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string DriverPath()
        {
            DriverPath(out string driverPath);
            return driverPath;
        }

        public ADLXResult PNPString(out string pnpString)
        {
            ADLXResult status = vtbl.PNPString(_ptr, out IntPtr namePtr);
            pnpString = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string PNPString()
        {
            PNPString(out string pnpString);
            return pnpString;
        }

        public ADLXResult HasDesktops(out bool hasDesktops)
        {
            return vtbl.HasDesktops(_ptr, out hasDesktops);
        }

        public bool HasDesktops()
        {
            HasDesktops(out bool hasDesktops);
            return hasDesktops;
        }

        public ADLXResult VRAMType(out string vramType)
        {
            ADLXResult status = vtbl.VRAMType(_ptr, out IntPtr namePtr);
            vramType = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string VRAMType()
        {
            VRAMType(out string vramType);
            return vramType;
        }

        public ADLXResult DeviceId(out string deviceId)
        {
            ADLXResult status = vtbl.DeviceId(_ptr, out IntPtr namePtr);
            deviceId = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string DeviceId()
        {
            DeviceId(out string deviceId);
            return deviceId;
        }

        public ADLXResult RevisionId(out string revisionId)
        {
            ADLXResult status = vtbl.RevisionId(_ptr, out IntPtr namePtr);
            revisionId = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string RevisionId()
        {
            RevisionId(out string revisionId);
            return revisionId;
        }

        public ADLXResult SubSystemId(out string subSystemId)
        {
            ADLXResult status = vtbl.SubSystemId(_ptr, out IntPtr namePtr);
            subSystemId = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string SubSystemId()
        {
            SubSystemId(out string subSystemId);
            return subSystemId;
        }

        public ADLXResult SubSystemVendorId(out string subSystemVendorId)
        {
            ADLXResult status = vtbl.SubSystemVendorId(_ptr, out IntPtr namePtr);
            subSystemVendorId = Marshal.PtrToStringAnsi(namePtr);
            return status;
        }

        public string SubSystemVendorId()
        {
            SubSystemVendorId(out string subSystemVendorId);
            return subSystemVendorId;
        }

        public ADLXResult UniqueId(out int uniqueId)
        {
            ADLXResult status = vtbl.UniqueId(_ptr, out uniqueId);
            return status;
        }

        public int UniqueId()
        {
            UniqueId(out int uniqueId);
            return uniqueId;
        }

        public ADLXResult Type(out GPUType type)
        {
            ADLXResult status = vtbl.Type(_ptr, out int iType);
            type = (GPUType)iType;
            return status;
        }

        public GPUType Type()
        {
            Type(out GPUType type);
            return type;
        }

        public ADLXResult ASICFamilyType(out ASICFamilyType asicFamilyType)
        {
            ADLXResult status = vtbl.ASICFamilyType(_ptr, out int iAsicType);
            asicFamilyType = (ASICFamilyType)iAsicType;
            return status;
        }

        public ASICFamilyType ASICFamilyType()
        {
            ASICFamilyType(out ASICFamilyType type);
            return type;
        }

        public ADLXResult BIOSInfo(out string partNumber, out string version, out string date)
        {
            IntPtr ptrPartNumber = IntPtr.Zero;
            IntPtr ptrVersion = IntPtr.Zero;
            IntPtr ptrDate = IntPtr.Zero;

            ADLXResult status = vtbl.BIOSInfo(_ptr, out ptrPartNumber, out ptrVersion, out ptrDate);
            partNumber = Marshal.PtrToStringAnsi(ptrPartNumber);
            version = Marshal.PtrToStringAnsi(ptrVersion);
            date = Marshal.PtrToStringAnsi(ptrDate);

            return status;
        }

        public BIOSInfo BIOSInfo()
        {
            BIOSInfo biosInfo = new();
            BIOSInfo(out biosInfo.partNumber, out biosInfo.version, out biosInfo.date);
            return biosInfo;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXGPU release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXGPU released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXGPUVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            // //IADLXGPU
            public ADLXGPUDelegates.VendorId VendorId;
            public ADLXGPUDelegates.ASICFamilyType ASICFamilyType;
            public ADLXGPUDelegates.Type Type;
            public ADLXGPUDelegates.IsExternal IsExternal;
            public ADLXGPUDelegates.Name Name;
            public ADLXGPUDelegates.DriverPath DriverPath;
            public ADLXGPUDelegates.PNPString PNPString;
            public ADLXGPUDelegates.HasDesktops HasDesktops;
            public ADLXGPUDelegates.TotalVRAM TotalVRAM;
            public ADLXGPUDelegates.VRAMType VRAMType;
            public ADLXGPUDelegates.BIOSInfo BIOSInfo;
            public ADLXGPUDelegates.DeviceId DeviceId;
            public ADLXGPUDelegates.RevisionId RevisionId;
            public ADLXGPUDelegates.SubSystemId SubSystemId;
            public ADLXGPUDelegates.SubSystemVendorId SubSystemVendorId;
            public ADLXGPUDelegates.UniqueId UniqueId;
        }

        protected static class ADLXGPUDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Name(IntPtr adlxGpu, out IntPtr name);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult TotalVRAM(IntPtr adlxGpu, out uint vramMB);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult VendorId(IntPtr adlxGpu, out IntPtr vendorId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsExternal(IntPtr adlxGpu, out bool external);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult DriverPath(IntPtr adlxGpu, out IntPtr driverPath);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult PNPString(IntPtr adlxGpu, out IntPtr pnpString);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult HasDesktops(IntPtr adlxGpu, out bool hasDesktops);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult VRAMType(IntPtr adlxGpu, out IntPtr vramType);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult DeviceId(IntPtr adlxGpu, out IntPtr deviceId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult RevisionId(IntPtr adlxGpu, out IntPtr revisionId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SubSystemId(IntPtr adlxGpu, out IntPtr subSystemId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SubSystemVendorId(IntPtr adlxGpu, out IntPtr subSystemVendorId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult UniqueId(IntPtr adlxGpu, out int uniqueId);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult ASICFamilyType(IntPtr adlxGpu, out int asicFamilyType);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Type(IntPtr adlxGpu, out int type);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult BIOSInfo(IntPtr adlxGpu, out IntPtr partNumber, out IntPtr version, out IntPtr date);
        }
    }

    /*************************************************************************
        ADLXGPUList
    *************************************************************************/
    public interface IADLXGPUList : IADLXList
    {
        ADLXResult At_GPUList(uint location, out IADLXGPU ppItem);
        ADLXResult Add_Back_GPUList(IADLXGPU ppItem);
    }

    private class ADLXGPUList : ADLXList, IADLXGPUList
    {
        private readonly IntPtr _ptr;
        private readonly ADLXGPUListVtbl vtbl;

        internal ADLXGPUList(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult At_GPUList(uint location, out IADLXGPU ppItem)
        {
            ADLXResult status = vtbl.At_GPUList(_ptr, location, out IntPtr ptr);

            if (status == ADLXResult.ADLX_OK && ptr != IntPtr.Zero)
            {
                ppItem = new ADLXGPU(ptr);
                return ADLXResult.ADLX_OK;
            }
            else
            {
                ppItem = null;
            }

            return status;
        }

        public ADLXResult Add_Back_GPUList(IADLXGPU ppItem)
        {
            IntPtr ptrGpu = ppItem.ToPointer();

            if (ptrGpu != IntPtr.Zero)
            {
                return vtbl.Add_Back_GPUList(_ptr, ptrGpu);
            }

            return ADLXResult.ADLX_INVALID_ARGS;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXGPUList release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxList.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXGPUList released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXGPUListVtbl
        {
            public ADLXListVtbl adlxList;

            public ADLXGPUListDelegates.At_GPUList At_GPUList;
            public ADLXGPUListDelegates.Add_Back_GPUList Add_Back_GPUList;
        }

        protected static class ADLXGPUListDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult At_GPUList(IntPtr adlxGpuList, uint location, out IntPtr ppItem);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Add_Back_GPUList(IntPtr adlxGpuList, IntPtr pItem);
        }
    }

    /*************************************************************************
        ADLXPerformanceMonitoringServices
    *************************************************************************/
    public interface IADLXPerformanceMonitoringServices : IADLXInterface
    {
        ADLXResult GetSamplingInterval(out int intervalMs);
        ADLXResult SetSamplingInterval(int intervalMs);
        ADLXResult GetSupportedGPUMetrics(IADLXGPU adlxGpu, out IADLXGPUMetricsSupport ppMetricsSupported);
        ADLXResult GetCurrentGPUMetrics(IADLXGPU adlxGpu, out IADLXGPUMetrics ppMetrics);
        ADLXResult GetCurrentFPS(out IADLXFPS ppMetrics);
        ADLXResult GetSamplingIntervalRange(out ADLX_IntRange range);
        int GetSamplingInterval();
        IADLXGPUMetricsSupport GetSupportedGPUMetrics(IADLXGPU gpadlxGpuu);
        IADLXGPUMetrics GetCurrentGPUMetrics(IADLXGPU adlxGpu);
        IADLXFPS GetCurrentFPS();
        ADLX_IntRange GetSamplingIntervalRange();
        int CurrentFPS();
        SupportedGPUMetrics GetSupportedGPUMetricsForUniqueId(int uniqueId);
        GPUMetrics GetCurrentGPUMetricsForUniqueId(int uniqueId);
    }

    private class ADLXPerformanceMonitoringServices : ADLXInterface, IADLXPerformanceMonitoringServices
    {
        private readonly IntPtr _ptr;
        private readonly ADLXPerformanceMonitoringServicesVtbl vtbl;
        internal ADLXPerformanceMonitoringServices(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public int CurrentFPS()
        {
            int fps = -1;

            using (IADLXFPS adlxFps = GetCurrentFPS())
            {
                if (adlxFps != null)
                    fps = adlxFps.FPS();
            }

            return fps;
        }

        public GPUMetrics GetCurrentGPUMetricsForUniqueId(int uniqueId)
        {
            GPUMetrics gpuMetrics = [];

            using (IADLXGPUList adlxGPUList = GetSystemServices().GetGPUs())
            {
                for (int i = 0; i < adlxGPUList.Size(); i++)
                {
                    IADLXGPU adlxGpu;
                    if (adlxGPUList.At_GPUList((uint)i, out adlxGpu) == ADLXResult.ADLX_OK)
                    {
                        using (adlxGpu)
                        {
                            if (adlxGpu.UniqueId() == uniqueId)
                            {
                                using (IADLXGPUMetricsSupport adlxMetrixSupport = GetSupportedGPUMetrics(adlxGpu))
                                {
                                    using (IADLXGPUMetrics adlxGPUMetrics = GetCurrentGPUMetrics(adlxGpu))
                                    {
                                        gpuMetrics.TimeStamp = adlxGPUMetrics.TimeStamp();
                                        gpuMetrics.Add(Metric.MetricType.GPUUsage, adlxMetrixSupport.IsSupportedGPUUsage(), adlxGPUMetrics.GPUUsage(), Metric.DataType.Double, adlxMetrixSupport.GetGPUUsageRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUClockSpeed, adlxMetrixSupport.IsSupportedGPUClockSpeed(), adlxGPUMetrics.GPUClockSpeed(), Metric.DataType.Integer, adlxMetrixSupport.GetGPUClockSpeedRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUVRAMClockSpeed, adlxMetrixSupport.IsSupportedGPUVRAMClockSpeed(), adlxGPUMetrics.GPUVRAMClockSpeed(), Metric.DataType.Integer, adlxMetrixSupport.GetGPUVRAMClockSpeedRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUTemperature, adlxMetrixSupport.IsSupportedGPUTemperature(), adlxGPUMetrics.GPUTemperature(), Metric.DataType.Double, adlxMetrixSupport.GetGPUTemperatureRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUHotspotTemperature, adlxMetrixSupport.IsSupportedGPUHotspotTemperature(), adlxGPUMetrics.GPUHotspotTemperature(), Metric.DataType.Double, adlxMetrixSupport.GetGPUHotspotTemperatureRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUPower, adlxMetrixSupport.IsSupportedGPUPower(), adlxGPUMetrics.GPUPower(), Metric.DataType.Double, adlxMetrixSupport.GetGPUPowerRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUTotalBoardPower, adlxMetrixSupport.IsSupportedGPUTotalBoardPower(), adlxGPUMetrics.GPUTotalBoardPower(), Metric.DataType.Double, adlxMetrixSupport.GetGPUTotalBoardPowerRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUFanSpeed, adlxMetrixSupport.IsSupportedGPUFanSpeed(), adlxGPUMetrics.GPUFanSpeed(), Metric.DataType.Integer, adlxMetrixSupport.GetGPUFanSpeedRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUVRAM, adlxMetrixSupport.IsSupportedGPUVRAM(), adlxGPUMetrics.GPUVRAM(), Metric.DataType.Integer, adlxMetrixSupport.GetGPUVRAMRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUVoltage, adlxMetrixSupport.IsSupportedGPUVoltage(), adlxGPUMetrics.GPUVoltage(), Metric.DataType.Integer, adlxMetrixSupport.GetGPUVoltageRange());
                                        gpuMetrics.Add(Metric.MetricType.GPUIntakeTemperature, adlxMetrixSupport.IsSupportedGPUIntakeTemperature(), adlxGPUMetrics.GPUIntakeTemperature(), Metric.DataType.Double, adlxMetrixSupport.GetGPUIntakeTemperatureRange());
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return gpuMetrics;
        }

        public SupportedGPUMetrics GetSupportedGPUMetricsForUniqueId(int uniqueId)
        {
            SupportedGPUMetrics supported = new();

            using (IADLXGPUList adlxGPUList = GetSystemServices().GetGPUs())
            {
                for (int i = 0; i < adlxGPUList.Size(); i++)
                {
                    IADLXGPU adlxGpu;
                    if (adlxGPUList.At_GPUList((uint)i, out adlxGpu) == ADLXResult.ADLX_OK)
                    {
                        using (adlxGpu)
                        {
                            if (adlxGpu.UniqueId() == uniqueId)
                            {
                                using (IADLXGPUMetricsSupport adlxMetrixSupport = GetSupportedGPUMetrics(adlxGpu))
                                {
                                    if (adlxMetrixSupport.IsSupportedGPUUsage())
                                    {
                                        supported.IsSupportedGPUUsage = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUClockSpeed())
                                    {
                                        supported.IsSupportedGPUClockSpeed = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUVRAMClockSpeed())
                                    {
                                        supported.IsSupportedGPUVRAMClockSpeed = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUTemperature())
                                    {
                                        supported.IsSupportedGPUTemperature = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUHotspotTemperature())
                                    {
                                        supported.IsSupportedGPUHotspotTemperature = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUPower())
                                    {
                                        supported.IsSupportedGPUPower = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUTotalBoardPower())
                                    {
                                        supported.IsSupportedGPUTotalBoardPower = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUFanSpeed())
                                    {
                                        supported.IsSupportedGPUFanSpeed = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUVRAM())
                                    {
                                        supported.IsSupportedGPUVRAM = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUVoltage())
                                    {
                                        supported.IsSupportedGPUVoltage = true;
                                    }

                                    if (adlxMetrixSupport.IsSupportedGPUIntakeTemperature())
                                    {
                                        supported.IsSupportedGPUIntakeTemperature = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return supported;
        }

        public ADLXResult GetCurrentFPS(out IADLXFPS adlxFps)
        {
            ADLXResult status = vtbl.GetCurrentFPS(_ptr, out IntPtr adlxFpsPtr);
            adlxFps = new ADLXFPS(adlxFpsPtr);
            return status;
        }

        public IADLXFPS GetCurrentFPS()
        {
            return GetCurrentFPS(out IADLXFPS adlxFps) == ADLXResult.ADLX_OK ? adlxFps : null;
        }

        public ADLXResult GetSupportedGPUMetrics(IADLXGPU adlxGpu, out IADLXGPUMetricsSupport ppMetricsSupported)
        {
            ADLXResult status = vtbl.GetSupportedGPUMetrics(_ptr, adlxGpu.ToPointer(), out IntPtr ppMetricsSupportedPtr);
            ppMetricsSupported = new ADLXGPUMetricsSupport(ppMetricsSupportedPtr);
            return status;
        }

        public IADLXGPUMetricsSupport GetSupportedGPUMetrics(IADLXGPU adlxGpu)
        {
            return GetSupportedGPUMetrics(adlxGpu, out IADLXGPUMetricsSupport ppMetricsSupported) == ADLXResult.ADLX_OK ? ppMetricsSupported : null;
        }

        public ADLXResult GetCurrentGPUMetrics(IADLXGPU adlxGpu, out IADLXGPUMetrics gpuMetrics)
        {
            ADLXResult status = vtbl.GetCurrentGPUMetrics(_ptr, adlxGpu.ToPointer(), out IntPtr gpuMetricsPtr);
            gpuMetrics = new ADLXGPUMetrics(gpuMetricsPtr);
            return status;
        }

        public IADLXGPUMetrics GetCurrentGPUMetrics(IADLXGPU adlxGpu)
        {
            return GetCurrentGPUMetrics(adlxGpu, out IADLXGPUMetrics gpuMetrics) == ADLXResult.ADLX_OK ? gpuMetrics : null;
        }

        public ADLXResult GetSamplingIntervalRange(out ADLX_IntRange range)
        {
            return vtbl.GetSamplingIntervalRange(_ptr, out range);

        }

        public ADLX_IntRange GetSamplingIntervalRange()
        {
            GetSamplingIntervalRange(out ADLX_IntRange range);
            return range;
        }

        public ADLXResult GetSamplingInterval(out int intervalMs)
        {
            return vtbl.GetSamplingInterval(_ptr, out intervalMs);
        }

        public int GetSamplingInterval()
        {
            GetSamplingInterval(out int intervalMs);
            return intervalMs;
        }

        public ADLXResult SetSamplingInterval(int intervalMs)
        {
            return vtbl.SetSamplingInterval(_ptr, intervalMs);
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXPerformanceMonitoringServices release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXPerformanceMonitoringServices released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXPerformanceMonitoringServicesVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXPerformanceMonitoringServicesDelegates.GetSamplingIntervalRange GetSamplingIntervalRange;
            public ADLXPerformanceMonitoringServicesDelegates.SetSamplingInterval SetSamplingInterval;
            public ADLXPerformanceMonitoringServicesDelegates.GetSamplingInterval GetSamplingInterval;
            public IntPtr GetMaxPerformanceMetricsHistorySizeRange;
            public IntPtr SetMaxPerformanceMetricsHistorySize;
            public IntPtr GetMaxPerformanceMetricsHistorySize;
            public IntPtr ClearPerformanceMetricsHistory;
            public IntPtr GetCurrentPerformanceMetricsHistorySize;
            public IntPtr StartPerformanceMetricsTracking;
            public IntPtr StopPerformanceMetricsTracking;
            public IntPtr GetAllMetricsHistory;
            public IntPtr GetGPUMetricsHistory;
            public IntPtr GetSystemMetricsHistory;
            public IntPtr GetFPSHistory;
            public IntPtr GetCurrentAllMetrics;
            public ADLXPerformanceMonitoringServicesDelegates.GetCurrentGPUMetrics GetCurrentGPUMetrics;
            public IntPtr GetCurrentSystemMetrics;
            public ADLXPerformanceMonitoringServicesDelegates.GetCurrentFPS GetCurrentFPS;
            public ADLXPerformanceMonitoringServicesDelegates.GetSupportedGPUMetrics GetSupportedGPUMetrics;
            public ADLXPerformanceMonitoringServicesDelegates.GetSupportedSystemMetrics GetSupportedSystemMetrics;
        }

        protected static class ADLXPerformanceMonitoringServicesDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetSupportedSystemMetrics(IntPtr adlxPerfMonServ, out IntPtr ppMetricsSupported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetSupportedGPUMetrics(IntPtr adlxPerfMonServ, IntPtr adlxGpu, out IntPtr ppMetricsSupported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetCurrentGPUMetrics(IntPtr adlxPerfMonServ, IntPtr adlxGpu, out IntPtr ppMetrics);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetCurrentFPS(IntPtr adlxPerfMonServ, out IntPtr ppFps);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetSamplingIntervalRange(IntPtr adlxPerfMonServ, out ADLX_IntRange range);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetSamplingInterval(IntPtr adlxPerfMonServ, out int intervalMs);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetSamplingInterval(IntPtr adlxPerfMonServ, int intervalMs);
        }
    }

    /*************************************************************************
        ADLXGPUMetricsSupport
    *************************************************************************/
    public interface IADLXGPUMetricsSupport : IADLXInterface
    {
        bool IsSupportedGPUUsage();
        bool IsSupportedGPUClockSpeed();
        bool IsSupportedGPUVRAMClockSpeed();
        bool IsSupportedGPUTemperature();
        bool IsSupportedGPUHotspotTemperature();
        bool IsSupportedGPUPower();
        bool IsSupportedGPUTotalBoardPower();
        bool IsSupportedGPUFanSpeed();
        bool IsSupportedGPUVRAM();
        bool IsSupportedGPUVoltage();
        bool IsSupportedGPUIntakeTemperature();
        ADLX_IntRange GetGPUUsageRange();
        ADLX_IntRange GetGPUClockSpeedRange();
        ADLX_IntRange GetGPUVRAMClockSpeedRange();
        ADLX_IntRange GetGPUTemperatureRange();
        ADLX_IntRange GetGPUHotspotTemperatureRange();
        ADLX_IntRange GetGPUPowerRange();
        ADLX_IntRange GetGPUFanSpeedRange();
        ADLX_IntRange GetGPUVRAMRange();
        ADLX_IntRange GetGPUVoltageRange();
        ADLX_IntRange GetGPUTotalBoardPowerRange();
        ADLX_IntRange GetGPUIntakeTemperatureRange();
        ADLXResult IsSupportedGPUUsage(out bool supported);
        ADLXResult IsSupportedGPUClockSpeed(out bool supported);
        ADLXResult IsSupportedGPUVRAMClockSpeed(out bool supported);
        ADLXResult IsSupportedGPUTemperature(out bool supported);
        ADLXResult IsSupportedGPUHotspotTemperature(out bool supported);
        ADLXResult IsSupportedGPUPower(out bool supported);
        ADLXResult IsSupportedGPUTotalBoardPower(out bool supported);
        ADLXResult IsSupportedGPUFanSpeed(out bool supported);
        ADLXResult IsSupportedGPUVRAM(out bool supported);
        ADLXResult IsSupportedGPUVoltage(out bool supported);
        ADLXResult IsSupportedGPUIntakeTemperature(out bool supported);
        ADLXResult GetGPUUsageRange(out int minValue, out int maxValue);
        ADLXResult GetGPUClockSpeedRange(out int minValue, out int maxValue);
        ADLXResult GetGPUVRAMClockSpeedRange(out int minValue, out int maxValue);
        ADLXResult GetGPUTemperatureRange(out int minValue, out int maxValue);
        ADLXResult GetGPUHotspotTemperatureRange(out int minValue, out int maxValue);
        ADLXResult GetGPUPowerRange(out int minValue, out int maxValue);
        ADLXResult GetGPUFanSpeedRange(out int minValue, out int maxValue);
        ADLXResult GetGPUVRAMRange(out int minValue, out int maxValue);
        ADLXResult GetGPUVoltageRange(out int minValue, out int maxValue);
        ADLXResult GetGPUTotalBoardPowerRange(out int minValue, out int maxValue);
        ADLXResult GetGPUIntakeTemperatureRange(out int minValue, out int maxValue);
    }

    private class ADLXGPUMetricsSupport : ADLXInterface, IADLXGPUMetricsSupport
    {
        private readonly IntPtr _ptr;
        private readonly ADLXGPUMetricsSupportVtbl vtbl;

        internal ADLXGPUMetricsSupport(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult IsSupportedGPUUsage(out bool supported)
        {
            return vtbl.IsSupportedGPUUsage(_ptr, out supported);
        }

        public bool IsSupportedGPUUsage()
        {
            IsSupportedGPUUsage(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUClockSpeed(out bool supported)
        {
            return vtbl.IsSupportedGPUClockSpeed(_ptr, out supported);
        }

        public bool IsSupportedGPUClockSpeed()
        {
            IsSupportedGPUClockSpeed(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUVRAMClockSpeed(out bool supported)
        {
            return vtbl.IsSupportedGPUVRAMClockSpeed(_ptr, out supported);
        }

        public bool IsSupportedGPUVRAMClockSpeed()
        {
            IsSupportedGPUVRAMClockSpeed(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUTemperature(out bool supported)
        {
            return vtbl.IsSupportedGPUTemperature(_ptr, out supported);
        }

        public bool IsSupportedGPUTemperature()
        {
            IsSupportedGPUTemperature(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUHotspotTemperature(out bool supported)
        {
            return vtbl.IsSupportedGPUHotspotTemperature(_ptr, out supported);
        }

        public bool IsSupportedGPUHotspotTemperature()
        {
            IsSupportedGPUHotspotTemperature(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUPower(out bool supported)
        {
            return vtbl.IsSupportedGPUPower(_ptr, out supported);
        }

        public bool IsSupportedGPUPower()
        {
            IsSupportedGPUPower(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUTotalBoardPower(out bool supported)
        {
            return vtbl.IsSupportedGPUTotalBoardPower(_ptr, out supported);
        }

        public bool IsSupportedGPUTotalBoardPower()
        {
            IsSupportedGPUTotalBoardPower(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUFanSpeed(out bool supported)
        {
            return vtbl.IsSupportedGPUFanSpeed(_ptr, out supported);
        }

        public bool IsSupportedGPUFanSpeed()
        {
            IsSupportedGPUFanSpeed(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUVRAM(out bool supported)
        {
            return vtbl.IsSupportedGPUVRAM(_ptr, out supported);
        }

        public bool IsSupportedGPUVRAM()
        {
            IsSupportedGPUVRAM(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUVoltage(out bool supported)
        {
            return vtbl.IsSupportedGPUVoltage(_ptr, out supported);
        }

        public bool IsSupportedGPUVoltage()
        {
            IsSupportedGPUVoltage(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedGPUIntakeTemperature(out bool supported)
        {
            return vtbl.IsSupportedGPUIntakeTemperature(_ptr, out supported);
        }

        public bool IsSupportedGPUIntakeTemperature()
        {
            IsSupportedGPUIntakeTemperature(out bool supported);
            return supported;
        }

        public ADLXResult GetGPUUsageRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUUsageRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUUsageRange()
        {
            GetGPUUsageRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = 0, maxValue = max };
        }

        public ADLXResult GetGPUClockSpeedRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUClockSpeedRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUClockSpeedRange()
        {
            GetGPUClockSpeedRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUVRAMClockSpeedRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUVRAMClockSpeedRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUVRAMClockSpeedRange()
        {
            GetGPUVRAMClockSpeedRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUTemperatureRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUTemperatureRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUTemperatureRange()
        {
            GetGPUTemperatureRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUHotspotTemperatureRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUHotspotTemperatureRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUHotspotTemperatureRange()
        {
            GetGPUHotspotTemperatureRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUPowerRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUPowerRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUPowerRange()
        {
            GetGPUPowerRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUFanSpeedRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUFanSpeedRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUFanSpeedRange()
        {
            GetGPUFanSpeedRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUVRAMRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUVRAMRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUVRAMRange()
        {
            GetGPUVRAMRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUVoltageRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUVoltageRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUVoltageRange()
        {
            GetGPUVoltageRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUTotalBoardPowerRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUTotalBoardPowerRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUTotalBoardPowerRange()
        {
            GetGPUTotalBoardPowerRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public ADLXResult GetGPUIntakeTemperatureRange(out int minValue, out int maxValue)
        {
            return vtbl.GetGPUIntakeTemperatureRange(_ptr, out minValue, out maxValue);
        }

        public ADLX_IntRange GetGPUIntakeTemperatureRange()
        {
            GetGPUIntakeTemperatureRange(out int min, out int max);
            return new ADLX_IntRange() { minValue = min, maxValue = max };
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXGPUMetricsSupport release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXGPUMetricsSupport released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXGPUMetricsSupportVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUUsage IsSupportedGPUUsage;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUClockSpeed IsSupportedGPUClockSpeed;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUVRAMClockSpeed IsSupportedGPUVRAMClockSpeed;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUTemperature IsSupportedGPUTemperature;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUHotspotTemperature IsSupportedGPUHotspotTemperature;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUPower IsSupportedGPUPower;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUTotalBoardPower IsSupportedGPUTotalBoardPower;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUFanSpeed IsSupportedGPUFanSpeed;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUVRAM IsSupportedGPUVRAM;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUVoltage IsSupportedGPUVoltage;
            public ADLXGPUMetricsSupportDelegates.GetGPUUsageRange GetGPUUsageRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUClockSpeedRange GetGPUClockSpeedRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUVRAMClockSpeedRange GetGPUVRAMClockSpeedRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUTemperatureRange GetGPUTemperatureRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUHotspotTemperatureRange GetGPUHotspotTemperatureRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUPowerRange GetGPUPowerRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUFanSpeedRange GetGPUFanSpeedRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUVRAMRange GetGPUVRAMRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUVoltageRange GetGPUVoltageRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUTotalBoardPowerRange GetGPUTotalBoardPowerRange;
            public ADLXGPUMetricsSupportDelegates.GetGPUIntakeTemperatureRange GetGPUIntakeTemperatureRange;
            public ADLXGPUMetricsSupportDelegates.IsSupportedGPUIntakeTemperature IsSupportedGPUIntakeTemperature;
        }

        protected static class ADLXGPUMetricsSupportDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUUsage(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUClockSpeed(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUVRAMClockSpeed(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUTemperature(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUHotspotTemperature(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUPower(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUTotalBoardPower(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUFanSpeed(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUVRAM(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUVoltage(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedGPUIntakeTemperature(IntPtr adlxSupMetric, out bool supported);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUUsageRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUClockSpeedRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUVRAMClockSpeedRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUTemperatureRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUHotspotTemperatureRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUPowerRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUFanSpeedRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUVRAMRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUVoltageRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUTotalBoardPowerRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUIntakeTemperatureRange(IntPtr adlxSupMetric, out int minValue, out int maxValue);
        }
    }

    /*************************************************************************
        ADLXGPUMetrics
    *************************************************************************/
    public interface IADLXGPUMetrics : IADLXInterface
    {
        ADLXResult TimeStamp(out long timestamp);
        ADLXResult GPUUsage(out double data);
        ADLXResult GPUClockSpeed(out int data);
        ADLXResult GPUVRAMClockSpeed(out int data);
        ADLXResult GPUTemperature(out double data);
        ADLXResult GPUHotspotTemperature(out double data);
        ADLXResult GPUPower(out double data);
        ADLXResult GPUTotalBoardPower(out double data);
        ADLXResult GPUFanSpeed(out int data);
        ADLXResult GPUVRAM(out int data);
        ADLXResult GPUVoltage(out int data);
        ADLXResult GPUIntakeTemperature(out double data);
        long TimeStamp();
        double GPUUsage();
        int GPUClockSpeed();
        int GPUVRAMClockSpeed();
        double GPUTemperature();
        double GPUHotspotTemperature();
        double GPUPower();
        double GPUTotalBoardPower();
        int GPUFanSpeed();
        int GPUVRAM();
        int GPUVoltage();
        double GPUIntakeTemperature();
    }

    private class ADLXGPUMetrics : ADLXInterface, IADLXGPUMetrics
    {
        private readonly IntPtr _ptr;
        private readonly ADLXGPUMetricsVtbl vtbl;

        internal ADLXGPUMetrics(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult TimeStamp(out long timestamp)
        {
            return vtbl.TimeStamp(_ptr, out timestamp);
        }

        public long TimeStamp()
        {
            TimeStamp(out long timestamp);
            return timestamp;
        }

        public ADLXResult GPUUsage(out double data)
        {
            return vtbl.GPUUsage(_ptr, out data);
        }

        public double GPUUsage()
        {
            GPUUsage(out double data);
            return data;
        }

        public ADLXResult GPUClockSpeed(out int data)
        {
            return vtbl.GPUClockSpeed(_ptr, out data);
        }

        public int GPUClockSpeed()
        {
            GPUClockSpeed(out int data);
            return data;
        }

        public ADLXResult GPUVRAMClockSpeed(out int data)
        {
            return vtbl.GPUVRAMClockSpeed(_ptr, out data);
        }

        public int GPUVRAMClockSpeed()
        {
            GPUVRAMClockSpeed(out int data);
            return data;
        }

        public ADLXResult GPUTemperature(out double data)
        {
            return vtbl.GPUTemperature(_ptr, out data);
        }

        public double GPUTemperature()
        {
            GPUTemperature(out double data);
            return data;
        }

        public ADLXResult GPUHotspotTemperature(out double data)
        {
            return vtbl.GPUHotspotTemperature(_ptr, out data);
        }

        public double GPUHotspotTemperature()
        {
            GPUHotspotTemperature(out double data);
            return data;
        }

        public ADLXResult GPUPower(out double data)
        {
            return vtbl.GPUPower(_ptr, out data);
        }

        public double GPUPower()
        {
            GPUPower(out double data);
            return data;
        }

        public ADLXResult GPUTotalBoardPower(out double data)
        {
            return vtbl.GPUTotalBoardPower(_ptr, out data);
        }

        public double GPUTotalBoardPower()
        {
            GPUTotalBoardPower(out double data);
            return data;
        }

        public ADLXResult GPUFanSpeed(out int data)
        {
            return vtbl.GPUFanSpeed(_ptr, out data);
        }

        public int GPUFanSpeed()
        {
            GPUFanSpeed(out int data);
            return data;
        }

        public ADLXResult GPUVRAM(out int data)
        {
            return vtbl.GPUVRAM(_ptr, out data);
        }

        public int GPUVRAM()
        {
            GPUVRAM(out int data);
            return data;
        }

        public ADLXResult GPUVoltage(out int data)
        {
            return vtbl.GPUVoltage(_ptr, out data);
        }

        public int GPUVoltage()
        {
            GPUVoltage(out int data);
            return data;
        }

        public ADLXResult GPUIntakeTemperature(out double data)
        {
            return vtbl.GPUIntakeTemperature(_ptr, out data);
        }

        public double GPUIntakeTemperature()
        {
            GPUIntakeTemperature(out double data);
            return data;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXGPUMetrics release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXGPUMetrics released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXGPUMetricsVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXGPUMetricsDelegates.TimeStamp TimeStamp;
            public ADLXGPUMetricsDelegates.GPUUsage GPUUsage;
            public ADLXGPUMetricsDelegates.GPUClockSpeed GPUClockSpeed;
            public ADLXGPUMetricsDelegates.GPUVRAMClockSpeed GPUVRAMClockSpeed;
            public ADLXGPUMetricsDelegates.GPUTemperature GPUTemperature;
            public ADLXGPUMetricsDelegates.GPUHotspotTemperature GPUHotspotTemperature;
            public ADLXGPUMetricsDelegates.GPUPower GPUPower;
            public ADLXGPUMetricsDelegates.GPUTotalBoardPower GPUTotalBoardPower;
            public ADLXGPUMetricsDelegates.GPUFanSpeed GPUFanSpeed;
            public ADLXGPUMetricsDelegates.GPUVRAM GPUVRAM;
            public ADLXGPUMetricsDelegates.GPUVoltage GPUVoltage;
            public ADLXGPUMetricsDelegates.GPUIntakeTemperature GPUIntakeTemperature;
        }

        protected static class ADLXGPUMetricsDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult TimeStamp(IntPtr adlxGpuMetric, out long timestamp);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUUsage(IntPtr adlxGpuMetric, out double data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUClockSpeed(IntPtr adlxGpuMetric, out int data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUVRAMClockSpeed(IntPtr adlxGpuMetric, out int data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUTemperature(IntPtr adlxGpuMetric, out double data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUHotspotTemperature(IntPtr adlxGpuMetric, out double data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUPower(IntPtr adlxGpuMetric, out double data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUTotalBoardPower(IntPtr adlxGpuMetric, out double data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUFanSpeed(IntPtr adlxGpuMetric, out int data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUVRAM(IntPtr adlxGpuMetric, out int data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUVoltage(IntPtr adlxGpuMetric, out int data);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GPUIntakeTemperature(IntPtr adlxGpuMetric, out double data);
        }
    }

    /*************************************************************************
        ADLXFPS
    *************************************************************************/
    public interface IADLXFPS : IADLXInterface
    {
        ADLXResult TimeStamp(out long timestamp);
        ADLXResult FPS(out int data);
        long TimeStamp();
        int FPS();
    }

    private class ADLXFPS : ADLXInterface, IADLXFPS
    {
        private readonly IntPtr _ptr;
        private readonly ADLXFPSVtbl vtbl;

        internal ADLXFPS(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult TimeStamp(out long timestamp)
        {
            return vtbl.TimeStamp(_ptr, out timestamp);
        }

        public long TimeStamp()
        {

            TimeStamp(out long timestamp);
            return timestamp;
        }

        public ADLXResult FPS(out int data)
        {
            return vtbl.FPS(_ptr, out data);
        }

        public int FPS()
        {
            int data = -1;
            FPS(out data);
            return data;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXFPS release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXFPS released");

            return release;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXFPSVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXFPSDelegates.TimeStamp TimeStamp;
            public ADLXFPSDelegates.FPS FPS;
        }

        protected static class ADLXFPSDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult TimeStamp(IntPtr adlxFps, out long timestamp);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult FPS(IntPtr adlxFps, out int data);
        }
    }

    /*************************************************************************
        IADLXGPUTuningServices
    *************************************************************************/
    public interface IADLXGPUTuningServices : IADLXInterface
    {
        ADLXResult IsAtFactory(IADLXGPU adlxGpu, out bool isFactory);
        ADLXResult ResetToFactory(IADLXGPU adlxGpu);
        ADLXResult IsSupportedAutoTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult IsSupportedPresetTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult IsSupportedManualGFXTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult IsSupportedManualVRAMTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult IsSupportedManualFanTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult IsSupportedManualPowerTuning(IADLXGPU adlxGpu, out bool supported);
        ADLXResult GetManualFanTuning(IADLXGPU adlxGpu, out IADLXManualFanTuning ppManualFanTuning);
        bool IsSupportedManualFanTuning(int gpuUniqueId);
        IADLXManualFanTuning GetManualFanTuning(IADLXGPU adlxGpu);
        bool IsSupportedAutoTuning(int gpuUniqueId);
        bool IsSupportedPresetTuning(int gpuUniqueId);
        bool IsSupportedManualGFXTuning(int gpuUniqueId);
        bool IsSupportedManualVRAMTuning(int gpuUniqueId);
        bool IsSupportedManualPowerTuning(int gpuUniqueId);

    }

    private class ADLXGPUTuningServices : ADLXInterface, IADLXGPUTuningServices
    {
        private readonly IntPtr _ptr;
        private readonly ADLXGPUTuningServicesVtbl vtbl;

        internal ADLXGPUTuningServices(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult GetManualFanTuning(IADLXGPU adlxGpu, out IADLXManualFanTuning ppManualFanTuning)
        {
            ADLXResult status = vtbl.GetManualFanTuning(_ptr, adlxGpu.ToPointer(), out IntPtr manualFanTuningPtr);
            ppManualFanTuning = new ADLXManualFanTuning(manualFanTuningPtr);
            return status;
        }

        public IADLXManualFanTuning GetManualFanTuning(IADLXGPU adlxGpu)
        {
            return GetManualFanTuning(adlxGpu, out IADLXManualFanTuning ppManualFanTuning) == ADLXResult.ADLX_OK ? ppManualFanTuning : null;
        }

        public ADLXResult IsSupportedManualFanTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedManualFanTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public bool IsSupportedManualFanTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedManualFanTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public ADLXResult IsSupportedAutoTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedAutoTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public ADLXResult IsSupportedPresetTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedPresetTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public ADLXResult IsSupportedManualGFXTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedManualGFXTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public ADLXResult IsSupportedManualVRAMTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedManualVRAMTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public ADLXResult IsSupportedManualPowerTuning(IADLXGPU adlxGpu, out bool supported)
        {
            return vtbl.IsSupportedManualPowerTuning(_ptr, adlxGpu.ToPointer(), out supported);
        }

        public bool IsSupportedAutoTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedAutoTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public bool IsSupportedPresetTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedPresetTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public bool IsSupportedManualGFXTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedManualGFXTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public bool IsSupportedManualVRAMTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedManualVRAMTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public bool IsSupportedManualPowerTuning(int gpuUniqueId)
        {
            using (IADLXGPU adlxGpu = systemInstance.GetADLXGPUByUniqueId(gpuUniqueId))
            {
                return IsSupportedManualVRAMTuning(adlxGpu, out bool supported) == ADLXResult.ADLX_OK ? supported : false;
            }
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXGPUTuningServices release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXGPUTuningServices released");

            return release;
        }

        public ADLXResult IsAtFactory(IADLXGPU adlxGpu, out bool isFactory)
        {
            throw new NotImplementedException();
        }

        public ADLXResult ResetToFactory(IADLXGPU adlxGpu)
        {
            throw new NotImplementedException();
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXGPUTuningServicesVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXGPUTuningServicesDelegates.GetGPUTuningChangedHandling GetGPUTuningChangedHandling;
            public ADLXGPUTuningServicesDelegates.IsAtFactory IsAtFactory;
            public ADLXGPUTuningServicesDelegates.ResetToFactory ResetToFactory;
            public ADLXGPUTuningServicesDelegates.IsSupportedAutoTuning IsSupportedAutoTuning;
            public ADLXGPUTuningServicesDelegates.IsSupportedPresetTuning IsSupportedPresetTuning;
            public ADLXGPUTuningServicesDelegates.IsSupportedManualGFXTuning IsSupportedManualGFXTuning;
            public ADLXGPUTuningServicesDelegates.IsSupportedManualVRAMTuning IsSupportedManualVRAMTuning;
            public ADLXGPUTuningServicesDelegates.IsSupportedManualFanTuning IsSupportedManualFanTuning;
            public ADLXGPUTuningServicesDelegates.IsSupportedManualPowerTuning IsSupportedManualPowerTuning;
            public ADLXGPUTuningServicesDelegates.GetAutoTuning GetAutoTuning;
            public ADLXGPUTuningServicesDelegates.GetPresetTuning GetPresetTuning;
            public ADLXGPUTuningServicesDelegates.GetManualGFXTuning GetManualGFXTuning;
            public ADLXGPUTuningServicesDelegates.GetManualVRAMTuning GetManualVRAMTuning;
            public ADLXGPUTuningServicesDelegates.GetManualFanTuning GetManualFanTuning;
            public ADLXGPUTuningServicesDelegates.GetManualPowerTuning GetManualPowerTuning;
        }

        protected static class ADLXGPUTuningServicesDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetGPUTuningChangedHandling(IntPtr iadlxGPUTuningServices, out IntPtr ppGPUTuningChangedHandling);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsAtFactory(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool isFactory);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult ResetToFactory(IntPtr iadlxGPUTuningServices, IntPtr pGPU);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedAutoTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedPresetTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedManualGFXTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedManualVRAMTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedManualFanTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedManualPowerTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetAutoTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppAutoTuning);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetPresetTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppPresetTuning);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetManualGFXTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppManualGFXTuning);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetManualVRAMTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppManualVRAMTuning);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetManualFanTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppManualFanTuning);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetManualPowerTuning(IntPtr iadlxGPUTuningServices, IntPtr pGPU, out IntPtr ppManualPowerTuning);
        }
    }

    /*************************************************************************
        IADLXManualFanTuning
    *************************************************************************/
    public interface IADLXManualFanTuning : IADLXInterface
    {
        ADLXResult GetFanTuningRanges(out ADLX_IntRange speedRange, out ADLX_IntRange temperatureRange);
        ADLXResult GetFanTuningStates(out IADLXManualFanTuningStateList ppStates);
        ADLXResult GetEmptyFanTuningStates(out IADLXManualFanTuningStateList ppStates);
        ADLXResult IsValidFanTuningStates(IADLXManualFanTuningStateList pStates, out int errorIndex);
        ADLXResult SetFanTuningStates(IADLXManualFanTuningStateList pStates);
        ADLXResult IsSupportedZeroRPM(out bool supported);
        ADLXResult GetZeroRPMState(out bool isSet);
        ADLXResult SetZeroRPMState(bool set);
        ADLXResult IsSupportedMinAcousticLimit(out bool supported);
        ADLXResult GetMinAcousticLimitRange(out ADLX_IntRange tuningRange);
        ADLXResult GetMinAcousticLimit(out int value);
        ADLXResult SetMinAcousticLimit(int value);
        ADLXResult IsSupportedMinFanSpeed(out bool supported);
        ADLXResult GetMinFanSpeedRange(out ADLX_IntRange tuningRange);
        ADLXResult GetMinFanSpeed(out int value);
        ADLXResult SetMinFanSpeed(int value);
        ADLXResult IsSupportedTargetFanSpeed(out bool supported);
        ADLXResult GetTargetFanSpeedRange(out ADLX_IntRange tuningRange);
        ADLXResult GetTargetFanSpeed(out int value);
        ADLXResult SetTargetFanSpeed(int value);

        IADLXManualFanTuningStateList GetFanTuningStates();
        IADLXManualFanTuningStateList GetEmptyFanTuningStates();
        int IsValidFanTuningStates(IADLXManualFanTuningStateList pStates);
        bool IsSupportedZeroRPM();
        bool IsSupportedMinAcousticLimit();
        bool IsSupportedMinFanSpeed();
        bool IsSupportedTargetFanSpeed();
        bool GetZeroRPMState();
        int GetMinAcousticLimit();
        int GetMinFanSpeed();
        int GetTargetFanSpeed();
        ADLX_IntRange GetMinAcousticLimitRange();
        ADLX_IntRange GetMinFanSpeedRange();
        ADLX_IntRange GetTargetFanSpeedRange();

    }

    private class ADLXManualFanTuning : ADLXInterface, IADLXManualFanTuning
    {
        private readonly IntPtr _ptr;
        private readonly ADLXManualFanTuningVtbl vtbl;

        internal ADLXManualFanTuning(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult GetEmptyFanTuningStates(out IADLXManualFanTuningStateList ppStates)
        {
            ADLXResult status = vtbl.GetEmptyFanTuningStates(_ptr, out IntPtr ppStatesPtr);
            ppStates = new ADLXManualFanTuningStateList(ppStatesPtr);
            return status;
        }

        public IADLXManualFanTuningStateList GetEmptyFanTuningStates()
        {
            GetEmptyFanTuningStates(out IADLXManualFanTuningStateList ppStates);
            return ppStates;
        }

        public ADLXResult GetFanTuningStates(out IADLXManualFanTuningStateList ppStates)
        {
            ADLXResult status = vtbl.GetFanTuningStates(_ptr, out IntPtr ppStatesPtr);
            ppStates = new ADLXManualFanTuningStateList(ppStatesPtr);
            return status;
        }

        public IADLXManualFanTuningStateList GetFanTuningStates()
        {
            GetFanTuningStates(out IADLXManualFanTuningStateList ppStates);
            return ppStates;
        }

        public ADLXResult GetFanTuningRanges(out ADLX_IntRange speedRange, out ADLX_IntRange temperatureRange)
        {
            return vtbl.GetFanTuningRanges(_ptr, out speedRange, out temperatureRange);
        }

        public ADLXResult GetMinAcousticLimit(out int value)
        {
            return vtbl.GetMinAcousticLimit(_ptr, out value);
        }

        public ADLXResult GetMinAcousticLimitRange(out ADLX_IntRange tuningRange)
        {
            return vtbl.GetMinAcousticLimitRange(_ptr, out tuningRange);
        }

        public ADLXResult GetMinFanSpeed(out int value)
        {
            return vtbl.GetMinFanSpeed(_ptr, out value);
        }

        public ADLXResult GetMinFanSpeedRange(out ADLX_IntRange tuningRange)
        {
            return vtbl.GetMinFanSpeedRange(_ptr, out tuningRange);
        }

        public ADLXResult GetTargetFanSpeed(out int value)
        {
            return vtbl.GetTargetFanSpeed(_ptr, out value);
        }

        public ADLXResult GetTargetFanSpeedRange(out ADLX_IntRange tuningRange)
        {
            return vtbl.GetTargetFanSpeedRange(_ptr, out tuningRange);
        }

        public ADLXResult IsSupportedZeroRPM(out bool supported)
        {
            return vtbl.IsSupportedZeroRPM(_ptr, out supported);
        }

        public bool IsSupportedZeroRPM()
        {
            IsSupportedZeroRPM(out bool supported);
            return supported;
        }

        public ADLXResult GetZeroRPMState(out bool isSet)
        {
            return vtbl.GetZeroRPMState(_ptr, out isSet);
        }

        public bool GetZeroRPMState()
        {
            GetZeroRPMState(out bool isSet);
            return isSet;
        }

        public ADLXResult IsSupportedMinAcousticLimit(out bool supported)
        {
            return vtbl.IsSupportedMinAcousticLimit(_ptr, out supported);
        }

        public bool IsSupportedMinAcousticLimit()
        {
            IsSupportedMinAcousticLimit(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedMinFanSpeed(out bool supported)
        {
            return vtbl.IsSupportedMinFanSpeed(_ptr, out supported);
        }

        public bool IsSupportedMinFanSpeed()
        {
            IsSupportedMinFanSpeed(out bool supported);
            return supported;
        }

        public ADLXResult IsSupportedTargetFanSpeed(out bool supported)
        {
            return vtbl.IsSupportedTargetFanSpeed(_ptr, out supported);
        }

        public bool IsSupportedTargetFanSpeed()
        {
            IsSupportedTargetFanSpeed(out bool supported);
            return supported;
        }

        public ADLXResult IsValidFanTuningStates(IADLXManualFanTuningStateList pStates, out int errorIndex)
        {
            return vtbl.IsValidFanTuningStates(_ptr, pStates.ToPointer(), out errorIndex);
        }

        public int IsValidFanTuningStates(IADLXManualFanTuningStateList pStates)
        {
            IsValidFanTuningStates(pStates, out int errorIndex);
            return errorIndex;
        }

        public ADLXResult SetFanTuningStates(IADLXManualFanTuningStateList pStates)
        {
            return vtbl.SetFanTuningStates(_ptr, pStates.ToPointer());
        }

        public ADLXResult SetMinAcousticLimit(int value)
        {
            return vtbl.SetMinAcousticLimit(_ptr, value);
        }

        public ADLXResult SetMinFanSpeed(int value)
        {
            return vtbl.SetMinFanSpeed(_ptr, value);
        }

        public ADLXResult SetTargetFanSpeed(int value)
        {
            return vtbl.SetTargetFanSpeed(_ptr, value);
        }

        public ADLXResult SetZeroRPMState(bool set)
        {
            return vtbl.SetZeroRPMState(_ptr, set);
        }

        public int GetMinAcousticLimit()
        {
            GetMinAcousticLimit(out int value);
            return value;
        }

        public int GetMinFanSpeed()
        {
            GetMinFanSpeed(out int value);
            return value;
        }

        public int GetTargetFanSpeed()
        {
            GetTargetFanSpeed(out int value);
            return value;
        }

        public ADLX_IntRange GetMinAcousticLimitRange()
        {
            GetMinAcousticLimitRange(out ADLX_IntRange range);
            return range;
        }

        public ADLX_IntRange GetMinFanSpeedRange()
        {
            GetMinFanSpeedRange(out ADLX_IntRange range);
            return range;
        }

        public ADLX_IntRange GetTargetFanSpeedRange()
        {
            GetTargetFanSpeedRange(out ADLX_IntRange range);
            return range;
        }


        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXManualFanTuning release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXManualFanTuning released");

            return release;
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXManualFanTuningVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXManualFanTuningDelegates.GetFanTuningRanges GetFanTuningRanges;
            public ADLXManualFanTuningDelegates.GetFanTuningStates GetFanTuningStates;
            public ADLXManualFanTuningDelegates.GetEmptyFanTuningStates GetEmptyFanTuningStates;
            public ADLXManualFanTuningDelegates.IsValidFanTuningStates IsValidFanTuningStates;
            public ADLXManualFanTuningDelegates.SetFanTuningStates SetFanTuningStates;
            public ADLXManualFanTuningDelegates.IsSupportedZeroRPM IsSupportedZeroRPM;
            public ADLXManualFanTuningDelegates.GetZeroRPMState GetZeroRPMState;
            public ADLXManualFanTuningDelegates.SetZeroRPMState SetZeroRPMState;
            public ADLXManualFanTuningDelegates.IsSupportedMinAcousticLimit IsSupportedMinAcousticLimit;
            public ADLXManualFanTuningDelegates.GetMinAcousticLimitRange GetMinAcousticLimitRange;
            public ADLXManualFanTuningDelegates.GetMinAcousticLimit GetMinAcousticLimit;
            public ADLXManualFanTuningDelegates.SetMinAcousticLimit SetMinAcousticLimit;
            public ADLXManualFanTuningDelegates.IsSupportedMinFanSpeed IsSupportedMinFanSpeed;
            public ADLXManualFanTuningDelegates.GetMinFanSpeedRange GetMinFanSpeedRange;
            public ADLXManualFanTuningDelegates.GetMinFanSpeed GetMinFanSpeed;
            public ADLXManualFanTuningDelegates.SetMinFanSpeed SetMinFanSpeed;
            public ADLXManualFanTuningDelegates.IsSupportedTargetFanSpeed IsSupportedTargetFanSpeed;
            public ADLXManualFanTuningDelegates.GetTargetFanSpeedRange GetTargetFanSpeedRange;
            public ADLXManualFanTuningDelegates.GetTargetFanSpeed GetTargetFanSpeed;
            public ADLXManualFanTuningDelegates.SetTargetFanSpeed SetTargetFanSpeed;
        }

        protected static class ADLXManualFanTuningDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetFanTuningRanges(IntPtr iadlxManualFanTuning, out ADLX_IntRange speedRange, out ADLX_IntRange temperatureRange);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetFanTuningStates(IntPtr iadlxManualFanTuning, out IntPtr ppStates);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetEmptyFanTuningStates(IntPtr iadlxManualFanTuning, out IntPtr ppStates);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsValidFanTuningStates(IntPtr iadlxManualFanTuning, IntPtr pStates, out int errorIndex);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetFanTuningStates(IntPtr iadlxManualFanTuning, IntPtr pStates);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedZeroRPM(IntPtr iadlxManualFanTuning, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetZeroRPMState(IntPtr iadlxManualFanTuning, out bool isSet);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetZeroRPMState(IntPtr iadlxManualFanTuning, bool set);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedMinAcousticLimit(IntPtr iadlxManualFanTuning, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetMinAcousticLimitRange(IntPtr iadlxManualFanTuning, out ADLX_IntRange tuningRange);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetMinAcousticLimit(IntPtr iadlxManualFanTuning, out int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetMinAcousticLimit(IntPtr iadlxManualFanTuning, int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedMinFanSpeed(IntPtr iadlxManualFanTuning, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetMinFanSpeedRange(IntPtr iadlxManualFanTuning, out ADLX_IntRange tuningRange);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetMinFanSpeed(IntPtr iadlxManualFanTuning, out int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetMinFanSpeed(IntPtr iadlxManualFanTuning, int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult IsSupportedTargetFanSpeed(IntPtr iadlxManualFanTuning, out bool supported);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetTargetFanSpeedRange(IntPtr iadlxManualFanTuning, out ADLX_IntRange tuningRange);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetTargetFanSpeed(IntPtr iadlxManualFanTuning, out int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetTargetFanSpeed(IntPtr iadlxManualFanTuning, int value);
        }
    }

    /*************************************************************************
        IADLXManualFanTuningStateList
    *************************************************************************/
    public interface IADLXManualFanTuningStateList : IADLXList
    {
        ADLXResult At_ManualFanTuningStateList(uint location, out IADLXManualFanTuningState ppItem);
        ADLXResult Add_Back_ManualFanTuningStateList(IADLXManualFanTuningState ppItem);
    }
    private class ADLXManualFanTuningStateList : ADLXList, IADLXManualFanTuningStateList
    {
        private readonly IntPtr _ptr;
        private readonly ADLXManualFanTuningStateListVtbl vtbl;

        internal ADLXManualFanTuningStateList(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult At_ManualFanTuningStateList(uint location, out IADLXManualFanTuningState ppItem)
        {
            ADLXResult status = vtbl.At_ManualFanTuningStateList(_ptr, location, out IntPtr ptr);

            if (status == ADLXResult.ADLX_OK && ptr != IntPtr.Zero)
            {
                ppItem = new ADLXManualFanTuningState(ptr);
                return ADLXResult.ADLX_OK;
            }
            else
            {
                ppItem = null;
            }

            return status;
        }

        public ADLXResult Add_Back_ManualFanTuningStateList(IADLXManualFanTuningState ppItem)
        {
            IntPtr ptrGpu = ppItem.ToPointer();

            if (ptrGpu != IntPtr.Zero)
            {
                return vtbl.Add_Back_ManualFanTuningStateList(_ptr, ptrGpu);
            }

            return ADLXResult.ADLX_INVALID_ARGS;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXManualFanTuningStateList release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxList.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXManualFanTuningStateList released");

            return release;
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXManualFanTuningStateListVtbl
        {
            public ADLXListVtbl adlxList;

            public ADLXManualFanTuningStateListDelegates.At_ManualFanTuningStateList At_ManualFanTuningStateList;
            public ADLXManualFanTuningStateListDelegates.Add_Back_ManualFanTuningStateList Add_Back_ManualFanTuningStateList;
        }
        protected static class ADLXManualFanTuningStateListDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult At_ManualFanTuningStateList(IntPtr iadlxManualFanTuningStateList, uint location, out IntPtr ppItem);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult Add_Back_ManualFanTuningStateList(IntPtr iadlxManualFanTuningStateList, IntPtr pItem);
        }
    }

    /*************************************************************************
        IADLXManualFanTuningState
    *************************************************************************/
    public interface IADLXManualFanTuningState : IADLXInterface
    {
        ADLXResult GetFanSpeed(out int fanSpeed);
        ADLXResult SetFanSpeed(int fanSpeed);
        ADLXResult GetTemperature(out int temperature);
        ADLXResult SetTemperature(int temperature);
        int GetFanSpeed();
        int GetTemperature();

    }
    private class ADLXManualFanTuningState : ADLXInterface, IADLXManualFanTuningState
    {
        private readonly IntPtr _ptr;
        private readonly ADLXManualFanTuningStateVtbl vtbl;

        internal ADLXManualFanTuningState(IntPtr ptr) : base(ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public ADLXResult GetFanSpeed(out int fanSpeed)
        {
            return vtbl.GetFanSpeed(_ptr, out fanSpeed);
        }

        public int GetFanSpeed()
        {
            GetFanSpeed(out int fanSpeed);
            return fanSpeed;
        }

        public ADLXResult GetTemperature(out int temperature)
        {
            return vtbl.GetTemperature(_ptr, out temperature);
        }

        public int GetTemperature()
        {
            GetTemperature(out int temperature);
            return temperature;
        }

        public ADLXResult SetFanSpeed(int fanSpeed)
        {
            return vtbl.SetFanSpeed(_ptr, fanSpeed);
        }

        public ADLXResult SetTemperature(int temperature)
        {
            return vtbl.SetTemperature(_ptr, temperature);
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override long Release()
        {
            LogDebug("+ADLXManualFanTuningState release started");
            long release = 0;
            if (_ptr != IntPtr.Zero)
            {
                release = vtbl.adlxInterface.Release(_ptr);
            }
            LogDebug("+ADLXManualFanTuningState released");

            return release;
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXManualFanTuningStateVtbl
        {
            public ADLXInterfaceVtbl adlxInterface;

            public ADLXManualFanTuningStateDelegates.GetFanSpeed GetFanSpeed;
            public ADLXManualFanTuningStateDelegates.SetFanSpeed SetFanSpeed;
            public ADLXManualFanTuningStateDelegates.GetTemperature GetTemperature;
            public ADLXManualFanTuningStateDelegates.SetTemperature SetTemperature;
        }

        protected static class ADLXManualFanTuningStateDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetFanSpeed(IntPtr iadlxManualFanTuningState, out int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetFanSpeed(IntPtr iadlxManualFanTuningState, int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetTemperature(IntPtr iadlxManualFanTuningState, out int value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult SetTemperature(IntPtr iadlxManualFanTuningState, int value);
        }
    }

    /*************************************************************************
        Base class for all ADLX interfaces
    *************************************************************************/
    public interface IADLXInterface : IDisposable
    {
        IntPtr ToPointer();
        long Acquire();
        long Release();
        ADLXResult QueryInterface(IntPtr pThis, string interfaceId, out IntPtr ppInterface);
    }

    protected class ADLXInterface : IADLXInterface
    {
        private readonly IntPtr _ptr;
        private readonly ADLXInterfaceVtbl vtbl;

        internal ADLXInterface(IntPtr ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
        }

        public IntPtr ToPointer()
        {
            return _ptr;
        }

        public virtual long Acquire()
        {
            throw new NotSupportedException("Acquire is not supported.");
        }

        public virtual long Release()
        {
            throw new NotImplementedException();
        }

        public ADLXResult QueryInterface(IntPtr pThis, string interfaceId, out IntPtr ppInterface)
        {
            return vtbl.QueryInterface(pThis, interfaceId, out ppInterface);
        }

        public void Dispose()
        {
            LogDebug("-ADLXInterface Dispose started");
            Release();
            LogDebug("-ADLXInterface Dispose finished");
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ADLXDefines.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXInterfaceVtbl
        {
            public ADLXInterfaceDelegates.Acquire Acquire;
            public ADLXInterfaceDelegates.Release Release;
            public ADLXInterfaceDelegates.QueryInterface QueryInterface;
        }

        protected static class ADLXInterfaceDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate long Acquire(IntPtr interfacePtr);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate long Release(IntPtr interfacePtr);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult QueryInterface(IntPtr interfacePtr, [MarshalAs(UnmanagedType.LPWStr)] string interfaceId, out IntPtr ppInterface);

        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLX_IntRange
    {
        public int minValue;
        public int maxValue;
        public int step;

        public override string ToString()
        {
            return "(minValue='" + minValue + "';maxValue='" + maxValue + "';step='" + step + "')";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    protected struct ADLXVtblPtr
    {
        public IntPtr ptr;
    }

    public enum ADLXResult
    {
        ADLX_OK = 0,                    /**< @ENG_START_DOX This result indicates success. @ENG_END_DOX */
        ADLX_ALREADY_ENABLED,           /**< @ENG_START_DOX This result indicates that the asked action is already enabled. @ENG_END_DOX */
        ADLX_ALREADY_INITIALIZED,       /**< @ENG_START_DOX This result indicates that ADLX has a unspecified type of initialization. @ENG_END_DOX */
        ADLX_FAIL,                      /**< @ENG_START_DOX This result indicates an unspecified failure. @ENG_END_DOX */
        ADLX_INVALID_ARGS,              /**< @ENG_START_DOX This result indicates that the arguments are invalid. @ENG_END_DOX */
        ADLX_BAD_VER,                   /**< @ENG_START_DOX This result indicates that the asked version is incompatible with the current version. @ENG_END_DOX */
        ADLX_UNKNOWN_INTERFACE,         /**< @ENG_START_DOX This result indicates that an unknown interface was asked. @ENG_END_DOX */
        ADLX_TERMINATED,                /**< @ENG_START_DOX This result indicates that the calls were made in an interface after ADLX was terminated. @ENG_END_DOX */
        ADLX_ADL_INIT_ERROR,            /**< @ENG_START_DOX This result indicates that the ADL initialization failed. @ENG_END_DOX */
        ADLX_NOT_FOUND,                 /**< @ENG_START_DOX This result indicates that the item is not found. @ENG_END_DOX */
        ADLX_INVALID_OBJECT,            /**< @ENG_START_DOX This result indicates that the method was called into an invalid object. @ENG_END_DOX */
        ADLX_ORPHAN_OBJECTS,            /**< @ENG_START_DOX This result indicates that ADLX was terminated with outstanding ADLX objects. Any interface obtained from ADLX points to invalid memory and calls in their methods will result in unexpected behavior. @ENG_END_DOX */
        ADLX_NOT_SUPPORTED,             /**< @ENG_START_DOX This result indicates that the asked feature is not supported. @ENG_END_DOX */
        ADLX_PENDING_OPERATION,         /**< @ENG_START_DOX This result indicates a failure due to an operation currently in progress. @ENG_END_DOX */
        ADLX_GPU_INACTIVE               /**< @ENG_START_DOX This result indicates that the GPU is inactive. @ENG_END_DOX */
    };

    public enum ASICFamilyType
    {
        ASIC_UNDEFINED = 0,             /**< @ENG_START_DOX The ASIC family type is not defined. @ENG_END_DOX */
        ASIC_RADEON,                    /**< @ENG_START_DOX The ASIC family type is discrete. @ENG_END_DOX */
        ASIC_FIREPRO,                   /**< @ENG_START_DOX The ASIC family type is Firepro. @ENG_END_DOX */
        ASIC_FIREMV,                    /**< @ENG_START_DOX The ASIC family type is FireMV. @ENG_END_DOX */
        ASIC_FIRESTREAM,                /**< @ENG_START_DOX The ASIC family type is FireStream. @ENG_END_DOX */
        ASIC_FUSION,                    /**< @ENG_START_DOX The ASIC family type is Fusion. @ENG_END_DOX */
        ASIC_EMBEDDED,                  /**< @ENG_START_DOX The ASIC family type is Embedded. @ENG_END_DOX */
    }

    public enum GPUType
    {
        GPUTYPE_UNDEFINED = 0,          /**< @ENG_START_DOX The GPU type is unknown. @ENG_END_DOX */
        GPUTYPE_INTEGRATED,             /**< @ENG_START_DOX The GPU type is an integrated GPU. @ENG_END_DOX */
        GPUTYPE_DISCRETE,               /**< @ENG_START_DOX The GPU type is a discrete GPU. @ENG_END_DOX */
    }

    public struct BIOSInfo
    {
        public string partNumber;
        public string version;
        public string date;

        public override string ToString()
        {
            return "(partNumber='" + partNumber + "';version='" + version + "';date='" + date + "')";
        }
    }

    /**************************************************************************/
    /*** Models ***/
    /**************************************************************************/
    public class GPU
    {
        public string Name { get; set; }
        public string VendorId { get; set; }
        public string DriverPath { get; set; }
        public string PNPString { get; set; }
        public string VRAMType { get; set; }
        public string DeviceId { get; set; }
        public string RevisionId { get; set; }
        public string SubSystemId { get; set; }
        public string SubSystemVendorId { get; set; }
        public int UniqueId { get; set; }
        public ASICFamilyType ASICFamilyType { get; set; }
        public GPUType Type { get; set; }
        public BIOSInfo BIOSInfo { get; set; }
        public bool IsExternal { get; set; }
        public bool HasDesktops { get; set; }

        public override string ToString()
        {
            return "Name='" + Name + "'; " +
                    "VendorId='" + VendorId + "'; " +
                    "DriverPath='" + DriverPath + "'; " +
                    "PNPString='" + PNPString + "'; " +
                    "VRAMType='" + VRAMType + "'; " +
                    "DeviceId='" + DeviceId + "'; " +
                    "RevisionId='" + RevisionId + "'; " +
                    "SubSystemId='" + SubSystemId + "'; " +
                    "SubSystemVendorId='" + SubSystemVendorId + "'; " +
                    "UniqueId='" + UniqueId + "'; " +
                    "ASICFamilyType='" + ASICFamilyType + "'; " +
                    "Type='" + Type + "'; " +
                    "BIOSInfo='" + BIOSInfo + "'; " +
                    "IsExternal='" + IsExternal + "'; " +
                    "HasDesktops='" + HasDesktops + "'";
        }
    }

    public class SupportedGPUMetrics
    {
        public bool IsSupportedGPUUsage { get; set; }
        public bool IsSupportedGPUClockSpeed { get; set; }
        public bool IsSupportedGPUVRAMClockSpeed { get; set; }
        public bool IsSupportedGPUTemperature { get; set; }
        public bool IsSupportedGPUHotspotTemperature { get; set; }
        public bool IsSupportedGPUPower { get; set; }
        public bool IsSupportedGPUTotalBoardPower { get; set; }
        public bool IsSupportedGPUFanSpeed { get; set; }
        public bool IsSupportedGPUVRAM { get; set; }
        public bool IsSupportedGPUVoltage { get; set; }
        public bool IsSupportedGPUIntakeTemperature { get; set; }
    }

    public class Metric
    {
        private readonly MetricType type;
        private readonly bool supported;
        private readonly double data;
        private readonly DataType dataType;
        private readonly int rangeMin;
        private readonly int rangeMax;

        public enum MetricType
        {
            GPUUsage,
            GPUClockSpeed,
            GPUVRAMClockSpeed,
            GPUTemperature,
            GPUHotspotTemperature,
            GPUPower,
            GPUTotalBoardPower,
            GPUFanSpeed,
            GPUVRAM,
            GPUVoltage,
            GPUIntakeTemperature
        }

        public enum DataType
        {
            Double,
            Float,
            Integer,
        }

        public Metric(MetricType type, bool supported, double data, DataType dataType, ADLX_IntRange metricRange)
        {
            this.type = type;
            this.supported = supported;
            this.data = data;
            this.dataType = dataType;
            this.rangeMin = metricRange.minValue;
            this.rangeMax = metricRange.maxValue;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Metric);
        }

        public bool Equals(Metric other)
        {
            return other != null && type.Equals(other.type);
        }

        public override int GetHashCode()
        {
            return type.GetHashCode();
        }

        public MetricType GetMetricType()
        {
            return type;
        }

        public bool IsSupported()
        {
            return supported;
        }

        public DataType GetDataType()
        {
            return dataType;
        }

        public double GetData()
        {
            return data;
        }

        public int GetInt()
        {
            return (int)data;
        }

        public float GetFloat()
        {
            return (float)data;
        }

        public override string ToString()
        {
            return "type='" + type + "'; " +
                    "supported='" + supported + "'; " +
                    "data='" + data + "'; " +
                    "dataType='" + dataType + "'; " +
                    "min='" + rangeMin + "'; " +
                    "max='" + rangeMax + "'";
        }
    }

    public class GPUMetrics : IEnumerable<Metric>
    {
        private readonly HashSet<Metric> metrics = [];
        public long TimeStamp { get; set; }

        public Metric Get(string name)
        {
            return metrics.First(m => m.GetMetricType().Equals(name));
        }

        public void Add(Metric.MetricType metricType, bool supported, double data, Metric.DataType dataType, ADLX_IntRange metricRange)
        {
            metrics.Add(new Metric(metricType, supported, data, dataType, metricRange));
        }

        IEnumerator<Metric> IEnumerable<Metric>.GetEnumerator()
        {
            return metrics.GetEnumerator();
        }

        public IEnumerator GetEnumerator()
        {
            return metrics.GetEnumerator();
        }
    }
}