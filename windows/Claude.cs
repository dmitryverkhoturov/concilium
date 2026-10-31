// Talks to Claude Code directly: its binary in stream-json mode, no Node, no local server.
// One live process per open conversation. It is started ahead of time (Claude takes ~10 s to boot),
// so by the time the person has typed, it is ready; follow-up messages go to the same process.
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Concilium;

public class ClaudeProcess : IDisposable
{
    public readonly Person Person;
    public string? SessionId { get; private set; }
    public readonly string? ResumedFrom;
    public bool Busy { get; private set; }
    public bool Alive => proc is { HasExited: false };
    // Tokens in the conversation's context after the last answer, and the model's window.
    public int Context { get; private set; }
    public int Window { get; private set; } = 200_000;

    // Raised on a background thread.
    public event Action<string>? Session;
    public event Action? Block;
    public event Action<string>? Text;
    public event Action<string, string>? Tool;
    public event Action<bool, string?>? Done;
    // Helpers the agent sent off in the background (research directions): the answer is not finished until they are back.
    public event Action<int, int>? Tasks;
    readonly HashSet<string> background = new();
    int backgroundTotal;
    public int Background { get { lock (background) return background.Count; } }

    readonly Process proc;
    readonly StringBuilder stderr = new();

    static readonly string[] ReadTools = { "Read", "Glob", "Grep", "WebSearch", "WebFetch", "Skill", "Task", "Agent", "TodoWrite", "ToolSearch", "Bash", "PowerShell" };

    public static List<string> Args(Person p, string? resume, bool persist = true, bool interactive = true)
    {
        var a = new List<string> { "-p", "--output-format", "stream-json", "--input-format", "stream-json", "--verbose" };
        if (interactive) a.Add("--include-partial-messages");
        // The agent does whatever the work needs in the person's folder: reads, edits, the terminal (rename, move, convert files).
        // Edits outside the folder would need a permission prompt, and in this mode that means "no".
        // Only the folder's own settings (and its CLAUDE.md): global allow rules of a Claude Code user would override all this.
        a.AddRange(new[] { "--setting-sources=project", "--permission-mode", "acceptEdits",
            "--allowedTools", string.Join(",", ReadTools),
            "--plugin-dir", Paths.Plugin,
            "--append-system-prompt-file", Paths.PrinciplesFile });
        if (!persist) a.Add("--no-session-persistence");
        if (resume != null) a.AddRange(new[] { "--resume", resume });
        return a;
    }

