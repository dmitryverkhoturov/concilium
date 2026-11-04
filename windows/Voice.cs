// Speaking instead of typing, with Claude Code's own dictation (/voice): Anthropic's speech to text on the person's own sign-in.
// It exists only in Claude Code's interactive terminal, so a hidden one runs in a pseudo-console (ConPTY) and the app taps
// Space as a person would: once to start, once to stop. The transcript comes back through a UserPromptSubmit HTTP hook that
// blocks it, so it never reaches the model and costs no tokens; if the app does not answer, the hook blocks anyway.
// The private speech endpoint itself is not called: the app keeps to the official CLI (see Claude.cs, Auth).
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Concilium;

public sealed class Voice
{
    public static readonly Voice One = new();
    public enum State { Off, Starting, Ready, Recording, Processing }
    public State Now { get; private set; }
    // Raised on a background thread.
    public event Action<State>? Changed;
    public event Action<string>? Text;
    public event Action<string>? Failed;
    // What the check prints: the steps taken inside.
    event Action<string>? Trace;

    Pty? pty;
    TcpListener? hook;
    readonly StringBuilder screen = new();
    readonly object gate = new();
    DateTime lastOutput, stoppedAt, tappedAt, enterAt;
    bool wantRecord, recording;
    int taps;
    Timer? tick;

    // Starts the hidden Claude Code ahead of time (it takes ~10 s), so the first press records at once.
    public void Warm()
    {
        lock (gate) { if (Now != State.Off) return; Set(State.Starting); }
        Task.Run(Start);
    }

    // The microphone button.
    public void Toggle()
    {
        lock (gate)
            switch (Now)
            {
                case State.Off: wantRecord = true; break;
                case State.Starting: wantRecord = !wantRecord; Changed?.Invoke(Now); return;
                case State.Ready: Record(); return;
                case State.Recording: Stop(); return;
                default: return;
            }
        Warm();
    }

    public bool Waiting => wantRecord && Now == State.Starting;

