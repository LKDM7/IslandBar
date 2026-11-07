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
        if (args.FirstOrDefault() == "--notify") return ScriptNotes.Send(string.Join(" ", args.Skip(1))); // sans guillemets, le texte arrive en plusieurs morceaux
        // Sans la carte graphique : 25 Mo de moins, et pas plus de CPU pour une fenêtre aussi petite.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        return new Application().Run(new MainWindow());
    }

    // Code de sortie 0 = OK. Lancer : IslandBar.exe --selftest ; echo $LASTEXITCODE
    static int SelfTest()
    {
        var t = new DateTime(2026, 1, 1);
        var main = new Monitor(@"\\.\DISPLAY1", 0, 0, 2560, 1392, true);
        var side = new Monitor(@"\\.\DISPLAY2", -1920, 156, 1920, 1236, false);
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
            && Avatars.PathIn("<toast><visual><binding><image src='file:///C%3A%5CTemp%5Ca.png' /></binding></visual></toast>") == @"C:\Temp\a.png"
            && Avatars.PathIn(@"<toast><visual><binding><image src='\\pirate\share\a.png' /></binding></visual></toast>") == null
            && Avatars.PathIn("<toast><visual><binding><image src='file:///%5C%5Cpirate%5Cshare%5Ca.png' /></binding></visual></toast>") == null
            && Avatars.PathIn("<toast><visual><binding><image src='file://pirate/share/a.png' /></binding></visual></toast>") == null
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
            && Monitors.Place(side, 460, 1) == (-1190, 156)
            && Monitors.Place(new(@"\\.\DISPLAY1", 0, 0, 2560, 1392, true), 460, 1.5) == (1280 / 1.5 - 230, 0)
            && side.Number == 2
            && Monitors.Moved(main, new(500, 300), new(460, 700), 20, 1) == (270, 280, false)
            && Monitors.Moved(main, new(500, 1000), new(460, 700), 20, 1) == (270, 320, true)
            && Monitors.Moved(main, new(-50, -50), new(460, 700), 20, 1) == (0, 0, false)
            && Monitors.Moved(main, new(9000, 9000), new(460, 700), 20, 1) == (2100, 692, true)
            && Monitors.Moved(side, new(-1000, 300), new(460, 700), 20, 1) == (-1230, 280, false)
            && Monitors.Moved(main, new(1000, 1000), new(230, 350), 10, 2) == (885, 346, true)
            && Monitors.Moved(new(@"\\.\DISPLAY3", 0, 0, 400, 800, false), new(100, 100), new(460, 700), 20, 1) == (0, 80, false) // plus étroit que la fenêtre
            && Monitors.Nearest([main, side], -100, 500) == side
            && Monitors.Nearest([main, side], 100, 500) == main
            && Monitors.Nearest([main, side], -10, 50) == main // le trou au-dessus de l'écran de gauche
            && JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Bar = new(1.5, 2) })).Bar == new Point(1.5, 2)
            && JsonSerializer.Deserialize<Settings>("{}").Bar == null
            && Settings.Parse("{abimé") is { Quiet: false, Bar: null, Apps.Count: 0 }
            && Settings.Parse("") is { Apps.Count: 0 }
            && Settings.Parse("null") is { Apps.Count: 0 }
            && Settings.Parse("{\"Quiet\":true}").Quiet
            && Settings.Parse("{\"Bar\":\"pas un point\"}").Bar == null
            && Settings.Parse("{\"Apps\":{\"x\":{\"Name\":\"X\",\"Show\":true}}}").Apps["x"] == new AppChoice("X", true) // fichier d'une ancienne version
            && !Settings.BannerOff("appli-qui-n-existe-pas")
            && AppVolume.ProcessName(uint.MaxValue) == ""
            && AppVolume.ProcessOf("Spotify.exe") == "Spotify"
            && AppVolume.ProcessOf("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "Spotify"
            && AppVolume.ProcessOf("Chrome") == "Chrome"
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