    public static ProcessStartInfo StartInfo(Person p, IEnumerable<string> args, bool redirectOut = true)
    {
        var psi = new ProcessStartInfo(Paths.Claude)
        {
            WorkingDirectory = p.path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = redirectOut,
            RedirectStandardError = redirectOut,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        if (redirectOut) { psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8; }
        foreach (var a in args) psi.ArgumentList.Add(a);
        CleanEnv(psi.Environment);
        return psi;
    }

    // Started from inside the Claude desktop app, its host-session variables leak in; drop them.
    public static void CleanEnv(IDictionary<string, string?> env)
    {
        if (!env.ContainsKey("CLAUDE_CODE_DESKTOP_APP_VERSION")) return;
        foreach (var k in env.Keys.ToList())
            if ((k.StartsWith("CLAUDE") && k != "CLAUDE_CONFIG_DIR") || k == "ANTHROPIC_BASE_URL") env.Remove(k);
    }

    public ClaudeProcess(Person p, string? resume)
    {
        Person = p;
        ResumedFrom = SessionId = resume;
        proc = Process.Start(StartInfo(p, Args(p, resume)))!;
        new Thread(ReadOut) { IsBackground = true }.Start();
        new Thread(() => { for (string? l; (l = proc.StandardError.ReadLine()) != null;) lock (stderr) { stderr.AppendLine(l); if (stderr.Length > 8000) stderr.Remove(0, 4000); } }) { IsBackground = true }.Start();
    }

    public void Send(string text)
    {
        Busy = true;
        var msg = new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = text } };
        proc.StandardInput.WriteLine(msg.ToJsonString());
        proc.StandardInput.Flush();
    }

    public void Interrupt()
    {
        try
        {
            var req = new JsonObject { ["type"] = "control_request", ["request_id"] = Guid.NewGuid().ToString("N"), ["request"] = new JsonObject { ["subtype"] = "interrupt" } };
            proc.StandardInput.WriteLine(req.ToJsonString());
            proc.StandardInput.Flush();
        }
        catch { }
    }

    void ReadOut()
    {
        try
        {
            for (string? line; (line = proc.StandardOutput.ReadLine()) != null;)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try { Handle(line); } catch (Exception e) { Paths.LogError(e); }
            }
        }
        catch { }
        if (Busy || Background > 0)
        {
            Busy = false;
            lock (background) background.Clear();
            string err;
            lock (stderr) err = stderr.ToString().Trim();
            Done?.Invoke(false, err.Length > 0 ? err.Split('\n')[^1] : "Claude stopped");
        }
    }

    void Handle(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var m = doc.RootElement;
        var type = Sessions.Str(m, "type");
        var top = !m.TryGetProperty("parent_tool_use_id", out var parent) || parent.ValueKind == JsonValueKind.Null;
        switch (type)
        {
            case "system" when Sessions.Str(m, "subtype") == "init":
                SessionId = Sessions.Str(m, "session_id");
                if (SessionId != null) Session?.Invoke(SessionId);
                break;
            case "system" when Sessions.Str(m, "subtype") == "task_started" && Sessions.Bool(m, "is_backgrounded")
                && Sessions.Str(m, "task_type") is "local_agent" or "local_workflow" && Sessions.Str(m, "task_id") is { } started:
                int done, total;
                lock (background) { if (background.Add(started)) backgroundTotal++; total = backgroundTotal; done = total - background.Count; }
                Tasks?.Invoke(done, total);
                break;
            case "system" when Sessions.Str(m, "subtype") == "task_notification" && Sessions.Str(m, "task_id") is { } ended:
                lock (background) { if (!background.Remove(ended)) break; total = backgroundTotal; done = total - background.Count; if (background.Count == 0) { backgroundTotal = 0; Busy = true; } }
                Tasks?.Invoke(done, total);
                break;
            case "stream_event" when top:
                var e = m.GetProperty("event");
                var et = Sessions.Str(e, "type");
                if (et == "content_block_delta" && e.TryGetProperty("delta", out var d) && Sessions.Str(d, "type") == "text_delta")
                    Text?.Invoke(Sessions.Str(d, "text") ?? "");
                else if (et == "content_block_start" && e.TryGetProperty("content_block", out var cb) && Sessions.Str(cb, "type") == "text")
                    Block?.Invoke();
                break;
            case "assistant":
                if (top && m.GetProperty("message").TryGetProperty("usage", out var u))
                {
                    int N(string k) => u.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                    var used = N("input_tokens") + N("cache_read_input_tokens") + N("cache_creation_input_tokens") + N("output_tokens");
                    if (used > 0) Context = used;
                }
                foreach (var b in m.GetProperty("message").GetProperty("content").EnumerateArray())
                    if (Sessions.Str(b, "type") == "tool_use")
                        Tool?.Invoke(Sessions.Str(b, "name") ?? "", Summarize(b.TryGetProperty("input", out var i) ? i : default));
                break;
            case "result":
                if (m.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
                    foreach (var model in mu.EnumerateObject())
                        if (model.Value.TryGetProperty("contextWindow", out var cw) && cw.ValueKind == JsonValueKind.Number && cw.GetInt32() > 0)
                            Window = cw.GetInt32();
                Busy = false;
                var ok = Sessions.Str(m, "subtype") == "success" && !Sessions.Bool(m, "is_error");
                // The CLI goes on by itself when the helpers report back; this was only the first part of the answer.
                if (ok && Background > 0) break;
                Done?.Invoke(ok, ok ? null : Sessions.Str(m, "result") ?? Sessions.Str(m, "subtype"));
                break;
        }
    }

    static string Summarize(JsonElement i)
    {
        if (i.ValueKind != JsonValueKind.Object) return "";
        if (Sessions.Str(i, "file_path") is { } f) return Path.GetFileName(f);
        if (Sessions.Str(i, "query") is { } q) return q;
        if (Sessions.Str(i, "url") is { } u) return Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : u;
        return Sessions.Str(i, "pattern") ?? Sessions.Str(i, "skill") ?? Sessions.Str(i, "description") ?? "";
    }

    public void Dispose()
    {
        try { if (!proc.HasExited) proc.Kill(true); } catch { }
        proc.Dispose();
    }
}

// Installing Claude Code without a terminal: Anthropic's official Windows installer, run hidden.
// It puts claude.exe into ~/.local/bin, where Paths.Claude looks first.
public static class Installer
{
    static Process? run;
    public static bool Pending => run is { HasExited: false };
    public static bool Failed => run is { HasExited: true } && !Paths.HasClaude;

