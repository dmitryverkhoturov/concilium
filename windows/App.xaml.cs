using System.Windows;

namespace Concilium;

public partial class App : Application
{
    // One window per computer: a second launch brings the first one forward.
    static Mutex? single;

    public static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    public static void Mark(string what) { if (Environment.GetEnvironmentVariable("CONCILIUM_SHOT") != null) Paths.LogError($"[t] {what} {Clock.ElapsedMilliseconds} ms (process {(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:0})"); }

    protected override void OnStartup(StartupEventArgs e)
    {
        Mark("startup");
        single = new Mutex(true, "Concilium.Desktop.Single", out var first);
        if (!first)
        {
            NativeMethods.FocusOther();
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, ex) => { Paths.LogError(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Paths.LogError(ex.ExceptionObject);
        base.OnStartup(e);
        var w = new MainWindow();
        Mark("window built");
        w.Show();
    }
}

// Corner radius for buttons: a number from Tag, or "pill" = exactly half the height
// (a radius larger than that makes WPF draw an oval, not a capsule).
public class RadiusConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] v, Type t, object p, System.Globalization.CultureInfo c)
    {
        var h = v.Length > 1 && v[1] is double d ? d : 0;
        if (v[0] is string s && s == "pill") return new CornerRadius(h / 2);
        return new CornerRadius(double.TryParse(v[0]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) ? Math.Min(r, h / 2) : 0);
    }
    public object[] ConvertBack(object v, Type[] t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
}

static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    public static void FocusOther()
    {
        var me = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
            if (p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); }
    }
}
