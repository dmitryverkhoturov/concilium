// The map as a native control: a small force layout drawn straight to the screen. Wheel zooms, drag pans or moves a note.
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Concilium;

public class GraphView : FrameworkElement
{
    public static readonly Dictionary<string, Color> Colors = new()
    {
        ["memory"] = C("#4b5e45"), ["patient facts"] = C("#5a7ea3"), ["hypotheses"] = C("#c0794f"), ["questions"] = C("#8a71ad"),
        ["todo"] = C("#b8963c"), ["history"] = C("#7f8c86"), ["identity"] = C("#7f8c86"), ["branch"] = C("#b25e85"),
        ["note"] = C("#6f9f94"), ["vault"] = C("#6f9f94"), ["document"] = C("#a39f8e"), ["system"] = C("#bfc8c2"), ["missing"] = C("#bfc8c2"),
    };
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public event Action<GNode>? NodeClicked;

    List<GNode> nodes = new();
    List<(GNode s, GNode t, int w)> links = new();
    readonly Dictionary<string, GNode> known = new();
    double scale = 1, ox, oy, alpha;
    bool fitted;
    GNode? hover, dragNode;
    Point? dragFrom;
    bool moved;
    static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    readonly Dictionary<string, Brush> brushes = Colors.ToDictionary(kv => kv.Key, kv => (Brush)new SolidColorBrush(kv.Value));
    static readonly Pen LinkPen = new(new SolidColorBrush(Color.FromArgb(0x30, 0x28, 0x3c, 0x2d)), 1);
    static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x40, 0x2f));
    static readonly Pen Ring = new(Brushes.White, 1.5);

    public GraphView()
    {
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.Arrow;
    }

    public void SetData(GraphData g)
    {
        var rnd = new Random(1);
        var fresh = new List<GNode>();
        foreach (var n in g.nodes)
        {
            if (known.TryGetValue(n.id, out var old)) { n.x = old.x; n.y = old.y; n.pinned = old.pinned; }
            else { var a = rnd.NextDouble() * Math.PI * 2; var r = 40 + rnd.NextDouble() * 120; n.x = Math.Cos(a) * r; n.y = Math.Sin(a) * r; fresh.Add(n); }
        }
        var newIds = g.nodes.Select(n => n.id).ToHashSet();
        var changed = fresh.Count > 0 || known.Keys.Any(k => !newIds.Contains(k));
        nodes = g.nodes;
        known.Clear();
        foreach (var n in nodes) known[n.id] = n;
        links = g.links.Where(l => known.ContainsKey(l.source) && known.ContainsKey(l.target)).Select(l => (known[l.source], known[l.target], l.weight)).ToList();
        if (changed) Reheat(fitted ? 0.5 : 1);
        InvalidateVisual();
    }

    void Reheat(double a)
    {
        var running = alpha > 0.01;
        alpha = Math.Max(alpha, a);
        if (!running) CompositionTarget.Rendering += Tick;
    }

    static double Radius(GNode n) => Math.Sqrt(2 + Math.Sqrt(n.deg) * 2) * 3.4;

    void Tick(object? sender, EventArgs e)
    {
        for (var k = 0; k < 3 && alpha > 0.01; k++) Step();
        if (alpha <= 0.01) { CompositionTarget.Rendering -= Tick; if (!fitted) { Fit(); fitted = true; } }
        else if (!fitted) Fit();
        InvalidateVisual();
    }

    void Step()
    {
        var n = nodes.Count;
        // Repulsion between all pairs; maps are hundreds of notes at most.
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var a = nodes[i]; var b = nodes[j];
                var dx = b.x - a.x; var dy = b.y - a.y;
                var d2 = dx * dx + dy * dy;
                if (d2 < 0.01) { dx = 0.1 * (i - j); dy = 0.1; d2 = 0.02; }
                if (d2 > 250000) continue;
                var f = 140 * alpha / d2;
                a.vx -= dx * f; a.vy -= dy * f; b.vx += dx * f; b.vy += dy * f;
            }
        foreach (var (s, t, w) in links)
        {
            var dx = t.x - s.x; var dy = t.y - s.y;
            var d = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.1);
            var f = (d - 45) / d * 0.06 * alpha * Math.Min(2, 1 + Math.Log(w));
            s.vx += dx * f; s.vy += dy * f; t.vx -= dx * f; t.vy -= dy * f;
        }
        foreach (var v in nodes)
        {
            v.vx -= v.x * 0.03 * alpha; v.vy -= v.y * 0.03 * alpha;
            if (v.pinned || v == dragNode) { v.vx = v.vy = 0; continue; }
            v.vx *= 0.6; v.vy *= 0.6;
            v.x += v.vx; v.y += v.vy;
        }
        alpha *= 0.985;
    }

    void Fit()
    {
        if (nodes.Count == 0 || ActualWidth < 10) return;
        double x0 = nodes.Min(n => n.x), x1 = nodes.Max(n => n.x), y0 = nodes.Min(n => n.y), y1 = nodes.Max(n => n.y);
        var w = Math.Max(x1 - x0, 60) + 80; var h = Math.Max(y1 - y0, 60) + 80;
        scale = Math.Clamp(Math.Min(ActualWidth / w, ActualHeight / h), 0.3, 2.5);
        ox = ActualWidth / 2 - (x0 + x1) / 2 * scale;
        oy = ActualHeight / 2 - (y0 + y1) / 2 * scale;
    }

    Point ToScreen(GNode n) => new(n.x * scale + ox, n.y * scale + oy);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        foreach (var (s, t, w) in links)
        {
            var pen = w > 1 ? new Pen(LinkPen.Brush, Math.Min(3, 0.6 + Math.Log(w + 1))) : LinkPen;
            dc.DrawLine(pen, ToScreen(s), ToScreen(t));
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var n in nodes)
        {
            var r = Radius(n) * Math.Sqrt(scale);
            dc.DrawEllipse(brushes.GetValueOrDefault(n.kind, brushes["note"]), n == hover ? Ring : null, ToScreen(n), r, r);
        }
        // Labels: the hovered and best-connected notes first; a label that would overlap one already drawn is skipped.
        var taken = new List<Rect>();
        foreach (var n in nodes.OrderByDescending(n => n == hover).ThenByDescending(n => n.kind == "memory").ThenByDescending(n => n.deg))
        {
            if (n != hover && n.kind is "missing" or "system" && scale < 1.6) continue;
            var p = ToScreen(n);
            var r = Radius(n) * Math.Sqrt(scale);
            var label = System.Text.RegularExpressions.Regex.Replace(n.label, @"^\S+ (?=(MEMORY|HISTORY|TODO|HYPOTHESES|QUESTIONS|IDENTITY|PATIENT FACTS)$)", "");
            if (label.Length > 26 && n != hover) label = label[..25] + "…";
            var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, n == hover ? 13 : 11.5, LabelBrush, dpi);
            var box = new Rect(p.X - ft.Width / 2 - 3, p.Y + r + 2, ft.Width + 6, ft.Height + 2);
            if (n != hover && taken.Any(t => t.IntersectsWith(box))) continue;
            taken.Add(box);
            if (n == hover) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xe6, 0xfa, 0xfc, 0xfa)), null, box, 6, 6);
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y + r + 3));
        }
    }

    GNode? Hit(Point pt)
    {
        GNode? best = null; var bd = double.MaxValue;
        foreach (var n in nodes)
        {
            var p = ToScreen(n);
            var d = (p - pt).Length;
            if (d < Math.Max(Radius(n) * Math.Sqrt(scale) + 4, 8) && d < bd) { best = n; bd = d; }
        }
        return best;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var k = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var ns = Math.Clamp(scale * k, 0.2, 6);
        ox = p.X - (p.X - ox) * ns / scale;
        oy = p.Y - (p.Y - oy) * ns / scale;
        scale = ns;
        fitted = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        dragFrom = e.GetPosition(this);
        dragNode = Hit(dragFrom.Value);
        moved = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
        {
            var d = p - from;
            if (!moved && d.Length < 4) return;
            moved = true;
            fitted = true;
            if (dragNode != null) { dragNode.x += d.X / scale; dragNode.y += d.Y / scale; dragNode.pinned = true; Reheat(0.2); }
            else { ox += d.X; oy += d.Y; }
            dragFrom = p;
            InvalidateVisual();
            return;
        }
        var h = Hit(p);
        if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Arrow; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (!moved && dragNode != null) NodeClicked?.Invoke(dragNode);
        dragFrom = null; dragNode = null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (hover != null) { hover = null; InvalidateVisual(); }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (!fitted || alpha > 0.01) Fit();
        else { ox += (info.NewSize.Width - info.PreviousSize.Width) / 2; oy += (info.NewSize.Height - info.PreviousSize.Height) / 2; }
        InvalidateVisual();
    }

    public void Refit() { Fit(); InvalidateVisual(); }
}
