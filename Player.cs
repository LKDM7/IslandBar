using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace IslandBar;

// Windows ne donne ni l'adresse de la page ni l'identifiant du morceau : on ramène le lecteur au premier plan et,
// dans un navigateur, on retrouve l'onglet qui joue grâce à son titre.
static class Player
{
    const int SW_RESTORE = 9;

    [DllImport("user32")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc each, nint lParam);
    [DllImport("user32")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint hwnd, char[] text, int max);
    delegate bool EnumProc(nint hwnd, nint lParam);

    // Faux si le lecteur n'a aucune fenêtre visible.
    public static bool Show(string sourceApp, string title)
    {
        var windows = Windows(sourceApp);
        foreach (var window in windows)
        {
            if (Tab(window, title) is not { } tab) continue;
            Front(window);
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            return true;
        }
        if (windows.Count == 0) return false;
        Front(windows.Find(window => window.Current.Name.Contains(title, StringComparison.OrdinalIgnoreCase)) ?? windows[0]);
        return true;
    }

    // Les fenêtres visibles et titrées du lecteur, demandées à Windows : par UI Automation, il fallait interroger une à
    // une toutes les fenêtres du bureau, deux allers-retours chacune.
    static List<(nint Handle, string Title)> Titled(string sourceApp)
    {
        var processes = Process.GetProcessesByName(AppVolume.ProcessOf(sourceApp));
        var ids = processes.Select(process => (uint)process.Id).ToHashSet();
        foreach (var process in processes) process.Dispose();
        var found = new List<(nint, string)>();
        var text = new char[512];
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindowThreadProcessId(hwnd, out var pid) != 0 && ids.Contains(pid)
                && GetWindowTextW(hwnd, text, text.Length) is > 0 and var length) found.Add((hwnd, new string(text, 0, length)));
            return true;
        }, 0);
        return found;
    }

    static List<AutomationElement> Windows(string sourceApp) => Titled(sourceApp).Select(window => AutomationElement.FromHandle(window.Handle)).ToList();

    // Un titre court se retrouve dans d'autres onglets : celui qui commence par ce titre passe avant.
    static AutomationElement Tab(AutomationElement window, string title) => title == "" ? null :
        window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>()
            .Where(tab => tab.Current.Name.Contains(title, StringComparison.OrdinalIgnoreCase)
                && tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
            .OrderByDescending(tab => tab.Current.Name.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    static void Front(AutomationElement window) => Front((nint)window.Current.NativeWindowHandle);

    // Faux si la fenêtre a été fermée entre-temps.
    public static bool Front(nint handle)
    {
        if (!IsWindow(handle)) return false;
        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
        SetForegroundWindow(handle);
        return true;
    }
}
