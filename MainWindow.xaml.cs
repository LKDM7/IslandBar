using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace IslandBar;

public record Note(string Title, string Body, DateTime When)
{
    public string Initial => StringInfo.GetNextTextElement(Title).ToUpperInvariant();
    public ImageSource Avatar { get; init; }
    public string Tag { get; init; } // étiquette Windows : Discord retrouve sa notification avec, voir ToastClick
    public string AppId { get; init; } = ""; // vide : message de l'île elle-même
    public string AppName { get; init; } = "IslandBar Lite";
    public int Count { get; init; } = 1; // messages regroupés sur cette ligne
    public Release Update { get; init; }
    public string Stamp => $"{AppName} · {When:HH:mm}";
    public string Label => Count > 1 ? $"{Title} ({Count})" : Title;

    // Même appli et même titre : même personne, au même endroit. Les messages de l'île restent séparés.
    public bool SameChat(Note other) => AppId != "" && AppId == other.AppId && Title == other.Title;

    public static Note From(IReadOnlyList<string> texts, DateTime when, string app = "Discord") =>
        new(texts.FirstOrDefault() ?? app, string.Join(" ", texts.Skip(1)).ReplaceLineEndings(" "), when) { AppName = app };

    // "com.squirrel.Discord.Discord" ou "5319275A.WhatsAppDesktop_xxx!App" : le processus est une partie de l'identifiant.
    public bool FromProcess(string process) =>
        AppId != "" && process != "" && AppId.Split('!')[0].Contains(process, StringComparison.OrdinalIgnoreCase);
}

public record Choice(string Name, bool On, Action Pick);

