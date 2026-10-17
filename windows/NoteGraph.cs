// The map: notes of a person's folder linked by their [[links]], resolved across the whole Obsidian vault like Obsidian does.
using System.IO;
using System.Text.RegularExpressions;

namespace Concilium;

public class GNode
{
    public string id = "", label = "", kind = "note";
    public int deg;
    public double x, y, vx, vy;
    public bool pinned;
}
public record GLink(string source, string target, int weight);
public record GraphData(List<GNode> nodes, List<GLink> links);

public static class NoteGraph
{
    static readonly string[] Kinds = { "MEMORY", "PATIENT FACTS", "HYPOTHESES", "QUESTIONS", "TODO", "HISTORY", "IDENTITY" };
    static readonly Regex Wiki = new(@"!?\[\[([^\]|#^]+)(?:[#^][^\]|]*)?(?:\|[^\]]*)?\]\]", RegexOptions.Compiled);

    public static string KindOf(string rel)
    {
        var b = Path.GetFileNameWithoutExtension(rel);
        var top = rel.Split('/')[0];
        if (top is "topics" or "branches" && rel.Contains('/')) return "branch";
        foreach (var k in Kinds)
            if (b == k || b.EndsWith(" " + k) || b.EndsWith("_" + k.Replace(' ', '_'))) return k.ToLowerInvariant();
        if (b is "AGENTS" or "CLAUDE" or "README" or "PRINCIPLES") return "system";
        return rel.EndsWith(".md") ? "note" : "document";
    }

    public static List<string> Walk(string dir)
    {
        var outp = new List<string>();
        void Rec(string d)
        {
            foreach (var e in new DirectoryInfo(d).EnumerateFileSystemInfos())
            {
                if (e.Name.StartsWith('.') || e.Name == "node_modules") continue;
                if (e is DirectoryInfo) Rec(e.FullName);
                else outp.Add(Path.GetRelativePath(dir, e.FullName).Replace('\\', '/'));
            }
        }
        try { Rec(dir); } catch { }
        return outp;
    }

    public static string? VaultOf(string dir)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".obsidian"))) return d.FullName;
        return null;
    }

    static readonly Dictionary<string, (DateTime at, Dictionary<string, string> byName)> vaultIndex = new();
    static Dictionary<string, string> VaultFiles(string vault)
    {
        lock (vaultIndex)
            if (vaultIndex.TryGetValue(vault, out var c) && DateTime.UtcNow - c.at < TimeSpan.FromMinutes(1)) return c.byName;
        var byName = new Dictionary<string, string>();
        foreach (var f in Walk(vault))
        {
            var key = Regex.Replace(Path.GetFileName(f), @"\.md$", "").ToLowerInvariant();
            if (!byName.ContainsKey(key) || f.EndsWith(".md")) byName[key] = f;
        }
        lock (vaultIndex) vaultIndex[vault] = (DateTime.UtcNow, byName);
        return byName;
    }

    // Node ids are paths relative to the person's folder; notes elsewhere in the vault get `../` paths.
    public static string Full(Person p, string id) => Path.GetFullPath(Path.Combine(p.path, id));

    public static bool Readable(Person p, string id)
    {
        var full = Full(p, id);
        var root = Path.GetFullPath(p.path);
        if (Inside(root, full)) return true;
        var vault = VaultOf(p.path);
        return vault != null && Inside(vault, full);
    }

    static bool Inside(string dir, string full)
    {
        var rel = Path.GetRelativePath(dir, full);
        return rel != "." && !rel.StartsWith("..") && !Path.IsPathRooted(rel);
    }

    public static GraphData Build(Person p)
    {
        var files = Walk(p.path);
        var byName = new Dictionary<string, string>();
        foreach (var f in files) byName[Regex.Replace(Path.GetFileName(f), @"\.md$", "").ToLowerInvariant()] = f;
        var vault = VaultOf(p.path);
        var inVault = vault != null ? VaultFiles(vault) : new();
        var nodes = new Dictionary<string, GNode>();
        var links = new Dictionary<string, int>();
        void Add(string id, string kind, string? label = null)
        {
            if (!nodes.ContainsKey(id)) nodes[id] = new GNode { id = id, kind = kind, label = label ?? Regex.Replace(Path.GetFileName(id), @"\.md$", "") };
        }
        foreach (var f in files) Add(f, KindOf(f));
        foreach (var f in files.Where(x => x.EndsWith(".md")))
        {
            string txt;
            try { txt = File.ReadAllText(Path.Combine(p.path, f)); } catch { continue; }
            if (Regex.IsMatch(txt, "^excalidraw-plugin:", RegexOptions.Multiline)) { nodes[f].kind = "system"; continue; }
            foreach (Match m in Wiki.Matches(txt))
            {
                var name = m.Groups[1].Value.Trim();
                var key = Regex.Replace(Path.GetFileName(name), @"\.md$", "").ToLowerInvariant();
                string? target = byName.GetValueOrDefault(name.ToLowerInvariant()) ?? byName.GetValueOrDefault(key);
                if (target == null && vault != null && inVault.TryGetValue(key, out var vf))
                {
                    target = Path.GetRelativePath(p.path, Path.Combine(vault, vf)).Replace('\\', '/');
                    Add(target, target.EndsWith(".md") ? "vault" : "document");
                }
                var to = target ?? "?" + name;
                if (target == null) Add(to, "missing", name);
                if (to == f) continue;
                var lk = string.CompareOrdinal(f, to) < 0 ? f + "→" + to : to + "→" + f;
                links[lk] = links.GetValueOrDefault(lk) + 1;
            }
        }
        var ll = links.Select(kv => { var s = kv.Key.Split('→'); return new GLink(s[0], s[1], kv.Value); }).ToList();
        foreach (var l in ll) { nodes[l.source].deg += l.weight; nodes[l.target].deg += l.weight; }
        return new(nodes.Values.ToList(), ll);
    }
}
