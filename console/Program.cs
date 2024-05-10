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
    private static AmdAdlx.IADLXLog adlxLogger = null;
    private static IntPtr adlContext = IntPtr.Zero;
    private static bool breakLoop = false;

    public static void Main(string[] args)
    {
        bool loop = args.Contains("loop"); // runs test loop
        bool debug = args.Contains("debug"); // debug messages
        bool noAdl = args.Contains("noadl"); // don't initialize adl
        bool noFps = args.Contains("nofps"); // skip fps counter
        bool noMetrics = args.Contains("nometrics"); // skip fps counter
        bool setrpm = args.Contains("setrpm"); // set fan rpm

        _logger = CreateLogger(debug && !loop);


        Console.CancelKeyPress += delegate
        {
            breakLoop = true;
            Thread.Sleep(1000);
            Close();
        };

        try
        {
            if (!noAdl && InitializeAdl(out adlContext))
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

            AmdAdlx.ADLXResult logStatus = system.EnableLog(AmdAdlx.LogDestination.APPLICATION, debug ? AmdAdlx.LogSeverity.LDEBUG : AmdAdlx.LogSeverity.LERROR, out adlxLogger, null);
            if (AmdAdlx.IsSucceeded(logStatus))
                Console.WriteLine("ADLXLog enabled");
            else
                Console.Error.WriteLine("ADLXLog not enabled, status = '{0}'", logStatus);

            if (loop)
            {
                // a loop to test memory management, run and monitor ram usage
                int i = 0;
                DateTime last = DateTime.Now;
                TimeSpan accFpsTime = TimeSpan.Zero;
                TimeSpan accHistFpsTime = TimeSpan.Zero;

                // 'using' is important to release the adlx interfaces
                using AmdAdlx.IADLXPerformanceMonitoringServices perf = system.GetPerformanceMonitoringServices();
                perf.StartPerformanceMetricsTracking();

                while (true)
                {
                    i++;
                    if (i % 10000 == 0)
                    {
                        Console.WriteLine("i = {0}, TimeSpan = {1:s\\.fffffff}s; GetCurrentFPS-Duration = {2:s\\.fffffff}s; GetFPSHistory-Duration = {3:s\\.fffffff}s;",
                                            i, DateTime.Now - last, accFpsTime, accHistFpsTime);
                        last = DateTime.Now;
                        accFpsTime = TimeSpan.Zero;
                    }

                    if (breakLoop)
                    {
                        Console.Error.WriteLine("Exiting loop");
                        break;
                    }


                    uint r = system.GetTotalSystemRAM();
                    List<AmdAdlx.GPU> gpuList2 = system.GetGPUList();

                    foreach (AmdAdlx.GPU gpu in gpuList2)
                    {
                        //get supported gpu metrics
                        AmdAdlx.SupportedGPUMetrics supportedGPUMetrics = perf.GetSupportedGPUMetricsForUniqueId(gpu.UniqueId);
                        //get all metrics
                        AmdAdlx.GPUMetrics gpuMetrics = perf.GetCurrentGPUMetricsForUniqueId(gpu.UniqueId, supportedGPUMetrics);
                        perf.GetHistoryGPUMetricsForUniqueId(gpu.UniqueId, supportedGPUMetrics);

                        AmdAdlx.ADLX_IntRange range = perf.GetSamplingIntervalRange();

                        using (AmdAdlx.IADLXGPUTuningServices gpuTuningServices = system.GetGPUTuningServices() ?? throw new Exception("GetGPUTuningServices failed"))
                        {
                            using (AmdAdlx.IADLXGPU adlxGpu = system.GetADLXGPUByUniqueId(gpu.UniqueId))
                            {
                                adlxGpu.QueryInterface(out AmdAdlx.IADLXGPU1 adlxGpu1);
                                adlxGpu1.Dispose();

                                gpuTuningServices.IsSupportedManualFanTuning(adlxGpu, out bool support);
                                AmdAdlx.ADLXResult status = gpuTuningServices.IsSupportedManualFanTuning(adlxGpu, out bool supportedManualFanTuning);

                                using AmdAdlx.IADLXManualFanTuning manual = gpuTuningServices.GetManualFanTuning(adlxGpu);
                                manual.GetFanTuningRanges(out AmdAdlx.ADLX_IntRange speedRange, out AmdAdlx.ADLX_IntRange temperatureRange);
                                manual.IsSupportedMinAcousticLimit();
                                manual.IsSupportedMinFanSpeed();
                                manual.IsSupportedTargetFanSpeed();
                                manual.IsSupportedZeroRPM();
                                manual.GetMinAcousticLimit();
                                manual.GetMinFanSpeed();
                                manual.GetTargetFanSpeed();
                                manual.GetZeroRPMState();
                                manual.GetMinAcousticLimitRange();
                                manual.GetMinFanSpeedRange();
                                manual.GetTargetFanSpeedRange();
                                gpuTuningServices.IsSupportedAutoTuning(gpu.UniqueId);
                                gpuTuningServices.IsSupportedPresetTuning(gpu.UniqueId);
                                gpuTuningServices.IsSupportedManualGFXTuning(gpu.UniqueId);
                                gpuTuningServices.IsSupportedManualVRAMTuning(gpu.UniqueId);
                                gpuTuningServices.IsSupportedManualPowerTuning(gpu.UniqueId);
                            }
                        }
                    }
                    if (!noFps)
                    {
                        DateTime beforeFps = DateTime.Now;
                        perf.CurrentFPS();
                        accFpsTime += DateTime.Now - beforeFps;
                    }

                    DateTime beforeHistFps = DateTime.Now;
                    perf.GetFPSHistory(0, 0);
                    accHistFpsTime += DateTime.Now - beforeHistFps;
                }
                perf.StopPerformanceMetricsTracking();
                return;
            }

            uint ram = system.GetTotalSystemRAM();
            Console.WriteLine("GetTotalSystemRAM = " + ram);
            List<AmdAdlx.GPU> gpuList = system.GetGPUList();

            using (AmdAdlx.IADLXGPUTuningServices gpuTuningServices = system.GetGPUTuningServices() ?? throw new Exception("GetGPUTuningServices failed"))
            {
                using AmdAdlx.IADLXGPUList iadlxGPUList = system.GetGPUs();
                if (AmdAdlx.IsFailed(iadlxGPUList.At_GPUList(0, out AmdAdlx.IADLXGPU adlxGpu)))
                {
                    throw new Exception("At_GPUList failed");
                }

                using (adlxGpu)
                {
                    AmdAdlx.ADLXResult status = gpuTuningServices.IsSupportedManualFanTuning(adlxGpu, out bool supportedManualFanTuning);
                    Console.WriteLine("Manual Fan Tuning support = {0}; GPUName = '{1}'; ADLXResult = {2}", supportedManualFanTuning, adlxGpu.Name(), status);

                    //QueryInterface test
                    if (AmdAdlx.IsSucceeded(adlxGpu.QueryInterface(out AmdAdlx.IADLXGPU1 adlxGpu1)))
                    {
                        using (adlxGpu1)
                        {
                            Console.WriteLine("ADLXGPU1: Name='{0}'; ProductName:{1}; PCIBusLaneWidth:{2}; PCIBusType:{3}; MultiGPUMode:{4}; ",
                                                adlxGpu1.Name(), adlxGpu1.ProductName(), adlxGpu1.PCIBusLaneWidth(), adlxGpu1.PCIBusType(), adlxGpu1.MultiGPUMode());
                            Console.WriteLine("IADLXGPU pointer = 0x{0:X}; Queried pointer = 0x{1:X};", adlxGpu.ToPointer(), adlxGpu1.ToPointer());
                        }
                    }
                    else
                    {
                        throw new Exception("ADLXGPU1 query interface failed");
                    }
                }
            }

            // 'using' is important to release the adlx interfaces
            using (AmdAdlx.IADLXPerformanceMonitoringServices perf = system.GetPerformanceMonitoringServices())
            {
                perf.StartPerformanceMetricsTracking();
                perf.SetSamplingInterval(700);
                foreach (AmdAdlx.GPU gpu in gpuList)
                {
                    //get supported gpu metrics
                    DateTime beforeSupMetrics = DateTime.Now;
                    AmdAdlx.SupportedGPUMetrics supportedGPUMetrics = perf.GetSupportedGPUMetricsForUniqueId(gpu.UniqueId);
                    Console.WriteLine("GetSupportedGPUMetrics took {0}ms", (DateTime.Now - beforeSupMetrics).TotalMilliseconds);

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

                    using AmdAdlx.IADLXGPU adlxGpu = system.GetADLXGPUByUniqueId(gpu.UniqueId);
                    if (!noMetrics)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            //get all metrics
                            DateTime beforeCurrentMetrics = DateTime.Now;
                            using (AmdAdlx.IADLXGPUMetrics adlxGpuCurMetrics = perf.GetCurrentGPUMetrics(adlxGpu))
                            {
                                Console.WriteLine("GetCurrentGPUMetrics took {0}ms", (DateTime.Now - beforeCurrentMetrics).TotalMilliseconds);
                            }

                            //get all metrics
                            DateTime beforeHistoryMetrics = DateTime.Now;
                            using (AmdAdlx.IADLXGPUMetricsList adlxGpuHistMetricsList = perf.GetGPUMetricsHistory(adlxGpu, 0, 0))
                            {
                                if (adlxGpuHistMetricsList.Size() > 0)
                                {
                                    adlxGpuHistMetricsList.At_GPUMetricsList(0, out AmdAdlx.IADLXGPUMetrics adlxGPUMetrics);
                                    Console.WriteLine("GetHistoryGPUMetrics took {0}ms", (DateTime.Now - beforeHistoryMetrics).TotalMilliseconds);
                                    adlxGPUMetrics.Dispose();
                                }
                            }
                            Console.WriteLine();

                            DateTime beforeCurFps = DateTime.Now;
                            Console.WriteLine("GetCurrentFPS={0}; Time={1}; took {2}ms", perf.CurrentFPS(), DateTime.Now.TimeOfDay, (DateTime.Now - beforeCurFps).TotalMilliseconds);
                            DateTime beforeHistFps = DateTime.Now;
                            using (AmdAdlx.IADLXFPSList adlxFpsList = perf.GetFPSHistory(0, 0))
                            {
                                if (adlxFpsList.Size() > 0)
                                {
                                    adlxFpsList.At_FPSList(0, out AmdAdlx.IADLXFPS adlxFps);
                                    Console.WriteLine("GetFPSHistory took {0}ms", (DateTime.Now - beforeHistFps).TotalMilliseconds);
                                    adlxFps.Dispose();
                                }
                            }
                            Thread.Sleep(1000);
                        }
                    }

                    DateTime beforeCurUniqMetrics = DateTime.Now;
                    perf.GetCurrentGPUMetricsForUniqueId(gpu.UniqueId, supportedGPUMetrics);
                    Console.WriteLine("GetCurrentGPUMetricsForUniqueId took {0}ms", (DateTime.Now - beforeCurUniqMetrics).TotalMilliseconds);

                    DateTime beforeHistUniqMetrics = DateTime.Now;
                    AmdAdlx.GPUMetrics gpuMetrics = perf.GetHistoryGPUMetricsForUniqueId(gpu.UniqueId, supportedGPUMetrics);
                    Console.WriteLine("GetHistoryGPUMetricsForUniqueId took {0}ms", (DateTime.Now - beforeHistUniqMetrics).TotalMilliseconds);

                    Console.WriteLine();

                    Console.WriteLine("GPU='{0}'; TimeStamp='{1}';", gpu.Name, gpuMetrics.TimeStamp);
                    foreach (AmdAdlx.Metric m in gpuMetrics)
                    {
                        Console.WriteLine(m);
                    }

                    AmdAdlx.ADLX_IntRange range = perf.GetSamplingIntervalRange();
                    Console.WriteLine("SamplingRange: min:{0};max:{1};step:{2};", range.minValue, range.maxValue, range.step);

                    Console.WriteLine("SamplingInterval: {0}", perf.GetSamplingInterval());

                    //// FAN TUNING
                    using (AmdAdlx.IADLXGPUTuningServices gpuTuningServices = system.GetGPUTuningServices() ?? throw new Exception("GetGPUTuningServices failed"))
                    {
                        Console.WriteLine("Manual Fan Tuning support = {0}; GPUName = '{1}';", gpuTuningServices.IsSupportedManualFanTuning(gpu.UniqueId), gpu.Name);

                        AmdAdlx.ADLXResult status = gpuTuningServices.IsSupportedManualFanTuning(adlxGpu, out bool supportedManualFanTuning);
                        Console.WriteLine("Manual Fan Tuning support = {0}; GPUName = '{1}'; ADLXResult = {2}", supportedManualFanTuning, adlxGpu.Name(), status);

                        using AmdAdlx.IADLXManualFanTuning manual = gpuTuningServices.GetManualFanTuning(adlxGpu);
                        manual.GetFanTuningRanges(out AmdAdlx.ADLX_IntRange speedRange, out AmdAdlx.ADLX_IntRange temperatureRange);
                        Console.WriteLine("Manual Fan - speedRange: min={0}, max={1}; temperatureRange: min={2}, max={3};",
                                            speedRange.minValue, speedRange.maxValue, temperatureRange.minValue, temperatureRange.maxValue);

                        Console.WriteLine("IsSupportedMinAcousticLimit={0}; IsSupportedMinFanSpeed={1}; IsSupportedTargetFanSpeed={2}; IsSupportedZeroRPM={3};",
                                             manual.IsSupportedMinAcousticLimit(), manual.IsSupportedMinFanSpeed(), manual.IsSupportedTargetFanSpeed(), manual.IsSupportedZeroRPM());
                        Console.WriteLine("GetMinAcousticLimit={0}; GetMinFanSpeed={1}; GetTargetFanSpeed={2}; GetZeroRPMState={3};",
                                             manual.GetMinAcousticLimit(), manual.GetMinFanSpeed(), manual.GetTargetFanSpeed(), manual.GetZeroRPMState());
                        Console.WriteLine("GetMinAcousticLimitRange={0}; GetMinFanSpeedRange={1}; GetTargetFanSpeedRange={2};",
                                             manual.GetMinAcousticLimitRange(), manual.GetMinFanSpeedRange(), manual.GetTargetFanSpeedRange());
                        Console.WriteLine("IsSupportedAutoTuning={0}; IsSupportedPresetTuning={1}; IsSupportedManualGFXTuning={2}; IsSupportedManualVRAMTuning={3}; IsSupportedManualPowerTuning={4};",
                                             gpuTuningServices.IsSupportedAutoTuning(gpu.UniqueId),
                                             gpuTuningServices.IsSupportedPresetTuning(gpu.UniqueId),
                                             gpuTuningServices.IsSupportedManualGFXTuning(gpu.UniqueId),
                                             gpuTuningServices.IsSupportedManualVRAMTuning(gpu.UniqueId),
                                             gpuTuningServices.IsSupportedManualPowerTuning(gpu.UniqueId)
                                             );

                        using (AmdAdlx.IADLXManualFanTuningStateList states = manual.GetFanTuningStates())
                        {
                            //Get Fan Speeds
                            for (uint crt = states.Begin(); crt != states.End(); ++crt)
                            {
                                AmdAdlx.ADLXResult res = states.At_ManualFanTuningStateList(crt, out AmdAdlx.IADLXManualFanTuningState fanState);
                                if (AmdAdlx.IsFailed(res))
                                {
                                    throw new Exception("At_ManualFanTuningStateList failed");
                                }
                                using (fanState)
                                {
                                    Console.WriteLine("Fan State {0}: speed={1}; temperature={2}", crt, fanState.GetFanSpeed(), fanState.GetTemperature());
                                }
                            }
                        }

                        if (setrpm)
                        {
                            // Set Empty Fan State
                            using AmdAdlx.IADLXManualFanTuningStateList emptyStates = manual.GetEmptyFanTuningStates();
                            for (uint crt = emptyStates.Begin(); crt != emptyStates.End(); ++crt)
                            {
                                AmdAdlx.ADLXResult res = emptyStates.At_ManualFanTuningStateList(crt, out AmdAdlx.IADLXManualFanTuningState fanState);
                                if (AmdAdlx.IsFailed(res))
                                {
                                    throw new Exception("At_ManualFanTuningStateList failed");
                                }
                                int fanSpeedStep = (speedRange.maxValue - speedRange.minValue) / (int)emptyStates.Size();
                                int fanTemperatureStep = (temperatureRange.maxValue - temperatureRange.minValue) / (int)emptyStates.Size();

                                fanState.SetFanSpeed((int)(speedRange.minValue + fanSpeedStep * crt));
                                int newSpeed = fanState.GetFanSpeed();

                                fanState.SetTemperature((int)(temperatureRange.minValue + fanTemperatureStep * crt));
                                int newTemperature = fanState.GetTemperature();

                                using (fanState)
                                {
                                    Console.WriteLine("Empty Fan State {0}: speed={1}; temperature={2}; set to speed={3}; temperature={4}",
                                                        crt, fanState.GetFanSpeed(), fanState.GetTemperature(), newSpeed, newTemperature);
                                }
                            }

                            if (AmdAdlx.IsSucceeded(manual.IsValidFanTuningStates(emptyStates, out int errorIndex)))
                            {
                                Console.WriteLine("\tIsValidGPUTuningStates, errorIndex is: {0}", errorIndex);
                                manual.SetFanTuningStates(emptyStates);
                                Console.WriteLine("AFTER SETTING:");
                                using (AmdAdlx.IADLXManualFanTuningStateList states = manual.GetFanTuningStates())
                                {
                                    //Get Fan Speeds
                                    for (uint crt = states.Begin(); crt != states.End(); ++crt)
                                    {
                                        AmdAdlx.ADLXResult res = states.At_ManualFanTuningStateList(crt, out AmdAdlx.IADLXManualFanTuningState fanState);
                                        if (AmdAdlx.IsFailed(res))
                                        {
                                            throw new Exception("At_ManualFanTuningStateList failed");
                                        }
                                        using (fanState)
                                        {
                                            Console.WriteLine("Current Fan State {0}: speed={1}; temperature={2}", crt, fanState.GetFanSpeed(), fanState.GetTemperature());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                perf.StopPerformanceMetricsTracking();
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
            Close();
        }
    }

    private static void Close()
    {
        breakLoop = true;
        Console.WriteLine("Closing ADLX");
        AmdAdlx.ADLXResult exitStatus = AmdAdlx.Terminate();
        adlxLogger?.Dispose();

        if (adlContext != IntPtr.Zero)
            AtiAdlxx.ADL2_Main_Control_Destroy(adlContext);
        if (exitStatus != AmdAdlx.ADLXResult.ADLX_OK)
            Console.Error.WriteLine("ADLX terminated with error: '{0}'", (AmdAdlx.ADLXResult)exitStatus);
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

    public static ILogger CreateLogger(bool debug)
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole(options =>
                                            {
                                                options.IncludeScopes = true;
                                                options.SingleLine = true;
                                                options.TimestampFormat = "[MMM dd HH:mm:ss] ";
                                            })
                .AddFilter(nameof(AmdAdlx), debug ? LogLevel.Debug : LogLevel.Information)
                .AddConsole()
                .AddDebug()
                );
        return factory.CreateLogger(nameof(AmdAdlx));
    }
}
