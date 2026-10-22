// Documents the person adds to a message: kept as they are in `documents/` of their folder; the agent reads them and links them into memory.
using System.IO;
using System.Text.RegularExpressions;

namespace Concilium;

public static class Documents
{
    const string Open = "<attached>", Close = "</attached>";

    // A copy in `documents/` under its own name; the agent reads it and gives it its proper name and date.
    // The app cannot know the document's date: a downloaded file's own date is the day of the download.
    // A file already in the folder stays where it is; the very same file added twice keeps one copy.
    public static string Keep(Person p, string src)
    {
        var root = Path.GetFullPath(p.path);
        var full = Path.GetFullPath(src);
        if (!Path.GetRelativePath(root, full).StartsWith("..")) return Rel(p, full);
        var dir = Path.Combine(root, "documents");
        Directory.CreateDirectory(dir);
        var hash = Hash(full);
        foreach (var f in Directory.EnumerateFiles(dir))
            if (new FileInfo(f).Length == new FileInfo(full).Length && Hash(f) == hash) return Rel(p, f);
        var name = Path.GetFileName(full);
        var dest = Path.Combine(dir, name);
        for (var i = 2; File.Exists(dest); i++)
            dest = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        File.Copy(full, dest);
        return Rel(p, dest);
    }

    static string Hash(string f)
    {
        using var s = File.OpenRead(f);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
    }

    static string Rel(Person p, string full) => Path.GetRelativePath(p.path, full).Replace('\\', '/');

    // What the agent gets with the message. The person sees only the files, not this text.
    public static string Note(IEnumerable<string> files) =>
        $"\n\n{Open}\nThe person added documents to this message. They are saved in the folder as they are:\n"
        + string.Join("\n", files.Select(f => "- " + f))
        + "\nFor each one: read it in full (for a photo, look at it) and find its own date and what it is — from the content, not from the file."
        + " If the same document is already in the folder (the same visit or test, downloaded again), delete the new copy and use the old one."
        + " Otherwise rename it to `documents/YYYY-MM-DD what.ext` in the person's language, e.g. `documents/2025-11-28 blood test.pdf`."
        + " Copy its facts into memory with that date, linked to the file by its full name with the extension, e.g. [[2025-11-28 blood test.pdf]], so it is connected on the map."
        + " Then answer about what it shows and what it changes. Never edit the file's content.\n" + Close;

    public static (string text, string[] files) Split(string t)
    {
        var m = Regex.Match(t, Regex.Escape(Open) + @"([\s\S]*?)" + Regex.Escape(Close));
        if (!m.Success) return (t, Array.Empty<string>());
        var files = Regex.Matches(m.Groups[1].Value, @"^- (.+?)\s*$", RegexOptions.Multiline).Select(x => x.Groups[1].Value).ToArray();
        return (t.Remove(m.Index, m.Length).Trim(), files);
    }
}
