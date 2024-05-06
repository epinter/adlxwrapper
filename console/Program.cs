using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ADLXWrapper;
using Microsoft.Extensions.Logging;

public class Program
{
    private static ILogger _logger = null;
    public static void Main(string[] args)
    {
        bool loop = args.Contains("loop");
        bool debug = args.Contains("debug");

        if (debug && !loop)
            _logger = CreateLogger();

        IntPtr adlContext = IntPtr.Zero;
        try
        {
            if (InitializeAdl(out adlContext))
            {
                PrintAdlInfo(adlContext);

                AmdAdlx.InitializeFromAdl(adlContext, _logger);
                AmdAdlx.GPU gpu = AmdAdlx.GetAdlMapping().GetADLXGPUFromAdlAdapterIndex(0);
                Console.WriteLine("ADLXGPU from adl = '{0}'", gpu);
            }
            else
            {
                AmdAdlx.Initialize(_logger);
            }

            AmdAdlx.IADLXSystem system = AmdAdlx.GetSystemServices();

            if (loop)
            {
                // a loop to test memory management, run and monitor ram usage
                int i = 0;
                DateTime last = DateTime.Now;
                while (true)
                {
                    i++;
                    if (i % 10000 == 0)
                    {
                        Console.WriteLine("i = {0}, TimeSpan = {1:s\\.fffffff}s;", i, DateTime.Now - last);
                        last = DateTime.Now;
                    }

                    uint r = system.GetTotalSystemRAM();
                    List<AmdAdlx.GPU> gpuList2 = system.GetGPUList();

                    // 'using' is important to release the adlx interfaces
                    using (AmdAdlx.IADLXPerformanceMonitoringServices perf = system.GetPerformanceMonitoringServices())
                    {
                        foreach (AmdAdlx.GPU gpu in gpuList2)
                        {
                            //get supported gpu metrics
                            AmdAdlx.SupportedGPUMetrics supportedGPUMetrics = perf.GetSupportedGPUMetricsForUniqueId(gpu.UniqueId);
                            //get all metrics
                            AmdAdlx.GPUMetrics gpuMetrics = perf.GetCurrentGPUMetricsForUniqueId(gpu.UniqueId);

                            AmdAdlx.ADLX_IntRange range = perf.GetSamplingIntervalRange();

                            perf.CurrentFPS();
                        }
                    }
                }
            }

            uint ram = system.GetTotalSystemRAM();
            Console.WriteLine("GetTotalSystemRAM = " + ram);
            List<AmdAdlx.GPU> gpuList = system.GetGPUList();

            // 'using' is important to release the adlx interfaces
            using (AmdAdlx.IADLXPerformanceMonitoringServices perf = system.GetPerformanceMonitoringServices())
            {
                foreach (AmdAdlx.GPU gpu in gpuList)
                {
                    //get supported gpu metrics
                    AmdAdlx.SupportedGPUMetrics supportedGPUMetrics = perf.GetSupportedGPUMetricsForUniqueId(gpu.UniqueId);
                    Console.WriteLine("GPU='{0}'", gpu.Name);
                    Console.WriteLine("IsSupportedGPUUsage = {0}", supportedGPUMetrics.IsSupportedGPUUsage);
                    Console.WriteLine("IsSupportedGPUClockSpeed = {0}", supportedGPUMetrics.IsSupportedGPUClockSpeed);
                    Console.WriteLine("IsSupportedGPUVRAMClockSpeed = {0}", supportedGPUMetrics.IsSupportedGPUVRAMClockSpeed);
                    Console.WriteLine("IsSupportedGPUTemperature = {0}", supportedGPUMetrics.IsSupportedGPUTemperature);
                    Console.WriteLine("IsSupportedGPUHotspotTemperature = {0}", supportedGPUMetrics.IsSupportedGPUHotspotTemperature);
                    Console.WriteLine("IsSupportedGPUPower = {0}", supportedGPUMetrics.IsSupportedGPUPower);
                    Console.WriteLine("IsSupportedGPUTotalBoardPower = {0}", supportedGPUMetrics.IsSupportedGPUTotalBoardPower);
                    Console.WriteLine("IsSupportedGPUFanSpeed = {0}", supportedGPUMetrics.IsSupportedGPUFanSpeed);
                    Console.WriteLine("IsSupportedGPUVRAM = {0}", supportedGPUMetrics.IsSupportedGPUVRAM);
                    Console.WriteLine("IsSupportedGPUVoltage = {0}", supportedGPUMetrics.IsSupportedGPUVoltage);
                    Console.WriteLine("IsSupportedGPUIntakeTemperature = {0}", supportedGPUMetrics.IsSupportedGPUIntakeTemperature);

                    //get all metrics
                    AmdAdlx.GPUMetrics gpuMetrics = perf.GetCurrentGPUMetricsForUniqueId(gpu.UniqueId);
                    Console.WriteLine("GPU='{0}'; TimeStamp='{1}';", gpu.Name, gpuMetrics.TimeStamp);
                    foreach (AmdAdlx.Metric m in gpuMetrics)
                    {
                        Console.WriteLine(m);
                    }

                    AmdAdlx.ADLX_IntRange range = perf.GetSamplingIntervalRange();
                    Console.WriteLine("SamplingRange: min:{0};max:{1};step:{2};", range.minValue, range.maxValue, range.step);

                    int j = 10;
                    while (j > 0)
                    {
                        j--;
                        Thread.Sleep(500);
                        Console.WriteLine("FPS={0}; Time={1};", perf.CurrentFPS(), DateTime.Now.TimeOfDay); //only detects fullscreen applications
                    }
                }
            }
        }
        catch (DllNotFoundException e)
        {
            Console.WriteLine("ADLX not found: {0}", e);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: {0}", e);
        }
        finally
        {
            if (adlContext != IntPtr.Zero)
                AtiAdlxx.ADL2_Main_Control_Destroy(adlContext);
        }

        AmdAdlx.Terminate();
    }

