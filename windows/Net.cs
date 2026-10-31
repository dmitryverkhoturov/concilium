// What the app asks the internet itself: whether Claude answers from this network, and whether a newer release is out.
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Concilium;

public static class Region
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    static (DateTime at, bool blocked)? cache;

    // Where Claude is not offered, its API refuses every request with 403; elsewhere a request without a key gets 401.
    // No list of places: the service itself says. No network or no answer: go ahead, Claude will say what is wrong.
    public static async Task<bool> Blocked()
    {
        if (Environment.GetEnvironmentVariable("CONCILIUM_REGION_BLOCKED") is { Length: > 0 } force) return force == "1";
        if (cache is { } c && DateTime.UtcNow - c.at < TimeSpan.FromMinutes(1)) return c.blocked;
        var blocked = false;
        try
        {
            using var r = await Http.PostAsync("https://api.anthropic.com/v1/messages", new StringContent("{}", Encoding.UTF8, "application/json"));
            blocked = r.StatusCode == HttpStatusCode.Forbidden;
        }
        catch { }
        cache = (DateTime.UtcNow, blocked);
        return blocked;
    }
}

// Updates itself from the GitHub releases: downloads quietly while the window is open,
// copies the new files after the last Concilium of this folder has exited (memory passes included),
// so the next start is the new version. People's folders and the app's data are not touched.
public static class Updater
{
    const string Repo = "dmitryverkhoturov/concilium";
    static string Dir => Path.Combine(Paths.Data, "update");
    static string Seen => Path.Combine(Paths.Data, "version.txt");

    public static Version Current
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("CONCILIUM_VERSION") is { Length: > 0 } s ? Version.Parse(s) : typeof(Updater).Assembly.GetName().Version!;
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    // The version this data folder ran last, if it is older than this one: the window says "updated".
    public static Version? JustUpdated()
    {
        Version? before = null;
        try { if (File.Exists(Seen) && Version.TryParse(File.ReadAllText(Seen).Trim(), out var v)) before = v; } catch { }
        try { File.WriteAllText(Seen, Current.ToString()); } catch { }
        return before != null && before < Current ? Current : null;
    }

    public static async Task Check()
    {
        if (!Paths.Packaged) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Concilium/" + Current);
            var rel = JsonNode.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"))!;
            var tag = rel["tag_name"]!.GetValue<string>().TrimStart('v');
            if (!Version.TryParse(tag, out var latest) || latest <= Current) return;
            var target = Path.Combine(Dir, tag);
            if (File.Exists(Path.Combine(target, "ready"))) return;
            var assets = (rel["assets"] as JsonArray ?? new()).Select(a => (name: a!["name"]!.GetValue<string>(), url: a["browser_download_url"]!.GetValue<string>())).ToList();
            var zip = assets.FirstOrDefault(a => a.name.EndsWith("-portable.zip"));
            if (zip.url == null) zip = assets.FirstOrDefault(a => a.name.StartsWith("Concilium-") && a.name.EndsWith("-win-x64.zip"));
            if (zip.url == null) return;
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, tag + ".zip");
            using (var s = await http.GetStreamAsync(zip.url))
            using (var f = File.Create(file)) await s.CopyToAsync(f);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(file, target);
            File.Delete(file);
            if (!File.Exists(Path.Combine(target, "Concilium", "Concilium.exe"))) { Directory.Delete(target, true); return; }
            File.WriteAllText(Path.Combine(target, "ready"), "");
            foreach (var old in Directory.GetDirectories(Dir).Where(d => d != target)) try { Directory.Delete(old, true); } catch { }
        }
        catch (Exception e) { Paths.LogError("update: " + e.Message); }
    }

    // Window closing: hand the copying to a hidden PowerShell that waits for every Concilium running from this folder.
    public static void ApplyOnExit()
    {
        try
        {
            if (!Paths.Packaged || !Directory.Exists(Dir)) return;
            var ready = Directory.GetDirectories(Dir)
                .Where(d => File.Exists(Path.Combine(d, "ready")) && Version.TryParse(Path.GetFileName(d), out var v) && v > Current)
                .OrderByDescending(d => Version.Parse(Path.GetFileName(d))).FirstOrDefault();
            if (ready == null) return;
            string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var dst = Paths.ExeDir.TrimEnd('\\');
            var script = $"""
                $dst = {Q(dst)}
                while (Get-Process Concilium -ErrorAction SilentlyContinue | Where-Object {"{"} $_.Path -and $_.Path.StartsWith($dst + '\', 'OrdinalIgnoreCase') {"}"}) {"{"} Start-Sleep 2 {"}"}
                robocopy {Q(Path.Combine(ready, "Concilium"))} $dst /E /XD 'Concilium data' /XF portable.txt /R:10 /W:3 /NFL /NDL /NJH /NJS | Out-Null
                if ($LASTEXITCODE -lt 8) {"{"} Remove-Item {Q(Dir)} -Recurse -Force {"}"}
                """;
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch (Exception e) { Paths.LogError(e); }
    }
}
