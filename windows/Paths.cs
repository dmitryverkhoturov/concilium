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

    // Portable: data sits next to the program. From a source checkout it goes to %APPDATA%\Concilium,
    // so nobody's data can end up in the repository.
    public static readonly string Data = Environment.GetEnvironmentVariable("CONCILIUM_DATA") is { Length: > 0 } d ? d
        : Packaged ? Path.Combine(ExeDir, "Concilium data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Concilium");

    public static string Plugin => Path.Combine(Data, "plugin");
    public static string PrinciplesFile => Path.Combine(Data, "principles.md");
    public static string Log => Path.Combine(Data, "concilium.log");

    // The Claude Code binary: shipped in `claude\` next to the exe; from a source checkout, the one the Agent SDK installed.
    public static readonly string Claude = new[]
    {
        Path.Combine(ExeDir, "claude", "claude.exe"),
        FindUp(Path.Combine("app", "node_modules", "@anthropic-ai", "claude-agent-sdk-win32-x64", "claude.exe")),
    }.FirstOrDefault(File.Exists) ?? OnPath("claude.exe") ?? "claude.exe";

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

    static JsonObject ReadState()
    {
        try { return JsonNode.Parse(File.ReadAllText(State)) as JsonObject ?? new(); } catch { return new(); }
    }

    // When each conversation was last consolidated into memory (ms since epoch, same as the conversation's last change).
    public static Dictionary<string, long> Dreamed(string personId)
    {
        var o = ReadState()[personId]?["dreamed"] as JsonObject;
        return o?.ToDictionary(kv => kv.Key, kv => (long)(kv.Value?.GetValue<double>() ?? 0)) ?? new();
    }

    public static void MarkDreamed(string personId, IDictionary<string, long> stamps)
    {
        lock (Pretty)
        {
            var st = ReadState();
            if (st[personId] is not JsonObject p) st[personId] = p = new JsonObject();
            if (p["dreamed"] is not JsonObject d) p["dreamed"] = d = new JsonObject();
            foreach (var (k, v) in stamps) d[k] = v;
            File.WriteAllText(State, st.ToJsonString(Pretty));
        }
    }
}
