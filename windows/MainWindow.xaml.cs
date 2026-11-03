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
    // Conversations waiting for the running memory pass of their person; conversations being written right now.
    readonly Dictionary<string, List<string>> queued = new();
    readonly HashSet<string> writing = new(), warned = new();
    // Screenshots never spend tokens on memory.
    static readonly bool Shot = Environment.GetEnvironmentVariable("CONCILIUM_SHOT") is { Length: > 0 };
    readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromMinutes(15) };
    List<Person> Shown => people.Where(p => !p.hidden).ToList();
    Person? Cur => Shown.FirstOrDefault(p => p.id == pid);
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
                // CONCILIUM_SHOT_ATTACH=<file;file>: attach files before sending (or just to show them).
                if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT_ATTACH") is { Length: > 0 } att)
                    AddAttachments(att.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                // CONCILIUM_SHOT_SEND=<text>: also send a message (or /skill) and wait for the answer first.
                if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT_SEND") is { Length: > 0 } say)
                {
                    // Several messages: "a || b". "@last" opens the latest conversation first.
                    foreach (var part in say.Split("||", StringSplitOptions.TrimEntries))
                    {
                        // "@<id>" opens that conversation.
                        if (part.StartsWith('@'))
                        {
                            if (part != "@last") OpenSession(part[1..]);
                            else if (Cur is { } cp && Sessions.List(cp).FirstOrDefault() is { } last) OpenSession(last.id);
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
        authed = Paths.HasClaude && Auth.LoggedIn();
        people = Store.People();
        try { Store.Baseline(people); } catch (Exception e) { Paths.LogError(e); }
        if (Cur == null) pid = Shown.FirstOrDefault()?.id ?? "";
        RenderPeople();
        updated = Updater.JustUpdated();
        // Snapshots do not update, unless a test pretends to be an older version (CONCILIUM_VERSION).
        var updates = !Shot || Environment.GetEnvironmentVariable("CONCILIUM_VERSION") != null;
        if (updates) _ = Updater.Check();
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
        AttachBtn.ToolTip = T.Attach;
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
        foreach (var p in Shown)
        {
            var b = new Button { Content = p.name, Style = (Style)FindResource(p.id == pid ? "PillActive" : "Pill") };
            b.Click += (_, _) => { if (!streaming) SelectPerson(p.id); };
            b.MouseRightButtonUp += (_, e) => { if (!streaming) ShowRemove(b, p); e.Handled = true; };
            People.Children.Add(b);
        }
        if (Shown.Count > 0)
        {
            var add = new Button { Content = T.AddPerson, Style = (Style)FindResource("Pill") };
            add.Click += (_, _) => { if (!streaming) RenderAddPerson(); };
            People.Children.Add(add);
        }
    }

    // Right click on a name: "Remove" with a line saying it comes back. Only hides the person; the folder is not touched.
    void ShowRemove(Button anchor, Person p)
    {
        var item = new StackPanel();
        item.Children.Add(new TextBlock { Text = T.Remove, FontWeight = FontWeights.SemiBold });
        item.Children.Add(new TextBlock { Text = T.RemoveHint, Foreground = Brush("Muted"), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Margin = new Thickness(0, 3, 0, 0) });
        var btn = new Button { Content = item, Style = (Style)FindResource("Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10) };
        var pop = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, VerticalOffset = 6,
            StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Child = btn, Background = Brush("GlassStrong"), BorderBrush = Brush("Edge"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(4) },
        };
        btn.Click += (_, _) => { pop.IsOpen = false; RemovePerson(p); };
        pop.IsOpen = true;
    }

    void RemovePerson(Person p)
    {
        var i = people.FindIndex(x => x.id == p.id);
        if (i < 0) return;
        people[i] = p with { hidden = true };
        Store.SavePeople(people);
        if (pid == p.id) SelectPerson(Shown.FirstOrDefault()?.id ?? "");
        else RenderPeople();
    }

    void SelectPerson(string id)
    {
        // Switching people does not start a memory pass: the conversation is remembered when the window closes,
        // after a quiet spell, or on the next start.
        dirty = false;
        idle.Stop();
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
        CatchUp(p);
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
        if (Shown.Count > 0) buttons.Children.Add(cancel);
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
        var full = Path.GetFullPath(folder).TrimEnd('\\');
        // One folder, one person: adding a removed person's folder brings them back as they were.
        var i = people.FindIndex(x => string.Equals(Path.GetFullPath(x.path).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            people[i] = people[i] with { name = name, hidden = false };
            Store.SavePeople(people);
            return people[i].id;
        }
        // The agent's rules go into any folder that lacks them; files already there are never replaced.
        // The memory skeleton only into an empty folder: notes without memory get theirs built by the agent from the notes,
        // and a folder with any "* MEMORY.md" (e.g. "Mom MEMORY.md") already has memory.
        var empty = !Directory.EnumerateFileSystemEntries(folder).Any();
        string[] rules = { "AGENTS.md", "CLAUDE.md", "PRINCIPLES.md" };
        foreach (var f in Directory.GetFiles(Paths.Template, "*.md"))
        {
            var n = Path.GetFileName(f);
            var to = Path.Combine(folder, n);
            if (n != "README.md" && (empty || rules.Contains(n)) && !File.Exists(to)) File.Copy(f, to);
        }
        var p = new Person(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x"), name, full);
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
            var meta = new StackPanel { Orientation = Orientation.Horizontal };
            meta.Children.Add(new TextBlock { Text = T.Ago((int)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s.at) / 60000)), FontSize = 12, Foreground = Brush("Muted"), FontWeight = FontWeights.Normal });
            if (writing.Contains(s.id)) { var w = (FrameworkElement)Pulse(T.Writing, 12, 6); w.Margin = new Thickness(10, 0, 0, 0); meta.Children.Add(w); }
            sp.Children.Add(meta);
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
        var (text, files) = Documents.Split(t);
        t = Regex.Replace(text, "<[^>]+>", "").Trim();
        return t.Length > 0 ? t : files.Length > 0 ? Path.GetFileName(files[0]) : "…";
    }

    async void OpenSession(string id)
    {
        if (streaming || Cur is not { } p) return;
        if (id != sid) { dirty = false; idle.Stop(); }
        sid = id;
        Prewarm(p, id);
        ClearMessages();
        var msgs = await Task.Run(() => Sessions.Conversation(p, id));
        if (sid != id) return;
        foreach (var m in msgs) AddMsg(m);
        // What the last memory pass took from this conversation, if it came after its last message.
        if (Store.Note(p.id, id) is { } note && Sessions.List(p).FirstOrDefault(x => x.id == id) is { } info && note.at >= info.at) AddNote(note.text);
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

    // A conversation "ends" with "New conversation" or a quiet spell. Then memory is consolidated in the background.
    void EndConversation()
    {
        if (dirty && sid != null && Cur is { } p) StartDream(p, new[] { sid });
        dirty = false;
        idle.Stop();
    }

    // ---------- memory ----------

    // Conversations newer than their last memory pass: from the Code tab, a pass that failed, the window closed mid-pass.
    static List<string> Unsaved(Person p, ISet<string> busy)
    {
        var dreamed = Store.Dreamed(p.id);
        return Sessions.List(p).Where(s => dreamed.GetValueOrDefault(s.id) < s.at && !busy.Contains(s.id)).Select(s => s.id).ToList();
    }

    // Nobody has to press "remember": what was not remembered yet is remembered as soon as the person is opened.
    async void CatchUp(Person p)
    {
        if (Shot || !authed) return;
        // A pass started by a window that has since closed may still be writing: wait for it instead of starting another.
        if (memory.GetValueOrDefault(p.id).status != "running" && Store.Running(p.id) is { } run)
        {
            memory[p.id] = ("running", DateTime.Now);
            writing.UnionWith(run);
            ShowMemory();
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(Store.RunningProc(p.id));
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => Dispatcher.BeginInvoke(() => Remembered(p, run));
                if (proc.HasExited) Remembered(p, run);
            }
            catch { Remembered(p, run); }
            return;
        }
        var busy = writing.ToHashSet();
        var ids = await Task.Run(() => Unsaved(p, busy));
        if (ids.Count > 0) StartDream(p, ids);
    }

    void StartDream(Person p, IList<string> ids)
    {
        // One pass at a time in a folder: two would edit the same files.
        if (memory.GetValueOrDefault(p.id).status == "running")
        {
            if (!queued.TryGetValue(p.id, out var q)) queued[p.id] = q = new();
            q.AddRange(ids.Except(q).Except(writing));
            return;
        }
        memory[p.id] = ("running", DateTime.Now);
        writing.UnionWith(ids);
        ShowMemory();
        if (p.id == pid) LoadSessions();
        if (Dream.Start(p, ids, _ => Dispatcher.BeginInvoke(() => Remembered(p, ids))) == null)
        {
            memory[p.id] = ("error", DateTime.Now);
            Dreamt(p, ids);
        }
    }

    // A pass ended (this window's or an earlier one's): show its outcome, and its note under the open conversation.
    void Remembered(Person p, IList<string> ids)
    {
        if (!writing.Overlaps(ids)) return;
        var ok = Store.Last(p.id)?.ok == true;
        memory[p.id] = (ok ? "done" : "error", DateTime.Now);
        Dreamt(p, ids);
        if (!ok || p.id != pid) return;
        LoadGraph();
        if (sid != null && ids.Contains(sid) && !streaming && Store.Note(p.id, sid) is { } note) { AddNote(note.text); ScrollEnd(); }
    }

    void Dreamt(Person p, IList<string> ids)
    {
        writing.ExceptWith(ids);
        ShowMemory();
        if (p.id == pid) LoadSessions();
        if (queued.Remove(p.id, out var q) && q.Count > 0) StartDream(p, q);
    }

    Version? updated;

    void ShowMemory()
    {
        Memory.Children.Clear();
        if (updated != null)
            Memory.Children.Add(new TextBlock { Text = string.Format(T.Updated, updated), FontSize = 12.5, Foreground = Brush("Muted"), Margin = new Thickness(6, 0, 0, 4) });
        if (Cur is not { } p || !authed) return;
        var (status, at) = memory.GetValueOrDefault(p.id, ("idle", DateTime.Now));
        if (T.Mem.TryGetValue(status, out var label))
            Memory.Children.Add(new TextBlock { Text = label + (status == "done" ? " · " + T.Ago((int)(DateTime.Now - at).TotalMinutes) : ""),
                FontSize = 12.5, Foreground = Brush("Muted"), Margin = new Thickness(6, 0, 0, 4) });
        if (status == "running") return;
        var b = new Button { Content = T.MemBtn, Style = (Style)FindResource("Ghost"), FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) =>
        {
            var ids = Unsaved(p, writing);
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
        // Claude Code is Anthropic's program: say so and ask before installing it, then sign-in follows by itself.
        var install = !Paths.HasClaude && !Installer.Pending;
        if ((install ? T.InstallText : T.LoginText) is { Length: > 0 } intro)
            w.Children.Add(new TextBlock { Text = intro, FontSize = 17, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 0, 24), TextWrapping = TextWrapping.Wrap });
        var btn = new Button { Content = install ? T.InstallBtn : T.LoginBtn, Style = (Style)FindResource("Primary"), FontSize = 18, Padding = new Thickness(28, 14, 28, 14), HorizontalAlignment = HorizontalAlignment.Left, Tag = "18" };
        w.Children.Add(btn);
        var pending = new StackPanel { Visibility = Visibility.Collapsed };
        var wait = new TextBlock { Text = T.LoginWaiting, FontSize = 17 };
        pending.Children.Add(wait);
        var err = new TextBlock { Foreground = Brush("Warn"), Margin = new Thickness(0, 14, 0, 0) };
        var startedAt = DateTime.Now;
        var fallback = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
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
        var rejected = 0;
        void Submit()
        {
            var text = code.Text.Trim();
            if (text.Length == 0) return;
            if (Auth.IsPageLink(text)) { err.Text = T.LoginPageLink; code.Text = ""; return; }
            rejected = Auth.Rejected;
            if (!Auth.Pending) { if (!Auth.Start()) { err.Text = T.Vpn; return; } rejected = 0; err.Text = T.LoginExpired; code.Text = ""; startedAt = DateTime.Now; return; }
            err.Text = "";
            wait.Text = Auth.Code(text) ? T.LoginChecking : T.LoginFailed;
        }
        codeBtn.Click += (_, _) => Submit();
        code.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        // A paste is the code: send it at once, nothing else to press.
        // A second paste replaces the first instead of sticking to it.
        DataObject.AddPastingHandler(code, (_, _) => { code.Clear(); Dispatcher.BeginInvoke(DispatcherPriority.Background, Submit); });
        fallback.Children.Add(codeRow);
        pending.Children.Add(fallback);
        w.Children.Add(pending);
        w.Children.Add(err);
        Messages.Children.Add(w);

        // "Open the page again" and the code field only once the sign-in page has been opened, not while getting ready or installing.
        void ShowPending(string text, bool page)
        {
            btn.Visibility = Visibility.Collapsed; pending.Visibility = Visibility.Visible; err.Text = ""; startedAt = DateTime.Now;
            wait.Text = text; fallback.Visibility = page ? Visibility.Visible : Visibility.Collapsed;
        }
        void ShowButton(string error) { btn.Visibility = Visibility.Visible; pending.Visibility = Visibility.Collapsed; err.Text = error; }
        // One button: if Claude Code is not on this computer yet, it is installed first, then sign-in starts by itself.
        async void Begin()
        {
            // The region check takes up to a few seconds: say something at once instead of a button that does nothing.
            ShowPending(T.Starting, false);
            if (await UnsupportedRegion.Blocked()) { ShowButton(T.Vpn); return; }
            if (Paths.HasClaude) { ShowPending(T.LoginWaiting, true); Auth.Start(); }
            else { ShowPending(T.Installing, false); Installer.Start(); }
            authPoll.Start();
        }
        btn.Click += (_, _) => Begin();
        authPoll.Tick -= AuthTick;
        authPoll.Tick += AuthTick;
        if (Installer.Pending) { ShowPending(T.Installing, false); authPoll.Start(); }
        else if (Auth.Pending) { ShowPending(T.LoginWaiting, true); authPoll.Start(); }

        void AuthTick(object? s, EventArgs e)
        {
            if (Installer.Pending)
            {
                var (done, total) = Installer.Progress();
                wait.Text = done > 0 && total > 0 && done < total ? string.Format(T.Downloading, done >> 20, total >> 20) : T.Installing;
                return;
            }
            if (!Paths.HasClaude)
            {
                authPoll.Stop();
                ShowButton(T.InstallFailed);
                return;
            }
            Installer.EnsurePath();
            // Just installed: go on to sign-in.
            if (!Auth.Pending && !Auth.LoggedIn() && !Auth.Failed)
            {
                if (!Auth.Start()) { authPoll.Stop(); ShowButton(T.Vpn); return; }
                ShowPending(T.LoginWaiting, true); return;
            }
            if (Auth.LoggedIn())
            {
                authPoll.Stop();
                Auth.Stop();
                authed = true;
                Actions.Visibility = Visibility.Visible;
                SelectPerson(pid);
                return;
            }
            // The CLI answers a wrong code on its output; say so instead of staying silent.
            if (Auth.Rejected > rejected) { rejected = Auth.Rejected; err.Text = T.LoginBadCode; wait.Text = T.LoginWaiting; code.SelectAll(); code.Focus(); }
            if (Auth.Failed)
            {
                authPoll.Stop();
                ShowButton(T.LoginFailed);
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
        if (Shown.Count == 0)
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
        // "Remember" is not a button: memory is kept by itself; a small link under the conversations stays for those who want it.
        foreach (var a in T.Actions.Where(a => a.skill != "remember"))
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
        foreach (var a in T.Actions.Where(a => a.skill != "remember"))
        {
            var b = new Button { Content = IconText(a.icon, a.title, Brush("Accent"), 15), Style = (Style)FindResource("Chip"), ToolTip = a.sub };
            b.Click += (_, _) => Send(null, a.skill);
            Actions.Children.Add(b);
        }
    }

    void AddMsg(Msg m)
    {
        if (m.skill != null) AddSkill(m.skill);
        else if (m.role == "user") AddUser(m.text ?? "", m.files);
        else AddAssistant(m.text ?? "");
    }

    void RemoveWelcome()
    {
        if (authed && Shown.Count > 0) Actions.Visibility = Visibility.Visible;
        foreach (var c in Messages.Children.OfType<FrameworkElement>().Where(c => c.Tag is "welcome").ToList()) Messages.Children.Remove(c);
    }

    void AddUser(string text, string[]? files = null)
    {
        RemoveWelcome();
        if (files is { Length: > 0 })
        {
            var w = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(60, 10, 0, text.Length > 0 ? -4 : 2) };
            foreach (var f in files)
            {
                var b = new Button { Content = IconText(IDoc, Path.GetFileName(f), Brush("Accent"), 15), Style = (Style)FindResource("Chip"), ToolTip = T.OpenFile };
                b.Click += (_, _) => OpenNote(f);
                w.Children.Add(b);
            }
            Messages.Children.Add(w);
        }
        if (text.Length == 0) return;
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

    // A quiet grey line, not a message: what memory took from the conversation.
    void AddNote(string text)
    {
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 620 };
        if (T.Actions.FirstOrDefault(x => x.skill == "remember") is { } a)
        {
            var icon = new Shp { Data = Geometry.Parse(a.icon), Stroke = Brush("Muted"), StrokeThickness = 1.6, Width = 13, Height = 13, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 3, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
        }
        line.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap });
        Messages.Children.Add(line);
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
        cp.Tasks += (done, total) => On(t => ToolLine(t, "", string.Format(T.TasksDone, done, total)));
        cp.Done += (ok, err) => On(t => Finish(t, ok, err));
        return cp;
    }

    async void Send(string? text, string? skill)
    {
        if (streaming || Cur is not { } p || !authed) return;
        text = (text ?? "").Trim();
        if (text.Length == 0 && skill == null && attached.Count == 0) return;
        // From a network where Claude is not offered nothing would come back: say so, the message stays in the box.
        if (await UnsupportedRegion.Blocked()) { AddError(T.Vpn); ScrollEnd(true); return; }
        string[] files;
        try { files = attached.Select(f => Documents.Keep(p, f)).ToArray(); }
        catch (Exception e) { Paths.LogError(e); AddError(T.Failed + e.Message); return; }
        attached.Clear();
        RenderAttached();
        var prompt = skill != null ? $"/concilium:{skill}{(text.Length > 0 ? " " + text : "")}" : text;
        if (files.Length > 0) prompt += Documents.Note(files);
        if (skill != null) { AddSkill(skill); if (files.Length > 0) AddUser("", files); }
        else AddUser(text, files);
        Input.Text = "";
        var work = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        work.Children.Add(Pulse(T.Thinking));
        Messages.Children.Add(work);
        ScrollEnd(true);
        var t = turn = new Turn { proc = null, work = work, skill = skill };
        SetBusy(true);
        // The last conversation may still be going into memory; read half-written, it would start from the old picture.
        if (memory.GetValueOrDefault(p.id).status == "running")
        {
            work.Children.Clear();
            work.Children.Add(Pulse(T.Recalling));
            var t0 = DateTime.Now;
            while (memory.GetValueOrDefault(p.id).status == "running" && !t.stopped && DateTime.Now - t0 < TimeSpan.FromMinutes(4)) await Task.Delay(300);
            work.Children.Clear();
            work.Children.Add(Pulse(T.Thinking));
        }
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

    UIElement Pulse(string text, double size = 14, double d = 8)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var dot = new Ellipse { Width = d, Height = d, Fill = Brush("Accent"), Margin = new Thickness(2, 0, d + 2, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        sp.Children.Add(dot);
        sp.Children.Add(new TextBlock { Text = text, Foreground = Brush("Muted"), FontSize = size });
        return sp;
    }

    void ToolLine(Turn t, string name, string input)
    {
        // Internal plumbing is not shown; file names mean nothing to the person, only web topics and sources are worth showing.
        string? label = null;
        if (name.Length > 0 && !T.Tool.TryGetValue(name, out label)) return;
        var text = name.Length == 0 ? input : name is "WebSearch" or "WebFetch" && input.Length > 0 ? $"{label}: {input}" : label!;
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
        dirty = t.skill != "remember";
        if (ok && t.skill == "remember" && sid != null && Cur is { } cur)
            Store.MarkDreamed(cur.id, new Dictionary<string, long> { [sid] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        // A long conversation makes the agent worse well before the window is full: offer a fresh one at 30%.
        if (ok && t.proc is { } cp && sid != null && cp.Context > cp.Window * 0.3 && warned.Add(sid)) AddLongTalk();
        idle.Stop();
        idle.Start();
        LoadSessions();
        LoadGraph();
        // The process stays for the next message; if it died, start a fresh one now.
        if (authed && Cur is { } p && (t.proc == null || !t.proc.Alive)) { DropLive(); Prewarm(p, sid); }
    }

    void AddLongTalk()
    {
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 10), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        box.Children.Add(new TextBlock { Text = T.LongTalk, Foreground = Brush("Muted"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var b = new Button { Content = IconText("M12,5 V19 M5,12 H19", T.NewChat, Brush("Accent"), 15), Style = (Style)FindResource("Chip"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        b.Click += NewChat_Click;
        box.Children.Add(b);
        Messages.Children.Add(box);
        ScrollEnd();
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
        // Waiting for helpers in the background, the CLI has no turn to interrupt.
        Task.Delay(cp.Busy ? 5000 : 0).ContinueWith(_ => { if (cp.Busy || cp.Background > 0) cp.Dispose(); });
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
        // Ctrl+V with files or a picture (a screenshot, a photo from the phone app) attaches it; text pastes as usual.
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try
            {
                if (Clipboard.ContainsFileDropList()) { AddAttachments(Clipboard.GetFileDropList().Cast<string>()); e.Handled = true; }
                else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
                {
                    var dir = Path.Combine(Path.GetTempPath(), "Concilium");
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"{T.Picture} {DateTime.Now:HH-mm-ss}.png");
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                    using (var fs = File.Create(file)) enc.Save(fs);
                    AddAttachments(new[] { file });
                    e.Handled = true;
                }
            }
            catch (Exception ex) { Paths.LogError(ex); }
        }
    }

    // ---------- documents ----------

    const string IDoc = "M7,3 H14 L19,8 V21 H7 Z M14,3 V8 H19";
    readonly List<string> attached = new();

    void Attach_Click(object s, RoutedEventArgs e)
    {
        // The system file picker is the one window allowed besides the folder picker: there is no other way to reach a file.
        var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = T.Attach };
        if (d.ShowDialog(this) == true) AddAttachments(d.FileNames);
    }

    void AddAttachments(IEnumerable<string> files)
    {
        if (Cur == null || !authed) return;
        foreach (var f in files) if (File.Exists(f) && !attached.Contains(f)) attached.Add(f);
        RenderAttached();
        Input.Focus();
    }

    void RenderAttached()
    {
        Attached.Children.Clear();
        foreach (var f in attached.ToList())
        {
            var sp = IconText(IDoc, Path.GetFileName(f), Brush("Accent"), 15);
            sp.Children.Add(new TextBlock { Text = "×", FontSize = 15, Margin = new Thickness(10, -1, 0, 0), Foreground = Brush("Muted"), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Style = (Style)FindResource("Chip"), ToolTip = T.Remove };
            b.Click += (_, _) => { attached.Remove(f); RenderAttached(); };
            Attached.Children.Add(b);
        }
        Attached.Visibility = attached.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Attached.Margin = new Thickness(0, Actions.Visibility == Visibility.Visible ? 0 : 8, 0, 0);
    }

    void Window_DragOver(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = Cur != null && authed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        AddAttachments(files);
    }

    void Input_TextChanged(object s, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Typing means the conversation goes on: the quiet spell before remembering starts over.
        if (dirty) { idle.Stop(); idle.Start(); }
    }

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
        // Closing the window remembers what is not remembered yet, for everyone: the pass is a program of its own and
        // outlives the window. If a pass is already writing a folder, the next start catches that person up instead.
        if (!Shot && authed)
            foreach (var p in people)
                if (memory.GetValueOrDefault(p.id).status != "running" && Store.Running(p.id) == null && Unsaved(p, writing) is { Count: > 0 } ids)
                    Dream.Start(p, ids);
        if (liveTask is { IsCompletedSuccessfully: true } t) t.Result.Dispose(); else DropLive();
        if (!Shot || Environment.GetEnvironmentVariable("CONCILIUM_VERSION") != null) Updater.ApplyOnExit();
        base.OnClosing(e);
    }
}
