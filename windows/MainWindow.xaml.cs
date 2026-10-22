using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;
using Shp = System.Windows.Shapes.Path;

namespace Concilium;

public partial class MainWindow : Window
{
    List<Person> people = new();
    string pid = "";
    string? sid;
    bool dirty, authed, panelOpen = true;
    GraphData? graph;
    readonly Dictionary<string, (string status, DateTime at)> memory = new();
    readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromMinutes(20) };
    Person? Cur => people.FirstOrDefault(p => p.id == pid);
    static Strings T => Strings.T;

    record Ui(string pid = "", bool panel = true);
    static string UiFile => Path.Combine(Paths.Data, "ui.json");

    public MainWindow()
    {
        InitializeComponent();
        DrawPetals();
        try
        {
            var ui = JsonSerializer.Deserialize<Ui>(File.ReadAllText(UiFile));
            if (ui != null) { pid = ui.pid; panelOpen = ui.panel; }
        }
        catch { }
        idle.Tick += (_, _) => { idle.Stop(); EndConversation(); };
        Graph.NodeClicked += n => { if (n.kind != "missing") OpenNote(n.id); };
        // Fit the screen: on a laptop the 1400×900 default would push the title bar off the top.
        var wa = SystemParameters.WorkArea;
        // Smoke-test mode never touches the real screen: the window is drawn off-screen, without focus,
        // at the size of the emulated screen (CONCILIUM_SCREEN=1366x720).
        var shotMode = Environment.GetEnvironmentVariable("CONCILIUM_SHOT") is { Length: > 0 };
        if (shotMode && Environment.GetEnvironmentVariable("CONCILIUM_SCREEN") is { } scr && scr.Split('x') is [var sw, var sh2]
            && double.TryParse(sw, out var w0) && double.TryParse(sh2, out var h0)) wa = new Rect(0, 0, w0, h0);
        if (shotMode)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = -20000;
            // "Maximized" on a small screen = the whole work area.
            if (wa.Width < 1450 || wa.Height < 950) { Width = wa.Width; Height = wa.Height; } else { Width = 1400; Height = 900; }
        }
        else if (wa.Width < 1450 || wa.Height < 950) WindowState = WindowState.Maximized;
        Loaded += (_, _) => Start();
        SizeChanged += (_, _) => { if (panelOpen) PanelCol.Width = new GridLength(PanelWidth); };
        // Always come up in front and visible, even if launched "minimized" by a shortcut.
        if (!shotMode) ContentRendered += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) WindowState = wa.Width < 1450 || wa.Height < 950 ? WindowState.Maximized : WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        };
        // CONCILIUM_SHOT=<file.png>: save a picture of the window and quit (smoke test, README images).
        if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT") is { Length: > 0 } shot)
            ContentRendered += async (_, _) =>
            {
                Paths.LogError($"[shot] window shown {(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms after start");
                await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("CONCILIUM_SHOT_DELAY"), out var d) ? d : 1500);
                // CONCILIUM_SHOT_SEND=<text>: also send a message (or /skill) and wait for the answer first.
                if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT_SEND") is { Length: > 0 } say)
                {
                    // Several messages: "a || b". "@last" opens the latest conversation first.
                    foreach (var part in say.Split("||", StringSplitOptions.TrimEntries))
                    {
                        if (part == "@last")
                        {
                            if (Cur is { } cp && Sessions.List(cp).FirstOrDefault() is { } last) OpenSession(last.id);
                            await Task.Delay(1500);
                            continue;
                        }
                        var t0 = DateTime.Now;
                        if (part.StartsWith('/')) Send(null, part[1..]); else Send(part, null);
                        while (streaming && DateTime.Now - t0 < TimeSpan.FromMinutes(5)) await Task.Delay(300);
                        Paths.LogError($"[shot] answer to '{part}' done in {(DateTime.Now - t0).TotalSeconds:0.0} s, session {sid}");
                        await Task.Delay(1500);
                    }
                }
                // CONCILIUM_SHOT_NOTE=<note id, e.g. MEMORY.md>: open that note on the right. CONCILIUM_SHOT_SCROLL=top: the talk from its start.
                if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT_NOTE") is { Length: > 0 } note) OpenNote(note);
                if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT_SCROLL") == "top") Scroller.ScrollToTop();
                await Task.Delay(800);
                var dpi = VisualTreeHelper.GetDpi(this);
                var root = (FrameworkElement)Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bmp.Render(bg);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var f = File.Create(shot)) enc.Save(f);
                Close();
            };
    }

    void Start()
    {
        App.Mark("loaded");
        try { Paths.Prepare(); } catch (Exception e) { Paths.LogError(e); }
        App.Mark("prepared");
        SetPanel(panelOpen);
        ApplyI18n();
        authed = Auth.LoggedIn();
        people = Store.People();
        if (Cur == null) pid = people.FirstOrDefault()?.id ?? "";
        RenderPeople();
        if (!authed) { RenderLogin(); return; }
        SelectPerson(pid);
        App.Mark("person selected");
    }

    void SaveUi()
    {
        try { File.WriteAllText(UiFile, JsonSerializer.Serialize(new Ui(pid, panelOpen))); } catch { }
    }

    void DrawPetals()
    {
        var fill = new LinearGradientBrush(Color.FromRgb(0x9d, 0xb8, 0x97), Color.FromRgb(0x3f, 0x56, 0x39), 90) { Opacity = 0.62 };
        for (var i = 0; i < 5; i++)
        {
            // Petal centre at (32, 20.5), rotated around the flower centre (32, 32).
            var e = new Ellipse { Width = 14.4, Height = 25, Fill = fill, RenderTransform = new RotateTransform(i * 72, 7.2, 24) };
            Canvas.SetLeft(e, 32 - 7.2);
            Canvas.SetTop(e, 20.5 - 12.5);
            Petals.Children.Add(e);
        }
    }

    // ---------- i18n ----------

    void ApplyI18n()
    {
        MapBtn.Content = T.Map;
        TabMap.Content = T.Map;
        TabNote.Content = T.Note;
        NewChatBtn.Content = IconText("M12,5 V19 M5,12 H19", T.NewChat, Brush("Ink"));
        ChatsTitle.Text = T.Chats.ToUpperInvariant();
        Placeholder.Text = T.Placeholder + T.ByVoice;
        RenderActions();
        RenderLegend();
    }

    static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    static StackPanel IconText(string path, string text, Brush stroke, double size = 18)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Shp { Data = Geometry.Parse(path), Stroke = stroke, StrokeThickness = 1.8, Width = size, Height = size, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap });
        return sp;
    }

    // ---------- people ----------

    void RenderPeople()
    {
        People.Children.Clear();
        foreach (var p in people)
        {
            var b = new Button { Content = p.name, Style = (Style)FindResource(p.id == pid ? "PillActive" : "Pill") };
            b.Click += (_, _) => { if (!streaming) SelectPerson(p.id); };
            People.Children.Add(b);
        }
        if (people.Count > 0)
        {
            var add = new Button { Content = T.AddPerson, Style = (Style)FindResource("Pill") };
            add.Click += (_, _) => { if (!streaming) RenderAddPerson(); };
            People.Children.Add(add);
        }
    }

    void SelectPerson(string id)
    {
        EndConversation();
        pid = id;
        sid = null;
        SaveUi();
        RenderPeople();
        RenderWelcome();
        graph = null;
        Graph.SetData(new(new(), new()));
        ShowMemory();
        if (Cur is not { } p) { SessionsList.Children.Clear(); return; }
        Prewarm(p, null);
        LoadSessions();
        LoadGraph();
    }

    void RenderAddPerson()
    {
        ClearMessages();
        string? folder = null;
        var w = new StackPanel { Margin = new Thickness(0, 40, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        w.Children.Add(new TextBlock { Text = T.AddTitle, FontFamily = (FontFamily)FindResource("Serif"), FontSize = 30, Margin = new Thickness(0, 0, 0, 20) });
        w.Children.Add(new TextBlock { Text = T.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var name = new TextBox { Style = (Style)FindResource("Field") };
        w.Children.Add(name);
        w.Children.Add(new TextBlock { Text = T.Folder, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        var row = new DockPanel();
        var pick = new Button { Content = T.PickFolder, Style = (Style)FindResource("Pill"), Margin = new Thickness(0) };
        DockPanel.SetDock(pick, Dock.Left);
        var shown = new TextBlock { Foreground = Brush("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        row.Children.Add(pick);
        row.Children.Add(shown);
        w.Children.Add(row);
        w.Children.Add(new TextBlock { Text = T.FolderHint, Foreground = Brush("Muted"), FontSize = 13, Margin = new Thickness(0, 6, 0, 0) });
        var err = new TextBlock { Foreground = Brush("Warn"), Margin = new Thickness(0, 12, 0, 0) };
        w.Children.Add(err);
        pick.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = T.Folder };
            if (dlg.ShowDialog(this) == true) { folder = dlg.FolderName; shown.Text = folder; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = T.Add, Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        var cancel = new Button { Content = T.Cancel, Style = (Style)FindResource("Ghost") };
        buttons.Children.Add(ok);
        if (people.Count > 0) buttons.Children.Add(cancel);
        w.Children.Add(buttons);
        cancel.Click += (_, _) => RenderWelcome();
        ok.Click += (_, _) =>
        {
            var n = name.Text.Trim();
            if (n.Length == 0) { err.Text = T.NameRequired; return; }
            try { SelectPerson(AddPerson(n, folder)); }
            catch (Exception ex) { err.Text = ex.Message; }
        };
        Messages.Children.Add(w);
        name.Focus();
    }

    string AddPerson(string name, string? folder)
    {
        if (folder == null)
        {
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Concilium", safe);
            Directory.CreateDirectory(folder);
        }
        if (!Directory.Exists(folder)) throw new Exception(T.FolderMissing);
        // A new, empty folder gets the memory skeleton from the template; an existing one is left as it is.
        if (!Directory.EnumerateFileSystemEntries(folder).Any())
            foreach (var f in Directory.GetFiles(Paths.Template, "*.md"))
                if (Path.GetFileName(f) != "README.md") File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
        var p = new Person(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x"), name, Path.GetFullPath(folder));
        people.Add(p);
        Store.SavePeople(people);
        // Conversations that existed before the folder was added count as already consolidated.
        Store.MarkDreamed(p.id, Sessions.List(p).ToDictionary(s => s.id, s => s.at));
        return p.id;
    }

    // ---------- conversations list ----------

    async void LoadSessions()
    {
        if (Cur is not { } p) return;
        var list = await Task.Run(() => Sessions.List(p));
        if (p.id != pid) return;
        SessionsList.Children.Clear();
        foreach (var s in list)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = CleanTitle(s.title), FontSize = 14, MaxHeight = 40, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = T.Ago((int)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s.at) / 60000)), FontSize = 12, Foreground = Brush("Muted"), FontWeight = FontWeights.Normal });
            var b = new Button { Content = sp, Style = (Style)FindResource("Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 2), Foreground = Brush("Text") };
            if (s.id == sid) b.Background = Brush("Soft");
            b.Click += (_, _) => OpenSession(s.id);
            SessionsList.Children.Add(b);
        }
    }

    static string CleanTitle(string t)
    {
        var m = Regex.Match(t, @"(?:concilium|health-agent):([\w-]+)");
        if (m.Success && T.Actions.FirstOrDefault(a => a.skill == m.Groups[1].Value) is { } a) return a.title;
        t = Regex.Replace(t, "<[^>]+>", "").Trim();
        return t.Length > 0 ? t : "…";
    }

    async void OpenSession(string id)
    {
        if (streaming || Cur is not { } p) return;
        if (id != sid) EndConversation();
        sid = id;
        Prewarm(p, id);
        ClearMessages();
        var msgs = await Task.Run(() => Sessions.Conversation(p, id));
        if (sid != id) return;
        foreach (var m in msgs) AddMsg(m);
        ScrollEnd(true);
        LoadSessions();
    }

    void NewChat_Click(object s, RoutedEventArgs e)
    {
        if (streaming || Cur is not { } p) return;
        EndConversation();
        sid = null;
        Prewarm(p, null);
        RenderWelcome();
        LoadSessions();
    }

    // A conversation "ends" when the person leaves it. Then memory is consolidated in the background.
    void EndConversation()
    {
        if (dirty && sid != null && Cur is { } p) StartDream(p, new[] { sid });
        dirty = false;
        idle.Stop();
    }

    // ---------- memory ----------

    void StartDream(Person p, IList<string> ids)
    {
        memory[p.id] = ("running", DateTime.Now);
        ShowMemory();
        var started = Dream.Start(p, ids, ok => Dispatcher.BeginInvoke(() =>
        {
            memory[p.id] = (ok ? "done" : "error", DateTime.Now);
            ShowMemory();
            if (ok && p.id == pid) LoadGraph();
        }));
        if (!started) { memory[p.id] = ("nothing", DateTime.Now); ShowMemory(); }
    }

    void ShowMemory()
    {
        Memory.Children.Clear();
        if (Cur is not { } p || !authed) return;
        var (status, at) = memory.GetValueOrDefault(p.id, ("idle", DateTime.Now));
        if (T.Mem.TryGetValue(status, out var label))
            Memory.Children.Add(new TextBlock { Text = label + (status == "done" ? " · " + T.Ago((int)(DateTime.Now - at).TotalMinutes) : ""),
                FontSize = 12.5, Foreground = Brush("Muted"), Margin = new Thickness(6, 0, 0, 4) });
        if (status == "running") return;
        var b = new Button { Content = T.MemBtn, Style = (Style)FindResource("Ghost"), FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) =>
        {
            var dreamed = Store.Dreamed(p.id);
            var ids = Sessions.List(p).Where(s => dreamed.GetValueOrDefault(s.id) < s.at).Select(s => s.id).ToList();
            if (ids.Count == 0) { memory[p.id] = ("nothing", DateTime.Now); ShowMemory(); return; }
            if (ids.Contains(sid ?? "")) dirty = false;
            StartDream(p, ids);
        };
        Memory.Children.Add(b);
    }

    // ---------- sign-in ----------

    readonly DispatcherTimer authPoll = new() { Interval = TimeSpan.FromSeconds(1.5) };

    void RenderLogin()
    {
        ClearMessages();
        Actions.Visibility = Visibility.Collapsed;
        var w = new StackPanel { Margin = new Thickness(0, 60, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        w.Children.Add(new TextBlock { Text = T.LoginTitle, FontFamily = (FontFamily)FindResource("Serif"), FontSize = 34, Margin = new Thickness(0, 0, 0, 14) });
        w.Children.Add(new TextBlock { Text = T.LoginText, FontSize = 17, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 0, 24) });
        var btn = new Button { Content = T.LoginBtn, Style = (Style)FindResource("Primary"), FontSize = 18, Padding = new Thickness(28, 14, 28, 14), HorizontalAlignment = HorizontalAlignment.Left, Tag = "18" };
        w.Children.Add(btn);
        var pending = new StackPanel { Visibility = Visibility.Collapsed };
        var wait = new TextBlock { Text = T.LoginWaiting, FontSize = 17 };
        pending.Children.Add(wait);
        var fallback = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 18, 0, 0) };
        var again = new Button { Content = T.LoginAgain, Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4, 0, 4) };
        again.Click += (_, _) => { if (Auth.Url is { } u) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u) { UseShellExecute = true }); };
        fallback.Children.Add(again);
        fallback.Children.Add(new TextBlock { Text = T.LoginCode, Margin = new Thickness(0, 12, 0, 6) });
        var codeRow = new DockPanel();
        var codeBtn = new Button { Content = T.LoginCodeBtn, Style = (Style)FindResource("Primary"), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(codeBtn, Dock.Right);
        var code = new TextBox { Style = (Style)FindResource("Field") };
        codeRow.Children.Add(codeBtn);
        codeRow.Children.Add(code);
        codeBtn.Click += (_, _) => { if (code.Text.Trim().Length > 0) Auth.Code(code.Text.Trim()); };
        fallback.Children.Add(codeRow);
        pending.Children.Add(fallback);
        w.Children.Add(pending);
        var err = new TextBlock { Foreground = Brush("Warn"), Margin = new Thickness(0, 14, 0, 0) };
        w.Children.Add(err);
        Messages.Children.Add(w);

        var startedAt = DateTime.Now;
        void ShowPending() { btn.Visibility = Visibility.Collapsed; pending.Visibility = Visibility.Visible; err.Text = ""; startedAt = DateTime.Now; }
        btn.Click += (_, _) => { Auth.Start(); ShowPending(); authPoll.Start(); };
        authPoll.Tick -= AuthTick;
        authPoll.Tick += AuthTick;
        if (Auth.Pending) { ShowPending(); authPoll.Start(); }

        void AuthTick(object? s, EventArgs e)
        {
            if (Auth.LoggedIn())
            {
                authPoll.Stop();
                Auth.Stop();
                authed = true;
                Actions.Visibility = Visibility.Visible;
                SelectPerson(pid);
                return;
            }
            // Claude opens the sign-in page itself. The fallback link and code box appear only if nothing happened for a while.
            if (DateTime.Now - startedAt > TimeSpan.FromSeconds(20)) fallback.Visibility = Visibility.Visible;
            if (Auth.Failed)
            {
                authPoll.Stop();
                err.Text = T.LoginFailed;
                btn.Visibility = Visibility.Visible;
                pending.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ---------- chat ----------

    bool streaming;

    void ClearMessages()
    {
        Messages.Children.Clear();
        Scroller.ScrollToTop();
    }

    void RenderWelcome()
    {
        if (!authed) { RenderLogin(); return; }
        ClearMessages();
        var w = new StackPanel { Margin = new Thickness(0, 50, 0, 0), Tag = "welcome" };
        if (people.Count == 0)
        {
            w.Children.Add(new TextBlock { Text = T.Hello(""), FontFamily = (FontFamily)FindResource("Serif"), FontSize = 36, Margin = new Thickness(0, 0, 0, 12) });
            w.Children.Add(new TextBlock { Text = T.NoPeople, FontSize = 17, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 0, 24) });
            var add = new Button { Content = T.NoPeopleBtn, Style = (Style)FindResource("Primary"), FontSize = 18, Padding = new Thickness(28, 14, 28, 14), HorizontalAlignment = HorizontalAlignment.Left, Tag = "18" };
            add.Click += (_, _) => RenderAddPerson();
            w.Children.Add(add);
            Messages.Children.Add(w);
            Actions.Visibility = Visibility.Collapsed;
            return;
        }
        Actions.Visibility = Visibility.Collapsed;
        w.Children.Add(new TextBlock { Text = T.Hello(Cur?.name ?? ""), FontFamily = (FontFamily)FindResource("Serif"), FontSize = 36, Margin = new Thickness(0, 0, 0, 10) });
        w.Children.Add(new TextBlock { Text = T.HelloSub, FontSize = 17, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 0, 22) });
        var cards = new WrapPanel();
        foreach (var a in T.Actions)
        {
            var sp = new StackPanel();
            sp.Children.Add(new Shp { Data = Geometry.Parse(a.icon), Stroke = Brush("Accent"), StrokeThickness = 1.6, Width = 30, Height = 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
            sp.Children.Add(new TextBlock { Text = a.title, FontWeight = FontWeights.SemiBold, FontSize = 16 });
            sp.Children.Add(new TextBlock { Text = a.sub, FontWeight = FontWeights.Normal, FontSize = 13.5, Foreground = Brush("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            var c = new Button { Content = sp, Style = (Style)FindResource("Card") };
            c.Click += (_, _) => Send(null, a.skill);
            cards.Children.Add(c);
        }
        w.Children.Add(cards);
        Messages.Children.Add(w);
    }

    void RenderActions()
    {
        Actions.Children.Clear();
        foreach (var a in T.Actions)
        {
            var b = new Button { Content = IconText(a.icon, a.title, Brush("Accent"), 15), Style = (Style)FindResource("Chip"), ToolTip = a.sub };
            b.Click += (_, _) => Send(null, a.skill);
            Actions.Children.Add(b);
        }
    }

    void AddMsg(Msg m)
    {
        if (m.skill != null) AddSkill(m.skill);
        else if (m.role == "user") AddUser(m.text ?? "");
        else AddAssistant(m.text ?? "");
    }

    void RemoveWelcome()
    {
        if (authed && people.Count > 0) Actions.Visibility = Visibility.Visible;
        foreach (var c in Messages.Children.OfType<FrameworkElement>().Where(c => c.Tag is "welcome").ToList()) Messages.Children.Remove(c);
    }

    void AddUser(string text)
    {
        RemoveWelcome();
        var tb = new TextBox { Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush("Ink"),
            TextWrapping = TextWrapping.Wrap, FontSize = 15.5, Padding = new Thickness(0), SelectionBrush = Brush("AccentHi") };
        Messages.Children.Add(new Border { Child = tb, Background = Brush("Accent"), CornerRadius = new CornerRadius(18, 18, 6, 18), Padding = new Thickness(16, 10, 16, 10),
            HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 620, Margin = new Thickness(60, 10, 0, 10) });
    }

    void AddSkill(string skill)
    {
        RemoveWelcome();
        var a = T.Actions.FirstOrDefault(x => x.skill == skill);
        Messages.Children.Add(new Border { Child = a != null ? IconText(a.icon, a.title, Brush("Accent"), 16) : new TextBlock { Text = skill },
            Background = Brush("GlassStrong"), BorderBrush = Brush("Edge"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 10) });
        TextElement.SetForeground((Border)Messages.Children[^1], Brush("AccentDeep"));
    }

    RichTextBox AddAssistant(string md)
    {
        RemoveWelcome();
        var rtb = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 10), Foreground = Brush("Text"), SelectionBrush = Brush("AccentHi"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Document = Markdown.Render(md, null, 15.5) };
        Messages.Children.Add(rtb);
        return rtb;
    }

    void AddError(string text) =>
        Messages.Children.Add(new TextBlock { Text = text, Foreground = Brush("Warn"), Margin = new Thickness(0, 6, 0, 6) });

    void ScrollEnd(bool force = false)
    {
        var atBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 60;
        if (force || atBottom) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Scroller.ScrollToEnd());
    }

    // One turn of the conversation as it streams in.
    class Turn
    {
        public required ClaudeProcess? proc;
        public required StackPanel work;
        public required string? skill;
        public readonly StringBuilder buf = new();
        public RichTextBox? bubble;
        public bool changed, stopped;
        public string lastTool = "";
    }
    Turn? turn;
    readonly DispatcherTimer render = new() { Interval = TimeSpan.FromMilliseconds(90) };

    // ---------- the live Claude process ----------

    (string pid, string? sid) liveKey;
    Task<ClaudeProcess>? liveTask;

    void Prewarm(Person p, string? s)
    {
        if (!authed) return;
        if (liveTask != null && liveKey == (p.id, s) && !liveTask.IsFaulted && (!liveTask.IsCompleted || liveTask.Result.Alive)) return;
        DropLive();
        liveKey = (p.id, s);
        liveTask = Task.Run(() => Wire(new ClaudeProcess(p, s)));
    }

    async Task<ClaudeProcess> Live(Person p, string? s)
    {
        if (liveTask != null && liveKey == (p.id, s))
        {
            try { var cp = await liveTask; if (cp.Alive && !cp.Busy) return cp; } catch { }
        }
        DropLive();
        liveKey = (p.id, s);
        liveTask = Task.Run(() => Wire(new ClaudeProcess(p, s)));
        return await liveTask;
    }

    void DropLive()
    {
        var t = liveTask;
        liveTask = null;
        t?.ContinueWith(x => { if (x.IsCompletedSuccessfully) x.Result.Dispose(); });
    }

    ClaudeProcess Wire(ClaudeProcess cp)
    {
        void On(Action<Turn> a) => Dispatcher.BeginInvoke(() => { if (turn != null && turn.proc == cp) a(turn); });
        cp.Session += id => Dispatcher.BeginInvoke(() =>
        {
            if (liveKey.pid == cp.Person.id && liveTask != null && liveTask.IsCompleted && liveTask.Result == cp) liveKey = (cp.Person.id, id);
            if (turn?.proc == cp) sid = id;
        });
        cp.Block += () => On(t => { if (t.buf.Length > 0) t.buf.Append("\n\n"); });
        cp.Text += s => On(t => { t.buf.Append(s); t.changed = true; });
        cp.Tool += (name, input) => On(t => ToolLine(t, name, input));
        cp.Done += (ok, err) => On(t => Finish(t, ok, err));
        return cp;
    }

    async void Send(string? text, string? skill)
    {
        if (streaming || Cur is not { } p || !authed) return;
        text = (text ?? "").Trim();
        if (text.Length == 0 && skill == null) return;
        var prompt = skill != null ? $"/concilium:{skill}{(text.Length > 0 ? " " + text : "")}" : text;
        if (skill != null) AddSkill(skill); else AddUser(text);
        Input.Text = "";
        var work = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        work.Children.Add(Pulse(T.Thinking));
        Messages.Children.Add(work);
        ScrollEnd(true);
        var t = turn = new Turn { proc = null, work = work, skill = skill };
        SetBusy(true);
        try
        {
            t.proc = await Live(p, sid);
            if (t.stopped) { Finish(t, false, null); return; }
            render.Tick -= RenderTick;
            render.Tick += RenderTick;
            render.Start();
            t.proc.Send(prompt);
        }
        catch (Exception e)
        {
            Paths.LogError(e);
            Finish(t, false, e.Message);
        }
    }

    UIElement Pulse(string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Brush("Accent"), Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        sp.Children.Add(dot);
        sp.Children.Add(new TextBlock { Text = text, Foreground = Brush("Muted"), FontSize = 14 });
        return sp;
    }

    void ToolLine(Turn t, string name, string input)
    {
        // Internal plumbing is not shown; file names mean nothing to the person, only web topics and sources are worth showing.
        if (!T.Tool.TryGetValue(name, out var label)) return;
        var text = name is "WebSearch" or "WebFetch" && input.Length > 0 ? $"{label}: {input}" : label;
        if (text == t.lastTool) return;
        t.lastTool = text;
        foreach (var c in t.work.Children.OfType<StackPanel>()) { c.Children[0].BeginAnimation(OpacityProperty, null); c.Children[0].Opacity = 0.3; }
        t.work.Children.Add(Pulse(text));
        while (t.work.Children.Count > 4) t.work.Children.RemoveAt(0);
        // Keep the activity under the latest text.
        Messages.Children.Remove(t.work);
        Messages.Children.Add(t.work);
        ScrollEnd();
    }

    void RenderTick(object? s, EventArgs e)
    {
        if (turn is not { changed: true } t) return;
        t.changed = false;
        if (t.bubble == null)
        {
            t.bubble = AddAssistant("");
            Messages.Children.Remove(t.work);
            Messages.Children.Add(t.work);
        }
        var bottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 60;
        t.bubble.Document = Markdown.Render(t.buf.ToString(), null, 15.5);
        if (bottom) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Scroller.ScrollToEnd());
    }

    void Finish(Turn t, bool ok, string? err)
    {
        if (turn != t) return;
        RenderTick(null, EventArgs.Empty);
        render.Stop();
        Messages.Children.Remove(t.work);
        turn = null;
        streaming = false;
        SetBusy(false);
        if (t.stopped) AddError(T.Stopped);
        else if (!ok)
        {
            var needsLogin = err != null && Regex.IsMatch(err, "login|authenticat|OAuth|401|API key", RegexOptions.IgnoreCase);
            AddError(needsLogin ? T.NeedsLogin : T.Failed + (err ?? ""));
            if (needsLogin) { authed = false; DropLive(); Dispatcher.BeginInvoke(DispatcherPriority.Background, RenderLogin); }
        }
        ScrollEnd();
        dirty = t.skill != "sleep";
        idle.Stop();
        idle.Start();
        LoadSessions();
        LoadGraph();
        // The process stays for the next message; if it died, start a fresh one now.
        if (authed && Cur is { } p && (t.proc == null || !t.proc.Alive)) { DropLive(); Prewarm(p, sid); }
    }

    void SetBusy(bool b)
    {
        streaming = b;
        SendIcon.Visibility = b ? Visibility.Collapsed : Visibility.Visible;
        StopIcon.Visibility = b ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in Actions.Children.OfType<Button>()) c.IsEnabled = !b;
        foreach (var c in Messages.Children.OfType<StackPanel>().SelectMany(w => w.Children.OfType<WrapPanel>()).SelectMany(w => w.Children.OfType<Button>())) c.IsEnabled = !b;
        NewChatBtn.IsEnabled = !b;
    }

    void Stop()
    {
        if (turn is not { } t) return;
        t.stopped = true;
        if (t.proc == null) return;
        t.proc.Interrupt();
        var cp = t.proc;
        // If it does not stop by itself, end the process; a new one is started for the next message.
        Task.Delay(5000).ContinueWith(_ => { if (cp.Busy) cp.Dispose(); });
    }

    void Send_Click(object s, RoutedEventArgs e)
    {
        if (streaming) Stop();
        else Send(Input.Text, null);
    }

    void Input_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!streaming) Send(Input.Text, null);
        }
    }

    void Input_TextChanged(object s, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---------- map and notes ----------

    void Map_Click(object s, RoutedEventArgs e) => SetPanel(!panelOpen);

    // The map gives way to the conversation on a laptop screen.
    double PanelWidth => ActualWidth > 0 && ActualWidth < 1450 ? 340 : 400;

    void SetPanel(bool open)
    {
        panelOpen = open;
        PanelBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PanelCol.Width = open ? new GridLength(PanelWidth) : new GridLength(0);
        SaveUi();
        if (open && graph == null) LoadGraph();
    }

    async void LoadGraph()
    {
        if (!panelOpen || Cur is not { } p) return;
        GraphData g;
        try { g = await Task.Run(() => NoteGraph.Build(p)); }
        catch (Exception e) { Paths.LogError(e); return; }
        if (p.id != pid) return;
        graph = g;
        Graph.SetData(g);
        RenderLegend();
    }

    void RenderLegend()
    {
        Legend.Children.Clear();
        if (graph == null) return;
        var kinds = graph.nodes.Select(n => n.kind).ToHashSet();
        foreach (var (k, v) in T.Legend)
        {
            if (!kinds.Contains(k)) continue;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
            sp.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(GraphView.Colors[k]), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = v, FontSize = 12, Foreground = Brush("Muted") });
            Legend.Children.Add(sp);
        }
    }

    void TabMap_Click(object s, RoutedEventArgs e) => ShowTab(false);
    void TabNote_Click(object s, RoutedEventArgs e) => ShowTab(true);

    void ShowTab(bool note)
    {
        MapTab.Visibility = note ? Visibility.Collapsed : Visibility.Visible;
        NoteTab.Visibility = note ? Visibility.Visible : Visibility.Collapsed;
        TabMap.Style = (Style)FindResource(note ? "Pill" : "PillActive");
        TabNote.Style = (Style)FindResource(note ? "PillActive" : "Pill");
    }

    void OpenNote(string id)
    {
        if (Cur is not { } p || !NoteGraph.Readable(p, id)) return;
        var full = NoteGraph.Full(p, id);
        if (!id.EndsWith(".md"))
        {
            // Documents (PDF, photos) open in their usual program.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true }); } catch { }
            return;
        }
        try
        {
            NoteTitle.Text = Path.GetFileNameWithoutExtension(id);
            NoteView.Document = Markdown.Render(File.ReadAllText(full), OpenWiki, 14.5);
            ShowTab(true);
        }
        catch (Exception e) { Paths.LogError(e); }
    }

    void OpenWiki(string name)
    {
        var n = name.ToLowerInvariant();
        var node = graph?.nodes.FirstOrDefault(x => x.label.ToLowerInvariant() == n || x.id.ToLowerInvariant() == n);
        if (node != null && node.kind != "missing") OpenNote(node.id);
    }

    // ---------- window buttons ----------

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximized window without the system frame hangs over the screen edge by the resize border.
        var b = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4) : new Thickness(0);
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Rounded corners like every Windows 11 window (ignored on Windows 10).
        var round = 2;
        try { DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 33, ref round, sizeof(int)); } catch { }
        OnStateChanged(EventArgs.Empty);
    }

    // ---------- closing ----------

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveUi();
        authPoll.Stop();
        Auth.Stop();
        // The memory pass outlives the window: it is a separate program run.
        if (dirty && sid != null && Cur is { } p) Dream.Start(p, new[] { sid });
        if (liveTask is { IsCompletedSuccessfully: true } t) t.Result.Dispose(); else DropLive();
        base.OnClosing(e);
    }
}
