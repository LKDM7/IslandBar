using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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

    public static Note From(IReadOnlyList<string> texts, DateTime when) =>
        new(texts.FirstOrDefault() ?? "Discord", string.Join(" ", texts.Skip(1)).ReplaceLineEndings(" "), when);
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

    const int WM_HOTKEY = 0x312, GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, MAPVK_VSC_TO_VK = 1;
    const uint ScanCodeSquare = 0x29; // la touche à gauche de "1" : ² en AZERTY, quelle que soit la disposition

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

    GlobalSystemMediaTransportControlsSessionManager media;
    Session session;
    UserNotificationListener listener;
    FrameworkElement shown;
    Note toast;
    DateTime toastUntil;
    string trackKey, lastError;
    bool expanded, playing, polling, springing, hidden = true, firstNotificationPoll = true;
    nint previousWindow;
    TimeSpan lastFrame;

    public MainWindow()
    {
        InitializeComponent();
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
        OpenDiscord(toast);
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
            case Key.Enter: OpenDiscord(notes.FirstOrDefault()); break;
            case Key.Delete: ClearNotes(null, null); break;
            default: return;
        }
        e.Handled = true;
    }

    void Quit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // --- Rendu ---

    void Apply()
    {
        var badge = notes.Count > 0;
        Badge.Visibility = badge ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = notes.Count.ToString();
        Empty.Visibility = badge ? Visibility.Collapsed : Visibility.Visible;
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
        await Safe(PollDiscord);
        if (expanded && !VolumeTrack.IsMouseCaptured) RefreshVolume();
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
        }

        var time = session.GetTimelineProperties();
        TimeRow.Visibility = time.EndTime > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        if (time.EndTime <= TimeSpan.Zero) return;
        var position = time.Position + (playing ? DateTimeOffset.Now - time.LastUpdatedTime : TimeSpan.Zero);
        if (position > time.EndTime) position = time.EndTime;
        ProgressScale.ScaleX = position / time.EndTime;
        TimeNow.Text = Clock(position);
        TimeEnd.Text = Clock(time.EndTime);
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

    async Task PollDiscord()
    {
        if (listener == null) return;
        var current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        foreach (var n in current.OrderBy(n => n.CreationTime))
        {
            if (!seen.Add(n.Id)) continue;
            // Discord retire parfois ses toasts lui-même : on garde notre copie jusqu'à ce qu'ils soient lus.
            try
            {
                if (!IsDiscord(n)) continue;
                var texts = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric).GetTextElements().Select(t => t.Text).ToList();
                var note = Note.From(texts, n.CreationTime.LocalDateTime) with { Avatar = LoadAvatar(n.Id) };
                notes.Insert(0, note);
                while (notes.Count > MaxNotes) notes.RemoveAt(notes.Count - 1);
                if (firstNotificationPoll || expanded) continue;
                toast = note;
                toastUntil = DateTime.Now.AddSeconds(ToastSeconds);
            }
            catch (Exception ex) { Log(ex); }
        }
        firstNotificationPoll = false;
        if (DiscordFocused()) notes.Clear();
    }

    static bool IsDiscord(UserNotification n) =>
        n.AppInfo?.AppUserModelId?.Contains("discord", StringComparison.OrdinalIgnoreCase) == true;

    static bool DiscordFocused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.StartsWith("Discord", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; } // le processus vient de se fermer
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

    void ClearNotes(object sender, RoutedEventArgs e)
    {
        notes.Clear();
        Apply();
    }

    void OpenDiscordClick(object sender, MouseButtonEventArgs e) =>
        OpenDiscord((e.OriginalSource as FrameworkElement)?.DataContext as Note);

    // Sans salon connu (Discord non autorisé, ou connexion locale tombée), on ouvre simplement Discord.
    void OpenDiscord(Note note)
    {
        Collapse(restoreFocus: false);
        var link = note != null && channelLinks.TryGetValue(note.Title, out var known) ? known : "discord://-/";
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Win32Exception ex) { Log(ex); }
    }
}
