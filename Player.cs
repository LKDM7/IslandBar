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

    static List<AutomationElement> Windows(string sourceApp)
    {
        var processes = Process.GetProcessesByName(AppVolume.ProcessOf(sourceApp));
        var ids = processes.Select(process => process.Id).ToHashSet();
        foreach (var process in processes) process.Dispose();
        return AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(window => ids.Contains(window.Current.ProcessId) && window.Current.Name != "").ToList();
    }

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
