using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IslandBar;

public record AudioOutput(string Id, string Name);

// Volume d'une seule application, celui du mélangeur de volume Windows : le volume général n'est jamais modifié.
static class AppVolume
{
    const int eRender = 0, eConsole = 0, eMultimedia = 1, eCommunications = 2, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23, STGM_READ = 0;

    [DllImport("ole32")] internal static extern int PropVariantClear(ref PropVariant value);

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

    public static List<AudioOutput> Outputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var devices);
        devices.GetCount(out var count);
        var outputs = new List<AudioOutput>();
        for (var d = 0; d < count; d++)
        {
            devices.Item(d, out var device);
            device.GetId(out var id);
            outputs.Add(new(id, FriendlyName(device)));
        }
        return outputs;
    }

    public static string DefaultOutput()
    {
        ((IMMDeviceEnumerator)new MMDeviceEnumerator()).GetDefaultAudioEndpoint(eRender, eMultimedia, out var device);
        device.GetId(out var id);
        return id;
    }

    // Change la sortie de tout le PC, comme le panneau Son de Windows : Discord suit aussi.
    public static void SetDefaultOutput(string id)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        foreach (var role in new[] { eConsole, eMultimedia, eCommunications }) policy.SetDefaultEndpoint(id, role);
    }

    static string FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(STGM_READ, out var store);
        var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 }; // PKEY_Device_FriendlyName
        store.GetValue(ref key, out var value);
        try { return Marshal.PtrToStringUni(value.Text); }
        finally { PropVariantClear(ref value); }
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
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
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
        void OpenPropertyStore(int access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    internal struct PropertyKey
    {
        public Guid Format;
        public int Id;
    }

    // PROPVARIANT réduit à ce qui sert ici : le type, puis le pointeur du texte à l'offset 8.
    [StructLayout(LayoutKind.Sequential, Size = 24)]
    internal struct PropVariant
    {
        public long Type;
        public nint Text;
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient;

    // Interface non documentée, la seule qui change la sortie par défaut : c'est celle du panneau Son de Windows.
    // L'ordre de la vtable compte : les méthodes inutilisées restent déclarées pour garder SetDefaultEndpoint à sa place.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
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