    private static bool InitializeAdl(out IntPtr adlContext)
    {
        AtiAdlxx.ADL2_Main_Control_Create(AtiAdlxx.Main_Memory_Alloc, 1, out adlContext);
        if (adlContext != IntPtr.Zero)
            return true;

        return false;
    }

    private static void PrintAdlInfo(IntPtr adlContext)
    {
        if (adlContext != IntPtr.Zero)
        {
            int numberOfAdapters = 0;
            AtiAdlxx.ADL2_Adapter_NumberOfAdapters_Get(adlContext, ref numberOfAdapters);

            Debug.WriteLine("adlContext={0}, numberOfAdapters={1}", adlContext, numberOfAdapters);
            AtiAdlxx.ADLAdapterInfo[] adapterInfo = new AtiAdlxx.ADLAdapterInfo[numberOfAdapters];
            if (AtiAdlxx.ADL2_Adapter_AdapterInfo_Get(ref adlContext, adapterInfo) == AtiAdlxx.ADLStatus.ADL_OK)
            {
                for (int i = 0; i < numberOfAdapters; i++)
                {
                    if (adapterInfo[i].VendorID != 1002)
                        continue;

                    Console.WriteLine("\t\tAdapterIndex: {0}", i);
                    Console.WriteLine("\t\tAdapterName: {0}", adapterInfo[i].AdapterName);
                    Console.WriteLine("\t\tUDID: {0}", adapterInfo[i].UDID);
                    Console.WriteLine("\t\tPNPString: {0}", adapterInfo[i].PNPString);
                    Console.WriteLine("\t\tPresent: {0}", adapterInfo[i].Present);
                    Console.WriteLine("\t\tVendorID: {0}", adapterInfo[i].VendorID);
                    Console.WriteLine("\t\tBusNumber: {0}", adapterInfo[i].BusNumber);
                    Console.WriteLine("\t\tDeviceNumber: {0}", adapterInfo[i].DeviceNumber);
                    Console.WriteLine("\t\tFunctionNumber: {0}", adapterInfo[i].FunctionNumber);
                    Console.WriteLine("");
                }
            }
        }
    }

    public static ILogger CreateLogger()
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole(options =>
                                            {
                                                options.IncludeScopes = true;
                                                options.SingleLine = true;
                                                options.TimestampFormat = "[MMM dd HH:mm:ss] ";
                                            })
                .AddFilter(nameof(AmdAdlx), LogLevel.Debug)
                .AddConsole()
                .AddDebug()
                );
        return factory.CreateLogger(nameof(AmdAdlx));
    }
}
