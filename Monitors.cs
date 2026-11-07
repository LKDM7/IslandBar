using System.Runtime.InteropServices;
using System.Windows;

namespace IslandBar;

// Bottom : le bas de la zone utile, au-dessus de la barre des tâches.
public record Monitor(string Device, int Left, int Top, int Width, int Bottom, bool Primary)
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
                found.Add(new(info.Device, info.Bounds.Left, info.Bounds.Top, info.Bounds.Right - info.Bounds.Left, info.Work.Bottom, (info.Flags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, 0);
        return found.OrderBy(m => m.Number).ToList();
    }

    // Le haut de l'écran, centré. Windows donne des pixels, WPF veut des unités à l'échelle de l'écran principal.
    public static (double Left, double Top) Place(Monitor monitor, double windowWidth, double scale) =>
        ((monitor.Left + monitor.Width / 2.0) / scale - windowWidth / 2, monitor.Top / scale);

    // Une place choisie à la main, ramenée sur l'écran : assez loin des côtés pour que le panneau tienne en largeur.
    // Sous le milieu de l'écran, l'île s'ouvre vers le haut, sinon le panneau sortirait par le bas.
    // shortcut: au milieu d'un petit écran, un panneau très déplié peut encore dépasser, à revoir si ça gêne.
    public static (double Left, double Top, bool Up) Moved(Monitor monitor, Point bar, Size window, double barEdge, double scale)
    {
        var (top, bottom) = (monitor.Top / scale, monitor.Bottom / scale);
        var up = bar.Y > (top + bottom) / 2;
        // Pas Math.Clamp : sur un écran plus étroit que la fenêtre, il lève une erreur à chaque lancement.
        return (Math.Max(monitor.Left / scale, Math.Min(bar.X - window.Width / 2, (monitor.Left + monitor.Width) / scale - window.Width)),
            up ? Math.Min(bar.Y + barEdge, bottom) - window.Height : Math.Max(bar.Y - barEdge, top),
            up);
    }

    // Le plus proche plutôt que celui qui contient le point : la barre peut être posée dans un trou entre deux écrans.
    public static Monitor Nearest(List<Monitor> monitors, double x, double y) => monitors.MinBy(m =>
        Math.Max(Math.Max(m.Left - x, x - m.Left - m.Width), 0) + Math.Max(Math.Max(m.Top - y, y - m.Bottom), 0));
}
