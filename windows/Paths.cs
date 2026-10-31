// Where things live: the folder template, the app's own data, the Claude binary.
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Concilium;

public static class Paths
{
    public static readonly string ExeDir = AppContext.BaseDirectory;

    // The folder template people can also use without the app; its skills and principles are the single source.
    // Shipped next to the exe; from a source checkout it is found up the tree.
    public static readonly string Template = FindUp("concilium-markdown") ?? Path.Combine(ExeDir, "concilium-markdown");

    static readonly bool Packaged = Directory.Exists(Path.Combine(ExeDir, "concilium-markdown"));

    // The portable build (a "portable.txt" next to the exe) keeps its data next to the program.
    // Otherwise data lives in %APPDATA%\Concilium: reinstalling or updating the program does not touch it,
    // and from a source checkout nobody's data can end up in the repository.
    public static readonly bool Portable = Packaged && File.Exists(Path.Combine(ExeDir, "portable.txt"));
    static string OldData => Path.Combine(ExeDir, "Concilium data");
    public static readonly string Data = Environment.GetEnvironmentVariable("CONCILIUM_DATA") is { Length: > 0 } d ? d
        : Portable ? OldData
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Concilium");

    public static string Plugin => Path.Combine(Data, "plugin");
    public static string PrinciplesFile => Path.Combine(Data, "principles.md");
    public static string Log => Path.Combine(Data, "concilium.log");
    // What each memory pass did: which files changed, its own report. For whoever looks after the app, not for the person.
    public static string RememberLog => Path.Combine(Data, "remember.log");

    // Claude Code is not shipped with the app: it is Anthropic's program, installed by Anthropic's own installer
    // (see Installer) into ~/.local/bin. Looked up on every use, because it may get installed while the app runs.
    // From a source checkout, the binary the Agent SDK installed also works.
    public static string Claude => new[]
    {
        Environment.GetEnvironmentVariable("CONCILIUM_CLAUDE"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"),
        OnPath("claude.exe"),
        // Installed with npm: claude.cmd on PATH runs the binary inside the package.
        OnPath("claude.cmd") is { } cmd ? Path.Combine(Path.GetDirectoryName(cmd)!, "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe") : null,
        FindUp(Path.Combine("app", "node_modules", "@anthropic-ai", "claude-agent-sdk-win32-x64", "claude.exe")),
    }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "";

    public static bool HasClaude => Claude.Length > 0;

    public static string ClaudeConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    static string? FindUp(string rel)
    {
        for (var d = new DirectoryInfo(ExeDir); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (Directory.Exists(p) || File.Exists(p)) return p;
        }
        return null;
    }

    static string? OnPath(string exe) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
        .Select(p => { try { return Path.Combine(p.Trim(), exe); } catch { return ""; } }).FirstOrDefault(File.Exists);

    public const string InApp = """

        ## In this app

        The person may be elderly and not technical at all. They do not know what files, memory, skills, agents or markdown are, and they must never need to. Never mention files, folders, skills, tools, sessions or `AGENTS.md`: say "I noted that", not "I updated PATIENT FACTS.md". Short, warm, plain words.
        """;

    // The CLI loads skills from a plugin; rebuild it from the template on every start (a handful of small files).
    public static void Prepare()
    {
        Directory.CreateDirectory(Data);
        // Up to 0.3.1 an installed copy kept its data next to the exe; carry the people and memory state over once.
        if (!Portable && Packaged && Data != OldData && Directory.Exists(OldData))
            foreach (var name in new[] { "config.json", "state.json", "ui.json" })
            {
                var from = Path.Combine(OldData, name); var to = Path.Combine(Data, name);
                if (File.Exists(from) && (!File.Exists(to) || File.GetLastWriteTimeUtc(from) > File.GetLastWriteTimeUtc(to))) File.Copy(from, to, true);
            }
        if (Directory.Exists(Plugin)) Directory.Delete(Plugin, true);
        Directory.CreateDirectory(Path.Combine(Plugin, ".claude-plugin"));
        File.WriteAllText(Path.Combine(Plugin, ".claude-plugin", "plugin.json"),
            """{"name":"concilium","description":"Concilium health skills"}""");
        CopyDir(Path.Combine(Template, ".claude", "skills"), Path.Combine(Plugin, "skills"));
        File.WriteAllText(PrinciplesFile, File.ReadAllText(Path.Combine(Template, "PRINCIPLES.md")) + InApp, new UTF8Encoding(false));
    }

    public static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public static void LogError(object e)
    {
        try { File.AppendAllText(Log, $"{DateTime.UtcNow:o} {e}\n"); } catch { }
    }

    public static void LogRemember(string text)
    {
        try { File.AppendAllText(RememberLog, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n"); } catch { }
    }
}

public record Person(string id, string name, string path);

// The list of people and per-conversation memory state. Same files as the old app, so nothing is lost on upgrade.
public static class Store
{
    static string Config => Path.Combine(Paths.Data, "config.json");
    static string State => Path.Combine(Paths.Data, "state.json");
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static List<Person> People()
    {
        try { return JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(Config))?.people ?? new(); }
        catch { return new(); }
    }

    public static void SavePeople(List<Person> people) =>
        File.WriteAllText(Config, JsonSerializer.Serialize(new ConfigFile { people = people }, Pretty));

    class ConfigFile { public List<Person> people { get; set; } = new(); }

    // The window and background memory passes are separate programs writing the same file.
    static readonly Mutex StateLock = new(false, "Concilium.State." +
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(Paths.Data.ToLowerInvariant())))[..12]);

