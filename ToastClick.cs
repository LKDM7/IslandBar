using System.IO;
using System.Runtime.InteropServices;

namespace IslandBar;

// Rejoue le clic sur une notification : Windows appelle ce même activateur, déclaré par l'appli sur son raccourci.
// Ni compte ni réseau : l'appli reçoit le clic et ouvre elle-même la bonne conversation.
static class ToastClick
{
    const int VT_CLSID = 72, ASFW_ANY = -1;

    // Seul ce raccourci porte l'activateur, pas ceux du bureau ni du dossier « Discord Inc ».
    public static readonly string DiscordShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Discord.lnk");

    [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHGetPropertyStoreFromParsingName(string path, nint context, int flags, ref Guid iid, out AppVolume.IPropertyStore store);

    [DllImport("user32")] static extern bool AllowSetForegroundWindow(int processId);

    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface INotificationActivationCallback
    {
        void Activate([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, nint data, uint count);
    }

    public static bool Send(string appId, string tag)
    {
        if (string.IsNullOrEmpty(tag) || ActivatorOf(DiscordShortcut) is not { } activator) return false;
        AllowSetForegroundWindow(ASFW_ANY); // sinon l'appli s'ouvre derrière les autres fenêtres
        var callback = (INotificationActivationCallback)Activator.CreateInstance(Type.GetTypeFromCLSID(activator, true));
        callback.Activate(appId, $"type=click&tag={tag}", 0, 0);
        return true;
    }

    public static Guid? ActivatorOf(string shortcut)
    {
        var iid = typeof(AppVolume.IPropertyStore).GUID;
        SHGetPropertyStoreFromParsingName(shortcut, 0, 0, ref iid, out var store);
        var key = new AppVolume.PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 26 }; // PKEY_AppUserModel_ToastActivatorCLSID
        store.GetValue(ref key, out var value);
        try { return (value.Type & 0xFFFF) == VT_CLSID ? Marshal.PtrToStructure<Guid>(value.Text) : null; }
        finally { AppVolume.PropVariantClear(ref value); }
    }
}
