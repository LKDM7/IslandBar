using System.Globalization;

namespace IslandBar;

// Français si Windows est en français, anglais sinon. Deux langues seulement : pas besoin de fichiers de ressources.
public static class Strings
{
    static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    static string T(string french, string english) => French ? french : english;

    public static string Previous => T("Précédent", "Previous");
    public static string PlayPause => T("Lecture / pause", "Play / pause");
    public static string Next => T("Suivant", "Next");
    public static string Settings => T("Réglages", "Settings");
    public static string Clear => T("Effacer", "Clear");
    public static string Notifications => "Notifications";
    public static string NothingPlaying => T("Rien en lecture", "Nothing playing");
    public static string Quit => T("Quitter IslandBar Lite", "Quit IslandBar Lite");
    public static string NoNotifications => T("Aucune notification", "No notifications");
    public static string AccessDenied => T("Accès refusé : Paramètres Windows › Confidentialité › Notifications", "Access denied: Windows Settings › Privacy › Notifications");
    public static string DoNotDisturb => T("Ne pas déranger", "Do not disturb");
    public static string NotificationsQuiet => $"{Notifications} · {DoNotDisturb}";
    public static string Always => T("Toujours", "Always");
    public static string InFullScreen => T("En plein écran", "In full screen");
    public static string Off => T("Désactivé", "Off");
    public static string ShowFrom => T("Afficher les notifications de", "Show notifications from");
    public static string OutOf(int part, int total) => T($"{part} sur {total}", $"{part} of {total}");
    public static string NoApps => T("Aucune application pour l'instant", "No apps yet");
    public static string ScreenLabel => T("Afficher l'île sur", "Show the island on");
    public static string ClickToInstall => T("Clique pour l'installer", "Click to install");
    public static string Downloading => T("Téléchargement en cours…", "Downloading…");
    public static string UpdateFailed => T("Mise à jour impossible", "Update failed");
    public static string HotkeyTaken => T("Ctrl + Alt + ² est déjà pris (IslandBar Lite tourne peut-être déjà).", "Ctrl + Alt + the key left of 1 is already taken (IslandBar Lite may already be running).");
    public static string Help => T(
        "Espace lecture · ← → piste · ↑ ↓ volume · S sortie\nEntrée ouvrir · Suppr effacer · Échap fermer",
        "Space play · ← → track · ↑ ↓ volume · S output\nEnter open · Del clear · Esc close");

    public static string VersionAvailable(string version) => T($"Version {version} disponible", $"Version {version} available");
    public static string Percent(double value) => French ? $"{value:0} %" : $"{value:0}%";
    public static string Microphone(string apps) => T($"Micro : {apps}", $"Microphone: {apps}");
    public static string Camera(string apps) => T($"Caméra : {apps}", $"Camera: {apps}");
    public static string Screen(int number, bool primary) => T($"Écran {number}", $"Screen {number}") + (primary ? T(" (principal)", " (main)") : "");
}
