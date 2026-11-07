using System.Runtime.InteropServices;

namespace IslandBar;

public record Monitor(string Device, int Left, int Top, int Width, bool Primary)
{
    public int Number => int.TryParse(Device.TrimStart('\\', '.').Replace("DISPLAY", ""), out var number) ? number : 1;
}

static class Monitors
{
    const int MONITORINFOF_PRIMARY = 1;

    delegate bool Visit(nint monitor, nint context, nint area, nint data);

    [DllImport("user32")] static extern bool EnumDisplayMonitors(nint context, nint clip, Visit visit, nint data);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(nint monitor, ref Info info);

    [StructLayout(LayoutKind.Sequential)]
    struct Area { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info
    {
        public int Size;
        public Area Bounds, Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    public static List<Monitor> All()
    {
        var found = new List<Monitor>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new Info { Size = Marshal.SizeOf<Info>() };
            if (GetMonitorInfoW(monitor, ref info))
                found.Add(new(info.Device, info.Bounds.Left, info.Bounds.Top, info.Bounds.Right - info.Bounds.Left, (info.Flags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, 0);
        return found.OrderBy(m => m.Number).ToList();
    }

    // Le haut de l'écran, centré. Windows donne des pixels, WPF veut des unités à l'échelle de l'écran principal.
    public static (double Left, double Top) Place(Monitor monitor, double windowWidth, double scale) =>
        ((monitor.Left + monitor.Width / 2.0) / scale - windowWidth / 2, monitor.Top / scale);
}
