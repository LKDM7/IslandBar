using System.Windows;

namespace IslandBar;

static class Program
{
    [STAThread]
    static int Main(string[] args) =>
        args.Contains("--selftest") ? SelfTest() : new Application().Run(new MainWindow());

    // Code de sortie 0 = OK. Lancer : IslandBar.exe --selftest ; echo $LASTEXITCODE
    static int SelfTest()
    {
        var t = new DateTime(2026, 1, 1);
        var ok = Note.From(["Alice (#general, Serveur)", "salut\nça va"], t) is { Title: "Alice (#general, Serveur)", Body: "salut ça va", Initial: "A" }
            && Note.From([], t) is { Title: "Discord", Body: "", Initial: "D" }
            && Note.From(["😀 Bob"], t).Initial == "😀"
            && Note.From([""], t).Initial == ""
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
            && DiscordRpc.Link("123", null) == null;
        return ok ? 0 : 1;
    }
}