    static JsonObject ReadState()
    {
        try { return JsonNode.Parse(File.ReadAllText(State)) as JsonObject ?? new(); } catch { return new(); }
    }

    static void Update(string personId, Action<JsonObject> change)
    {
        try { StateLock.WaitOne(5000); } catch (AbandonedMutexException) { }
        try
        {
            var st = ReadState();
            if (st[personId] is not JsonObject p) st[personId] = p = new JsonObject();
            change(p);
            File.WriteAllText(State, st.ToJsonString(Pretty));
        }
        finally { try { StateLock.ReleaseMutex(); } catch { } }
    }

    static JsonObject Obj(JsonObject p, string key) => p[key] as JsonObject ?? (JsonObject)(p[key] = new JsonObject());

    // When each conversation was last consolidated into memory (ms since epoch, same as the conversation's last change).
    public static Dictionary<string, long> Dreamed(string personId)
    {
        var o = ReadState()[personId]?["dreamed"] as JsonObject;
        return o?.ToDictionary(kv => kv.Key, kv => (long)(kv.Value?.GetValue<double>() ?? 0)) ?? new();
    }

    public static void MarkDreamed(string personId, IDictionary<string, long> stamps) =>
        Update(personId, p => { var d = Obj(p, "dreamed"); foreach (var (k, v) in stamps) d[k] = v; });

    // A fresh install (or a reinstall that lost its state) does not go through every old conversation:
    // memory starts from now; an old one can still be remembered by hand.
    public static void Baseline(IEnumerable<Person> people)
    {
        if (File.Exists(State)) return;
        foreach (var p in people) MarkDreamed(p.id, Sessions.List(p).ToDictionary(s => s.id, s => s.at));
        if (!File.Exists(State)) File.WriteAllText(State, "{}");
    }

    public static void SetRunning(string personId, int proc, IList<string> ids) =>
        Update(personId, p => p["running"] = new JsonObject { ["proc"] = proc, ["ids"] = new JsonArray(ids.Select(i => (JsonNode)i!).ToArray()) });

    // The pass that is writing this person's memory now, if its program is still alive (also one left from a closed window).
    public static IList<string>? Running(string personId)
    {
        var r = ReadState()[personId]?["running"];
        if (r?["proc"]?.GetValue<int>() is not int id) return null;
        try { if (System.Diagnostics.Process.GetProcessById(id).ProcessName != System.Diagnostics.Process.GetCurrentProcess().ProcessName) return null; }
        catch { return null; }
        return (r["ids"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? new();
    }

    public static int RunningProc(string personId) => ReadState()[personId]?["running"]?["proc"]?.GetValue<int>() ?? -1;

    // End of a pass: marks only when it worked (stamps != null); the note is what the person sees under the conversation.
    public static void Finish(string personId, IList<string> ids, IDictionary<string, long>? stamps, string? note) =>
        Update(personId, p =>
        {
            p.Remove("running");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            p["last"] = new JsonObject { ["ok"] = stamps != null, ["at"] = now };
            if (stamps == null) return;
            var d = Obj(p, "dreamed"); var n = Obj(p, "notes");
            foreach (var (k, v) in stamps) { d[k] = v; if (note != null) n[k] = new JsonObject { ["text"] = note, ["at"] = now }; }
        });

    public static (bool ok, long at)? Last(string personId) =>
        ReadState()[personId]?["last"] is JsonObject l ? (l["ok"]!.GetValue<bool>(), l["at"]!.GetValue<long>()) : null;

    public static (string text, long at)? Note(string personId, string id) =>
        ReadState()[personId]?["notes"]?[id] is JsonObject o ? (o["text"]!.GetValue<string>(), o["at"]!.GetValue<long>()) : null;
}
