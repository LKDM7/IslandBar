using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IslandBar;

// Volume d'une seule application, celui du mélangeur de volume Windows : le volume général n'est jamais modifié.
static class AppVolume
{
    const int eRender = 0, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;

    // Lit le premier flux trouvé : une app garde le même niveau sur tous ses flux, puisque Set les règle ensemble.
    public static float? Get(string sourceApp)
    {
        foreach (var session in Sessions(sourceApp))
        {
            session.GetMasterVolume(out var level);
            return level;
        }
        return null;
    }

    public static void Set(string sourceApp, float level)
    {
        var context = Guid.Empty;
        foreach (var session in Sessions(sourceApp)) session.SetMasterVolume(level, ref context);
    }

    // sourceApp vaut par exemple "Chrome", "Spotify.exe" ou "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify".
    public static bool Matches(string sourceApp, string processName)
    {
        var app = sourceApp.Split('!')[^1];
        if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app[..^4];
        return app.Equals(processName, StringComparison.OrdinalIgnoreCase);
    }

    // Parcourt toutes les sorties actives : une app peut être routée ailleurs que sur la sortie par défaut.
    static IEnumerable<ISimpleAudioVolume> Sessions(string sourceApp)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var devices);
        devices.GetCount(out var deviceCount);
        for (var d = 0; d < deviceCount; d++)
        {
            devices.Item(d, out var device);
            var managerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref managerId, CLSCTX_ALL, 0, out var manager);
            ((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions);
            sessions.GetCount(out var sessionCount);
            for (var s = 0; s < sessionCount; s++)
            {
                sessions.GetSession(s, out var control);
                control.GetProcessId(out var pid);
                if (Matches(sourceApp, ProcessName(pid))) yield return (ISimpleAudioVolume)control;
            }
        }
    }

    static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException) { return ""; } // le processus vient de se fermer
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(nint sessionId, int flags, out nint control);
        void GetSimpleAudioVolume(nint sessionId, int flags, out nint volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    // L'ordre de la vtable compte : les méthodes inutilisées restent déclarées pour garder GetProcessId à sa place.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName(out nint name);
        void SetDisplayName(nint name, ref Guid context);
        void GetIconPath(out nint path);
        void SetIconPath(nint path, ref Guid context);
        void GetGroupingParam(out Guid group);
        void SetGroupingParam(ref Guid group, ref Guid context);
        void RegisterAudioSessionNotification(nint client);
        void UnregisterAudioSessionNotification(nint client);
        void GetSessionIdentifier(out nint id);
        void GetSessionInstanceIdentifier(out nint id);
        void GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context);
        void GetMasterVolume(out float level);
    }
}