    void Start()
    {
        try
        {
            // Starting Claude Code reaches Anthropic: not from a network where Claude is not offered.
            if (!Paths.HasClaude || UnsupportedRegion.Blocked().GetAwaiter().GetResult()) { Fail(Strings.T.Vpn); Off(); return; }
            var dir = Path.Combine(Paths.Data, "voice");
            Directory.CreateDirectory(dir);
            hook = new TcpListener(IPAddress.Loopback, 0);
            hook.Start();
            new Thread(Listen) { IsBackground = true }.Start();
            var settings = new JsonObject
            {
                ["voice"] = new JsonObject { ["enabled"] = true, ["mode"] = "tap" },
                ["language"] = Strings.Lang == "ru" ? "russian" : "english",
                ["hooks"] = new JsonObject { ["UserPromptSubmit"] = new JsonArray(new JsonObject { ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "http", ["url"] = $"http://127.0.0.1:{((IPEndPoint)hook.LocalEndpoint).Port}/", ["timeout"] = 10, ["onFailure"] = "block",
                }) }) },
            };
            var file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, settings.ToJsonString());
            var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string?)e.Value);
            ClaudeProcess.CleanEnv(env);
            // The classic renderer: a hidden start that never finishes would count as a failed fullscreen start
            // and turn fullscreen off for the person's own Claude Code.
            env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1";
            lock (gate) { screen.Clear(); lastOutput = DateTime.Now; }
            // No settings of the person's own and no MCP servers: this Claude Code only listens.
            var args = new List<string> { "--settings", file, "--setting-sources", "", "--strict-mcp-config" };
            // Claude Code's own log of this instance, only on request: its voice_stream lines say why a recording did not get through.
            // Not by default: it holds every dictated word in plain text and grows by a line per audio chunk.
            var debug = Path.Combine(dir, "debug.log");
            try { File.Delete(debug); } catch { }
            if (Environment.GetEnvironmentVariable("CONCILIUM_VOICE_DEBUG") is { Length: > 0 }) args.AddRange(new[] { "--debug-file", debug });
            pty = new Pty(Paths.Claude, args, dir, env!, Output);
            pty.Exited += () => { Paths.LogError("voice: Claude Code exited\n" + Tail(1500)); Off(); };
            tick = new Timer(_ => Tick(), null, 300, 300);
        }
        catch (Exception e) { Paths.LogError(e); Fail(e.Message); Off(); }
    }

    void Output(string s)
    {
        lock (gate)
        {
            screen.Append(s);
            if (screen.Length > 20000) screen.Remove(0, 10000);
            lastOutput = DateTime.Now;
        }
    }

    static readonly Regex Ansi = new(@"\x1b\[[0-9;?<>=]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)|\x1b[@-_]", RegexOptions.Compiled);
    string Tail(int n) { lock (gate) { var t = Ansi.Replace(screen.ToString(), ""); return t.Length > n ? t[^n..] : t; } }
    // Claude Code draws with cursor moves, so words come apart into pieces: compare without spaces.
    string Flat(int n) => Regex.Replace(Tail(n), @"\s+", "");

    void Key(string k) { try { pty?.Write(k); } catch (Exception e) { Paths.LogError(e); } }
    void Clear() { lock (gate) screen.Clear(); }

    void Tick()
    {
        try
        {
            lock (gate)
            {
                var f = Flat(3000);
                switch (Now)
                {
                    case State.Starting:
                        // What a first start of Claude Code asks, answered as the person would.
                        if (f.Contains("Choosethetextstyle") || f.Contains("PressEntertocontinue")) { Clear(); Key("\r"); }
                        else if (f.Contains("trustthisfolder") && f.Contains("Entertoconfirm"))
                        {
                            // "No, exit" is the default: one down to "Yes, I trust this folder". Claude Code remembers the answer.
                            Clear(); Key("\x1b[B"); Thread.Sleep(400); Key("\r");
                        }
                        else if (f.Contains("Esctokeepbrowsertoolsoff")) { Clear(); Key("\x1b"); }
                        // The prompt box: a rule, then "❯" (or ">" in the classic renderer).
                        else if (Regex.IsMatch(f, "─[>❯]") && !f.Contains("Entertoconfirm") && !f.Contains("Entertocontinue") && DateTime.Now - lastOutput > TimeSpan.FromSeconds(2))
                        {
                            Set(State.Ready);
                            if (wantRecord) Record();
                        }
                        break;
                    case State.Recording:
                        // Claude Code stops by itself after 15 s of silence or 2 minutes: then it is already processing.
                        if (recording && f.Contains("Voice:")) { stoppedAt = DateTime.Now; Set(State.Processing); break; }
                        if (Recorded(f)) recording = true;
                        else if (!recording && DateTime.Now - tappedAt > TimeSpan.FromSeconds(5)) { if (taps < 3) Tap(); else Missed(f); }
                        if (Nothing(f)) Missed(f);
                        break;
                    case State.Processing:
                        if (Nothing(f)) { Missed(f); break; }
                        // Under three words Claude Code puts the text in and waits for Enter. Enter while it is still
                        // turning speech into text breaks that (it never finishes), and finishing takes up to ~5 s
                        // (its own safety timeout). After that, Enter on a still screen, repeated: on an empty box it does nothing.
                        if (DateTime.Now - stoppedAt > TimeSpan.FromSeconds(6) && DateTime.Now - lastOutput > TimeSpan.FromSeconds(0.8) && DateTime.Now - enterAt > TimeSpan.FromSeconds(2))
                        {
                            enterAt = DateTime.Now; Key("\r");
                            Trace?.Invoke("enter, screen: " + f[^Math.Min(f.Length, 300)..]);
                        }
                        // In silence it sometimes stays at "processing" for good.
                        if (DateTime.Now - stoppedAt > TimeSpan.FromSeconds(15)) Missed(f);
                        break;
                }
            }
        }
        catch (Exception e) { Paths.LogError(e); }
    }

    // "No speech detected", "No audio detected from microphone", a denied microphone, a failed connection.
    // Redrawing with cursor moves drops letters ("No spech detected"), so loosely.
    static bool Nothing(string f) => Regex.IsMatch(f, "No.{0,8}detected|Microphoneaccess|Voicemoderequires|connectionfailed|streamerror|Dictationerror|failingrepeatedly");

    // "● REC · tap to send"; letters can drop out, so any piece of it.
    static bool Recorded(string f) => Regex.IsMatch(f, "REC|●|taptosend|tosend");

    void Record()
    {
        wantRecord = false;
        recording = false;
        taps = 0;
        Tap();
        Set(State.Recording);
    }

    // Dictation comes up a few seconds after the prompt box is drawn; until then Space types a space. "REC" shows
    // 1.5–3 s after a tap that worked, and a second tap then would stop it. So: tap, and only if nothing shows
    // for 5 s, take the space back and tap again.
    void Tap()
    {
        if (taps > 0) Key("\x7f");
        Clear();
        Key(" ");
        tappedAt = DateTime.Now;
        taps++;
    }

    void Stop()
    {
        Key(" ");
        stoppedAt = DateTime.Now;
        Set(State.Processing);
    }

    void Missed(string what)
    {
        Paths.LogError("voice: nothing recognized\n" + what);
        Set(State.Ready);
        Fail(Regex.IsMatch(what, "connectionfailed|streamerror|Dictationerror|failingrepeatedly") ? Strings.T.VoiceLost : Strings.T.VoiceNothing);
    }

    void Listen()
    {
        while (hook != null)
        {
            try
            {
                using var c = hook.AcceptTcpClient();
                c.ReceiveTimeout = 5000;
                var s = c.GetStream();
                var body = ReadRequest(s);
                var reply = Encoding.UTF8.GetBytes("{\"decision\":\"block\",\"reason\":\"voice\"}");
                s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {reply.Length}\r\nConnection: close\r\n\r\n"));
                s.Write(reply);
                var raw = JsonNode.Parse(body)?["prompt"]?.GetValue<string>() ?? "";
                var prompt = raw.Trim();
                // A blocked prompt goes back into Claude Code's input box; the next dictation would be added to it.
                Thread.Sleep(500);
                Key(new string('\x7f', raw.Length + 2));
                lock (gate) if (Now is State.Processing or State.Recording) Set(State.Ready);
                if (!string.IsNullOrEmpty(prompt)) Text?.Invoke(prompt);
            }
            catch (Exception e) { if (hook != null) Paths.LogError(e); }
        }
    }

    // A minimal HTTP/1.1 request: headers, then a body by Content-Length or chunked.
    static string ReadRequest(Stream s)
    {
        var buf = new MemoryStream();
        int b;
        while ((b = s.ReadByte()) != -1)
        {
            buf.WriteByte((byte)b);
            if (buf.Length >= 4 && buf.GetBuffer()[(int)buf.Length - 4] == '\r' && buf.GetBuffer()[(int)buf.Length - 1] == '\n' && buf.GetBuffer()[(int)buf.Length - 2] == '\r') break;
        }
        var head = Encoding.ASCII.GetString(buf.ToArray());
        var body = new MemoryStream();
        if (Regex.Match(head, @"content-length:\s*(\d+)", RegexOptions.IgnoreCase) is { Success: true } m)
        {
            var left = int.Parse(m.Groups[1].Value);
            var chunk = new byte[8192];
            while (left > 0 && s.Read(chunk, 0, Math.Min(left, chunk.Length)) is var n and > 0) { body.Write(chunk, 0, n); left -= n; }
        }
        else if (Regex.IsMatch(head, @"transfer-encoding:\s*chunked", RegexOptions.IgnoreCase))
            for (; ; )
            {
                var size = Convert.ToInt32(ReadLine(s).Split(';')[0].Trim(), 16);
                if (size == 0) break;
                var chunk = new byte[size];
                for (var got = 0; got < size;) got += s.Read(chunk, got, size - got) is var n and > 0 ? n : throw new EndOfStreamException();
                body.Write(chunk);
                ReadLine(s);
            }
        return Encoding.UTF8.GetString(body.ToArray());
    }

    static string ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        for (int b; (b = s.ReadByte()) != -1 && b != '\n';) if (b != '\r') sb.Append((char)b);
        return sb.ToString();
    }

    void Set(State s) { Now = s; Changed?.Invoke(s); }
    void Fail(string why) => Failed?.Invoke(why);

    void Off()
    {
        lock (gate)
        {
            tick?.Dispose(); tick = null;
            var h = hook; hook = null; try { h?.Stop(); } catch { }
            var p = pty; pty = null; p?.Dispose();
            wantRecord = false;
            Set(State.Off);
        }
    }

    public void Close() => Off();

    // "Concilium.exe --voice-check": the whole path without a window or a voice — start, record (silence), stop,
    // then a typed line through the hook. Writes voice/check.log; exit code 0 = everything worked.
    public static int Check()
    {
        var v = One;
        var log = new StringBuilder();
        var t0 = DateTime.Now;
        void L(string s) { lock (log) log.AppendLine($"{(DateTime.Now - t0).TotalSeconds,6:0.0}s {s}"); }
        string? text = null;
        v.Changed += s => L("state " + s);
        v.Failed += w => L("failed " + w);
        v.Trace += L;
        v.Text += s => { text = s; L("text " + s); };
        bool Wait(Func<bool> ok, int sec) { for (var i = 0; i < sec * 10 && !ok(); i++) Thread.Sleep(100); return ok(); }
        var pass = false;
        try
        {
            v.Warm();
            if (!Wait(() => v.Now == State.Ready, 90)) { L("not ready\n" + v.Tail(2000)); return 1; }
            // In the window it waits, warm, until the person presses the button.
            Thread.Sleep(int.TryParse(Environment.GetEnvironmentVariable("CONCILIUM_VOICE_IDLE"), out var idle) ? idle * 1000 : 0);
            v.Toggle();
            if (!Wait(() => Recorded(v.Flat(3000)), 20)) { L("no REC\n" + v.Tail(1500)); return 1; }
            L("recording");
            Thread.Sleep(int.TryParse(Environment.GetEnvironmentVariable("CONCILIUM_VOICE_SECONDS"), out var sec) ? sec * 1000 : 3000);
            v.Toggle();
            for (var i = 1; i <= 7 && v.Now == State.Processing; i++) { Thread.Sleep(1000); L($"{i} s after stop: " + v.Flat(200)); }
            if (!Wait(() => v.Now == State.Ready, 40)) { L("stuck after stop\n" + v.Tail(1500)); return 1; }
            Thread.Sleep(1000);
            v.Key("voice check one two three");
            Thread.Sleep(1000);
            v.Key("\r");
            pass = Wait(() => text == "voice check one two three", 30);
            if (!pass) L("no text through the hook\n" + v.Tail(1500));
            return pass ? 0 : 1;
        }
        finally
        {
            L(pass ? "PASS" : "FAIL");
            v.Close();
            Directory.CreateDirectory(Path.Combine(Paths.Data, "voice"));
            File.WriteAllText(Path.Combine(Paths.Data, "voice", "check.log"), log.ToString());
        }
    }
}

