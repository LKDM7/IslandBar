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
        if (args.Contains("--restore-banners"))
        {
            Settings.Load().SetBanners(show: true);
            return 0;
        }
        if (args.FirstOrDefault() == "--notify")
        {
            var icon = args.Length > 2 && args[1] == "--icon" ? System.IO.Path.GetFullPath(args[2]) : null; // chemin complet : l'île ne tourne pas dans le même dossier
            return ScriptNotes.Send(string.Join(" ", args.Skip(icon == null ? 1 : 3)), icon); // sans guillemets, le texte arrive en plusieurs morceaux
        }
        // Sans la carte graphique : 25 Mo de moins, et pas plus de CPU pour une fenêtre aussi petite.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        return new Application().Run(new MainWindow());
    }

}