    public static void Start()
    {
        if (Pending) return;
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "irm https://claude.ai/install.ps1 | iex" }) psi.ArgumentList.Add(a);
        ClaudeProcess.CleanEnv(psi.Environment);
        try { run = Process.Start(psi); } catch (Exception e) { Paths.LogError(e); }
    }

    // Anthropic's installer puts claude.exe into ~/.local/bin and only advises adding it to PATH.
    // The app finds it there anyway; PATH is for the agent's own terminal and for anyone typing "claude".
    // Written straight to the registry: Environment.SetEnvironmentVariable would expand "%USERPROFILE%"-style entries
    // of the person's PATH for good. Once per run, off the window's thread (the change broadcast can hang on a busy window).
    static int pathDone;
    public static void EnsurePath()
    {
        if (Interlocked.Exchange(ref pathDone, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
                if (!File.Exists(Path.Combine(bin, "claude.exe"))) return;
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Environment");
                var raw = key.GetValue("Path", "", Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                if (raw.Split(';').Any(p => string.Equals(Environment.ExpandEnvironmentVariables(p.Trim()).TrimEnd('\\'), bin, StringComparison.OrdinalIgnoreCase))) return;
                key.SetValue("Path", raw.Length == 0 ? bin : raw.TrimEnd(';') + ";" + bin, Microsoft.Win32.RegistryValueKind.ExpandString);
                SendMessageTimeout((IntPtr)0xffff, 0x1A, IntPtr.Zero, "Environment", 2, 5000, out _);
                Environment.SetEnvironmentVariable("Path", Environment.GetEnvironmentVariable("Path") + ";" + bin);
            }
            catch (Exception e) { Paths.LogError(e); }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

// Signing in without a terminal: Claude's own sign-in opens the browser, the person presses "Authorize".
public static class Auth
{
    // Checking the credentials file is instant; `claude auth status` takes 10+ seconds.
    public static bool LoggedIn()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) return true;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN"))) return true;
        try
        {
            var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Paths.ClaudeConfigDir, ".credentials.json")));
            var o = j?["claudeAiOauth"];
            return !string.IsNullOrEmpty(o?["refreshToken"]?.GetValue<string>()) || !string.IsNullOrEmpty(o?["accessToken"]?.GetValue<string>());
        }
        catch { return false; }
    }

    static Process? login;
    static readonly StringBuilder output = new();
    public static string? Url { get; private set; }
    public static bool Pending => login is { HasExited: false };
    public static bool Failed => login is { HasExited: true } && login.ExitCode != 0 && !LoggedIn();

    public static void Start()
    {
        if (Pending) return;
        Url = null;
        lock (output) output.Clear();
        var psi = new ProcessStartInfo(Paths.Claude)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("auth"); psi.ArgumentList.Add("login"); psi.ArgumentList.Add("--claudeai");
        ClaudeProcess.CleanEnv(psi.Environment);
        login = Process.Start(psi)!;
        void Read(StreamReader r)
        {
            for (string? l; (l = r.ReadLine()) != null;)
                lock (output)
                {
                    output.AppendLine(l);
                    Url ??= System.Text.RegularExpressions.Regex.Match(output.ToString(), @"https://\S+") is { Success: true } m ? m.Value : null;
                }
        }
        new Thread(() => Read(login.StandardOutput)) { IsBackground = true }.Start();
        new Thread(() => Read(login.StandardError)) { IsBackground = true }.Start();
    }

    // Without a terminal the CLI signs in by code: the page shows "code#state", the CLI reads it from stdin.
    // People paste whatever they copied: the code with spaces or line breaks, without the "#state" half,
    // or the whole address of the page. All of these become "code#state".
    public static string Normalize(string raw)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", "");
        string? Param(string text, string key) =>
            System.Text.RegularExpressions.Regex.Match(text, $@"[?&#]{key}=([^&#]+)") is { Success: true } m ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
        if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Param(t, "code") is { } c && !t.Contains("/oauth/authorize"))
            t = c + (Param(t, "state") is { } st ? "#" + st : "");
        if (!t.Contains('#') && Url != null && Param(Url, "state") is { } state) t += "#" + state;
        return t;
    }

    // The address of the sign-in page itself, pasted back by mistake.
    public static bool IsPageLink(string raw) => raw.Contains("/oauth/authorize");

    public static int Rejected { get { lock (output) return System.Text.RegularExpressions.Regex.Matches(output.ToString(), "Invalid code", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count; } }

    public static bool Code(string raw)
    {
        try
        {
            if (!Pending) return false;
            login!.StandardInput.WriteLine(Normalize(raw));
            login.StandardInput.Flush();
            return true;
        }
        catch (Exception e) { Paths.LogError(e); return false; }
    }

    public static void Stop() { try { if (Pending) login!.Kill(true); } catch { } }
}

