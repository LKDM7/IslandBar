using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace IslandBar;

public record AppChoice(string Name, bool Show);

// Les applis vues dans les notifications, et celles que l'utilisateur veut dans l'île.
public class Settings
{
    const string Banners = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\";

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandBar", "settings.json");

    public Dictionary<string, AppChoice> Apps { get; set; } = [];
    public bool Quiet { get; set; } // ne pas déranger, posé à la main
    public string Screen { get; set; } // nom Windows de l'écran choisi ; absent : l'écran principal

    // Fichier absent ou abîmé : on repart des réglages par défaut plutôt que d'empêcher l'île de démarrer.
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, overwrite: true); // en deux temps : une coupure ne doit pas effacer les réglages
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
            if (app.Show) SetBanner(id, show);
    }
}
