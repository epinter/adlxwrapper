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

    public static bool IsAdlxInitialized()
    {
        return adlxInitialized;
    }

    public static bool IsMappingAdl()
    {
        return mappingAdl;
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

        public GPU GetADLXGPUFromAdlAdapterIndex(int adlAdapterIndex)
        {
            ADLXResult status = vtbl.GetADLXGPUFromAdlAdapterIndex(_ptr, adlAdapterIndex, out IntPtr adlxGpuPtr);
            GPU gpu = null;
            if (status == ADLXResult.ADLX_OK)
            {
                using (IADLXGPU adlxGpu = new ADLXGPU(adlxGpuPtr))
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
                LogDebug("failed: status = {0}", status);
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
        uint GetTotalSystemRAM();
        List<GPU> GetGPUList();
        IADLXGPUList GetGPUs();
        IADLXPerformanceMonitoringServices GetPerformanceMonitoringServices();
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
            ADLXResult status = vtbl.GetGPUs(_ptr, out nint gpuListPtr);
            return status == ADLXResult.ADLX_OK ? new ADLXGPUList(gpuListPtr) : null;
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
                            LogDebug(String.Format("GPU: Name='{0}'; VendorId='{1}'; IsExternal='{2}'; DriverPath='{3}'; PNPString='{4}'; HasDesktops='{5}'; VRAMType='{6}'; Type='{7}'" +
                                            "DeviceId='{8}'; RevisionId='{9}'; SubSystemId='{10}'; SubSystemVendorId='{11}'; UniqueId='{12}'; BIOSInfo='{13}'; ASICFamilyType='{14}'",
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
            vtbl.TotalSystemRAM(_ptr, out uint ramMB);
            return ramMB;
        }

        public IADLXPerformanceMonitoringServices GetPerformanceMonitoringServices()
        {
            ADLXResult status = vtbl.GetPerformanceMonitoringServices(_ptr, out IntPtr performanceMonitoringServices);
            return status == ADLXResult.ADLX_OK ? new ADLXPerformanceMonitoringServices(performanceMonitoringServices) : null;
        }

        // See https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem.h
        [StructLayout(LayoutKind.Sequential)]
        protected struct ADLXSystemVtbl
        {
            public IntPtr GetHybridGraphicsType;
            public ADLXSystemDelegates.ADLXSystem_GetGPUs GetGPUs;
            public ADLXSystemDelegates.ADLXSystem_QueryInterface QueryInterface;
            public IntPtr GetDisplaysServices;
            public IntPtr GetDesktopsServices;
            public IntPtr GetGPUsChangedHandling;
            public IntPtr EnableLog;
            public IntPtr Get3DSettingsServices;
            public IntPtr GetGPUTuningServices;
            public ADLXSystemDelegates.GetPerformanceMonitoringServices GetPerformanceMonitoringServices;
            public ADLXSystemDelegates.TotalSystemRAM TotalSystemRAM;
            public IntPtr GetI2C;
        }

        protected static class ADLXSystemDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult TotalSystemRAM(IntPtr adlxSystem, out uint ramMB);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult ADLXSystem_GetGPUs(IntPtr adlxSystem, out IntPtr gpus);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult ADLXSystem_QueryInterface(IntPtr adlxSystem, [MarshalAs(UnmanagedType.LPWStr)] string interfaceId, out IntPtr ppInterface);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate ADLXResult GetPerformanceMonitoringServices(IntPtr adlxSystem, out IntPtr gpus);
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

        public string Name()
        {
            vtbl.Name(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public uint TotalVRAM()
        {
            vtbl.TotalVRAM(_ptr, out uint vram);
            return vram;
        }

        public string VendorId()
        {
            vtbl.VendorId(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public bool IsExternal()
        {
            vtbl.IsExternal(_ptr, out bool isExternal);
            return isExternal;
        }

        public string DriverPath()
        {
            vtbl.DriverPath(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public string PNPString()
        {
            vtbl.PNPString(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public bool HasDesktops()
        {
            vtbl.HasDesktops(_ptr, out bool hasDesktops);
            return hasDesktops;
        }

        public string VRAMType()
        {
            vtbl.VRAMType(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public string DeviceId()
        {
            vtbl.DeviceId(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public string RevisionId()
        {
            vtbl.RevisionId(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public string SubSystemId()
        {
            vtbl.SubSystemId(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public string SubSystemVendorId()
        {
            vtbl.SubSystemVendorId(_ptr, out IntPtr namePtr);
            return Marshal.PtrToStringAnsi(namePtr);
        }

        public int UniqueId()
        {
            vtbl.UniqueId(_ptr, out int uniqueId);
            return uniqueId;
        }

        public GPUType Type()
        {
            vtbl.Type(_ptr, out int type);
            return (GPUType)type;
        }

        public ASICFamilyType ASICFamilyType()
        {
            vtbl.ASICFamilyType(_ptr, out int asicFamilyType);
            return (ASICFamilyType)asicFamilyType;
        }

        public BIOSInfo BIOSInfo()
        {
            BIOSInfo biosInfo = new();
            IntPtr partNumber = IntPtr.Zero;
            IntPtr version = IntPtr.Zero;
            IntPtr date = IntPtr.Zero;

            vtbl.BIOSInfo(_ptr, out partNumber, out version, out date);
            biosInfo.partNumber = Marshal.PtrToStringAnsi(partNumber);
            biosInfo.version = Marshal.PtrToStringAnsi(version);
            biosInfo.date = Marshal.PtrToStringAnsi(date);
            return biosInfo;
        }

        public new IntPtr ToPointer()
        {
            return _ptr;
        }

        public override void Acquire()
        {
            vtbl.adlxInterface.Acquire(_ptr);
            LogDebug("=ADLXGPU acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXGPU release started");

            if (_ptr != IntPtr.Zero)
            {
                vtbl.adlxInterface.Release(_ptr);
            }

            LogDebug("+ADLXGPU released");
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

        public override void Acquire()
        {
            if (_ptr != IntPtr.Zero)
                vtbl.adlxList.adlxInterface.Acquire(_ptr);

            LogDebug("=ADLXGPUList acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXGPUList release started");

            if (_ptr != IntPtr.Zero)
                vtbl.adlxList.adlxInterface.Release(_ptr);

            LogDebug("+ADLXGPUList released");
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
        IADLXGPUMetricsSupport GetSupportedGPUMetrics(IntPtr gpu);
        IADLXFPS GetCurrentFPS();
        int GetSamplingInterval();
        ADLXResult SetSamplingInterval(int intervalMs);
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
                                using (IADLXGPUMetricsSupport adlxMetrixSupport = GetSupportedGPUMetrics(adlxGpu.ToPointer()))
                                {
                                    using (IADLXGPUMetrics adlxGPUMetrics = GetCurrentGPUMetrics(adlxGpu.ToPointer()))
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
                                using (IADLXGPUMetricsSupport adlxMetrixSupport = GetSupportedGPUMetrics(adlxGpu.ToPointer()))
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

        public IADLXFPS GetCurrentFPS()
        {
            ADLXResult status = vtbl.GetCurrentFPS(_ptr, out IntPtr ppFps);
            return status == ADLXResult.ADLX_OK ? new ADLXFPS(ppFps) : null;
        }

        public IADLXGPUMetricsSupport GetSupportedGPUMetrics(IntPtr gpu)
        {
            ADLXResult status = vtbl.GetSupportedGPUMetrics(_ptr, gpu, out IntPtr ppMetricsSupported);
            return status == ADLXResult.ADLX_OK ? new ADLXGPUMetricsSupport(ppMetricsSupported) : null;
        }

        public IADLXGPUMetrics GetCurrentGPUMetrics(IntPtr gpu)
        {
            ADLXResult status = vtbl.GetCurrentGPUMetrics(_ptr, gpu, out IntPtr ppMetrics);
            return status == ADLXResult.ADLX_OK ? new ADLXGPUMetrics(ppMetrics) : null;
        }

        public ADLX_IntRange GetSamplingIntervalRange()
        {
            vtbl.GetSamplingIntervalRange(_ptr, out ADLX_IntRange range);
            return range;
        }

        public int GetSamplingInterval()
        {
            vtbl.GetSamplingInterval(_ptr, out int intervalMs);
            return intervalMs;
        }

        public ADLXResult SetSamplingInterval(int intervalMs)
        {
            return vtbl.SetSamplingInterval(_ptr, intervalMs);
        }

        public override void Acquire()
        {
            vtbl.adlxInterface.Acquire(_ptr);
            LogDebug("=ADLXGPUMetrics acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXPerformanceMonitoringServices release started");
            vtbl.adlxInterface.Release(_ptr);
            LogDebug("+ADLXPerformanceMonitoringServices released");
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
        MetricRange GetGPUUsageRange();
        MetricRange GetGPUClockSpeedRange();
        MetricRange GetGPUVRAMClockSpeedRange();
        MetricRange GetGPUTemperatureRange();
        MetricRange GetGPUHotspotTemperatureRange();
        MetricRange GetGPUPowerRange();
        MetricRange GetGPUFanSpeedRange();
        MetricRange GetGPUVRAMRange();
        MetricRange GetGPUVoltageRange();
        MetricRange GetGPUTotalBoardPowerRange();
        MetricRange GetGPUIntakeTemperatureRange();
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

        public bool IsSupportedGPUUsage()
        {
            vtbl.IsSupportedGPUUsage(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUClockSpeed()
        {
            vtbl.IsSupportedGPUClockSpeed(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUVRAMClockSpeed()
        {
            vtbl.IsSupportedGPUVRAMClockSpeed(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUTemperature()
        {
            vtbl.IsSupportedGPUTemperature(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUHotspotTemperature()
        {
            vtbl.IsSupportedGPUHotspotTemperature(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUPower()
        {
            vtbl.IsSupportedGPUPower(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUTotalBoardPower()
        {
            vtbl.IsSupportedGPUTotalBoardPower(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUFanSpeed()
        {
            vtbl.IsSupportedGPUFanSpeed(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUVRAM()
        {
            vtbl.IsSupportedGPUVRAM(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUVoltage()
        {
            vtbl.IsSupportedGPUVoltage(_ptr, out bool supported);
            return supported;
        }

        public bool IsSupportedGPUIntakeTemperature()
        {
            vtbl.IsSupportedGPUIntakeTemperature(_ptr, out bool supported);
            return supported;
        }

        public MetricRange GetGPUUsageRange()
        {
            vtbl.GetGPUUsageRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUClockSpeedRange()
        {
            vtbl.GetGPUClockSpeedRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUVRAMClockSpeedRange()
        {
            vtbl.GetGPUVRAMClockSpeedRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUTemperatureRange()
        {
            vtbl.GetGPUTemperatureRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUHotspotTemperatureRange()
        {
            vtbl.GetGPUHotspotTemperatureRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUPowerRange()
        {
            vtbl.GetGPUPowerRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUFanSpeedRange()
        {
            vtbl.GetGPUFanSpeedRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUVRAMRange()
        {
            vtbl.GetGPUVRAMRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUVoltageRange()
        {
            vtbl.GetGPUVoltageRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUTotalBoardPowerRange()
        {
            vtbl.GetGPUTotalBoardPowerRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }
        public MetricRange GetGPUIntakeTemperatureRange()
        {
            vtbl.GetGPUIntakeTemperatureRange(_ptr, out int minValue, out int maxValue);
            return new MetricRange() { min = minValue, max = maxValue };
        }

        public override void Acquire()
        {
            vtbl.adlxInterface.Acquire(_ptr);
            LogDebug("=ADLXGPUMetricsSupport acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXGPUMetricsSupport release started");
            vtbl.adlxInterface.Release(_ptr);
            LogDebug("+ADLXGPUMetricsSupport released");
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

        public long TimeStamp()
        {
            vtbl.TimeStamp(_ptr, out long data);
            return data;
        }

        public double GPUUsage()
        {
            vtbl.GPUUsage(_ptr, out double data);
            return data;
        }

        public int GPUClockSpeed()
        {
            vtbl.GPUClockSpeed(_ptr, out int data);
            return data;
        }

        public int GPUVRAMClockSpeed()
        {
            vtbl.GPUVRAMClockSpeed(_ptr, out int data);
            return data;
        }

        public double GPUTemperature()
        {
            vtbl.GPUTemperature(_ptr, out double data);
            return data;
        }

        public double GPUHotspotTemperature()
        {
            vtbl.GPUHotspotTemperature(_ptr, out double data);
            return data;
        }

        public double GPUPower()
        {
            vtbl.GPUPower(_ptr, out double data);
            return data;
        }

        public double GPUTotalBoardPower()
        {
            vtbl.GPUTotalBoardPower(_ptr, out double data);
            return data;
        }

        public int GPUFanSpeed()
        {
            vtbl.GPUFanSpeed(_ptr, out int data);
            return data;
        }

        public int GPUVRAM()
        {
            vtbl.GPUVRAM(_ptr, out int data);
            return data;
        }

        public int GPUVoltage()
        {
            vtbl.GPUVoltage(_ptr, out int data);
            return data;
        }

        public double GPUIntakeTemperature()
        {
            vtbl.GPUIntakeTemperature(_ptr, out double data);
            return data;
        }

        public override void Acquire()
        {
            vtbl.adlxInterface.Acquire(_ptr);
            LogDebug("=ADLXGPUMetrics acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXGPUMetrics release started");
            vtbl.adlxInterface.Release(_ptr);
            LogDebug("+ADLXGPUMetrics released");

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

        public long TimeStamp()
        {
            vtbl.TimeStamp(_ptr, out long data);
            return data;
        }

        public int FPS()
        {
            int data = -1;
            vtbl.FPS(_ptr, out data);
            return data;
        }

        public override void Acquire()
        {
            vtbl.adlxInterface.Acquire(_ptr);
            LogDebug("=ADLXFPS acquired");
        }

        public override void Release()
        {
            LogDebug("+ADLXFPS release started");
            vtbl.adlxInterface.Release(_ptr);
            LogDebug("+ADLXFPS released");

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
        Base class for all ADLX interfaces
    *************************************************************************/
    public interface IADLXInterface : IDisposable
    {
        IntPtr ToPointer();
        void Acquire();
        void Release();
    }

    protected class ADLXInterface : IADLXInterface
    {
        private readonly IntPtr _ptr;
        private readonly ADLXInterfaceVtbl vtbl;

        internal ADLXInterface(IntPtr ptr)
        {
            _ptr = ptr;
            GetVtblPointer(ptr, out vtbl);
            // Acquire not needed, results in memory leak when enabled
            vtbl.Acquire = null;
        }

        public IntPtr ToPointer()
        {
            return _ptr;
        }

        public virtual void Acquire()
        {
            throw new NotImplementedException();
        }

        public virtual void Release()
        {
            throw new NotImplementedException();
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
            public IntPtr QueryInterface;
        }

        protected static class ADLXInterfaceDelegates
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate long Acquire(IntPtr interfacePtr);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate long Release(IntPtr interfacePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLX_IntRange
    {
        public int minValue;
        public int maxValue;
        public int step;
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

    public struct MetricRange
    {
        public int min;
        public int max;
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

        public Metric(MetricType type, bool supported, double data, DataType dataType, MetricRange metricRange)
        {
            this.type = type;
            this.supported = supported;
            this.data = data;
            this.dataType = dataType;
            this.rangeMin = metricRange.min;
            this.rangeMax = metricRange.max;
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

        public void Add(Metric.MetricType metricType, bool supported, double data, Metric.DataType dataType, MetricRange metricRange)
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