// After a conversation ends, a separate headless run of the remember skill files it into memory.
// The window does not run it itself: it starts "Concilium.exe --remember <person> <ids>", a program of its own
// that outlives the window, waits for Claude, checks which memory files changed, keeps a log and marks
// the conversations as remembered only when that worked.
public static class Dream
{
    // Window side: start the helper. Raises done(true/false) on a background thread if the window is still there.
    public static Process? Start(Person p, IList<string> ids, Action<bool>? done = null)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--remember"); psi.ArgumentList.Add(p.id); psi.ArgumentList.Add(string.Join(",", ids));
            var proc = Process.Start(psi)!;
            Store.SetRunning(p.id, proc.Id, ids);
            if (done != null)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => done(proc.ExitCode == 0);
            }
            return proc;
        }
        catch (Exception e) { Paths.LogError(e); done?.Invoke(false); return null; }
    }

    // Helper side. Exit code 0 = remembered (or nothing new), 1 = failed.
    public static int Run(string personId, IList<string> ids)
    {
        var p = Store.People().FirstOrDefault(x => x.id == personId);
        if (p == null) return 1;
        // Without access to Claude nothing is marked: the next pass picks these conversations up.
        if (Region.Blocked().GetAwaiter().GetResult())
        {
            Paths.LogRemember("Claude is not available from this network; nothing done");
            Store.Finish(personId, ids, null, null);
            return 1;
        }
        try
        {
            var parts = new List<string>();
            var stamps = new Dictionary<string, long>();
            var infos = Sessions.List(p).ToDictionary(s => s.id);
            var dir = Sessions.ProjectDir(p.path);
            foreach (var id in ids)
            {
                var conv = Sessions.Conversation(p, id);
                if (conv.Count == 0) continue;
                var at = infos.TryGetValue(id, out var s) ? s.at : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                stamps[id] = at;
                var file = dir == null ? "" : $" file=\"{Path.Combine(dir, id + ".jsonl")}\"";
                parts.Add($"<conversation id=\"{id}\" date=\"{DateTimeOffset.FromUnixTimeMilliseconds(at):yyyy-MM-dd}\"{file}>\n" +
                    string.Join("\n\n", conv.Select(m => $"{(m.role == "user" ? "PERSON" : "AGENT")}: {m.text ?? $"[asked for: {m.skill}]"}")) +
                    "\n</conversation>");
            }
            if (parts.Count == 0) { Store.Finish(personId, ids, stamps, null); return 0; }

            var before = MemoryFiles(p.path);
            var psi = ClaudeProcess.StartInfo(p, ClaudeProcess.Args(p, null, persist: false, interactive: false));
            var proc = Process.Start(psi)!;
            var msg = new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user",
                ["content"] = "/concilium:remember\n\nConversations to consolidate:\n\n" + string.Join("\n\n", parts) } };
            proc.StandardInput.WriteLine(msg.ToJsonString());
            proc.StandardInput.Close();
            var errTask = proc.StandardError.ReadToEndAsync();
            string? result = null; var failed = false;
            while (proc.StandardOutput.ReadLine() is { } line)
            {
                try
                {
                    var o = JsonNode.Parse(line);
                    if (o?["type"]?.GetValue<string>() != "result") continue;
                    result = o["result"]?.GetValue<string>();
                    failed = o["is_error"]?.GetValue<bool>() == true;
                }
                catch { }
            }
            proc.WaitForExit();
            var err = errTask.Result;

            var after = MemoryFiles(p.path);
            var changed = after.Where(kv => !before.TryGetValue(kv.Key, out var t) || t != kv.Value).Select(kv => Path.GetRelativePath(p.path, kv.Key)).ToList();
            var ok = proc.ExitCode == 0 && !failed && result != null;
            var person = result == null ? null : System.Text.RegularExpressions.Regex.Match(result, @"<person>\s*(.*?)\s*</person>", System.Text.RegularExpressions.RegexOptions.Singleline) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
            // The files are the proof: a pass that changed nothing noted nothing, whatever it says.
            var note = !ok ? null : changed.Count == 0 ? Strings.T.NothingNew : person ?? Strings.T.Noted;
            Paths.LogRemember($"{p.name} [{string.Join(", ", ids)}] exit {proc.ExitCode}{(failed ? " error" : "")}\n" +
                $"changed: {(changed.Count == 0 ? "nothing" : string.Join(", ", changed))}\n" +
                (err.Trim().Length > 0 ? $"stderr: {err.Trim()}\n" : "") + $"{result ?? "(no result)"}\n");
            if (!ok) { Store.Finish(personId, ids, null, null); return 1; }
            Store.Finish(personId, ids, stamps, note);
            return 0;
        }
        catch (Exception e)
        {
            Paths.LogRemember($"{p.name} [{string.Join(", ", ids)}] crashed: {e}\n");
            Store.Finish(personId, ids, null, null);
            return 1;
        }
    }

    // Memory is the folder's .md files (not the documents, not the skills): path → last write time.
    static Dictionary<string, DateTime> MemoryFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".claude" + Path.DirectorySeparatorChar))
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
