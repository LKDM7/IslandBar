using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace IslandBar;

// Qui utilise le micro et la caméra. Le registre ConsentStore ne sert plus : Windows 11 récent garde ces dates dans sa
// propre base, illisible ici. Le micro se lit dans les sessions de capture audio, la caméra via Media Foundation.
static class Sensors
{
    const uint MF_VERSION = 0x20070, MFSTARTUP_LITE = 1;
    static readonly TimeSpan CheckDevices = TimeSpan.FromMinutes(1); // Windows ne prévient pas ici d'un micro qu'on branche
    static readonly AutoResetEvent moved = new(false);
    static uint[] cameraPids = [];
    static IMFSensorActivityMonitor monitor; // gardé en vie : c'est lui qui rappelle Report

    [DllImport("mfplat")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfsensorgroup")] static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback, out IMFSensorActivityMonitor monitor);

    public static void Watch(Action<List<string>, List<string>> changed, Action<Exception> failed) => new Thread(() =>
    {
        StartCamera();
        uint[] microphone = [], camera = [];
        while (true)
        {
            try
            {
                var heard = AppVolume.Capturing(() => moved.Set()).Distinct().Order().ToArray();
                var seen = cameraPids;
                if (!heard.SequenceEqual(microphone) || !seen.SequenceEqual(camera))
                {
                    (microphone, camera) = (heard, seen);
                    changed(Names(microphone), Names(camera));
                }
            }
            catch (Exception ex) { failed(ex); }
            moved.WaitOne(CheckDevices);
        }
    }) { IsBackground = true, Name = "Sensors" }.Start();

    static void StartCamera()
    {
        try { if (MFStartup(MF_VERSION, MFSTARTUP_LITE) == 0 && MFCreateSensorActivityMonitor(new Report(), out monitor) == 0) monitor.Start(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { } // Windows trop ancien : pas de point caméra
    }

    static List<string> Names(uint[] pids) => pids.Select(AppName).Where(name => name != null).Distinct().ToList();

    public static string AppName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            try { return process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } name ? name : process.ProcessName; }
            catch (Win32Exception) { return process.ProcessName; } // processus protégé, le nom court suffit
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; } // déjà fermé
    }

    class Report : IMFSensorActivitiesReportCallback
    {
        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            var pids = new List<uint>();
            report.GetCount(out var devices);
            for (uint d = 0; d < devices; d++)
            {
                report.GetActivityReport(d, out var device);
                device.GetProcessCount(out var processes);
                for (uint p = 0; p < processes; p++)
                {
                    device.GetProcessActivity(p, out var process);
                    process.GetStreamingState(out var streaming);
                    process.GetProcessId(out var pid);
                    if (streaming) pids.Add(pid);
                }
            }
            cameraPids = pids.Distinct().Order().ToArray();
            moved.Set();
            return 0;
        }
    }

    [ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivitiesReportCallback
    {
        [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport report);
    }

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivityMonitor
    {
        void Start();
        void Stop();
    }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivitiesReport
    {
        void GetCount(out uint count);
        void GetActivityReport(uint index, out IMFSensorActivityReport report);
    }

    // L'ordre de la vtable compte : les méthodes inutilisées restent déclarées pour garder les suivantes à leur place.
    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivityReport
    {
        void GetFriendlyName();
        void GetSymbolicLink();
        void GetProcessCount(out uint count);
        void GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorProcessActivity
    {
        void GetProcessId(out uint pid);
        void GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
    }
}
