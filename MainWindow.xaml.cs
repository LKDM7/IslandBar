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
    public string AppId { get; init; } = ""; // vide : message de l'île elle-même
    public string AppName { get; init; } = "IslandBar";
    public Release Update { get; init; }
    public string Stamp => $"{AppName} · {When:HH:mm}";

    public static Note From(IReadOnlyList<string> texts, DateTime when, string app = "Discord") =>
        new(texts.FirstOrDefault() ?? app, string.Join(" ", texts.Skip(1)).ReplaceLineEndings(" "), when) { AppName = app };

    // "com.squirrel.Discord.Discord" ou "5319275A.WhatsAppDesktop_xxx!App" : le processus est une partie de l'identifiant.
    public bool FromProcess(string process) =>
        AppId != "" && process != "" && AppId.Split('!')[0].Contains(process, StringComparison.OrdinalIgnoreCase);
}

public partial class MainWindow : Window
{
    [DllImport("user32")] static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
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

    const double ToastSeconds = 5, MaxNotes = 30, VolumeStep = .05;
    const double Stiffness = 230, Damping = 23; // amortissement < 1 : le léger rebond façon iOS

    static readonly Geometry PlayGeometry = Geometry.Parse("M7 4.5v15l12.5-7.5z");
    static readonly Geometry PauseGeometry = Geometry.Parse("M6 4.5h4.5v15H6zM13.5 4.5H18v15h-4.5z");
    static readonly Brush NoArt = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
    static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "IslandBar.log");

    readonly ObservableCollection<Note> notes = [];
    readonly HashSet<uint> seen = [];
    readonly Dictionary<string, string> channelLinks = []; // titre de notification -> lien vers son salon
    readonly double[] size = [96, 28, 14], velocity = new double[3], goal = [96, 28, 14]; // largeur, hauteur, rayon
    readonly Settings settings = Settings.Load();
    readonly SolidColorBrush accent = new(Colors.White);

    GlobalSystemMediaTransportControlsSessionManager media;
    Session session;
    UserNotificationListener listener;
    FrameworkElement shown;
    Note toast;
    DateTime toastUntil;
    string trackKey, lastError;
    bool expanded, playing, polling, springing, hidden = true, firstNotificationPoll = true, quietByHand;
    List<AudioOutput> outputs = [];
    nint previousWindow;
    TimeSpan lastFrame;

    public MainWindow()
    {
        InitializeComponent();
        // Posé à la main : un pinceau placé dans les ressources est gelé par WPF, et celui-ci s'anime.
        ProgressFill.Fill = accent;
        foreach (System.Windows.Shapes.Shape bar in Bars.Children) bar.Fill = accent;
        shown = Compact;
        NoteList.ItemsSource = notes;
        MiniArt.Fill = BigArt.Fill = NoArt;
        Center();
        SystemEvents.DisplaySettingsChanged += (_, _) => Center();
        Loaded += OnLoaded;
    }

    void Center() => Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        if (RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, MapVirtualKeyW(ScanCodeSquare, MAPVK_VSC_TO_VK))) return;
        MessageBox.Show("Ctrl + Alt + ² est déjà pris (IslandBar tourne peut-être déjà).", "IslandBar");
        Application.Current.Shutdown();
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Safe(async () =>
        {
            listener = UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed) return;
            listener = null;
            Empty.Text = "Accès refusé : Paramètres Windows › Confidentialité › Notifications";
        });
        _ = new DiscordRpc((title, link) => channelLinks[title] = link, Log).Run();
        Poll();
        // shortcut: polling 1 s au lieu des events, une seule boucle pour tout ; passer aux events si l'app est packagée MSIX
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher).Start();
        _ = Safe(CheckUpdates);
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
        Apply();
    }

    // La barre ne doit jamais voler le focus, sauf quand on l'ouvre pour la piloter au clavier.
    void SetNoActivate(bool on)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, on ? style | WS_EX_NOACTIVATE : style & ~(nint)WS_EX_NOACTIVATE);
    }

    void OnDeactivated(object sender, EventArgs e) => Collapse(restoreFocus: false);

    void PillClick(object sender, MouseButtonEventArgs e)
    {
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
            case Key.S: OutputMenu(null, null); break;
            case Key.Enter: OpenNote(notes.FirstOrDefault()); break;
            case Key.Delete: ClearNotes(null, null); break;
            default: return;
        }
        e.Handled = true;
    }

    void Quit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    void MenuOpened(object sender, RoutedEventArgs e)
    {
        QuietItem.IsChecked = quietByHand;
        AppsMenu.IsEnabled = settings.Apps.Count > 0;
        AppsMenu.Items.Clear();
        foreach (var (id, app) in settings.Apps.OrderBy(a => a.Value.Name))
        {
            var item = Item(app.Name);
            (item.IsCheckable, item.IsChecked, item.StaysOpenOnClick) = (true, app.Show, true);
            item.Click += (_, _) => ChooseApp(id, item.IsChecked);
            AppsMenu.Items.Add(item);
        }
    }

    void ChooseApp(string id, bool show)
    {
        settings.Apps[id] = settings.Apps[id] with { Show = show };
        try
        {
            Settings.SetBanner(id, !show);
            settings.Save();
        }
        catch (Exception ex) { Log(ex); }
    }

    void QuietClick(object sender, RoutedEventArgs e)
    {
        quietByHand = QuietItem.IsChecked;
        Apply();
    }

    // --- Rendu ---

    void Apply()
    {
        var quiet = Quiet;
        var badge = notes.Count > 0 && !quiet;
        Badge.Visibility = badge ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = notes.Count.ToString();
        Empty.Visibility = notes.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        NotesHeader.Text = quiet ? "Notifications · Ne pas déranger" : "Notifications";
        MiniArt.Visibility = MiniTitle.Visibility = Bars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;

        // Rien à montrer : l'île disparaît, et un pixel à alpha 0 laisse passer les clics vers la fenêtre dessous.
        var hide = !expanded && toast == null && !playing && !badge;
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

    void SetBars(bool animate)
    {
        var i = 0;
        foreach (FrameworkElement bar in Bars.Children)
        {
            if (bar.RenderTransform is not ScaleTransform scale) bar.RenderTransform = scale = new ScaleTransform(1, .35);
            var wave = new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(300 + 90 * i++)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(wave, 24); // l'animation tourne en continu : on limite le CPU
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animate ? wave : null); // null arrête l'horloge, sinon WPF redessine en pause
        }
    }

    // --- Boucle de mise à jour ---

    async void Poll()
    {
        if (polling) return;
        polling = true;
        await Safe(PollMusic);
        await Safe(PollNotifications);
        if (expanded && !VolumeTrack.IsMouseCaptured) RefreshVolume();
        if (expanded) RefreshOutput();
        if (toast != null && DateTime.Now > toastUntil) toast = null;
        Apply();
        polling = false;
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
        File.AppendAllText(LogPath, $"{DateTime.Now:s} {ex}\n");
    }

    static bool IsPlaying(Session s) => s.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing;

    public static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    async Task PollMusic()
    {
        media ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        session = media.GetSessions().FirstOrDefault(IsPlaying) ?? media.GetCurrentSession();
        var nowPlaying = session != null && IsPlaying(session);
        if (nowPlaying != playing) SetBars(nowPlaying);
        playing = nowPlaying;
        PlayIcon.Data = playing ? PauseGeometry : PlayGeometry;

        if (session == null)
        {
            trackKey = null;
            TrackTitle.Text = "Rien en lecture";
            TrackArtist.Text = MiniTitle.Text = "";
            MiniArt.Fill = BigArt.Fill = NoArt;
            SetAccent(NoArt);
            TimeRow.Visibility = Visibility.Collapsed;
            return;
        }

        var track = await session.TryGetMediaPropertiesAsync();
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

    static async Task<Brush> LoadArt(GlobalSystemMediaTransportControlsSessionMediaProperties track)
    {
        if (track.Thumbnail == null) return NoArt;
        using var thumbnail = await track.Thumbnail.OpenReadAsync();
        using var source = thumbnail.AsStreamForRead();
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy);
        copy.Position = 0;
        return new ImageBrush(Decode(copy)) { Stretch = Stretch.UniformToFill };
    }

    static BitmapImage Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // Sans avatar, la notification reste affichée avec son initiale.
    ImageSource LoadAvatar(uint notificationId)
    {
        try
        {
            var path = Avatars.PathFor(notificationId);
            if (path == null) return null;
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
                Show(Note.From(texts, n.CreationTime.LocalDateTime, name) with { AppId = id, Avatar = LoadAvatar(n.Id) });
            }
            catch (Exception ex) { Log(ex); }
        }
        firstNotificationPoll = false;
        var process = ForegroundProcess();
        foreach (var read in notes.Where(n => n.FromProcess(process)).ToList()) notes.Remove(read); // ouvrir l'appli vaut lecture
    }

    // Toute appli vue rejoint le menu, mais seule Discord s'affiche sans avoir été cochée.
    bool Wanted(string id, string name)
    {
        if (!settings.Apps.TryGetValue(id, out var app))
        {
            settings.Apps[id] = app = new(name, IsDiscord(id));
            settings.Save();
        }
        return app.Show;
    }

    void Show(Note note)
    {
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
    bool Quiet => quietByHand || SHQueryUserNotificationState(out var state) == 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    static string ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException) { return ""; } // le processus vient de se fermer
    }

    async Task CheckUpdates()
    {
        Release release;
        while ((release = await Updater.Check()) == null) await Task.Delay(TimeSpan.FromHours(6));
        Show(new Note($"Version {release.Tag.TrimStart('v')} disponible", "Clique pour l'installer", DateTime.Now) { Update = release });
    }

    async void InstallUpdate(Note note)
    {
        notes.Remove(note); // un second clic relancerait le téléchargement
        Flash("Téléchargement en cours…", 120);
        try
        {
            await Updater.Install(note.Update);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException)
        {
            Log(ex);
            notes.Insert(0, note);
            Flash("Mise à jour impossible", ToastSeconds);
        }
    }

    void Flash(string text, double seconds)
    {
        toast = new Note("IslandBar", text, DateTime.Now);
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

    void SeekFromMouse(MouseEventArgs e)
    {
        if (session == null) return;
        var fraction = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
        ProgressScale.ScaleX = fraction;
        TimeNow.Text = Clock(session.GetTimelineProperties().EndTime * fraction);
    }

    void SeekDown(object sender, MouseButtonEventArgs e)
    {
        ProgressTrack.CaptureMouse();
        SeekFromMouse(e);
    }

    void SeekDrag(object sender, MouseEventArgs e)
    {
        if (ProgressTrack.IsMouseCaptured) SeekFromMouse(e);
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
        VolumeText.Text = $"{level * 100:0} %";
    }

    void SetVolume(double level)
    {
        if (session == null || VolumeRow.Visibility != Visibility.Visible) return;
        level = Math.Clamp(level, 0, 1);
        ShowVolume(level);
        try { AppVolume.Set(session.SourceAppUserModelId, (float)level); }
        catch (COMException ex) { Log(ex); }
    }

    void VolumeFromMouse(MouseEventArgs e) => SetVolume(e.GetPosition(VolumeTrack).X / VolumeTrack.ActualWidth);

    void VolumeDown(object sender, MouseButtonEventArgs e)
    {
        VolumeTrack.CaptureMouse();
        VolumeFromMouse(e);
    }

    void VolumeDrag(object sender, MouseEventArgs e)
    {
        if (VolumeTrack.IsMouseCaptured) VolumeFromMouse(e);
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

    // Un menu plutôt qu'un simple cycle : les sorties virtuelles (Steam, Voicemod...) se comptent vite par dizaine.
    void OutputMenu(object sender, MouseButtonEventArgs e)
    {
        if (outputs.Count < 2) return;
        var menu = new ContextMenu { PlacementTarget = OutputRow };
        foreach (var output in outputs)
        {
            var item = Item(output.Name);
            item.IsChecked = output.Name == OutputText.Text;
            item.Click += (_, _) => ChooseOutput(output);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void ChooseOutput(AudioOutput output)
    {
        try
        {
            AppVolume.SetDefaultOutput(output.Id);
            OutputText.Text = output.Name;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Log(ex); }
    }

    // Un Header texte transformerait "_" en raccourci clavier et le ferait disparaître du nom.
    static MenuItem Item(string text) => new() { Header = new TextBlock { Text = text } };

    void ClearNotes(object sender, RoutedEventArgs e)
    {
        notes.Clear();
        Apply();
    }

    void NoteClick(object sender, MouseButtonEventArgs e) =>
        OpenNote((e.OriginalSource as FrameworkElement)?.DataContext as Note);

    void OpenNote(Note note)
    {
        Collapse(restoreFocus: false);
        if (note?.Update != null)
        {
            InstallUpdate(note);
            return;
        }
        if (note?.AppId == "") return; // message de l'île : il n'y a rien à ouvrir
        if (note != null) notes.Remove(note); // certaines applis du Store ne sont pas reconnues au premier plan
        try { Process.Start(new ProcessStartInfo(LinkFor(note)) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log(ex); }
    }

    // Sans salon connu (Discord non autorisé, ou connexion locale tombée), on ouvre simplement Discord.
    string LinkFor(Note note)
    {
        if (note != null && !IsDiscord(note.AppId)) return $@"shell:AppsFolder\{note.AppId}";
        return note != null && channelLinks.TryGetValue(note.Title, out var known) ? known : "discord://-/";
    }
}
