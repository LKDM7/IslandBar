using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Win32;

namespace IslandBar;

public record AppChoice(string Name, bool Show, bool OffInWindows = false); // OffInWindows : bannière déjà coupée avant l'île, à ne pas rétablir

// Les applis vues dans les notifications, et celles que l'utilisateur veut dans l'île.
public class Settings
{
    const string Banners = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\";

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandBar", "settings.json");

    public Dictionary<string, AppChoice> Apps { get; set; } = [];
    public bool Quiet { get; set; } // ne pas déranger, posé à la main
    public bool QuietFullscreen { get; set; } = true; // ne pas déranger quand Windows signale un plein écran ; absent d'un ancien fichier : vrai
    public string Screen { get; set; } // nom Windows de l'écran choisi ; absent : l'écran principal
    public Point? Bar { get; set; } // milieu de la barre posée à la main ; absent : en haut, au centre de l'écran
    public Side Side { get; set; } // collée debout contre un côté, à la hauteur de Bar ; None : couchée

    [JsonIgnore] public bool Unreadable { get; private set; } // fichier présent mais illisible pour l'instant, voir Load

    // Fichier absent ou abîmé : on repart des réglages par défaut plutôt que d'empêcher l'île de démarrer.
    // Présent mais bloqué (antivirus, sauvegarde) : les défauts servent pour cette fois, sans jamais écraser le vrai fichier.
    public static Settings Load(string path = null)
    {
        try { return Parse(File.ReadAllText(path ?? FilePath)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new() { Unreadable = true }; }
    }

    public static Settings Parse(string json)
    {
        try { return JsonSerializer.Deserialize<Settings>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        if (Unreadable) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        var temporary = FilePath + ".tmp";
        // Écrit sur le disque avant de remplacer : après une coupure de courant, un fichier vide ferait croire que les
        // bannières coupées par l'île l'avaient été par l'utilisateur, et elles ne reviendraient plus.
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, this);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, FilePath, overwrite: true); // en deux temps : une coupure ne doit pas effacer les réglages
    }

    public static bool BannerOff(string appId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Banners + appId);
        return key?.GetValue("ShowBanner") is 0;
    }

    // Une appli affichée dans l'île ne doit pas aussi afficher sa bannière Windows : c'est la double notification.
    // La clé existe dès que l'appli a notifié ; sinon on ne crée rien et Windows garde sa bannière.
    public static void SetBanner(string appId, bool show)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Banners + appId, writable: true);
        if (key == null) return;
        if (show) key.DeleteValue("ShowBanner", throwOnMissingValue: false);
        else key.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
    }

    // Seules les applis cochées passent par l'île : les autres gardent le réglage choisi dans Windows.
    public void SetBanners(bool show)
    {
        foreach (var (id, app) in Apps)
            if (app.Show && !app.OffInWindows) SetBanner(id, show);
    }
}

public enum Side { None, Left, Right }
