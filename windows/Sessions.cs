// Conversations are Claude Code's own: ~/.claude/projects/<folder>/<id>.jsonl.
// So the app sees conversations started in that folder from Claude Code, and the other way round.
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Concilium;

public record SessionInfo(string id, string title, long at);
public record Msg(string role, string? text, string? skill = null, string[]? files = null);

public static class Sessions
{
    public static string? ProjectDir(string folder)
    {
        var root = Path.Combine(Paths.ClaudeConfigDir, "projects");
        var name = Regex.Replace(Path.GetFullPath(folder).TrimEnd('\\'), "[^a-zA-Z0-9]", "-");
        var exact = Path.Combine(root, name);
        if (Directory.Exists(exact)) return exact;
        // Very long paths are cut and get a hash suffix.
        if (name.Length > 200 && Directory.Exists(root))
            return Directory.GetDirectories(root, name[..200] + "*").FirstOrDefault();
        return null;
    }

    record Cached(DateTime mtime, SessionInfo? info);
    static readonly Dictionary<string, Cached> cache = new();

    public static List<SessionInfo> List(Person p)
    {
        var dir = ProjectDir(p.path);
        if (dir == null) return new();
        var list = new List<SessionInfo>();
        foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
        {
            var mtime = File.GetLastWriteTimeUtc(f);
            lock (cache)
                if (cache.TryGetValue(f, out var c) && c.mtime == mtime) { if (c.info != null) list.Add(c.info); continue; }
            var info = Read(f, mtime);
            lock (cache) cache[f] = new(mtime, info);
            if (info != null) list.Add(info);
        }
        return list.OrderByDescending(s => s.at).ToList();
    }

    static SessionInfo? Read(string file, DateTime mtime)
    {
        string? custom = null, summary = null, first = null;
        DateTimeOffset? last = null;
        foreach (var line in ReadLines(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                var type = Str(r, "type");
                // The time of the last real message: resuming a conversation (switching to it, the app's
                // background memory pass) appends service lines, so the file's own time says "just now".
                if (type is "user" or "assistant" && !Bool(r, "isMeta") && !Bool(r, "isSidechain")
                    && DateTimeOffset.TryParse(Str(r, "timestamp"), out var ts)) last = ts;
                if (type == "custom-title") custom = Str(r, "customTitle") ?? custom;
                else if (type == "summary") summary ??= Str(r, "summary");
                else if (type == "user" && first == null && !Bool(r, "isMeta") && !Bool(r, "isSidechain"))
                {
                    var t = UserText(r);
                    if (t != null && !t.StartsWith("Base directory for this skill")) first = t;
                }
            }
            catch { }
        }
        if (first == null) return null;
        // Background memory passes of the old app were stored as conversations; they are not the person's talks.
        if (first.Contains("Conversations to consolidate")) return null;
        var at = (last ?? new DateTimeOffset(mtime)).ToUnixTimeMilliseconds();
        return new(Path.GetFileNameWithoutExtension(file), custom ?? summary ?? first, at);
    }

    public static List<Msg> Conversation(Person p, string id)
    {
        var dir = ProjectDir(p.path);
        var file = dir == null ? null : Path.Combine(dir, id + ".jsonl");
        var outp = new List<Msg>();
        if (file == null || !File.Exists(file)) return outp;
        foreach (var line in ReadLines(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                var type = Str(r, "type");
                if (Bool(r, "isSidechain") || Bool(r, "isMeta") || Bool(r, "isCompactSummary")) continue;
                if (type == "user")
                {
                    var t = UserText(r)?.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("Base directory for this skill")) continue;
                    var m = Pretty(t);
                    if (m != null) outp.Add(m);
                }
                else if (type == "assistant")
                {
                    var t = Texts(r.GetProperty("message").GetProperty("content")).Trim();
                    if (t.Length == 0) continue;
                    if (outp.Count > 0 && outp[^1].role == "assistant") outp[^1] = outp[^1] with { text = outp[^1].text + "\n\n" + t };
                    else outp.Add(new("assistant", t));
                }
            }
            catch { }
        }
        return outp;
    }

    // Skill buttons are stored as slash commands; show them as the button the person pressed.
    static readonly Dictionary<string, string> Renamed = new() { ["update-memory"] = "sleep", ["hypotheses"] = "research" };
    static Msg? Pretty(string t)
    {
        var m = Regex.Match(t, @"<command-name>/?(?:concilium:|health-agent:)?([\w-]+)</command-name>");
        if (m.Success) return new("user", null, Renamed.GetValueOrDefault(m.Groups[1].Value, m.Groups[1].Value));
        if (Regex.IsMatch(t, "^<(local-command|command-message|command-args)")) return null;
        m = Regex.Match(t, @"^/(?:concilium:)([\w-]+)\s*(.*)$", RegexOptions.Singleline);
        if (m.Success) return new("user", null, m.Groups[1].Value);
        var (text, files) = Documents.Split(t);
        return text.Length == 0 && files.Length == 0 ? null : new("user", text, null, files);
    }

    static string? UserText(JsonElement r)
    {
        if (!r.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var c)) return null;
        if (c.ValueKind == JsonValueKind.String) return c.GetString();
        if (c.ValueKind != JsonValueKind.Array) return null;
        foreach (var b in c.EnumerateArray()) if (Str(b, "type") == "tool_result") return null;
        var t = Texts(c);
        return t.Length > 0 ? t : null;
    }

    static string Texts(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", content.EnumerateArray().Where(b => Str(b, "type") == "text").Select(b => Str(b, "text") ?? ""));
    }

    // The file is being appended to by a running conversation: open it shared.
    static IEnumerable<string> ReadLines(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        for (string? l; (l = sr.ReadLine()) != null;) if (l.Length > 0) yield return l;
    }

    public static string? Str(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static bool Bool(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
}