// A Windows pseudo-console: the program sees a real terminal, the app reads what it draws and types into it.
sealed class Pty : IDisposable
{
    readonly IntPtr console;
    readonly FileStream input;
    readonly IntPtr process;
    public event Action? Exited;
    bool disposed;

    public Pty(string exe, IEnumerable<string> args, string cwd, IDictionary<string, string> env, Action<string> output)
    {
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
            throw new System.ComponentModel.Win32Exception();
        var hr = CreatePseudoConsole(new Coord { X = 120, Y = 40 }, inRead, outWrite, 0, out console);
        if (hr != 0) throw new System.ComponentModel.Win32Exception(hr);
        inRead.Dispose(); outWrite.Dispose();
        input = new FileStream(inWrite, FileAccess.Write);

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attrs = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(attrs, 1, 0, ref size)) throw new System.ComponentModel.Win32Exception();
            if (!UpdateProcThreadAttribute(attrs, 0, (IntPtr)0x00020016, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
            var si = new StartupInfoEx { lpAttributeList = attrs };
            si.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            // Without this the child would take the app's own (absent) standard handles instead of the pseudo-console.
            si.StartupInfo.dwFlags = 0x100;
            var line = string.Join(" ", new[] { exe }.Concat(args).Select(Quote));
            var block = string.Concat(env.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={kv.Value}\0")) + "\0";
            if (!CreateProcessW(null, new StringBuilder(line), IntPtr.Zero, IntPtr.Zero, false, 0x00080000 | 0x00000400, block, cwd, ref si, out var pi))
                throw new System.ComponentModel.Win32Exception();
            CloseHandle(pi.hThread);
            process = pi.hProcess;
        }
        finally { DeleteProcThreadAttributeList(attrs); Marshal.FreeHGlobal(attrs); }

        new Thread(() =>
        {
            var dec = Encoding.UTF8.GetDecoder();
            using var src = new FileStream(outRead, FileAccess.Read);
            var bytes = new byte[8192]; var chars = new char[8192];
            try { for (int n; (n = src.Read(bytes, 0, bytes.Length)) > 0;) output(new string(chars, 0, dec.GetChars(bytes, 0, n, chars, 0))); } catch { }
        }) { IsBackground = true }.Start();
        new Thread(() => { WaitForSingleObject(process, -1); if (!disposed) Exited?.Invoke(); }) { IsBackground = true }.Start();
    }

    public void Write(string s) { var b = Encoding.UTF8.GetBytes(s); input.Write(b); input.Flush(); }

    static string Quote(string a) =>
        a.Length > 0 && !a.Any(c => c is ' ' or '\t' or '"') ? a : "\"" + Regex.Replace(Regex.Replace(a, @"(\\*)""", @"$1$1\"""), @"(\\+)$", "$1$1") + "\"";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { TerminateProcess(process, 0); } catch { }
        try { input.Dispose(); } catch { }
        ClosePseudoConsole(console);
        CloseHandle(process);
    }

    [StructLayout(LayoutKind.Sequential)] struct Coord { public short X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr sa, int size);
    [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr ret);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, string env, string cwd, ref StartupInfoEx si, out ProcessInformation pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, int ms);
}
