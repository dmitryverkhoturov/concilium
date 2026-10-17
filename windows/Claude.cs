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

    // Raised on a background thread.
    public event Action<string>? Session;
    public event Action? Block;
    public event Action<string>? Text;
    public event Action<string, string>? Tool;
    public event Action<bool, string?>? Done;

    readonly Process proc;
    readonly StringBuilder stderr = new();

    static readonly string[] ReadTools = { "Read", "Glob", "Grep", "WebSearch", "WebFetch", "Skill", "Task", "Agent", "TodoWrite", "ToolSearch" };

    public static List<string> Args(Person p, string? resume, bool persist = true, bool interactive = true)
    {
        var a = new List<string> { "-p", "--output-format", "stream-json", "--input-format", "stream-json", "--verbose" };
        if (interactive) a.Add("--include-partial-messages");
        // Reads and web research are free. Edits are accepted inside the person's folder (the working directory);
        // anything else would need a permission prompt, and in this mode that means "no". No terminal, ever.
        // Only the folder's own settings (and its CLAUDE.md): global allow rules of a Claude Code user would override all this.
        a.AddRange(new[] { "--setting-sources=project", "--permission-mode", "acceptEdits",
            "--allowedTools", string.Join(",", ReadTools),
            "--disallowedTools", "Bash,PowerShell,NotebookEdit,KillShell",
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
        if (Busy)
        {
            Busy = false;
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
            case "stream_event" when top:
                var e = m.GetProperty("event");
                var et = Sessions.Str(e, "type");
                if (et == "content_block_delta" && e.TryGetProperty("delta", out var d) && Sessions.Str(d, "type") == "text_delta")
                    Text?.Invoke(Sessions.Str(d, "text") ?? "");
                else if (et == "content_block_start" && e.TryGetProperty("content_block", out var cb) && Sessions.Str(cb, "type") == "text")
                    Block?.Invoke();
                break;
            case "assistant":
                foreach (var b in m.GetProperty("message").GetProperty("content").EnumerateArray())
                    if (Sessions.Str(b, "type") == "tool_use")
                        Tool?.Invoke(Sessions.Str(b, "name") ?? "", Summarize(b.TryGetProperty("input", out var i) ? i : default));
                break;
            case "result":
                Busy = false;
                var ok = Sessions.Str(m, "subtype") == "success" && !Sessions.Bool(m, "is_error");
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

    public static void Code(string code)
    {
        try { if (Pending) { login!.StandardInput.WriteLine(code); login.StandardInput.Flush(); } } catch { }
    }

    public static void Stop() { try { if (Pending) login!.Kill(true); } catch { } }
}

// After a conversation ends, a separate headless run of the sleep skill files it into memory.
// It is not a child we wait on: closing the window must not cut it short.
public static class Dream
{
    public static bool Start(Person p, IList<string> ids, Action<bool>? done = null)
    {
        var parts = new List<string>();
        var stamps = new Dictionary<string, long>();
        var infos = Sessions.List(p).ToDictionary(s => s.id);
        foreach (var id in ids)
        {
            var conv = Sessions.Conversation(p, id);
            if (conv.Count == 0) continue;
            var at = infos.TryGetValue(id, out var s) ? s.at : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            stamps[id] = at;
            parts.Add($"<conversation id=\"{id}\" date=\"{DateTimeOffset.FromUnixTimeMilliseconds(at):yyyy-MM-dd}\">\n" +
                string.Join("\n\n", conv.Select(m => $"{(m.role == "user" ? "PERSON" : "AGENT")}: {m.text ?? $"[asked for: {m.skill}]"}")) +
                "\n</conversation>");
        }
        if (parts.Count == 0) { done?.Invoke(true); return false; }
        // Marked up front, so a pass started as the window closes is not repeated next time.
        Store.MarkDreamed(p.id, stamps);
        try
        {
            var proc = Process.Start(ClaudeProcess.StartInfo(p, ClaudeProcess.Args(p, null, persist: false, interactive: false), redirectOut: false))!;
            var msg = new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user",
                ["content"] = "/concilium:sleep\n\nConversations to consolidate:\n\n" + string.Join("\n\n", parts) } };
            proc.StandardInput.WriteLine(msg.ToJsonString());
            proc.StandardInput.Close();
            if (done != null)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) =>
                {
                    var ok = proc.ExitCode == 0;
                    if (!ok) Store.MarkDreamed(p.id, stamps.ToDictionary(kv => kv.Key, _ => 0L));
                    done(ok);
                };
            }
            return true;
        }
        catch (Exception e)
        {
            Paths.LogError(e);
            Store.MarkDreamed(p.id, stamps.ToDictionary(kv => kv.Key, _ => 0L));
            done?.Invoke(false);
            return false;
        }
    }
}
