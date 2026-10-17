// Markdown → WPF FlowDocument. Answers and notes are rendered natively, no browser engine.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WBlock = System.Windows.Documents.Block;
using WInline = System.Windows.Documents.Inline;
using WTable = System.Windows.Documents.Table;
using WRow = System.Windows.Documents.TableRow;
using WCell = System.Windows.Documents.TableCell;
using MdRow = Markdig.Extensions.Tables.TableRow;
using MdCell = Markdig.Extensions.Tables.TableCell;

namespace Concilium;

public static class Markdown
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().UseTaskLists().UseAutoLinks().Build();

    public static Brush Accent = new SolidColorBrush(Color.FromRgb(0x4b, 0x5e, 0x45));
    public static Brush Soft = new SolidColorBrush(Color.FromArgb(0x1c, 0x4b, 0x5e, 0x45));
    public static Brush Line = new SolidColorBrush(Color.FromArgb(0x26, 0x20, 0x34, 0x28));
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    static readonly FontFamily Serif = new("Georgia");

    // onWiki: called with the note name of a clicked [[link]]; external links open in the browser.
    public static FlowDocument Render(string md, Action<string>? onWiki = null, double size = 15)
    {
        md = Regex.Replace(md ?? "", @"\A---\r?\n[\s\S]*?\r?\n---\r?\n", "");
        md = Regex.Replace(md, @"!?\[\[([^\]|#^]+)(?:[#^][^\]|]*)?(?:\|([^\]]*))?\]\]",
            m => $"[{(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value).Trim()}](<wl:{m.Groups[1].Value.Trim()}>)");
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = size,
            LineHeight = size * 1.5,
            TextAlignment = TextAlignment.Left,
            IsOptimalParagraphEnabled = false,
        };
        var r = new Renderer(onWiki, size);
        foreach (var b in Markdig.Markdown.Parse(md, Pipeline)) if (r.Block(b) is { } wb) doc.Blocks.Add(wb);
        if (doc.Blocks.FirstBlock is { } first) first.Margin = new Thickness(first.Margin.Left, 0, first.Margin.Right, first.Margin.Bottom);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return doc;
    }

    class Renderer(Action<string>? onWiki, double size)
    {
        public WBlock? Block(MdBlock b)
        {
            switch (b)
            {
                case HeadingBlock h:
                    var hp = new Paragraph { FontFamily = Serif, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, size * 0.9, 0, size * 0.35),
                        FontSize = size * (h.Level switch { 1 => 1.45, 2 => 1.25, 3 => 1.1, _ => 1.0 }) };
                    Inlines(h.Inline, hp.Inlines);
                    return hp;
                case ParagraphBlock pb:
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, size * 0.6) };
                    Inlines(pb.Inline, p.Inlines);
                    return p;
                case ListBlock lb:
                    var list = new List { MarkerStyle = lb.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, size * 0.6), Padding = new Thickness(size * 1.3, 0, 0, 0) };
                    if (lb.IsOrdered && int.TryParse(lb.OrderedStart, out var start)) list.StartIndex = start;
                    foreach (var item in lb.OfType<ListItemBlock>())
                    {
                        var li = new ListItem();
                        foreach (var c in item) if (Block(c) is { } cb) { cb.Margin = new Thickness(0, 0, 0, size * 0.2); li.Blocks.Add(cb); }
                        list.ListItems.Add(li);
                    }
                    return list;
                case QuoteBlock q:
                    var sec = new Section { BorderBrush = Accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(size * 0.8, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, size * 0.6), Foreground = new SolidColorBrush(Color.FromRgb(0x4a, 0x5a, 0x50)) };
                    foreach (var c in q) if (Block(c) is { } qb) sec.Blocks.Add(qb);
                    return sec;
                case CodeBlock cbk:
                    var code = new Paragraph(new Run(string.Join("\n", cbk.Lines.Lines.Take(cbk.Lines.Count).Select(l => l.ToString()))))
                        { FontFamily = Mono, FontSize = size * 0.85, Background = Soft, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, size * 0.6) };
                    return code;
                case ThematicBreakBlock:
                    return new Paragraph { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, size), FontSize = 2 };
                case Markdig.Extensions.Tables.Table t:
                    return Table(t);
                case HtmlBlock hb:
                    return new Paragraph(new Run(hb.Lines.ToString())) { Margin = new Thickness(0, 0, 0, size * 0.6) };
                case ContainerBlock cont:
                    var s = new Section();
                    foreach (var c in cont) if (Block(c) is { } x) s.Blocks.Add(x);
                    return s;
                case LeafBlock leaf when leaf.Inline != null:
                    var lp = new Paragraph();
                    Inlines(leaf.Inline, lp.Inlines);
                    return lp;
            }
            return null;
        }

        WTable Table(Markdig.Extensions.Tables.Table t)
        {
            var table = new WTable { CellSpacing = 0, Margin = new Thickness(0, 0, 0, size * 0.6), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
            var cols = t.OfType<MdRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
            for (var i = 0; i < cols; i++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            foreach (var row in t.OfType<MdRow>())
            {
                var tr = new WRow();
                if (row.IsHeader) tr.FontWeight = FontWeights.SemiBold;
                foreach (var cell in row.OfType<MdCell>())
                {
                    var tc = new WCell { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 4, 6, 4) };
                    foreach (var c in cell) if (Block(c) is { } cb) { cb.Margin = new Thickness(0); tc.Blocks.Add(cb); }
                    tr.Cells.Add(tc);
                }
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }

        void Inlines(ContainerInline? c, InlineCollection into)
        {
            if (c == null) return;
            foreach (var i in c) if (Inline(i) is { } wi) into.Add(wi);
        }

        WInline? Inline(MdInline i)
        {
            switch (i)
            {
                case LiteralInline l: return new Run(l.Content.ToString());
                case TaskList tl: return new Run(tl.Checked ? "☑ " : "☐ ");
                case CodeInline ci: return new Run(ci.Content) { FontFamily = Mono, FontSize = size * 0.88, Background = Soft };
                case LineBreakInline lb: return lb.IsHard ? new LineBreak() : new Run(" ");
                case HtmlEntityInline he: return new Run(he.Transcoded.ToString());
                case HtmlInline h: return Regex.IsMatch(h.Tag, @"^<br\s*/?>$", RegexOptions.IgnoreCase) ? new LineBreak() : null;
                case AutolinkInline al: return Link(al.Url, new Run(al.Url));
                case LinkInline link when link.IsImage:
                    return new Run(string.Concat(link.Descendants<LiteralInline>().Select(x => x.Content.ToString())));
                case LinkInline link:
                    var span = new Span();
                    Inlines(link, span.Inlines);
                    return Link(link.Url ?? "", span);
                case EmphasisInline e:
                    Span s = e.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough }
                        : e.DelimiterCount >= 2 ? new Bold() : new Italic();
                    if (e.DelimiterChar == '=') s = new Span { Background = Soft };
                    Inlines(e, s.Inlines);
                    return s;
                case ContainerInline ci2:
                    var sp = new Span();
                    Inlines(ci2, sp.Inlines);
                    return sp;
            }
            return null;
        }

        WInline Link(string url, WInline content)
        {
            var h = new Hyperlink(content) { Foreground = Accent, Cursor = System.Windows.Input.Cursors.Hand };
            if (url.StartsWith("wl:"))
            {
                var name = url[3..];
                h.TextDecorations = null;
                h.FontWeight = FontWeights.SemiBold;
                h.Click += (_, _) => onWiki?.Invoke(name);
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            {
                h.ToolTip = u.Host;
                h.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
            }
            return h;
        }
    }
}
