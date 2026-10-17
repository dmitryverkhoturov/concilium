// Scrolling like Windows 11 apps: the wheel glides to its target instead of jumping a fixed step.
// Turned on for every ScrollViewer by the implicit style in App.xaml.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Concilium;

public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ScrollViewer sv && (bool)e.NewValue) sv.PreviewMouseWheel += OnWheel;
        }));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    class State { public double target; public bool running; public TimeSpan last; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> states = new();

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        // An inner scroller that can still move takes the wheel itself.
        if (e.OriginalSource is DependencyObject src && Inner(src, sv) is { } inner && inner.ScrollableHeight > 0) return;
        if (sv.ScrollableHeight <= 0) return;
        e.Handled = true;
        var st = states.GetOrCreateValue(sv);
        if (!st.running) st.target = sv.VerticalOffset;
        // One wheel notch (120) ≈ three lines; touchpads send small deltas and stay precise.
        st.target = Math.Clamp(st.target - e.Delta * 0.9, 0, sv.ScrollableHeight);
        if (st.running) return;
        st.running = true;
        st.last = TimeSpan.Zero;
        void Tick(object? s, EventArgs a)
        {
            var now = ((RenderingEventArgs)a).RenderingTime;
            var dt = st.last == TimeSpan.Zero ? 16 : Math.Clamp((now - st.last).TotalMilliseconds, 1, 50);
            st.last = now;
            var d = st.target - sv.VerticalOffset;
            // Exponential ease-out, frame-rate independent: ~90% of the way in 120 ms.
            var step = d * (1 - Math.Exp(-dt / 52));
            if (Math.Abs(d) < 0.5 || Math.Abs(step) < 0.05)
            {
                sv.ScrollToVerticalOffset(st.target);
                CompositionTarget.Rendering -= Tick;
                st.running = false;
                return;
            }
            sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
        }
        CompositionTarget.Rendering += Tick;
    }

    static ScrollViewer? Inner(DependencyObject src, ScrollViewer outer)
    {
        for (var d = src; d != null && d != outer; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ScrollViewer sv && GetEnabled(sv)) return sv;
        return null;
    }
}