public partial class MainWindow : Window
{
    [DllImport("user32")] static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("kernel32")] static extern bool SetProcessWorkingSetSize(nint process, nint min, nint max);
    [DllImport("user32")] static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32")] static extern nint GetForegroundWindow();
    [DllImport("user32")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32")] static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("shell32")] static extern int SHQueryUserNotificationState(out int state);

    const int WM_HOTKEY = 0x312, GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, MAPVK_VSC_TO_VK = 1;
    const uint ScanCodeSquare = 0x29; // la touche à gauche de "1" : ² en AZERTY, quelle que soit la disposition
    const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    const double ToastSeconds = 5, MaxNotes = 30, VolumeStep = .05, BarFps = 15;
    const double Stiffness = 230, Damping = 23; // amortissement < 1 : le léger rebond façon iOS
    const double HoldMs = 250, BarEdge = 20, HomeSnap = 60; // BarEdge : du bord de la fenêtre au milieu de la barre

    static readonly Geometry PlayGeometry = Geometry.Parse("M7 4.5v15l12.5-7.5z");
    static readonly Geometry PauseGeometry = Geometry.Parse("M6 4.5h4.5v15H6zM13.5 4.5H18v15h-4.5z");
    static readonly Brush NoArt = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
    static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "IslandBar.log");
    static readonly string NotificationWal = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db-wal");
    const int FullCheckEvery = 10;
    static readonly TimeSpan PlayerTimeout = TimeSpan.FromSeconds(3); // un lecteur figé ne doit pas bloquer les notifications

    readonly ObservableCollection<Note> notes = [];
    readonly HashSet<uint> seen = [];
    readonly Dictionary<string, string> channelLinks = []; // titre de notification -> lien vers son salon
    readonly double[] size = [96, 28, 14], velocity = new double[3], goal = [96, 28, 14]; // largeur, hauteur, rayon
    readonly Settings settings = Settings.Load();
    readonly SolidColorBrush accent = new(Colors.White);
    readonly ScaleTransform[] barScales;
    readonly DispatcherTimer wave, hold;

    GlobalSystemMediaTransportControlsSessionManager media;
    Session session;
    UserNotificationListener listener;
    FrameworkElement shown;
    Note toast;
    DateTime toastUntil;
    string trackKey, lastError;
    bool expanded, playing, polling, springing, hidden = true, firstNotificationPoll = true;
    List<AudioOutput> outputs = [];
    TimeSpan duration;
    nint previousWindow;
    Point grip;
    object grabbed;
    bool dragged;
    TimeSpan lastFrame;
    (long, DateTime) notificationStamp;
    List<string> microphoneApps = [], cameraApps = [];
    int followUps, idlePolls = FullCheckEvery; // le premier passage lit toujours les notifications

    public MainWindow()
    {
        Dispatcher.UnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true; // fermée, l'île laisserait les bannières coupées et les notifications invisibles
        };
        InitializeComponent();
        // Posé à la main : un pinceau placé dans les ressources est gelé par WPF, et celui-ci s'anime.
        ProgressFill.Fill = accent;
        foreach (System.Windows.Shapes.Shape bar in Bars.Children) bar.Fill = accent;
        barScales = Bars.Children.Cast<UIElement>().Select(bar => (ScaleTransform)(bar.RenderTransform = new ScaleTransform(1, .35))).ToArray();
        wave = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / BarFps), DispatcherPriority.Render, (_, _) => Wave(), Dispatcher) { IsEnabled = false };
        hold = new DispatcherTimer(TimeSpan.FromMilliseconds(HoldMs), DispatcherPriority.Input, (_, _) => Lift(), Dispatcher) { IsEnabled = false };
        shown = Compact;
        NoteList.ItemsSource = notes;
        MiniArt.Fill = BigArt.Fill = NoArt;
        Center();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Center); // cet événement arrive sur un autre fil
        Loaded += OnLoaded;
    }

    Monitor Chosen(List<Monitor> monitors) => monitors.Find(m => m.Device == settings.Screen) ?? monitors.Find(m => m.Primary);

    static double Scale(List<Monitor> monitors) => monitors.Find(m => m.Primary).Width / SystemParameters.PrimaryScreenWidth;

    void Center()
    {
        var monitors = Monitors.All();
        var scale = Scale(monitors);
        var up = false;
        (Left, Top) = Monitors.Place(Chosen(monitors), Width, scale);
        if (settings.Bar is { } bar) (Left, Top, up) = Monitors.Moved(Chosen(monitors), bar, new(Width, Height), BarEdge, scale);
        Pill.VerticalAlignment = up ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Pill.Margin = new Thickness(0, up ? 0 : 6, 0, up ? 6 : 0);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        if (RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, MapVirtualKeyW(ScanCodeSquare, MAPVK_VSC_TO_VK))) return;
        MessageBox.Show(Strings.HotkeyTaken, "IslandBar Lite");
        Environment.Exit(1); // pas de fermeture normale : l'île déjà lancée garde les bannières coupées, voir OnClosed
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Safe(async () =>
        {
            settings.SetBanners(show: false); // l'île prend le relais tant qu'elle tourne, voir Quit
            listener = UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed) return;
            listener = null;
            Empty.Text = Strings.AccessDenied;
        });
        _ = new DiscordRpc((title, link) => channelLinks[title] = link, Log).Run();
        Poll();
        // shortcut: polling 1 s au lieu des events, une seule boucle pour tout ; passer aux events si l'app est packagée MSIX
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher).Start();
        new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => FreeMemory(), Dispatcher).Start();
        _ = Safe(CheckUpdates);
        _ = Safe(() => ScriptNotes.Listen((text, icon) =>
        {
            Show(new Note("IslandBar Lite", text, DateTime.Now) { Avatar = LoadImage(Avatars.Local(icon)) });
            Apply();
        }));
        Sensors.Watch(
            (microphone, camera) => Dispatcher.BeginInvoke(() => ShowSensors(microphone, camera)),
            ex => Dispatcher.BeginInvoke(() => Log(ex)));
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return 0;
        if (expanded) Collapse(restoreFocus: true); else Expand();
        handled = true;
        return 0;
    }

    // --- Ouverture / fermeture ---

    void Expand()
    {
        previousWindow = GetForegroundWindow();
        expanded = true;
        toast = null;
        SetNoActivate(false);
        Activate();
        RefreshVolume();
        RefreshOutput();
        Apply();
    }

    void Collapse(bool restoreFocus)
    {
        if (!expanded) return;
        expanded = false;
        SetNoActivate(true);
        if (restoreFocus) SetForegroundWindow(previousWindow);
        Unfold(null);
    }

    // La barre ne doit jamais voler le focus, sauf quand on l'ouvre pour la piloter au clavier.
    void SetNoActivate(bool on)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, on ? style | WS_EX_NOACTIVATE : style & ~(nint)WS_EX_NOACTIVATE);
    }

    void OnDeactivated(object sender, EventArgs e) => Collapse(restoreFocus: false);

    void PillHover(object sender, MouseEventArgs e) => Apply(); // le nom de l'appli qui écoute s'affiche au survol

    void PillRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!expanded) Expand();
        if (!SettingsMenu.IsVisible) Unfold(SettingsMenu);
    }

    void PillClick(object sender, MouseButtonEventArgs e)
    {
        hold.Stop();
        if (Pill.IsMouseCaptured)
        {
            var moved = dragged;
            Pill.ReleaseMouseCapture();
            if (moved) return;
            if (grabbed == MiniArt) // clic un peu long, resté sur place : c'est un clic normal
            {
                ArtClick(sender, e);
                return;
            }
        }
        if (expanded) return;
        if (toast == null)
        {
            Expand();
            return;
        }
        OpenNote(toast);
        toast = null;
        Apply();
    }

    // --- Déplacement : clic maintenu sur la barre, ou sur le fond du panneau ---

    void PillPress(object sender, MouseButtonEventArgs e)
    {
        if (expanded && e.OriginalSource != Pill) return; // les boutons et les curseurs gardent leur clic
        grip = e.GetPosition(this);
        grabbed = e.OriginalSource;
        hold.Start();
    }

    // La souris n'est prise qu'ici : un clic bref doit encore atteindre la pochette.
    void Lift()
    {
        hold.Stop();
        if (Mouse.LeftButton != MouseButtonState.Pressed || !Pill.CaptureMouse()) return;
        dragged = false;
        Pill.Cursor = Cursors.SizeAll;
    }

    void PillDrag(object sender, MouseEventArgs e)
    {
        if (!Pill.IsMouseCaptured) return;
        var offset = e.GetPosition(this) - grip;
        if (!dragged && offset.Length < SystemParameters.MinimumHorizontalDragDistance) return; // la main qui tremble ne déplace rien
        dragged = true;
        Left += offset.X;
        Top += offset.Y;
    }

    void PillDropped(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource != Pill) return; // un curseur ou un bouton qui rend la souris remonte aussi jusqu'ici
        Pill.Cursor = null;
        if (!dragged) return;
        var monitors = Monitors.All();
        var scale = Scale(monitors);
        var bar = new Point(Left + Width / 2, Pill.VerticalAlignment == VerticalAlignment.Bottom ? Top + Height - BarEdge : Top + BarEdge);
        var screen = Monitors.Nearest(monitors, bar.X * scale, bar.Y * scale);
        var home = Monitors.Place(screen, Width, scale);
        settings.Screen = screen.Device;
        settings.Bar = (bar - new Point(home.Left + Width / 2, home.Top + BarEdge)).Length < HomeSnap ? null : bar;
        SaveSettings();
        Center();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!expanded) return;
        switch (e.Key)
        {
            case Key.Escape: Collapse(restoreFocus: true); break;
            case Key.Space: PlayPause(null, null); break;
            case Key.Left: Prev(null, null); break;
            case Key.Right: Next(null, null); break;
            case Key.Up: SetVolume(VolumeScale.ScaleX + VolumeStep); break;
            case Key.Down: SetVolume(VolumeScale.ScaleX - VolumeStep); break;
            case Key.S: OutputClick(null, null); break;
            case Key.Enter: OpenNote(notes.FirstOrDefault()); break;
            case Key.Delete: ClearNotes(null, null); break;
            default: return;
        }
        e.Handled = true;
    }

    // L'île dort presque tout le temps : 150 Mo de mémoire vive tombent à 25 Mo si elle les rend à Windows, sans CPU en plus.
    void FreeMemory()
    {
        if (expanded || springing) return; // pas pendant que l'utilisateur regarde une animation
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(process.Handle, -1, -1);
    }

    void ShowSensors(List<string> microphone, List<string> camera)
    {
        (microphoneApps, cameraApps) = (microphone, camera);
        var used = new List<string>();
        if (microphone.Count > 0) used.Add(Strings.Microphone(string.Join(", ", microphone)));
        if (camera.Count > 0) used.Add(Strings.Camera(string.Join(", ", camera)));
        SensorText.Text = string.Join(" · ", used);
        Apply();
    }

    // Sans île, Windows doit pouvoir afficher lui-même les notifications, sinon une alerte passerait inaperçue.
    // Ici plutôt que sur Quitter : Alt + F4 et la fermeture de session passent aussi par là.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { settings.SetBanners(show: true); }
        catch (Exception ex) { Log(ex); }
    }

    void Quit(object sender, RoutedEventArgs e) => Close();

    // Les menus se déroulent dans l'île, qui s'agrandit avec eux ; un seul à la fois.
    void Unfold(FrameworkElement menu)
    {
        var open = menu?.Visibility == Visibility.Visible ? null : menu;
        OutputMenu.Visibility = open == OutputMenu ? Visibility.Visible : Visibility.Collapsed;
        SettingsMenu.Visibility = open == SettingsMenu ? Visibility.Visible : Visibility.Collapsed;
        if (open != SettingsMenu) ScreenHeader.IsChecked = AppsHeader.IsChecked = false;
        OutputChevron.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open == OutputMenu ? 180 : 0, TimeSpan.FromMilliseconds(180)));
        if (open == OutputMenu) FillOutputs();
        if (open == SettingsMenu) FillSettings();
        Apply();
    }

    // Un seul sous-menu ouvert à la fois : le panneau ne doit pas dépasser de la fenêtre.
    void SectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var section in new[] { ScreenHeader, AppsHeader })
            if (section != sender) section.IsChecked = false;
        UpdateLayout(); // Apply mesure le panneau, et ces lignes sont trop profondes pour l'invalider seules
        Apply();
    }

    async void ArtClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // sinon le même clic ouvrirait aussi le panneau
        if (session == null) return;
        var (app, title) = (session.SourceAppUserModelId, TrackTitle.Text);
        Collapse(restoreFocus: false);
        try
        {
            // Hors du fil de l'interface : parcourir les onglets d'un navigateur prend plusieurs centaines de ms.
            if (await Task.Run(() => Player.Show(app, title))) return;
            ToastClick.AllowSetForegroundWindow(ToastClick.ASFW_ANY); // sinon l'appli s'ouvre derrière les autres fenêtres
            Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{app}") { UseShellExecute = true });
        }
        catch (Exception ex) { Log(ex); } // fenêtre fermée entre-temps, ou lecteur introuvable
    }

    void ChoiceClick(object sender, RoutedEventArgs e) => ((Choice)((FrameworkElement)sender).DataContext).Pick();

    void GearClick(object sender, RoutedEventArgs e) => Unfold(SettingsMenu);

    void OutputClick(object sender, RoutedEventArgs e)
    {
        if (OutputRow.IsVisible) Unfold(OutputMenu);
    }

    void FillOutputs() =>
        OutputList.ItemsSource = outputs.Select(o => new Choice(o.Name, o.Name == OutputText.Text, () => ChooseOutput(o))).ToList();

    void FillSettings()
    {
        QuietRow.Content = new Choice(Strings.DoNotDisturb, settings.Quiet, ToggleQuiet);
        var monitors = Monitors.All();
        ScreenChoice.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var chosen = Chosen(monitors);
        ScreenHeader.Tag = Strings.Screen(chosen.Number, chosen.Primary);
        ScreenList.ItemsSource = monitors.Select(m => new Choice(Strings.Screen(m.Number, m.Primary), m == chosen, () => ChooseScreen(m))).ToList();
        var seen = settings.Apps.Count;
        AppsHeader.IsEnabled = seen > 0;
        AppsHeader.Content = seen > 0 ? Strings.ShowFrom : Strings.NoApps;
        AppsHeader.Tag = seen > 0 ? Strings.OutOf(settings.Apps.Values.Count(a => a.Show), seen) : "";
        AppList.ItemsSource = settings.Apps.OrderBy(a => a.Value.Name)
            .Select(a => new Choice(a.Value.Name, a.Value.Show, () => ChooseApp(a.Key, !a.Value.Show))).ToList();
    }

    void ChooseApp(string id, bool show)
    {
        settings.Apps[id] = settings.Apps[id] with { Show = show };
        try
        {
            if (!settings.Apps[id].OffInWindows) Settings.SetBanner(id, !show);
            settings.Save();
        }
        catch (Exception ex) { Log(ex); }
        FillSettings();
    }

    void SaveSettings()
    {
        try { settings.Save(); }
        catch (Exception ex) { Log(ex); }
        FillSettings();
    }

    void ToggleQuiet()
    {
        settings.Quiet = !settings.Quiet;
        SaveSettings();
        Apply();
    }

    void ChooseScreen(Monitor monitor)
    {
        settings.Screen = monitor.Device;
        settings.Bar = null;
        SaveSettings();
        Center();
    }

    // --- Rendu ---

    void Apply()
    {
        var quiet = Quiet;
        var badge = notes.Count > 0 && !quiet;
        Badge.Visibility = badge ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = notes.Sum(n => n.Count).ToString();
        Empty.Visibility = notes.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        NotesHeader.Text = quiet ? Strings.NotificationsQuiet : Strings.Notifications;
        NotesHeader.Foreground = (Brush)FindResource(quiet ? "QuietBlue" : "Dim");
        MiniArt.Visibility = MiniTitle.Visibility = Bars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;

        var sensing = microphoneApps.Count + cameraApps.Count > 0;
        MicDot.Visibility = microphoneApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = cameraApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SensorText.Visibility = sensing && Pill.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;

        // Rien à montrer : l'île disparaît, et un pixel à alpha 0 laisse passer les clics vers la fenêtre dessous.
        var hide = !expanded && toast == null && !playing && !badge && !sensing;
        if (hide != hidden)
        {
            hidden = hide;
            Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(180)));
        }

        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        FrameworkElement next;
        double width, height;
        if (expanded)
        {
            Expanded.Measure(infinite);
            (next, width, height) = (Expanded, Expanded.DesiredSize.Width, Expanded.DesiredSize.Height);
        }
        else if (toast != null)
        {
            ToastView.Content = toast;
            (next, width, height) = (ToastView, 390, 62);
        }
        else
        {
            Compact.Measure(infinite);
            (next, width, height) = (Compact, Math.Max(96, Compact.DesiredSize.Width), playing ? 34 : 28);
        }
        var radius = expanded ? 34 : height / 2;

        if (next != shown)
        {
            shown = next;
            foreach (var layer in new FrameworkElement[] { Compact, ToastView, Expanded })
            {
                layer.IsHitTestVisible = layer == next;
                layer.BeginAnimation(OpacityProperty, new DoubleAnimation(layer == next ? 1 : 0, TimeSpan.FromMilliseconds(layer == next ? 240 : 90)));
            }
        }
        wave.IsEnabled = playing && next == Compact && !Fullscreen; // l'égaliseur est le plus gros poste : à l'arrêt dès qu'il est invisible

        if (goal[0] == width && goal[1] == height && goal[2] == radius) return;
        (goal[0], goal[1], goal[2]) = (width, height, radius);
        if (springing) return;
        springing = true;
        CompositionTarget.Rendering += Step;
    }

    // Un seul ressort anime largeur, hauteur et rayon ; il ne tourne que pendant une transition.
    void Step(object sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = Math.Min((now - lastFrame).TotalSeconds, 1 / 30.0);
        lastFrame = now;
        var settled = true;
        for (var i = 0; i < 3; i++)
        {
            velocity[i] += (Stiffness * (goal[i] - size[i]) - Damping * velocity[i]) * dt;
            size[i] += velocity[i] * dt;
            settled &= Math.Abs(goal[i] - size[i]) < .2 && Math.Abs(velocity[i]) < 2;
        }
        if (settled)
        {
            Array.Copy(goal, size, 3);
            Array.Clear(velocity);
            springing = false;
            CompositionTarget.Rendering -= Step;
        }
        Pill.Width = size[0];
        Pill.Height = size[1];
        Pill.CornerRadius = new CornerRadius(size[2]);
        Pill.Clip = new RectangleGeometry(new Rect(0, 0, size[0], size[1]), size[2], size[2]);
    }

    // Une seule horloge pour les quatre barres : elles changent ensemble, donc un seul dessin par tick.
    void Wave()
    {
        var seconds = Environment.TickCount64 / 1000.0;
        for (var i = 0; i < barScales.Length; i++) barScales[i].ScaleY = BarScale(seconds, i);
    }

    public static double BarScale(double seconds, int bar)
    {
        var phase = seconds / (.3 + .09 * bar) % 2; // chaque barre a sa période
        return .35 + .65 * (phase < 1 ? phase : 2 - phase);
    }

    // --- Boucle de mise à jour ---

    async void Poll()
    {
        if (polling) return;
        polling = true;
        try
        {
            await Safe(PollMusic);
            await Safe(PollNotifications);
            if (expanded && !VolumeTrack.IsMouseCaptured) RefreshVolume();
            if (expanded) RefreshOutput();
            if (toast != null && DateTime.Now > toastUntil) toast = null;
            Apply();
        }
        finally { polling = false; } // sinon une erreur arrêterait la boucle pour de bon
    }

    async Task Safe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { Log(ex); }
    }

    // Une erreur peut se répéter à chaque seconde : on ne l'écrit qu'une fois.
    void Log(Exception ex)
    {
        if (ex.Message == lastError) return;
        lastError = ex.Message;
        try { File.AppendAllText(LogPath, $"{DateTime.Now:s} {ex}\n"); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { } // un journal bloqué ne doit pas fermer l'île
    }

    static bool IsPlaying(Session s) => s.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing;

    public static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    async Task PollMusic()
    {
        media ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        session = media.GetSessions().FirstOrDefault(IsPlaying) ?? media.GetCurrentSession();
        playing = session != null && IsPlaying(session);
        BigArt.Cursor = session != null ? Cursors.Hand : null;
        PlayIcon.Data = playing ? PauseGeometry : PlayGeometry;

        if (session == null)
        {
            trackKey = null;
            TrackTitle.Text = Strings.NothingPlaying;
            TrackArtist.Text = MiniTitle.Text = "";
            MiniArt.Fill = BigArt.Fill = NoArt;
            SetAccent(NoArt);
            TimeRow.Visibility = Visibility.Collapsed;
            return;
        }

        var track = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(PlayerTimeout);
        var key = $"{session.SourceAppUserModelId}|{track.Title}|{track.Artist}";
        // Certains lecteurs publient la pochette après le titre : on la redemande tant qu'elle manque.
        if (key != trackKey || BigArt.Fill == NoArt)
        {
            trackKey = key;
            TrackTitle.Text = MiniTitle.Text = track.Title;
            TrackArtist.Text = track.Artist;
            MiniArt.Fill = BigArt.Fill = await LoadArt(track);
            SetAccent(BigArt.Fill);
        }

        var time = session.GetTimelineProperties();
        duration = time.EndTime;
        TimeRow.Visibility = time.EndTime > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        if (time.EndTime <= TimeSpan.Zero) return;
        var seekable = session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled;
        ProgressTrack.IsHitTestVisible = seekable;
        ProgressTrack.Cursor = seekable ? Cursors.Hand : null;
        if (ProgressTrack.IsMouseCaptured) return; // le curseur tenu à la souris ne doit pas revenir en arrière
        var position = time.Position + (playing ? DateTimeOffset.Now - time.LastUpdatedTime : TimeSpan.Zero);
        if (position > time.EndTime) position = time.EndTime;
        ProgressScale.ScaleX = position / time.EndTime;
        TimeNow.Text = Clock(position);
        TimeEnd.Text = Clock(time.EndTime);
    }

    void SetAccent(Brush art)
    {
        var color = art is ImageBrush { ImageSource: BitmapSource image } ? Accent(Pixels(image)) : Colors.White;
        if (color == accent.Color) return; // sans lecture, cet appel revient à chaque seconde : pas d'animation inutile
        accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(500)));
    }

    static byte[] Pixels(BitmapSource image)
    {
        const double Side = 24; // assez pour la couleur dominante, sans parcourir une pochette entière
        var small = new TransformedBitmap(image, new ScaleTransform(Side / image.PixelWidth, Side / image.PixelHeight));
        var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    // La teinte vive la plus présente, sans moyenne qui virerait au gris, éclaircie pour rester lisible sur le noir.
    public static Color Accent(byte[] bgra)
    {
        const double Gray = 24, White = .25;
        var bins = new Dictionary<int, (double Weight, double R, double G, double B)>();
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var weight = (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) * bgra[i + 3] / 255.0;
            if (weight < Gray) continue;
            var bin = r >> 5 << 6 | g >> 5 << 3 | b >> 5;
            bins.TryGetValue(bin, out var sum);
            bins[bin] = (sum.Weight + weight, sum.R + r * weight, sum.G + g * weight, sum.B + b * weight);
        }
        if (bins.Count == 0) return Colors.White;
        var best = bins.Values.MaxBy(s => s.Weight);
        var peak = Math.Max(best.R, Math.Max(best.G, best.B));
        byte Light(double channel) => (byte)Math.Round(255 * (channel / peak * (1 - White) + White));
        return Color.FromRgb(Light(best.R), Light(best.G), Light(best.B));
    }

    // En cas d'échec, NoArt plutôt que la pochette du morceau précédent : elle sera redemandée au passage suivant.
    async Task<Brush> LoadArt(GlobalSystemMediaTransportControlsSessionMediaProperties track)
    {
        if (track.Thumbnail == null) return NoArt;
        try
        {
            using var thumbnail = await track.Thumbnail.OpenReadAsync().AsTask().WaitAsync(PlayerTimeout);
            using var source = thumbnail.AsStreamForRead();
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy);
            copy.Position = 0;
            return new ImageBrush(Decode(copy)) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log(ex);
            return NoArt;
        }
    }

    static BitmapImage Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 128; // le double de la plus grande pochette affichée : une image 720p prendrait 3,7 Mo
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // Sans avatar, la notification reste affichée avec son initiale.
    string LoadTag(uint notificationId)
    {
        try { return Avatars.TagFor(notificationId); }
        catch (Exception ex) { Log(ex); return null; }
    }

    ImageSource LoadAvatar(uint notificationId)
    {
        try { return LoadImage(Avatars.PathFor(notificationId)); }
        catch (Exception ex)
        {
            Log(ex); // base de notifications illisible
            return null;
        }
    }

    // Image absente ou abîmée : la pastille garde l'initiale, et l'écoute des scripts continue.
    ImageSource LoadImage(string path)
    {
        if (path == null) return null;
        try
        {
            using var file = File.OpenRead(path);
            return Decode(file);
        }
        catch (Exception ex)
        {
            Log(ex);
            return null;
        }
    }

    async Task PollNotifications()
    {
        if (listener == null) return;
        if (NotificationsMoved())
        {
            var current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in current.OrderBy(n => n.CreationTime))
            {
                if (!seen.Add(n.Id)) continue;
                // Discord retire parfois ses toasts lui-même : on garde notre copie jusqu'à ce qu'ils soient lus.
                try
                {
                    var id = n.AppInfo?.AppUserModelId;
                    if (id == null) continue;
                    var name = AppName(n);
                    if (!Wanted(id, name)) continue;
                    var texts = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric).GetTextElements().Select(t => t.Text).ToList();
                    Show(Note.From(texts, n.CreationTime.LocalDateTime, name) with { AppId = id, Avatar = LoadAvatar(n.Id), Tag = IsDiscord(id) ? LoadTag(n.Id) : null });
                }
                catch (Exception ex) { Log(ex); }
            }
            firstNotificationPoll = false;
        }
        if (notes.Count == 0) return;
        var process = ForegroundProcess();
        foreach (var read in notes.Where(n => n.FromProcess(process)).ToList()) notes.Remove(read); // ouvrir l'appli vaut lecture
    }

    // GetNotificationsAsync est l'appel le plus cher de la boucle (5 ms) : on attend que la base de Windows bouge.
    // Son journal change 30 à 50 ms après une notif et reste immobile sinon ; le relevé régulier couvre un Windows qui ferait autrement.
    bool NotificationsMoved()
    {
        var wal = new FileInfo(NotificationWal);
        var stamp = wal.Exists ? (wal.Length, wal.LastWriteTimeUtc) : default;
        if (stamp != notificationStamp) (notificationStamp, followUps) = (stamp, 2); // deux lectures : Windows peut écrire avant de publier
        if (followUps == 0 && ++idlePolls < FullCheckEvery) return false;
        if (followUps > 0) followUps--;
        idlePolls = 0;
        return true;
    }

    // Toute appli s'affiche d'office : le menu sert à retirer celles qu'on ne veut pas voir.
    bool Wanted(string id, string name)
    {
        if (settings.Apps.TryGetValue(id, out var app)) return app.Show;
        settings.Apps[id] = new(name, true, Settings.BannerOff(id));
        Settings.SetBanner(id, show: false);
        settings.Save();
        return true;
    }

    void Show(Note note)
    {
        var same = notes.FirstOrDefault(note.SameChat);
        if (same != null)
        {
            notes.Remove(same);
            note = note with { Count = same.Count + 1, Avatar = note.Avatar ?? same.Avatar };
        }
        notes.Insert(0, note);
        while (notes.Count > MaxNotes) notes.RemoveAt(notes.Count - 1);
        if (firstNotificationPoll || expanded || Quiet) return;
        toast = note;
        toastUntil = DateTime.Now.AddSeconds(ToastSeconds);
    }

    // Windows lève une erreur pour certaines applis sans nom déclaré : la notification reste utile avec son identifiant.
    static string AppName(UserNotification n)
    {
        try { return n.AppInfo.DisplayInfo.DisplayName; }
        catch (Exception) { return n.AppInfo.AppUserModelId; }
    }

    static bool IsDiscord(string appId) => appId.Contains("discord", StringComparison.OrdinalIgnoreCase);

    // Jeu ou vidéo en plein écran, présentation, ou choix manuel : les notifications attendent dans le badge.
    static bool Fullscreen => SHQueryUserNotificationState(out var state) == 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    bool Quiet => settings.Quiet || Fullscreen;

    static string ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return AppVolume.ProcessName(pid);
    }

    async Task CheckUpdates()
    {
        while (true)
        {
            await Safe(async () => Offer(await Updater.Check()));
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    // Effacée ou poussée hors de la liste par d'autres notifications, la proposition revient au relevé suivant.
    void Offer(Release release)
    {
        if (release == null || notes.Any(n => n.Update == release)) return;
        Show(new Note(Strings.VersionAvailable(release.Tag.TrimStart('v')), Strings.ClickToInstall, DateTime.Now) { Update = release });
        Apply();
    }

    async void InstallUpdate(Note note)
    {
        notes.Remove(note); // un second clic relancerait le téléchargement
        Flash(Strings.Downloading, 120);
        try
        {
            await Updater.Install(note.Update);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException)
        {
            Log(ex);
            notes.Insert(0, note);
            Flash(Strings.UpdateFailed, ToastSeconds);
        }
    }

    void Flash(string text, double seconds)
    {
        toast = new Note("IslandBar Lite", text, DateTime.Now);
        toastUntil = DateTime.Now.AddSeconds(seconds);
        Apply();
    }

    // --- Actions ---

    async void Control(Func<Session, Windows.Foundation.IAsyncOperation<bool>> action)
    {
        var target = session;
        if (target == null) return;
        await Safe(async () => await action(target));
        await Task.Delay(200); // laisse le lecteur publier son nouvel état
        Poll();
    }

    void PlayPause(object sender, RoutedEventArgs e) => Control(s => s.TryTogglePlayPauseAsync());
    void Prev(object sender, RoutedEventArgs e) => Control(s => s.TrySkipPreviousAsync());
    void Next(object sender, RoutedEventArgs e) => Control(s => s.TrySkipNextAsync());

    static double Fraction(MouseEventArgs e, FrameworkElement track) =>
        Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1);

    // Au survol, un repère et une bulle montrent où mènerait le clic.
    static void Preview(Canvas overlay, double fraction, string text)
    {
        var (tick, bubble) = ((FrameworkElement)overlay.Children[0], (Border)overlay.Children[1]);
        var x = fraction * ((FrameworkElement)overlay.Parent).ActualWidth;
        ((TextBlock)bubble.Child).Text = text;
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(bubble, x - bubble.DesiredSize.Width / 2);
        Canvas.SetLeft(tick, x - tick.Width / 2);
    }

    void SeekFromMouse(MouseEventArgs e)
    {
        var fraction = Fraction(e, ProgressTrack);
        ProgressScale.ScaleX = fraction;
        TimeNow.Text = Clock(duration * fraction);
    }

    void SeekDown(object sender, MouseButtonEventArgs e)
    {
        ProgressTrack.CaptureMouse();
        SeekFromMouse(e);
    }

    void SeekDrag(object sender, MouseEventArgs e)
    {
        if (ProgressTrack.IsMouseCaptured) SeekFromMouse(e);
        var fraction = Fraction(e, ProgressTrack);
        Preview(ProgressPreview, fraction, Clock(duration * fraction));
    }

    // On ne saute qu'au relâchement : un lecteur n'aime pas recevoir une position à chaque pixel.
    void SeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!ProgressTrack.IsMouseCaptured) return;
        ProgressTrack.ReleaseMouseCapture();
        var fraction = ProgressScale.ScaleX;
        Control(s => s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().EndTime * fraction).Ticks));
    }

    // Sans session audio reconnue pour cette app, la ligne reste cachée : rien d'autre n'est jamais réglé à sa place.
    void RefreshVolume()
    {
        float? level = null;
        try { if (session != null) level = AppVolume.Get(session.SourceAppUserModelId); }
        catch (COMException ex) { Log(ex); }
        VolumeRow.Visibility = level == null ? Visibility.Collapsed : Visibility.Visible;
        if (level != null) ShowVolume(level.Value);
    }

    void ShowVolume(double level)
    {
        VolumeScale.ScaleX = level;
        VolumeText.Text = Strings.Percent(level * 100);
    }

    void SetVolume(double level)
    {
        if (session == null || VolumeRow.Visibility != Visibility.Visible) return;
        level = Math.Clamp(level, 0, 1);
        ShowVolume(level);
        try { AppVolume.Set(session.SourceAppUserModelId, (float)level); }
        catch (COMException ex) { Log(ex); }
    }

    void VolumeFromMouse(MouseEventArgs e) => SetVolume(Fraction(e, VolumeTrack));

    void VolumeDown(object sender, MouseButtonEventArgs e)
    {
        VolumeTrack.CaptureMouse();
        VolumeFromMouse(e);
    }

    void VolumeDrag(object sender, MouseEventArgs e)
    {
        if (VolumeTrack.IsMouseCaptured) VolumeFromMouse(e);
        var fraction = Fraction(e, VolumeTrack);
        Preview(VolumePreview, fraction, Strings.Percent(fraction * 100));
    }

    void VolumeUp(object sender, MouseButtonEventArgs e) => VolumeTrack.ReleaseMouseCapture();

    void RefreshOutput()
    {
        try
        {
            outputs = AppVolume.Outputs();
            var current = AppVolume.DefaultOutput();
            OutputText.Text = outputs.Find(o => o.Id == current)?.Name;
            OutputRow.Visibility = outputs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log(ex);
            OutputRow.Visibility = Visibility.Collapsed;
        }
    }

    void ChooseOutput(AudioOutput output)
    {
        try
        {
            AppVolume.SetDefaultOutput(output.Id);
            OutputText.Text = output.Name;
            Unfold(OutputMenu);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Log(ex); }
    }

    void ClearNotes(object sender, RoutedEventArgs e)
    {
        notes.Clear();
        Apply();
    }

    void NoteClick(object sender, MouseButtonEventArgs e) =>
        OpenNote((e.OriginalSource as FrameworkElement)?.DataContext as Note);

    async void OpenNote(Note note)
    {
        if (note == null) return; // liste vide, ou clic entre deux lignes
        Collapse(restoreFocus: false);
        if (note.Update != null)
        {
            InstallUpdate(note);
            return;
        }
        if (note.AppId == "") return; // message de l'île : il n'y a rien à ouvrir
        notes.Remove(note); // certaines applis du Store ne sont pas reconnues au premier plan
        if (await Replay(note)) return;
        try { Process.Start(new ProcessStartInfo(LinkFor(note)) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log(ex); }
    }

    // Le clic Windows rejoué marche pour tout le monde, sans RPC ni testeurs : Discord ouvre lui-même le salon.
    // Une notification déjà fermée par Discord (11 s encore bon, 20 s trop tard) est ignorée sans erreur, d'où le contrôle du premier plan.
    async Task<bool> Replay(Note note)
    {
        if (!IsDiscord(note.AppId)) return false;
        try
        {
            // Hors du fil de l'interface : si Discord est fermé, Windows le relance et l'appel peut durer.
            if (!await Task.Run(() => ToastClick.Send(note.AppId, note.Tag))) return false;
            await Task.Delay(1000);
            return note.FromProcess(ForegroundProcess());
        }
        catch (Exception ex) { Log(ex); return false; } // activateur absent : le lien ouvre au moins Discord
    }

    // Sans salon connu (Discord non autorisé, ou connexion locale tombée), on ouvre simplement Discord.
    string LinkFor(Note note)
    {
        if (!IsDiscord(note.AppId)) return $@"shell:AppsFolder\{note.AppId}";
        return channelLinks.TryGetValue(note.Title, out var known) ? known : "discord://-/";
    }
}
