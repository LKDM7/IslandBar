using System.Windows;
using System.Windows.Controls;

namespace IslandBar;

// Barre posée contre un côté de l'écran : elle se met debout, et la bulle comme le panneau s'ouvrent vers l'intérieur,
// à la hauteur où on l'a lâchée.
public partial class MainWindow
{
    const double SideSnap = 60; // un bout de la barre lâché à moins de 60 px d'un côté : elle s'y colle, voir Monitors.SideOf
    bool upright; // barre debout, voir Orient
    double anchor; // milieu de la barre debout, en hauteur dans la fenêtre, voir Monitors.Docked
    double room = double.PositiveInfinity; // hauteur d'écran utile sous le haut de la fenêtre, barre debout

    // Les éléments de la barre minimale s'empilent de haut en bas ; leurs écarts passent de la largeur à la hauteur.
    void Orient(bool stand)
    {
        if (stand == upright) return;
        upright = stand;
        Transpose(Compact);
        Compact.Orientation = stand ? Orientation.Vertical : Orientation.Horizontal;
        foreach (FrameworkElement child in Compact.Children)
        {
            Transpose(child);
            child.HorizontalAlignment = HorizontalAlignment.Center; // sans effet couchée ; debout, le badge s'étirerait
        }
    }

    static void Transpose(FrameworkElement element)
    {
        var margin = element.Margin;
        element.Margin = new Thickness(margin.Top, margin.Left, margin.Bottom, margin.Right);
    }

    // Debout, la barre grandit autour de son milieu sans sortir de l'écran, à 6 px du côté comme elle l'est du haut.
    void PlacePill()
    {
        if (!upright) return;
        var top = Monitors.PillTop(anchor, size[1], room, Height);
        Pill.Margin = settings.Side == Side.Left ? new Thickness(6, top, 0, 0) : new Thickness(0, top, 6, 0);
    }

    // Où se trouve la barre fermée, en largeur, quelle que soit sa forme du moment : panneau ouvert ou bulle, on la
    // déplace aussi, mais c'est la barre fermée qu'on colle ou décolle d'un côté.
    (double From, double To) BarSpan()
    {
        var thickness = Compact.DesiredSize.Width;
        if (!upright) return (Left + Width / 2 - Math.Max(96, thickness) / 2, Left + Width / 2 + Math.Max(96, thickness) / 2);
        thickness = Math.Max(28, thickness);
        return settings.Side == Side.Left ? (Left + 6, Left + 6 + thickness) : (Left + Width - 6 - thickness, Left + Width - 6);
    }
}
