using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace IslandBar;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest();
        if (args.Contains("--restore-banners"))
        {
            Settings.Load().SetBanners(show: true);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--notify") return ScriptNotes.Send(args[1]);
        // Sans la carte graphique : 25 Mo de moins, et pas plus de CPU pour une fenêtre aussi petite.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        return new Application().Run(new MainWindow());
    }

    // Code de sortie 0 = OK. Lancer : IslandBar.exe --selftest ; echo $LASTEXITCODE
    static int SelfTest()
    {
        var t = new DateTime(2026, 1, 1);
        var ok = Note.From(["Alice (#general, Serveur)", "salut\nça va"], t) is { Title: "Alice (#general, Serveur)", Body: "salut ça va", Initial: "A" }
            && Note.From([], t) is { Title: "Discord", Body: "", Initial: "D" }
            && Note.From(["😀 Bob"], t).Initial == "😀"
            && Note.From([""], t).Initial == ""
            && Note.From([], t, "WhatsApp") is { Title: "WhatsApp", AppName: "WhatsApp", Stamp: "WhatsApp · 00:00" }
            && new Note("a", "", t) { AppId = "com.squirrel.Discord.Discord" }.FromProcess("Discord")
            && new Note("a", "", t) { AppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" }.FromProcess("WhatsApp")
            && !new Note("a", "", t) { AppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" }.FromProcess("App!")
            && !new Note("a", "", t) { AppId = "Chrome" }.FromProcess("")
            && !new Note("a", "", t).FromProcess("IslandBar")
            && MainWindow.Accent([128, 128, 128, 255]) == Colors.White
            && MainWindow.Accent([0, 0, 200, 0]) == Colors.White
            && MainWindow.Accent([0, 0, 200, 255]) == Color.FromRgb(255, 64, 64)
            && MainWindow.Accent([200, 0, 0, 255, 200, 0, 0, 255, 0, 0, 200, 255]) == Color.FromRgb(64, 64, 255)
            && Updater.IsNewer("v1.2.0", new(1, 1, 0, 0))
            && !Updater.IsNewer("v1.1.0", new(1, 1, 0, 0))
            && !Updater.IsNewer("v1.0.9", new(1, 1, 0, 0))
            && !Updater.IsNewer("nimportequoi", new(1, 1, 0, 0))
            && !Updater.IsNewer(null, new(1, 1, 0, 0))
            && Updater.Parse(Asset("v1.2.0", GoodUrl, "sha256:ab"), new(1, 1, 0, 0)) is { Tag: "v1.2.0", Url: GoodUrl, Sha256: "ab" }
            && Updater.Parse(Asset("v1.1.0", GoodUrl, "sha256:ab"), new(1, 1, 0, 0)) == null
            && Updater.Parse(Asset("v1.2.0", "https://pirate.example/IslandBar-Setup.exe", "sha256:ab"), new(1, 1, 0, 0)) == null
            && Updater.Parse(Asset("v1.2.0", "https://github.com/LKDM7/IslandBar/releases/download/../../../../pirate/x.exe", "sha256:ab"), new(1, 1, 0, 0)) == null
            && Updater.Parse(Asset("v1.2.0", GoodUrl, "md5:ab"), new(1, 1, 0, 0)) == null
            && Updater.Verified("abc"u8.ToArray(), "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
            && !Updater.Verified("abd"u8.ToArray(), "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
            && JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Apps = { ["x"] = new("X", true) } })).Apps["x"] == new AppChoice("X", true)
            && JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Quiet = true })).Quiet
            && !JsonSerializer.Deserialize<Settings>("{\"Apps\":{}}").Quiet
            && MainWindow.Clock(TimeSpan.FromSeconds(3725)) == "62:05"
            && Avatars.PathIn(@"<toast><visual><binding><image placement='appLogoOverride' src='C:\Temp/a.png' /></binding></visual></toast>") == @"C:\Temp\a.png"
            && Avatars.PathIn("<toast><visual><binding><image src='file:///C:/Temp/a.png' /></binding></visual></toast>") == @"C:\Temp\a.png"
            && Avatars.PathIn(@"<toast><visual><binding><image src='\\pirate\share\a.png' /></binding></visual></toast>") == null
            && Avatars.PathIn("<toast><visual><binding><image src='https://pirate.example/a.png' /></binding></visual></toast>") == null
            && Avatars.PathIn("<toast><visual><binding></binding></visual></toast>") == null
            && Avatars.PathIn(null) == null
            && AppVolume.Matches("Chrome", "chrome")
            && AppVolume.Matches("Spotify.exe", "Spotify")
            && AppVolume.Matches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")
            && !AppVolume.Matches("Chrome", "msedge")
            && !AppVolume.Matches("Chrome", "")
            && DiscordRpc.Link("123", "456") == "discord://-/channels/123/456"
            && DiscordRpc.Link(null, "456") == "discord://-/channels/@me/456"
            && DiscordRpc.Link("", "456") == "discord://-/channels/@me/456"
            && DiscordRpc.Link("123", "456&calc") == null
            && DiscordRpc.Link("1/../2", "456") == null
            && DiscordRpc.Link("123", null) == null
            && new Note("Alice", "2", t) { AppId = "a", Count = 3 }.Label == "Alice (3)"
            && new Note("Alice", "2", t) { AppId = "a" }.Label == "Alice"
            && new Note("Alice", "2", t) { AppId = "a" }.SameChat(new Note("Alice", "1", t) { AppId = "a" })
            && !new Note("Alice", "2", t) { AppId = "a" }.SameChat(new Note("Bob", "1", t) { AppId = "a" })
            && !new Note("Alice", "2", t) { AppId = "a" }.SameChat(new Note("Alice", "1", t) { AppId = "b" })
            && !new Note("IslandBar", "2", t).SameChat(new Note("IslandBar", "1", t))
            && Monitors.Place(new(@"\\.\DISPLAY2", -1920, 156, 1920, false), 460, 1) == (-1190, 156)
            && Monitors.Place(new(@"\\.\DISPLAY1", 0, 0, 2560, true), 460, 1.5) == (1280 / 1.5 - 230, 0)
            && new Monitor(@"\\.\DISPLAY2", 0, 0, 1, false).Number == 2
            && Sensors.AppName((uint)Environment.ProcessId) is { Length: > 0 }
            && Sensors.AppName(uint.MaxValue) == null
            && Math.Abs(MainWindow.BarScale(0, 0) - .35) < 1e-9
            && Math.Abs(MainWindow.BarScale(.3, 0) - 1) < 1e-9
            && Math.Abs(MainWindow.BarScale(.45, 0) - .675) < 1e-9
            && Math.Abs(MainWindow.BarScale(.72, 2) - .675) < 1e-9; // 3e barre : un aller dure .48 s
        return ok ? 0 : 1;
    }

    const string GoodUrl = "https://github.com/LKDM7/IslandBar/releases/download/v1.2.0/IslandBar-Setup.exe";

    static string Asset(string tag, string url, string digest) =>
        $$"""{"tag_name":"{{tag}}","assets":[{"name":"IslandBar-Setup.exe","browser_download_url":"{{url}}","digest":"{{digest}}"}]}""";
}
