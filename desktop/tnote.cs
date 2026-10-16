// tnote: a text editor modeled on Windows 11 Notepad. Feature set comes from NotepadXamlUI.winmd
// (TabsBar, MainMenuBar, FormattingToolbar, FindReplaceDialog, GotoDialogContent, StatusBar,
// FontSettingSection, ThemeSettingSection, WordWrapSettingSection, RecentFilesViewModel);
// colors and layout are measured from a screenshot of the real app. AI (Cowriter) is left out.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TNote
{
    class TabDoc
    {
        public MdEditor Box;
        public string Path;
        public Encoding Enc = new UTF8Encoding(false);
        public string Eol = "\r\n";
        public bool Dirty;
        public string Title { get { return Path == null ? "제목 없음" : System.IO.Path.GetFileName(Path); } }
    }

    // Colors sampled from Windows 11 Notepad (light) plus a matching dark set
    class Palette
    {
        public Color Title, Chrome, Editor, Border, Text, Muted, Hover, Pressed, Accent, Menu, MenuBorder;
        public static readonly Palette Light = new Palette
        {
            Title = Color.FromArgb(0xD1, 0xDB, 0xEC), Chrome = Color.FromArgb(0xF1, 0xF4, 0xF9), Editor = Color.FromArgb(0xF9, 0xF9, 0xF9),
            Border = Color.FromArgb(0xE4, 0xE7, 0xEC), Text = Color.FromArgb(0x1A, 0x1A, 0x1A), Muted = Color.FromArgb(0x5F, 0x5F, 0x5F),
            Hover = Color.FromArgb(0xE3, 0xE7, 0xEE), Pressed = Color.FromArgb(0xD8, 0xDD, 0xE6), Accent = Color.FromArgb(0x00, 0x5F, 0xB8),
            Menu = Color.FromArgb(0xF9, 0xF9, 0xF9), MenuBorder = Color.FromArgb(0xE0, 0xE0, 0xE0)
        };
        public static readonly Palette Dark = new Palette
        {
            Title = Color.FromArgb(0x20, 0x20, 0x20), Chrome = Color.FromArgb(0x2C, 0x2C, 0x2C), Editor = Color.FromArgb(0x27, 0x27, 0x27),
            Border = Color.FromArgb(0x1D, 0x1D, 0x1D), Text = Color.FromArgb(0xFF, 0xFF, 0xFF), Muted = Color.FromArgb(0xC5, 0xC5, 0xC5),
            Hover = Color.FromArgb(0x38, 0x38, 0x38), Pressed = Color.FromArgb(0x33, 0x33, 0x33), Accent = Color.FromArgb(0x60, 0xCD, 0xFF),
            Menu = Color.FromArgb(0x2C, 0x2C, 0x2C), MenuBorder = Color.FromArgb(0x3A, 0x3A, 0x3A)
        };
    }

    static class Ui
    {
        // One UI font: Malgun Gothic covers Hangul, digits and Latin, so mixed text keeps one size and baseline
        public static readonly Font Body = new Font("Malgun Gothic", 10f);
        public static readonly Font Small = new Font("Malgun Gothic", 9f);        public static Font Icons(float size) { return new Font("Segoe Fluent Icons", size); }
        public static readonly Font Icon10 = Icons(10f), Icon8 = Icons(7.5f), Icon7 = Icons(6f);
        public const string Add = "", Close = "", ChromeClose = "", Min = "", Max = "", Restore = "",
            Bold = "", Italic = "", Strike = "", Link = "", List = "", Table = "", Chevron = "",
            Settings = "", Block = "", Eye = "", Clear = "", Page = "", Dot = "";

        // Malgun Gothic has no semibold face, so it's faked: the text drawn twice, half a pixel apart
        public static void SemiBold(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            var hint = g.TextRenderingHint;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None })
                for (float dx = 0; dx <= 0.5f; dx += 0.5f) g.DrawString(s, f, b, new RectangleF(r.X + dx, r.Y, r.Width, r.Height), sf);
            g.TextRenderingHint = hint;
        }

        public static void Glyph(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(s, f, b, r, sf);
        }

        public static GraphicsPath Round(RectangleF r, float rad, bool bottom = true)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            if (bottom) { p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); }
            else { p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); }
            p.CloseFigure(); return p;
        }
    }

    // Title bar: app icon, tabs, "+" and caption buttons, all custom-drawn like Notepad's TabsBar
    class TabStrip : Control
    {
        public Func<IList<TabDoc>> Docs;
        public Func<TabDoc> Active;
        public Action<TabDoc> Select, CloseDoc;
        public Action NewTab;
        public Palette P = Palette.Light;
        Point mouse = new Point(-1, -1);
        const int TabTop = 6, TabH = 36, TabMax = 240, TabMin = 80, CapW = 46, IconW = 48;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 42;
            Dock = DockStyle.Top;
        }

        Form Win { get { return FindForm(); } }

        int TabWidth()
        {
            int n = Math.Max(1, Docs().Count), room = Width - IconW - CapW * 3 - 60;
            return Math.Max(TabMin, Math.Min(TabMax, room / n));
        }
        Rectangle TabRect(int i) { int w = TabWidth(); return new Rectangle(IconW + i * w, TabTop, w, TabH); }
        Rectangle CloseRect(int i) { var r = TabRect(i); return new Rectangle(r.Right - 36, r.Y + 6, 28, 24); }
        Rectangle PlusRect() { var r = TabRect(Docs().Count); return new Rectangle(r.X + 6, 8, 34, 30); }
        Rectangle Cap(int k) { return new Rectangle(Width - CapW * (3 - k), 0, CapW, 32); } // 0 min,1 max,2 close

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(P.Title);
            var docs = Docs(); var active = Active();
            // app glyph
            Ui.Glyph(g, Ui.Page, Ui.Icons(12f), new Rectangle(13, 9, 24, 26), P.Accent);
            for (int i = 0; i < docs.Count; i++)
            {
                var r = TabRect(i); var d = docs[i]; bool on = d == active, hov = r.Contains(mouse);
                if (on || hov)
                {
                    using (var path = Ui.Round(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), 8, false))
                    using (var b = new SolidBrush(on ? P.Chrome : P.Hover)) g.FillPath(b, path);
                }
                else if (i > 0 && docs[i - 1] != active)
                    using (var pen = new Pen(Color.FromArgb(60, P.Muted))) g.DrawLine(pen, r.X, r.Y + 10, r.X, r.Bottom - 10);
                var tr = new Rectangle(r.X + 12, r.Y, r.Width - 52, r.Height);
                if (on) Ui.SemiBold(g, d.Title, Ui.Body, tr, P.Text);
                else TextRenderer.DrawText(g, d.Title, Ui.Body, tr, P.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                var cr = CloseRect(i); bool chov = cr.Contains(mouse);
                if (chov) using (var path = Ui.Round(cr, 4)) using (var b = new SolidBrush(P.Pressed)) g.FillPath(b, path);
                if (d.Dirty && !hov)
                    using (var b = new SolidBrush(P.Text)) g.FillEllipse(b, cr.X + cr.Width / 2 - 3.5f, cr.Y + cr.Height / 2 - 3.5f, 7, 7);
                else
                    Ui.Glyph(g, Ui.Close, Ui.Icon8, cr, P.Text);
            }
            var pr = PlusRect();
            if (pr.Contains(mouse)) using (var path = Ui.Round(pr, 4)) using (var b = new SolidBrush(P.Hover)) g.FillPath(b, path);
            Ui.Glyph(g, Ui.Add, Ui.Icon10, pr, P.Text);
            bool maxed = Win != null && Win.WindowState == FormWindowState.Maximized;
            string[] caps = { Ui.Min, maxed ? Ui.Restore : Ui.Max, Ui.ChromeClose };
            for (int k = 0; k < 3; k++)
            {
                var r = Cap(k); bool hov = r.Contains(mouse);
                if (hov) using (var b = new SolidBrush(k == 2 ? Color.FromArgb(0xC4, 0x2B, 0x1C) : P.Hover)) g.FillRectangle(b, r);
                Ui.Glyph(g, caps[k], Ui.Icon7, r, hov && k == 2 ? Color.White : P.Text);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { mouse = new Point(-1, -1); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var docs = Docs();
            for (int k = 0; k < 3; k++)
                if (Cap(k).Contains(e.Location) && e.Button == MouseButtons.Left)
                {
                    if (k == 0) Win.WindowState = FormWindowState.Minimized;
                    else if (k == 1) Win.WindowState = Win.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    else Win.Close();
                    return;
                }
            if (PlusRect().Contains(e.Location) && e.Button == MouseButtons.Left) { NewTab(); return; }
            for (int i = 0; i < docs.Count; i++)
            {
                if (!TabRect(i).Contains(e.Location)) continue;
                if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(i).Contains(e.Location))) CloseDoc(docs[i]);
                else if (e.Button == MouseButtons.Left) Select(docs[i]);
                return;
            }
        }

        bool IsEmptyArea(Point p)
        {
            if (p.Y < 4) return false;
            for (int k = 0; k < 3; k++) if (Cap(k).Contains(p)) return false;
            if (PlusRect().Contains(p)) return false;
            for (int i = 0; i < Docs().Count; i++) if (TabRect(i).Contains(p)) return false;
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                // Let the form treat the top edge and empty strip as resize border / caption (drag, snap, double-click)
                if (p.Y < 4 && Win.WindowState != FormWindowState.Maximized || IsEmptyArea(p)) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            }
            base.WndProc(ref m);
        }
    }

    // Status bar laid out like Notepad's: position | characters | file type | zoom | line ending | encoding
    class StatusPanel : Control
    {
        public string Pos = "", Chars = "", Kind = "일반 텍스트", Zoom = "", Eol = "", Enc = "";
        public Palette P = Palette.Light;
        public StatusPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 32; Dock = DockStyle.Bottom;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(P.Chrome);
            using (var pen = new Pen(P.Border)) g.DrawLine(pen, 0, 0, Width, 0);
            int w = Width;
            // column starts measured at 1000px: 24, 104, 304*, 612, 680, 848 (right-anchored from zoom on)
            // the position column grows with its text ("줄 1234, 열 567" doesn't fit 80px in Malgun Gothic)
            int chars = 24 + Math.Max(80, TextRenderer.MeasureText(g, Pos, Ui.Small).Width + 24);
            int[] x = { 24, chars, Math.Max(chars + 96, w / 2 - 196), w - 388, w - 320, w - 152 };
            string[] t = { Pos, Chars, Kind, Zoom, Eol, Enc };
            using (var pen = new Pen(P.Border))
                for (int i = 0; i < 6; i++)
                {
                    if (i > 0) g.DrawLine(pen, x[i] - 10, 9, x[i] - 10, 23);
                    TextRenderer.DrawText(g, t[i], Ui.Small, new Rectangle(x[i], 0, (i < 5 ? x[i + 1] : w) - x[i] - 14, Height), P.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
        }
    }

    class FlatRenderer : ToolStripProfessionalRenderer
    {
        public Palette P;
        public FlatRenderer(Palette p) : base(new Colors(p)) { P = p; RoundedEdges = false; }
        class Colors : ProfessionalColorTable
        {
            readonly Palette p; public Colors(Palette p) { this.p = p; }
            public override Color MenuStripGradientBegin { get { return p.Chrome; } }
            public override Color MenuStripGradientEnd { get { return p.Chrome; } }
            public override Color ToolStripDropDownBackground { get { return p.Menu; } }
            public override Color ImageMarginGradientBegin { get { return p.Menu; } }
            public override Color ImageMarginGradientMiddle { get { return p.Menu; } }
            public override Color ImageMarginGradientEnd { get { return p.Menu; } }
            public override Color MenuBorder { get { return p.MenuBorder; } }
            public override Color SeparatorDark { get { return p.MenuBorder; } }
            public override Color SeparatorLight { get { return p.Menu; } }
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var it = e.Item;
            bool hot = it.Selected || (it is ToolStripMenuItem && ((ToolStripMenuItem)it).DropDown.Visible) || (it is ToolStripMenuItem && ((ToolStripMenuItem)it).Checked && it.IsOnDropDown == false);
            if (!hot || !it.Enabled) return;
            var r = it.IsOnDropDown ? new Rectangle(4, 1, it.Width - 8, it.Height - 2) : new Rectangle(0, 3, it.Width, it.Height - 6);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(r, 4)) using (var b = new SolidBrush(P.Hover)) e.Graphics.FillPath(b, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? P.Text : P.Muted; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = P.Muted; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, "", Ui.Icon8, e.ImageRectangle, P.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(P.MenuBorder))
                if (e.Vertical) e.Graphics.DrawLine(pen, e.Item.Width / 2, 8, e.Item.Width / 2, e.Item.Height - 8);
                else e.Graphics.DrawLine(pen, 0, e.Item.Height / 2, e.Item.Width, e.Item.Height / 2);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) using (var pen = new Pen(P.MenuBorder)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            else using (var pen = new Pen(P.Border)) e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }
    }

    // Markdown spans for live formatting. Offsets index the editor text ('\n' line breaks).
    static class MdSpans
    {
        // Mark: syntax that's hidden unless markers are shown or the caret is on its line (**, #, link URL...)
        public enum K { Heading, Quote, Bold, Italic, Strike, Code, Dim, Mark, Accent, Link }
        public struct Span { public int A, N, Level; public K Kind; }

        static readonly Regex Head = new Regex(@"^(#{1,6}\s+)(.*)$"), Hr = new Regex(@"^\s*(---+|\*\*\*+|___+)\s*$"),
            Item = new Regex(@"^\s*([-*+]|\d+\.)( \[[ xX]\])? "), Sep = new Regex(@"^\s*\|[\s\-:|]+\|?\s*$"),
            Code = new Regex(@"`[^`]+`"), Link = new Regex(@"\[([^\]]+)\]\([^)\s]+\)"),
            BoldItalic = new Regex(@"\*\*\*(?![\s*]).+?\*\*\*"), Bold = new Regex(@"\*\*(?!\s).+?\*\*"), Strike = new Regex(@"~~(?!\s).+?~~"), Italic = new Regex(@"(?<![*\w])\*(?![\s*])[^*]+?\*(?!\*)");

        public static List<Span> Parse(string s)
        {
            var r = new List<Span>();
            var lines = s.Split('\n');
            bool fence = false;
            for (int i = 0, a = 0; i < lines.Length; a += lines[i].Length + 1, i++)
            {
                string l = lines[i]; Match m;
                if (l.TrimStart().StartsWith("```")) { fence = !fence; Add(r, a, l.Length, K.Dim); continue; }
                if (fence) { Add(r, a, l.Length, K.Code); continue; }
                if ((m = Head.Match(l)).Success)
                {
                    Add(r, a, l.Length, K.Heading, m.Groups[1].Value.TrimEnd().Length);
                    Add(r, a, m.Groups[1].Length, K.Mark);
                    Inline(r, a + m.Groups[2].Index, m.Groups[2].Value);
                }
                else if (Hr.IsMatch(l)) Add(r, a, l.Length, K.Dim);
                else if (l.StartsWith(">"))
                {
                    int n = l.Length - l.TrimStart('>', ' ').Length;
                    Add(r, a, l.Length, K.Quote); Add(r, a, n, K.Dim);
                    Inline(r, a + n, l.Substring(n));
                }
                else if ((m = Item.Match(l)).Success)
                {
                    int p = m.Groups[1].Index;
                    Add(r, a + p, m.Length - p, K.Accent);
                    if (m.Groups[2].Success && m.Groups[2].Value.Trim() != "[ ]") { Add(r, a + m.Length, l.Length - m.Length, K.Strike); Add(r, a + m.Length, l.Length - m.Length, K.Dim); }
                    Inline(r, a + m.Length, l.Substring(m.Length));
                }
                else if (l.TrimStart().StartsWith("|"))
                {
                    if (Sep.IsMatch(l)) { Add(r, a, l.Length, K.Dim); continue; }
                    if (i + 1 < lines.Length && Sep.IsMatch(lines[i + 1])) Add(r, a, l.Length, K.Bold);
                    Inline(r, a, l);
                    for (int j = 0; j < l.Length; j++) if (l[j] == '|') Add(r, a + j, 1, K.Dim);
                }
                else Inline(r, a, l);
            }
            return r;
        }

        static void Inline(List<Span> r, int a, string l)
        {
            var masked = l.ToCharArray(); // code spans hide their contents from the other rules
            foreach (Match m in Code.Matches(l))
            {
                Add(r, a + m.Index, m.Length, K.Code); Add(r, a + m.Index, 1, K.Mark); Add(r, a + m.Index + m.Length - 1, 1, K.Mark);
                for (int j = m.Index; j < m.Index + m.Length; j++) masked[j] = ' ';
            }
            string t = new string(masked);
            foreach (Match m in Link.Matches(t))
            {
                int text = m.Groups[1].Index, end = text + m.Groups[1].Length;
                Add(r, a + m.Index, 1, K.Mark); Add(r, a + end, m.Index + m.Length - end, K.Mark); // "[" and "](url)"
                Add(r, a + text, m.Groups[1].Length, K.Link);
            }
            Pair(r, a, t, Strike, 2, K.Strike); // before ***, whose masking would hide ~~ that wraps it
            foreach (Match m in BoldItalic.Matches(t)) // ***x*** is bold and italic; hide it from the ** and * rules
            {
                Add(r, a + m.Index, m.Length, K.Bold); Add(r, a + m.Index, m.Length, K.Italic);
                Add(r, a + m.Index, 3, K.Mark); Add(r, a + m.Index + m.Length - 3, 3, K.Mark);
                t = t.Substring(0, m.Index) + new string(' ', m.Length) + t.Substring(m.Index + m.Length);
            }
            Pair(r, a, t, Bold, 2, K.Bold);
            Pair(r, a, t, Italic, 1, K.Italic);
        }

        static void Pair(List<Span> r, int a, string t, Regex re, int mark, K k)
        {
            foreach (Match m in re.Matches(t))
            {
                Add(r, a + m.Index, m.Length, k);
                Add(r, a + m.Index, mark, K.Mark); Add(r, a + m.Index + m.Length - mark, mark, K.Mark);
            }
        }

        static void Add(List<Span> r, int a, int n, K k, int level = 0) { if (n > 0) r.Add(new Span { A = a, N = n, Kind = k, Level = level }); }
    }

    // Converts between Markdown and formatted text. Like Windows Notepad, the editor holds plain text
    // plus character formatting with no markers; Markdown exists only in .md files and the symbol view.
    static class MdFormat
    {
        [Flags] public enum A { None = 0, Bold = 1, Italic = 2, Strike = 4, Code = 8 }
        public class Fmt { public int Start, Length, Heading; public A Attr; public string Url; public bool CodeBlock; }

        // Heading sizes relative to body text; H6 must still be distinguishable from body
        public static readonly float[] HeadScale = { 1f, 1.7f, 1.45f, 1.25f, 1.12f, 1.07f, 1.03f };
        public static int HeadingLevel(float ratio)
        {
            if (ratio < 1.015f) return 0;
            int best = 1;
            for (int n = 2; n <= 6; n++) if (Math.Abs(HeadScale[n] - ratio) < Math.Abs(HeadScale[best] - ratio)) best = n;
            return best;
        }

        static readonly Regex Head = new Regex(@"^(#{1,6})\s+(.*)$"), LinePrefix = new Regex(@"^\s*([-*+] \[[ xX]\] |[-*+] |\d+\. |> )");
        // Leftmost match wins; at the same spot earlier alternatives win, so *** beats ** beats *
        static readonly Regex Tok = new Regex(@"\\([\\`*_~\[\]#|])|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)" +
            @"|\*\*\*(?![\s*])(.+?)(?<!\s)\*\*\*|\*\*(?!\s)(.+?)(?<!\s)\*\*|~~(?!\s)(.+?)(?<!\s)~~|(?<![*\\])\*(?![\s*])(.+?)(?<!\s)\*(?!\*)");

        // Markdown -> plain text plus formats. Lists, quotes, tables and rules stay as text.
        public static string Parse(string md, List<Fmt> fmts)
        {
            var sb = new StringBuilder(); bool fence = false, first = true;
            foreach (var l in md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (l.TrimStart().StartsWith("```")) { fence = !fence; continue; }
                if (!first) sb.Append('\n');
                first = false;
                int a = sb.Length; Match m;
                if (fence) { sb.Append(l); fmts.Add(new Fmt { Start = a, Length = l.Length + 1, CodeBlock = true }); } // +1: the line break marks a block line
                else if ((m = Head.Match(l)).Success) { Inline(m.Groups[2].Value, sb, fmts); fmts.Add(new Fmt { Start = a, Length = sb.Length - a + 1, Heading = m.Groups[1].Length }); }
                else Inline(l, sb, fmts);
            }
            return sb.ToString();
        }

        static void Inline(string s, StringBuilder sb, List<Fmt> fmts)
        {
            int pos = 0;
            foreach (Match m in Tok.Matches(s))
            {
                sb.Append(s, pos, m.Index - pos); pos = m.Index + m.Length;
                if (m.Groups[1].Success) { sb.Append(m.Groups[1].Value); continue; } // escaped character
                int a = sb.Length; A attr = A.None; string url = null;
                if (m.Groups[2].Success) { sb.Append(m.Groups[2].Value); attr = A.Code; }
                else if (m.Groups[3].Success) { Inline(m.Groups[3].Value, sb, fmts); url = m.Groups[4].Value; }
                else if (m.Groups[5].Success) { Inline(m.Groups[5].Value, sb, fmts); attr = A.Bold | A.Italic; }
                else if (m.Groups[6].Success) { Inline(m.Groups[6].Value, sb, fmts); attr = A.Bold; }
                else if (m.Groups[7].Success) { Inline(m.Groups[7].Value, sb, fmts); attr = A.Strike; }
                else { Inline(m.Groups[8].Value, sb, fmts); attr = A.Italic; }
                fmts.Add(new Fmt { Start = a, Length = sb.Length - a, Attr = attr, Url = url });
            }
            sb.Append(s, pos, s.Length - pos);
        }

        // Formatted text -> Markdown. Per character: attributes, link URL and font size relative to body.
        // A line is a code block line when its line break carries the code background.
        public static string Serialize(string text, A[] attr, string[] url, float[] ratio)
        {
            var o = new StringBuilder(); int n = text.Length, ls = 0; bool inBlock = false;
            while (true)
            {
                int le = text.IndexOf('\n', ls); if (le < 0) le = n;
                bool block = le < n ? (attr[le] & A.Code) != 0 : inBlock && le > ls && Enumerable.Range(ls, le - ls).All(i => (attr[i] & A.Code) != 0);
                if (block != inBlock) { o.Append("```\n"); inBlock = block; }
                if (block) o.Append(text, ls, le - ls);
                else
                {
                    float mx = 1f; for (int i = ls; i < le; i++) mx = Math.Max(mx, ratio[i]);
                    int h = HeadingLevel(mx);
                    if (h > 0) o.Append('#', h).Append(' ');
                    // A list or quote prefix stays outside the markers, or it would stop being a list
                    var pm = LinePrefix.Match(text.Substring(ls, le - ls));
                    if (pm.Success) o.Append(pm.Value);
                    InlineMd(o, text, ls + pm.Length, le, attr, url, h > 0);
                }
                if (le >= n) break;
                o.Append('\n'); ls = le + 1;
            }
            if (inBlock) o.Append("\n```");
            return o.ToString();
        }

        // Plain text as Markdown: only escapes, no formatting
        public static string Escape(string plain)
        {
            int n = plain.Length;
            return Serialize(plain, new A[n + 1], new string[n + 1], Enumerable.Repeat(1f, n + 1).ToArray());
        }

        static void InlineMd(StringBuilder o, string text, int ls, int le, A[] attr0, string[] url0, bool heading)
        {
            int len = le - ls;
            var at = new A[len]; var u = new string[len];
            for (int i = 0; i < len; i++) { at[i] = attr0[ls + i]; if (heading) at[i] &= ~A.Bold; u[i] = url0[ls + i]; }
            // Markers must hug text, so whitespace at the edges of a run is left outside it
            foreach (var f in new[] { A.Bold, A.Italic, A.Strike })
                for (int i = 0; i < len; )
                {
                    if ((at[i] & f) == 0) { i++; continue; }
                    int j = i; while (j < len && (at[j] & f) != 0) j++;
                    for (int k = i; k < j && char.IsWhiteSpace(text[ls + k]); k++) at[k] &= ~f;
                    for (int k = j - 1; k >= i && char.IsWhiteSpace(text[ls + k]); k--) at[k] &= ~f;
                    i = j;
                }
            // Open markers form a stack so nested ones close in reverse order (~~***x***~~)
            var stack = new List<string>(); int start = o.Length;
            for (int i = 0; i <= len; i++)
            {
                var want = new List<string>();
                if (i < len)
                {
                    if (u[i] != null) want.Add("L" + u[i]);
                    if ((at[i] & A.Strike) != 0) want.Add("~~");
                    if ((at[i] & A.Bold) != 0) want.Add("**");
                    if ((at[i] & A.Italic) != 0) want.Add("*");
                    if ((at[i] & A.Code) != 0) want.Add("`");
                }
                int keep = 0; while (keep < stack.Count && keep < want.Count && stack[keep] == want[keep]) keep++;
                for (int k = stack.Count - 1; k >= keep; k--) o.Append(stack[k][0] == 'L' ? "](" + stack[k].Substring(1) + ")" : stack[k]);
                stack.RemoveRange(keep, stack.Count - keep);
                if (i == len) break;
                for (int k = keep; k < want.Count; k++) { o.Append(want[k][0] == 'L' ? "[" : want[k]); stack.Add(want[k]); }
                char c = text[ls + i];
                bool code = (at[i] & A.Code) != 0;
                if (!code && ("\\*`[]~".IndexOf(c) >= 0 || (c == '#' && o.Length == start))) o.Append('\\');
                o.Append(c);
            }
        }
    }

    // Editor control. Normally it shows formatted text (no Markdown markers); in the symbol view it
    // shows Markdown source, styled in place. Styling goes through the Text Object Model with undo
    // suspended, so it never dirties the document or the undo stack.
    class MdEditor : RichTextBox
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string name);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, out IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const int EM_GETOLEINTERFACE = 0x0400 + 60, tomTrue = -1, tomSuspend = -9999995, tomResume = -9999994,
            tomAutoColor = -9999997, tomCharFormat = 13, tomParagraph = 4;
        const string CodeFont = "Consolas";
        public const int MaxStyledLength = 300000;

        public bool Styling;
        dynamic tom;

        public MdEditor() { Multiline = true; DetectUrls = false; AutoWordSelection = false; }


        // RichEdit's own OLE drop target swallows dropped files, so replace it with plain WM_DROPFILES
        [DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr h);
        [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr h, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int DragQueryFile(IntPtr drop, int i, StringBuilder buf, int len);
        [DllImport("shell32.dll")] static extern void DragFinish(IntPtr drop);
        public event Action<string[]> FilesDropped;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0233 /* WM_DROPFILES */)
            {
                int n = DragQueryFile(m.WParam, -1, null, 0);
                var files = new string[n];
                for (int i = 0; i < n; i++) { var sb = new StringBuilder(DragQueryFile(m.WParam, i, null, 0) + 1); DragQueryFile(m.WParam, i, sb, sb.Capacity); files[i] = sb.ToString(); }
                DragFinish(m.WParam);
                if (FilesDropped != null) FilesDropped(files);
                return;
            }
            if (BlockMessage(ref m)) return;
            base.WndProc(ref m);
            // Not while the IME composes: querying the control then would split the Hangul syllable
            if (m.Msg == 0x000F /* WM_PAINT */ && !Composing)
            {
                if (SelectionLength > 0) PaintParagraphGaps();
                if (blockOn) PaintBlock();
            }
        }

        // RichEdit paints the selection over the space between paragraphs; Notepad's D2D RichEdit doesn't.
        // The gap holds no text, so repainting it with the background restores Notepad's look.
        string paintText; // cached text for painting; reset on every change
        protected override void OnTextChanged(EventArgs e) { paintText = null; if (!blockQuiet) ClearBlock(); base.OnTextChanged(e); }
        void PaintParagraphGaps()
        {
            string s = PaintText;
            using (var g = CreateGraphics())
            using (var br = new SolidBrush(BackColor))
            {
                int gap = GapPx(g);
                if (gap <= 0) return;
                int line = GetLineFromCharIndex(GetCharIndexFromPosition(new Point(0, 0)));
                for (; ; line++)
                {
                    int next = GetFirstCharIndexFromLine(line + 1);
                    if (next <= 0 || next > s.Length) break;
                    if (s[next - 1] != '\n') continue; // a wrapped line, not a new paragraph
                    int y = GetPositionFromCharIndex(next).Y;
                    if (y - gap > ClientSize.Height) break;
                    g.FillRectangle(br, 0, y - gap, ClientSize.Width, gap);
                }
            }
        }
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, out int w, out int l);
        string PaintText { get { if (paintText == null) paintText = Text; return paintText; } }
        float Zoom { get { int zn, zd; SendMessage(Handle, 0x0400 + 224 /* EM_GETZOOM */, out zn, out zd); return zn > 0 && zd > 0 ? (float)zn / zd : 1f; } }
        int GapPx(Graphics g) { return (int)Math.Round(BaseSize * GapScale / 72f * g.DpiY * Zoom); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams; // RichEdit 4.1+ supports suspending undo
                if (LoadLibrary("msftedit.dll") != IntPtr.Zero) cp.ClassName = "RICHEDIT50W";
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            tom = null; base.OnHandleCreated(e);
            RevokeDragDrop(Handle); DragAcceptFiles(Handle, true);
            // Advanced typography draws italic overhangs that the selection highlight would otherwise clip
            SendMessage(Handle, 0x0400 + 202 /* EM_SETTYPOGRAPHYOPTIONS */, (IntPtr)1 /* TO_ADVANCEDTYPOGRAPHY */, (IntPtr)1);
            if (base.Text.Length == 0) Quietly(LayoutAll); // a new tab starts with Notepad's line layout
        }

        // Line layout like Notepad: a fixed line height plus space between paragraphs. RichEdit doesn't
        // paint the paragraph space when selecting, which leaves Notepad's gap between highlighted lines.
        const int tomLineSpaceSingle = 0, tomLineSpaceExactly = 4;
        const float GapScale = 0.27f;
        public void SetLineLayout(dynamic para, bool heading)
        {
            if (heading) para.SetLineSpacing(tomLineSpaceSingle, 1f); // big heading text must not be clipped
            else para.SetLineSpacing(tomLineSpaceExactly, BaseSize * 1.5f);
            para.SpaceAfter = BaseSize * GapScale;
        }

        // Body layout for the whole text; headings are then set per line
        void LayoutAll(dynamic doc) { SetLineLayout(doc.Range(0, int.MaxValue).Para, false); }

        [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr h);
        [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr h, IntPtr c);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)] static extern int ImmGetCompositionString(IntPtr c, int index, IntPtr buf, int len);

        // True while the IME is composing a character (e.g. a Hangul syllable). Querying or restyling
        // the control then cancels the composition and splits the syllable into separate jamo.
        public bool Composing
        {
            get
            {
                if (!IsHandleCreated) return false;
                IntPtr c = ImmGetContext(Handle);
                if (c == IntPtr.Zero) return false;
                try { return ImmGetCompositionString(c, 0x0008 /* GCS_COMPSTR */, IntPtr.Zero, 0) > 0; }
                finally { ImmReleaseContext(Handle, c); }
            }
        }

        // RichEdit's own formatting shortcuts (alignment, line spacing, underline, font size...) would add
        // formatting that Markdown can't hold, so they're swallowed
        static readonly Keys[] NativeFormatKeys = {
            Keys.Control | Keys.L, Keys.Control | Keys.E, Keys.Control | Keys.R, Keys.Control | Keys.J, Keys.Control | Keys.U,
            Keys.Control | Keys.D1, Keys.Control | Keys.D2, Keys.Control | Keys.D5, Keys.Control | Keys.Shift | Keys.A,
            Keys.Control | Keys.Shift | Keys.L, Keys.Control | Keys.Shift | Keys.Oemcomma, Keys.Control | Keys.Shift | Keys.OemPeriod,
            Keys.Control | Keys.OemOpenBrackets, Keys.Control | Keys.OemCloseBrackets };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (blockOn && (keyData == Keys.Delete || keyData == Keys.Back)) { BlockDelete(); return true; }
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert)) { PastePlain(); return true; }
            if (base.ProcessCmdKey(ref msg, keyData)) return true; // menu shortcuts (e.g. Ctrl+B) come first
            return Array.IndexOf(NativeFormatKeys, keyData) >= 0;
        }

        public void PastePlain() { if (Clipboard.ContainsText()) SelectedText = Clipboard.GetText().Replace("\r\n", "\n"); }

        dynamic Doc
        {
            get
            {
                if (tom == null && IsHandleCreated)
                {
                    IntPtr p; SendMessage(Handle, EM_GETOLEINTERFACE, IntPtr.Zero, out p);
                    if (p != IntPtr.Zero) { tom = Marshal.GetObjectForIUnknown(p); Marshal.Release(p); }
                }
                return tom;
            }
        }

        static int Rgb(Color c) { return c.R | c.G << 8 | c.B << 16; }
        static Color Mix(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

        // ---------- symbol view: Markdown source styled in place ----------
        public void Restyle(Palette P)
        {
            string s = Text;
            bool on = s.Length <= MaxStyledLength;
            Styling = true;
            try
            {
                var doc = Doc; if (doc == null) return;
                doc.Undo(tomSuspend); doc.Freeze();
                try
                {
                    float size = Font.SizeInPoints;
                    var f = doc.Range(0, s.Length + 1).Font;
                    f.Bold = 0; f.Italic = 0; f.StrikeThrough = 0; f.Underline = 0;
                    f.Name = Font.Name; f.Size = size; f.ForeColor = Rgb(P.Text); f.BackColor = tomAutoColor;
                    LayoutAll(doc);
                    if (!on) return;
                    int dim = Rgb(Mix(P.Text, P.Editor, 0.55f));
                    foreach (var sp in MdSpans.Parse(s))
                    {
                        f = doc.Range(sp.A, sp.A + sp.N).Font;
                        switch (sp.Kind)
                        {
                            case MdSpans.K.Heading: f.Bold = tomTrue; f.Size = size * MdFormat.HeadScale[sp.Level]; SetLineLayout(doc.Range(sp.A, sp.A + sp.N).Para, true); break;
                            case MdSpans.K.Quote: f.Italic = tomTrue; f.ForeColor = Rgb(P.Muted); break;
                            case MdSpans.K.Bold: f.Bold = tomTrue; break;
                            case MdSpans.K.Italic: f.Italic = tomTrue; break;
                            case MdSpans.K.Strike: f.StrikeThrough = tomTrue; break;
                            case MdSpans.K.Code: f.Name = CodeFont; f.BackColor = Rgb(P.Hover); break;
                            case MdSpans.K.Dim:
                            case MdSpans.K.Mark: f.ForeColor = dim; break;
                            case MdSpans.K.Accent: f.ForeColor = Rgb(P.Accent); break;
                            case MdSpans.K.Link: f.ForeColor = Rgb(P.Accent); f.Underline = 1; break;
                        }
                    }
                }
                finally { doc.Unfreeze(); doc.Undo(tomResume); }
            }
            catch (Exception) { tom = null; } // styling is cosmetic; never break editing over it
            finally { Styling = false; }
        }

        // ---------- formatted editing ----------
        // Links have no in-text marker, so each is a live TOM range (it moves with edits) plus its URL
        readonly List<KeyValuePair<dynamic, string>> links = new List<KeyValuePair<dynamic, string>>();
        float BaseSize { get { return Font.SizeInPoints; } }

        // Runs fn with undo suspended and drawing frozen, so loading or recoloring isn't an undoable edit
        void Quietly(Action<dynamic> fn)
        {
            var doc = Doc; if (doc == null) return;
            Styling = true;
            doc.Undo(tomSuspend); doc.Freeze();
            try { fn(doc); }
            finally { doc.Unfreeze(); doc.Undo(tomResume); Styling = false; }
        }

        void ResetFormat(dynamic f, Palette P)
        {
            f.Bold = 0; f.Italic = 0; f.StrikeThrough = 0; f.Underline = 0;
            f.Name = Font.Name; f.Size = BaseSize; f.ForeColor = Rgb(P.Text); f.BackColor = tomAutoColor;
        }

        public void LoadMarkdown(string md, Palette P)
        {
            var fmts = new List<MdFormat.Fmt>();
            string text = MdFormat.Parse(md, fmts);
            Styling = true; Text = text; Styling = false;
            links.Clear();
            Quietly(doc =>
            {
                int n = text.Length;
                ResetFormat(doc.Range(0, n + 1).Font, P);
                LayoutAll(doc);
                foreach (var fm in fmts)
                {
                    int a = Math.Min(fm.Start, n), b = Math.Min(fm.Start + fm.Length, n + 1); // n + 1: the final paragraph mark
                    if (b <= a) continue;
                    dynamic r = doc.Range(a, b); var f = r.Font;
                    if (fm.CodeBlock || (fm.Attr & MdFormat.A.Code) != 0) { f.Name = CodeFont; f.BackColor = Rgb(P.Hover); }
                    if ((fm.Attr & MdFormat.A.Bold) != 0 || fm.Heading > 0) f.Bold = tomTrue;
                    if ((fm.Attr & MdFormat.A.Italic) != 0) f.Italic = tomTrue;
                    if ((fm.Attr & MdFormat.A.Strike) != 0) f.StrikeThrough = tomTrue;
                    if (fm.Heading > 0) { f.Size = BaseSize * MdFormat.HeadScale[fm.Heading]; SetLineLayout(r.Para, true); }
                    if (fm.Url != null) { f.ForeColor = Rgb(P.Accent); f.Underline = 1; links.Add(new KeyValuePair<dynamic, string>(r, fm.Url)); }
                }
            });
            ClearUndo();
        }

        public string ToMarkdown() { bool formatted; return ToMarkdown(out formatted); }

        // formatted: whether anything would be lost by saving as plain text
        public string ToMarkdown(out bool formatted)
        {
            string s = Text; int n = s.Length;
            var at = new MdFormat.A[n + 1]; var url = new string[n + 1]; var ratio = new float[n + 1];
            formatted = false;
            var doc = Doc;
            if (doc != null)
            {
                float bs = BaseSize;
                int story = (int)doc.Range(0, int.MaxValue).End, pos = 0;
                while (pos < story)
                {
                    dynamic r = doc.Range(pos, pos);
                    if ((int)r.MoveEnd(tomCharFormat, 1) == 0) break;
                    int b = (int)r.End; if (b <= pos) break;
                    var f = r.Font;
                    var v = MdFormat.A.None;
                    if ((int)f.Bold == tomTrue) v |= MdFormat.A.Bold;
                    if ((int)f.Italic == tomTrue) v |= MdFormat.A.Italic;
                    if ((int)f.StrikeThrough == tomTrue) v |= MdFormat.A.Strike;
                    if ((int)f.BackColor != tomAutoColor) v |= MdFormat.A.Code;
                    float rt = (float)f.Size / bs;
                    for (int i = pos; i < b && i <= n; i++) { at[i] = v; ratio[i] = rt; }
                    pos = b;
                }
                links.RemoveAll(l => (int)l.Key.End <= (int)l.Key.Start); // link text was deleted
                foreach (var l in links) for (int i = Math.Max(0, (int)l.Key.Start); i < Math.Min(n, (int)l.Key.End); i++) url[i] = l.Value;
            }
            for (int i = 0; i < n && !formatted; i++)
                formatted = at[i] != MdFormat.A.None || url[i] != null || MdFormat.HeadingLevel(ratio[i]) > 0;
            return MdFormat.Serialize(s, at, url, ratio);
        }

        public void ToggleAttr(MdFormat.A a, Palette P)
        {
            var doc = Doc; if (doc == null) return;
            var f = doc.Selection.Font; // with no selection this sets the format for what's typed next
            switch (a)
            {
                case MdFormat.A.Bold: f.Bold = (int)f.Bold == tomTrue ? 0 : tomTrue; break;
                case MdFormat.A.Italic: f.Italic = (int)f.Italic == tomTrue ? 0 : tomTrue; break;
                case MdFormat.A.Strike: f.StrikeThrough = (int)f.StrikeThrough == tomTrue ? 0 : tomTrue; break;
                case MdFormat.A.Code:
                    if ((int)f.BackColor == Rgb(P.Hover)) { f.BackColor = tomAutoColor; f.Name = Font.Name; }
                    else { f.BackColor = Rgb(P.Hover); f.Name = CodeFont; }
                    break;
            }
        }

        dynamic SelectedLines(dynamic doc) { dynamic r = doc.Range((int)doc.Selection.Start, (int)doc.Selection.End); r.Expand(tomParagraph); return r; }

        public void SetHeading(int level)
        {
            var doc = Doc; if (doc == null) return;
            dynamic r = SelectedLines(doc); var f = r.Font;
            f.Size = BaseSize * MdFormat.HeadScale[level]; f.Bold = level > 0 ? tomTrue : 0;
            SetLineLayout(r.Para, level > 0);
        }

        public void ToggleCodeBlock(Palette P)
        {
            var doc = Doc; if (doc == null) return;
            var f = SelectedLines(doc).Font;
            if ((int)f.BackColor == Rgb(P.Hover)) { f.BackColor = tomAutoColor; f.Name = Font.Name; }
            else { f.BackColor = Rgb(P.Hover); f.Name = CodeFont; }
        }

        public void AddLink(string url, Palette P)
        {
            var doc = Doc; if (doc == null) return;
            dynamic sel = doc.Selection;
            if ((int)sel.End == (int)sel.Start) sel.Text = url; // no selection: the URL is the link text
            dynamic r = doc.Range((int)sel.Start, (int)sel.End);
            r.Font.ForeColor = Rgb(P.Accent); r.Font.Underline = 1;
            links.Add(new KeyValuePair<dynamic, string>(r, url));
        }

        public void ClearFormat(Palette P, bool all = false)
        {
            var doc = Doc; if (doc == null) return;
            dynamic r = all ? doc.Range(0, int.MaxValue) : doc.Range((int)doc.Selection.Start, (int)doc.Selection.End);
            int a = r.Start, b = r.End;
            ResetFormat(r.Font, P);
            if (all) LayoutAll(doc);
            if (!all) // headings and code blocks belong to whole lines
            {
                r.Expand(tomParagraph);
                r.Font.Size = BaseSize; r.Font.BackColor = tomAutoColor; r.Font.Name = Font.Name;
                SetLineLayout(r.Para, false);
            }
            links.RemoveAll(l => (int)l.Key.Start < b && (int)l.Key.End > a);
        }

        // After a theme change: text color, link color and code background follow the palette
        public void Recolor(Palette P)
        {
            Quietly(doc =>
            {
                int story = (int)doc.Range(0, int.MaxValue).End, pos = 0;
                doc.Range(0, story).Font.ForeColor = Rgb(P.Text);
                while (pos < story)
                {
                    dynamic r = doc.Range(pos, pos);
                    if ((int)r.MoveEnd(tomCharFormat, 1) == 0) break;
                    int b = (int)r.End; if (b <= pos) break;
                    if ((int)r.Font.BackColor != tomAutoColor) r.Font.BackColor = Rgb(P.Hover);
                    pos = b;
                }
                foreach (var l in links) l.Key.Font.ForeColor = Rgb(P.Accent);
            });
        }

        // A new line after a heading starts as body text, like Notepad
        public bool Formatted;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Formatted || e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
            BeginInvoke((Action)(() =>
            {
                var doc = Doc; if (doc == null) return;
                dynamic r = doc.Range((int)doc.Selection.Start, (int)doc.Selection.Start); r.Expand(tomParagraph);
                if (((string)r.Text).Trim().Length == 0 && Math.Abs((float)r.Font.Size - BaseSize) > 0.01f)
                {
                    r.Font.Size = BaseSize; r.Font.Bold = 0; SetLineLayout(r.Para, false);
                    doc.Selection.Font.Size = BaseSize; doc.Selection.Font.Bold = 0;
                }
            }));
        }

        // ---------- block (column) selection: Alt+drag ----------
        // RichEdit keeps a single selection, so the block is held here and painted over the text.
        // Its corners are visual lines and document x (client x + horizontal scroll), so scrolling keeps it in place.
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Point l);
        bool blockOn, blockDrag, blockQuiet, blockUnwrapped;
        int blockLine0, blockLine1, blockX0, blockX1;
        Timer blockTimer;
        public bool BlockActive { get { return blockOn; } }
        public bool BlockMode; // the toolbar toggle: a plain drag selects a block too
        public event Action BlockChanged;

        Point ScrollPos { get { var p = Point.Empty; SendMessage(Handle, 0x0400 + 221 /* EM_GETSCROLLPOS */, IntPtr.Zero, ref p); return p; } }
        int LineAt(int y) { return GetLineFromCharIndex(GetCharIndexFromPosition(new Point(0, Math.Max(0, Math.Min(ClientSize.Height - 1, y))))); }

        bool BlockMessage(ref Message m)
        {
            const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_KEYDOWN = 0x0100;
            if (m.Msg == WM_LBUTTONDOWN && (BlockMode || (ModifierKeys & Keys.Alt) != 0))
            {
                Focus();
                var p = PointToClient(Cursor.Position);
                int ci = GetCharIndexFromPosition(p);
                if (WordWrap) // lines are unwrapped while the block lasts, with the clicked character kept under the pointer
                {
                    var c0 = GetPositionFromCharIndex(ci); int dx = p.X - c0.X, dy = p.Y - c0.Y;
                    blockUnwrapped = true; WordWrap = false;
                    var c = GetPositionFromCharIndex(ci); var s0 = ScrollPos;
                    var to = new Point(Math.Max(0, c.X + s0.X - c0.X), Math.Max(0, c.Y + s0.Y - c0.Y));
                    SendMessage(Handle, 0x0400 + 222 /* EM_SETSCROLLPOS */, IntPtr.Zero, ref to);
                    c = GetPositionFromCharIndex(ci);
                    var np = new Point(c.X + dx, c.Y + dy);
                    if (np != p) { p = np; Cursor.Position = PointToScreen(p); }
                }
                var sp = ScrollPos;
                blockQuiet = true; Select(ci, 0); blockQuiet = false;
                blockLine0 = blockLine1 = LineAt(p.Y);
                blockX0 = blockX1 = Math.Max(0, Math.Min(ClientSize.Width - 1, p.X)) + sp.X;
                blockOn = blockDrag = true; Capture = true; if (BlockChanged != null) BlockChanged();
                if (blockTimer == null) { blockTimer = new Timer { Interval = 40 }; blockTimer.Tick += (s, e) => BlockAutoScroll(); }
                blockTimer.Start();
                Invalidate();
                return true;
            }
            if (blockDrag)
            {
                if (m.Msg == WM_MOUSEMOVE) { UpdateBlock(); return true; }
                if (m.Msg == WM_LBUTTONUP || m.Msg == 0x001F /* WM_CANCELMODE */ || m.Msg == 0x0215 /* WM_CAPTURECHANGED */)
                {
                    blockDrag = false; blockTimer.Stop(); if (m.Msg == WM_LBUTTONUP) Capture = false;
                    return m.Msg == WM_LBUTTONUP;
                }
            }
            else if (blockOn)
            {
                if ((m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK) && blockUnwrapped)
                {
                    // rewrapping moves the text, so place the caret at the clicked character instead
                    int ci = GetCharIndexFromPosition(PointToClient(Cursor.Position));
                    ClearBlock(); Select(ci, 0);
                    return true;
                }
                if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK) ClearBlock();
                else if (m.Msg == WM_KEYDOWN)
                {
                    var k = (Keys)(int)m.WParam;
                    if (k != Keys.ShiftKey && k != Keys.ControlKey && k != Keys.Menu) ClearBlock();
                }
            }
            if ((m.Msg == 0x0115 /* WM_VSCROLL */ || m.Msg == 0x0114 /* WM_HSCROLL */ || m.Msg == 0x020A /* WM_MOUSEWHEEL */) && blockOn) BeginInvoke((Action)Invalidate);
            return false;
        }

        void UpdateBlock()
        {
            var p = PointToClient(Cursor.Position);
            int line = LineAt(p.Y), x = Math.Max(0, Math.Min(ClientSize.Width - 1, p.X)) + ScrollPos.X;
            if (line == blockLine1 && x == blockX1) return;
            blockLine1 = line; blockX1 = x;
            Invalidate();
        }

        // Dragging past an edge keeps scrolling, so the block can reach beyond the visible text
        void BlockAutoScroll()
        {
            var p = PointToClient(Cursor.Position);
            if (p.Y < 0) SendMessage(Handle, 0x0115, IntPtr.Zero /* SB_LINEUP */, IntPtr.Zero);
            else if (p.Y >= ClientSize.Height) SendMessage(Handle, 0x0115, (IntPtr)1 /* SB_LINEDOWN */, IntPtr.Zero);
            if (!WordWrap)
            {
                if (p.X < 0) SendMessage(Handle, 0x0114, IntPtr.Zero /* SB_LINELEFT */, IntPtr.Zero);
                else if (p.X >= ClientSize.Width) SendMessage(Handle, 0x0114, (IntPtr)1 /* SB_LINERIGHT */, IntPtr.Zero);
            }
            UpdateBlock();
        }

        public void ClearBlock()
        {
            if (!blockOn) return;
            blockOn = blockDrag = false;
            if (BlockChanged != null) BlockChanged();
            if (blockTimer != null) blockTimer.Stop();
            if (blockUnwrapped)
            {
                blockUnwrapped = false;
                if (!BlockMode) { WordWrap = true; ScrollToCaret(); }
            }
            Invalidate();
        }

        protected override void OnSelectionChanged(EventArgs e)
        {
            if (blockOn && !blockQuiet && SelectionLength > 0) ClearBlock(); // e.g. Select All
            base.OnSelectionChanged(e);
        }

        protected override void OnResize(EventArgs e) { if (WordWrap) ClearBlock(); base.OnResize(e); }

        // Character range [a, b) of a visual line, without its paragraph mark
        void LineRange(int line, out int a, out int b)
        {
            string s = PaintText;
            a = Math.Min(GetFirstCharIndexFromLine(line), s.Length);
            int n = GetFirstCharIndexFromLine(line + 1);
            b = n <= a ? s.Length : Math.Min(n, s.Length);
            if (b > a && s[b - 1] == '\n') b--;
        }

        // Left edge x of the character boundary i on the line [a, b] at height y
        int XOf(int i, int b, int y)
        {
            if (i < b) return GetPositionFromCharIndex(i).X;
            var p = GetPositionFromCharIndex(i);
            if (i == 0 || Math.Abs(p.Y - y) < 2) return p.X;
            // the end of a wrapped line: the next character is already on the next line
            string s = PaintText;
            var q = GetPositionFromCharIndex(i - 1);
            return q.X + (int)Math.Round(TextRenderer.MeasureText(s.Substring(i - 1, 1), Font, Size.Empty, TextFormatFlags.NoPadding).Width * Zoom);
        }

        // Character boundary nearest to client x on the line [a, b]
        int IndexAtX(int a, int b, int x)
        {
            if (a == b) return a;
            int y = GetPositionFromCharIndex(a).Y, lo = a, hi = b;
            while (lo < hi) // first character whose middle is right of x
            {
                int mid = (lo + hi) / 2;
                if ((XOf(mid, b, y) + XOf(mid + 1, b, y)) / 2 >= x) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        // Per visual line: start, end, line
        List<int[]> BlockSegments(int from, int to)
        {
            var list = new List<int[]>();
            int sx = ScrollPos.X, xl = Math.Min(blockX0, blockX1) - sx, xr = Math.Max(blockX0, blockX1) - sx;
            for (int line = from; line <= to; line++)
            {
                int a, b; LineRange(line, out a, out b);
                list.Add(new[] { IndexAtX(a, b, xl), IndexAtX(a, b, xr), line });
            }
            return list;
        }
        List<int[]> BlockSegments() { return BlockSegments(Math.Min(blockLine0, blockLine1), Math.Max(blockLine0, blockLine1)); }

        void PaintBlock()
        {
            int l0 = Math.Min(blockLine0, blockLine1), l1 = Math.Max(blockLine0, blockLine1);
            int first = Math.Max(l0, LineAt(0)), last = Math.Min(l1, LineAt(ClientSize.Height - 1));
            if (first > last) return;
            string s = PaintText;
            using (var g = CreateGraphics())
            using (var br = new SolidBrush(Color.FromArgb(110, SystemColors.Highlight)))
            {
                int gap = GapPx(g), pitch = (int)Math.Round(BaseSize * 1.5f / 72f * g.DpiY * Zoom);
                foreach (var seg in BlockSegments(first, last))
                {
                    int a, b; LineRange(seg[2], out a, out b);
                    int y = GetPositionFromCharIndex(a).Y, h = pitch;
                    int next = GetFirstCharIndexFromLine(seg[2] + 1);
                    if (next > 0 && next <= s.Length) h = GetPositionFromCharIndex(next).Y - y - (s[next - 1] == '\n' ? gap : 0);
                    int x0 = XOf(seg[0], b, y), x1 = XOf(seg[1], b, y);
                    g.FillRectangle(br, x0, y, Math.Max(x1 - x0, 1), h); // an empty part still shows as a thin caret
                }
            }
        }

        public string BlockText() { return string.Join("\r\n", BlockSegments().Select(sg => PaintText.Substring(sg[0], sg[1] - sg[0]))); }

        public void BlockCopy() { Clipboard.SetText(BlockText()); }
        public void BlockCut() { BlockCopy(); BlockDelete(); }

        // Bold, italic or strikethrough on each line's part of the block: formatting in place (the block stays),
        // or markers around each part in the symbol view (the text moves, so the block ends)
        public void BlockFormat(MdFormat.A a, string marker, bool marks)
        {
            var segs = BlockSegments().Where(sg => sg[1] > sg[0]).ToList();
            var doc = Doc; if (segs.Count == 0 || doc == null) return;
            blockQuiet = true;
            bool grouped = false;
            try { doc.BeginEditCollection(); grouped = true; } catch (Exception) { }
            try
            {
                if (marks)
                {
                    string s = PaintText; int m = marker.Length;
                    foreach (var sg in segs) // markers can't sit next to a space, so edge spaces stay outside
                    {
                        while (sg[0] < sg[1] && char.IsWhiteSpace(s[sg[0]])) sg[0]++;
                        while (sg[1] > sg[0] && char.IsWhiteSpace(s[sg[1] - 1])) sg[1]--;
                    }
                    segs.RemoveAll(sg => sg[1] <= sg[0]);
                    if (segs.Count == 0) return;
                    Func<int[], bool> has = sg => sg[0] >= m && sg[1] + m <= s.Length && s.Substring(sg[0] - m, m) == marker && s.Substring(sg[1], m) == marker;
                    bool all = segs.All(has);
                    for (int i = segs.Count - 1; i >= 0; i--) // bottom up, and each part's end before its start
                    {
                        var sg = segs[i];
                        if (all) { doc.Range(sg[1], sg[1] + m).Text = ""; doc.Range(sg[0] - m, sg[0]).Text = ""; }
                        else if (!has(sg)) { doc.Range(sg[1], sg[1]).Text = marker; doc.Range(sg[0], sg[0]).Text = marker; }
                    }
                }
                else
                {
                    Func<dynamic, bool> on = f => (int)(a == MdFormat.A.Bold ? f.Bold : a == MdFormat.A.Italic ? f.Italic : f.StrikeThrough) == tomTrue;
                    int v = segs.All(sg => on(doc.Range(sg[0], sg[1]).Font)) ? 0 : tomTrue;
                    foreach (var sg in segs)
                    {
                        var f = doc.Range(sg[0], sg[1]).Font;
                        if (a == MdFormat.A.Bold) f.Bold = v; else if (a == MdFormat.A.Italic) f.Italic = v; else f.StrikeThrough = v;
                    }
                }
            }
            finally { if (grouped) doc.EndEditCollection(); blockQuiet = false; }
            if (marks) ClearBlock(); else Invalidate();
        }

        public void BlockDelete()
        {
            var segs = BlockSegments();
            blockQuiet = true;
            try
            {
                var doc = Doc;
                bool grouped = false;
                if (doc != null) try { doc.BeginEditCollection(); grouped = true; } catch (Exception) { } // one undo step (RichEdit 8+)
                try
                {
                    for (int i = segs.Count - 1; i >= 0; i--) // bottom up, so earlier offsets stay valid
                    {
                        if (segs[i][1] <= segs[i][0]) continue;
                        if (doc != null) doc.Range(segs[i][0], segs[i][1]).Text = "";
                        else { Select(segs[i][0], segs[i][1] - segs[i][0]); SelectedText = ""; }
                    }
                }
                finally { if (grouped) doc.EndEditCollection(); }
                Select(segs[0][0], 0);
            }
            finally { blockQuiet = false; }
            ClearBlock();
        }
    }

    class MainForm : Form
    {
        readonly TabStrip strip = new TabStrip();
        readonly StatusPanel status = new StatusPanel();
        readonly Panel editorHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 16, 0, 0) };
        readonly List<TabDoc> docs = new List<TabDoc>();
        readonly ToolStripMenuItem recentMenu = new ToolStripMenuItem("최근 파일");
        MenuStrip menu;
        ToolStripMenuItem wrapItem, statusItem, darkItem, previewBtn, marksItem;
        readonly List<ToolStripItem> iconItems = new List<ToolStripItem>();
        Font editorFont = new Font("Consolas", 11f);
        float zoom = 1f;
        bool wordWrap = true, dark, previewOn, statusBar = true, blockMode;
        bool showMarks; // symbol view: edit Markdown source instead of formatted text
        readonly Timer restyleTimer = new Timer { Interval = 150 };
        Palette P = Palette.Light;
        TabDoc active;
        readonly WebBrowser preview = new WebBrowser { Dock = DockStyle.Fill, IsWebBrowserContextMenuEnabled = false, AllowWebBrowserDrop = false, Visible = false };
        static readonly string SettingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tnote", "recent.txt");
        static readonly string ViewSettingsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsPath), "settings.txt");

        // ---------- view settings (font, wrap, theme...) kept across runs as key=value lines ----------
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(ViewSettingsPath)) return;
                var kv = File.ReadAllLines(ViewSettingsPath).Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                string v; bool b; float f;
                if (kv.TryGetValue("font", out v)) { var font = (Font)System.ComponentModel.TypeDescriptor.GetConverter(typeof(Font)).ConvertFromInvariantString(v); if (font != null) editorFont = font; }
                if (kv.TryGetValue("wrap", out v) && bool.TryParse(v, out b)) wordWrap = b;
                if (kv.TryGetValue("dark", out v) && bool.TryParse(v, out b)) dark = b;
                if (kv.TryGetValue("statusBar", out v) && bool.TryParse(v, out b)) statusBar = b;
                if (kv.TryGetValue("markdownSymbols", out v) && bool.TryParse(v, out b)) showMarks = b;
                if (kv.TryGetValue("zoom", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) zoom = Math.Max(0.1f, Math.Min(5f, f));
            }
            catch (Exception) { } // a broken settings file just means defaults
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ViewSettingsPath));
                File.WriteAllLines(ViewSettingsPath, new[] {
                    "font=" + System.ComponentModel.TypeDescriptor.GetConverter(typeof(Font)).ConvertToInvariantString(editorFont),
                    "wrap=" + wordWrap, "dark=" + dark, "statusBar=" + statusBar, "markdownSymbols=" + showMarks,
                    "zoom=" + zoom.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
            catch (IOException) { }
        }
        List<string> recent = new List<string>();
        string lastFind = "";
        bool matchCase;

        // one-time move of settings from the old app-data folder
        static void MigrateSettings()
        {
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dst = System.IO.Path.Combine(root, "tnote"), src = System.IO.Path.Combine(root, "NotepadClone");
                if (Directory.Exists(dst) || !Directory.Exists(src)) return;
                Directory.CreateDirectory(dst);
                foreach (var f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)));
            }
            catch (Exception) { }
        }

        public MainForm(string[] args)
        {
            MigrateSettings();
            Text = "tnote";
            Size = new Size(1000, 640);
            MinimumSize = new Size(480, 300);
            KeyPreview = true;
            DoubleBuffered = true;
            Font = Ui.Body;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { } // taskbar: the exe's tnote.ico
            Padding = new Padding(1, 0, 1, 1); // edge strips the form hit-tests as resize borders

            strip.Docs = () => docs; strip.Active = () => active; strip.Select = Switch; strip.CloseDoc = CloseTab; strip.NewTab = () => NewTab();
            LoadSettings();
            menu = BuildMenu();
            darkItem.Checked = dark; marksItem.Checked = showMarks; statusItem.Checked = status.Visible = statusBar;
            MainMenuStrip = menu;
            editorHost.Controls.Add(preview);
            Controls.Add(editorHost);
            Controls.Add(status);
            Controls.Add(menu);
            Controls.Add(strip);
            foreach (Control c in new Control[] { this, editorHost, status, menu, strip }) AcceptFileDrops(c);
            LoadRecent();

            if (args.Length > 0) foreach (var a in args) OpenFile(a);
            if (docs.Count == 0) NewTab();
            restyleTimer.Tick += (s, e) =>
            {
                if (Cur != null && Cur.Box.Composing) return; // try again on the next tick, once the syllable is done
                restyleTimer.Stop();
                if (Cur != null) { if (showMarks) Restyle(Cur); UpdateStatus(); }
            };
            ApplyTheme();
            Resize += (s, e) => strip.Invalidate();
            FormClosing += (s, e) => { foreach (var d in docs.ToList()) if (!ConfirmDiscard(d)) { e.Cancel = true; return; } };
        }

        // ---------- borderless window that keeps native resize, snap and rounded corners ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE = round
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27); // SWP_FRAMECHANGED|NOMOVE|NOSIZE|NOZORDER
        }
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    // A maximized window overhangs the screen by its frame; pull the client area back in
                    var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int f = SystemInformation.FrameBorderSize.Width + 4;
                    r.L += f; r.T += f; r.R -= f; r.B -= f;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                int b = 6;
                if (WindowState != FormWindowState.Maximized)
                {
                    bool l = p.X < b, r = p.X >= Width - b, t = p.Y < b, bt = p.Y >= Height - b;
                    if (t && l) { m.Result = (IntPtr)13; return; }
                    if (t && r) { m.Result = (IntPtr)14; return; }
                    if (bt && l) { m.Result = (IntPtr)16; return; }
                    if (bt && r) { m.Result = (IntPtr)17; return; }
                    if (l) { m.Result = (IntPtr)10; return; }
                    if (r) { m.Result = (IntPtr)11; return; }
                    if (t) { m.Result = (IntPtr)12; return; }
                    if (bt) { m.Result = (IntPtr)15; return; }
                }
                if (p.Y < strip.Height) { m.Result = (IntPtr)2; return; } // HTCAPTION
                m.Result = (IntPtr)1; return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x0005) strip.Invalidate(); // WM_SIZE: caption button glyph
        }

        TabDoc Cur { get { return active; } }

        MenuStrip BuildMenu()
        {
            var m = new MenuStrip { Padding = new Padding(8, 1, 8, 1), AutoSize = false, Height = 33, Dock = DockStyle.Top, ShowItemToolTips = true, Font = Ui.Body };
            var file = new ToolStripMenuItem("파일");
            file.DropDownItems.Add(Item("새 탭", Keys.Control | Keys.N, (s, e) => NewTab()));
            file.DropDownItems.Add(Item("새 창", Keys.Control | Keys.Shift | Keys.N,
                (s, e) => System.Diagnostics.Process.Start(Application.ExecutablePath)));
            file.DropDownItems.Add(Item("열기", Keys.Control | Keys.O, (s, e) => OpenDialog()));
            file.DropDownItems.Add(recentMenu);
            file.DropDownItems.Add(Item("저장", Keys.Control | Keys.S, (s, e) => Save(Cur, false)));
            file.DropDownItems.Add(Item("다른 이름으로 저장", Keys.Control | Keys.Shift | Keys.S, (s, e) => Save(Cur, true)));
            file.DropDownItems.Add(Item("모두 저장", Keys.Control | Keys.Alt | Keys.S, (s, e) => { foreach (var d in docs) if (d.Dirty) Save(d, false); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("탭 닫기", Keys.Control | Keys.W, (s, e) => CloseTab(Cur)));
            file.DropDownItems.Add(Item("창 닫기", Keys.Control | Keys.Shift | Keys.W, (s, e) => Close()));
            file.DropDownItems.Add(Item("끝내기", Keys.None, (s, e) => Close()));

            var edit = new ToolStripMenuItem("편집");
            edit.DropDownItems.Add(Item("실행 취소", Keys.Control | Keys.Z, (s, e) => Cur.Box.Undo()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("잘라내기", Keys.Control | Keys.X, (s, e) => { if (Cur.Box.BlockActive) Cur.Box.BlockCut(); else Cur.Box.Cut(); }));
            edit.DropDownItems.Add(Item("복사", Keys.Control | Keys.C, (s, e) => { if (Cur.Box.BlockActive) Cur.Box.BlockCopy(); else Cur.Box.Copy(); }));
            edit.DropDownItems.Add(Item("붙여넣기", Keys.Control | Keys.V, (s, e) => Cur.Box.PastePlain()));
            // Shown as Del but not bound: a bound Delete shortcut reaches the menu before the RichTextBox,
            // so the key would only ever clear a selection and never delete the next character.
            var del = Item("삭제", Keys.None, (s, e) => { if (Cur.Box.BlockActive) Cur.Box.BlockDelete(); else Cur.Box.SelectedText = ""; });
            del.ShortcutKeyDisplayString = "Del";
            edit.DropDownItems.Add(del);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("찾기", Keys.Control | Keys.F, (s, e) => ShowFind(false)));
            edit.DropDownItems.Add(Item("다음 찾기", Keys.F3, (s, e) => FindNext(lastFind, true)));
            edit.DropDownItems.Add(Item("이전 찾기", Keys.Shift | Keys.F3, (s, e) => FindNext(lastFind, false)));
            edit.DropDownItems.Add(Item("바꾸기", Keys.Control | Keys.H, (s, e) => ShowFind(true)));
            edit.DropDownItems.Add(Item("이동", Keys.Control | Keys.G, (s, e) => GotoLine()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("모두 선택", Keys.Control | Keys.A, (s, e) => Cur.Box.SelectAll()));
            edit.DropDownItems.Add(Item("시간/날짜", Keys.F5, (s, e) => Cur.Box.SelectedText = DateTime.Now.ToString("tt h:mm yyyy-MM-dd")));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("글꼴", Keys.None, (s, e) => ChooseFont()));

            var view = new ToolStripMenuItem("보기");
            var zm = new ToolStripMenuItem("확대/축소");
            zm.DropDownItems.Add(Item("확대", Keys.Control | Keys.Oemplus, (s, e) => SetZoom(zoom + 0.1f)));
            zm.DropDownItems.Add(Item("축소", Keys.Control | Keys.OemMinus, (s, e) => SetZoom(zoom - 0.1f)));
            zm.DropDownItems.Add(Item("기본 확대/축소 복원", Keys.Control | Keys.D0, (s, e) => SetZoom(1f)));
            view.DropDownItems.Add(zm);
            statusItem = Item("상태 표시줄", Keys.None, (s, e) => { statusBar = !statusBar; status.Visible = statusItem.Checked = statusBar; SaveSettings(); });
            wrapItem = Item("자동 줄 바꿈", Keys.None, (s, e) => SetWrap(!wordWrap));
            wrapItem.Checked = wordWrap;
            darkItem = Item("어두운 테마", Keys.None, (s, e) => SetDark(!dark));
            marksItem = Item("Markdown 기호 표시", Keys.None, (s, e) => SetShowMarks(!showMarks));
            previewBtn = Item("서식 미리 보기", Keys.Control | Keys.Shift | Keys.M, (s, e) => TogglePreview());
            view.DropDownItems.AddRange(new ToolStripItem[] { statusItem, wrapItem, darkItem, new ToolStripSeparator(), marksItem, previewBtn });

            foreach (var top in new[] { file, edit, view }) top.Padding = new Padding(10, 0, 10, 0);
            m.Items.AddRange(new ToolStripItem[] { file, edit, view });
            AddFormatBar(m);
            m.Layout += (s, e) => LayoutFormatBar();
            return m;
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            var i = new ToolStripMenuItem(text, null, h);
            if (keys != Keys.None) i.ShortcutKeys = keys;
            return i;
        }

        // ---------- formatting toolbar (Notepad stores formatting as Markdown) ----------
        ToolStripLabel formatSpacer;
        // Owner-drawn icon button: glyph (or text) plus an optional small chevron, like Notepad's toolbar
        ToolStripMenuItem IconBtn(string glyph, string tip, EventHandler h, bool chevron = false, bool textLabel = false)
        {
            var i = new ToolStripMenuItem("", null, h)
            { ToolTipText = tip, AutoToolTip = false, DisplayStyle = ToolStripItemDisplayStyle.None, AutoSize = false, Size = new Size(chevron ? 52 : 34, 30), Margin = new Padding(1, 0, 1, 0) };
            i.Paint += (s, e) =>
            {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new SolidBrush(i.Enabled ? P.Text : P.Muted))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    var main = new RectangleF(chevron ? 4 : 0, 0, chevron ? 30 : i.Width, i.Height);
                    g.DrawString(glyph, textLabel ? Ui.Body : Ui.Icons(11f), b, main, sf);
                    if (chevron) g.DrawString(Ui.Chevron, Ui.Icons(7f), b, new RectangleF(32, 1, 16, i.Height), sf);
                }
            };
            iconItems.Add(i);
            return i;
        }

        void AddFormatBar(MenuStrip m)
        {
            formatSpacer = new ToolStripLabel("") { AutoSize = false, Width = 160 };

            var head = IconBtn("H1", "제목", null, true, true);
            head.DropDownItems.Add(Item("본문", Keys.None, (s, e) => SetHeading(0)));
            for (int n = 1; n <= 6; n++) { int k = n; head.DropDownItems.Add(Item("제목 " + n, Keys.None, (s, e) => SetHeading(k))); }

            var list = IconBtn(Ui.List, "목록", null, true);
            list.DropDownItems.Add(Item("글머리 기호 목록", Keys.None, (s, e) => ToggleLinePrefix("- ")));
            list.DropDownItems.Add(Item("번호 매기기 목록", Keys.None, (s, e) => ToggleLinePrefix("1. ")));
            list.DropDownItems.Add(Item("할 일 목록", Keys.None, (s, e) => ToggleLinePrefix("- [ ] ")));
            foreach (ToolStripItem d in list.DropDownItems) d.Font = Ui.Body;

            var table = IconBtn(Ui.Table, "표", null, true);
            table.DropDownItems.Add(Item("표 삽입 (3x2)", Keys.None, (s, e) => InsertText("\r\n| 제목 1 | 제목 2 | 제목 3 |\r\n| --- | --- | --- |\r\n|  |  |  |\r\n|  |  |  |\r\n")));
            table.DropDownItems.Add(Item("인라인 코드", Keys.None, (s, e) => Format(MdFormat.A.Code, "`")));
            table.DropDownItems.Add(Item("코드 블록", Keys.None, (s, e) => { if (showMarks) WrapSel("```\r\n", "\r\n```"); else { EnsureEditing(); Cur.Box.ToggleCodeBlock(P); Changed(); } }));
            table.DropDownItems.Add(Item("인용", Keys.None, (s, e) => ToggleLinePrefix("> ")));
            table.DropDownItems.Add(Item("가로줄", Keys.None, (s, e) => InsertText("\r\n\r\n---\r\n\r\n")));
            foreach (ToolStripItem d in table.DropDownItems) d.Font = Ui.Body;

            var settings = IconBtn(Ui.Settings, "설정", null);
            settings.Alignment = ToolStripItemAlignment.Right;
            settings.DropDownItems.Add(Item("자동 줄 바꿈 전환", Keys.None, (s, e) => SetWrap(!wordWrap)));
            settings.DropDownItems.Add(Item("어두운 테마 전환", Keys.None, (s, e) => SetDark(!dark)));
            settings.DropDownItems.Add(Item("글꼴...", Keys.None, (s, e) => ChooseFont()));
            foreach (ToolStripItem d in settings.DropDownItems) d.Font = Ui.Body;
            var eye = IconBtn(Ui.Eye, "서식 미리 보기 (Ctrl+Shift+M)", (s, e) => TogglePreview());
            eye.Alignment = ToolStripItemAlignment.Right;

            var link = IconBtn(Ui.Link, "링크 (Ctrl+K)", (s, e) => InsertLink());
            var clear = IconBtn(Ui.Clear, "서식 지우기", (s, e) => ClearFormatting());
            lineFormatItems.AddRange(new ToolStripItem[] { head, list, link, table, clear });

            m.Items.AddRange(new ToolStripItem[] {
                formatSpacer, head, list,
                IconBtn(Ui.Bold, "굵게 (Ctrl+B)", (s, e) => Format(MdFormat.A.Bold, "**")),
                IconBtn(Ui.Italic, "기울임꼴 (Ctrl+I)", (s, e) => Format(MdFormat.A.Italic, "*")),
                IconBtn(Ui.Strike, "취소선", (s, e) => Format(MdFormat.A.Strike, "~~")),
                link, table, clear,
                BlockBtn(),
                settings, eye });
        }

        // With a block, only bold, italic and strikethrough apply (to each line's part); the rest work on
        // whole lines or the caret, which a block doesn't have, so they're off while it lasts
        readonly List<ToolStripItem> lineFormatItems = new List<ToolStripItem>();
        bool BlockFormatting { get { return blockMode || (Cur != null && Cur.Box.BlockActive); } }
        void UpdateFormatItems() { foreach (var i in lineFormatItems) { i.Enabled = !BlockFormatting; i.Invalidate(); } }

        // Toggle: while on, a plain drag selects a block (Alt+drag always does) and lines are shown unwrapped
        ToolStripMenuItem BlockBtn()
        {
            ToolStripMenuItem b = null;
            b = IconBtn(Ui.Block, "블록 선택 (Alt+드래그)", (s, e) =>
            {
                blockMode = b.Checked = !blockMode;
                foreach (var d in docs) { d.Box.BlockMode = blockMode; if (!blockMode) d.Box.ClearBlock(); }
                ApplyWrap(); UpdateFormatItems();
                if (Cur != null) Cur.Box.Focus();
            });
            return b;
        }

        void LayoutFormatBar()
        {
            // Notepad starts the formatting group at about a third of the width
            if (menu == null || formatSpacer == null) return;
            int menusRight = menu.Items.Cast<ToolStripItem>().TakeWhile(i => i != formatSpacer).Sum(i => i.Width + i.Margin.Horizontal) + menu.Padding.Left;
            int w = Math.Max(16, (int)(menu.Width * 0.34) - menusRight);
            if (formatSpacer.Width != w) formatSpacer.Width = w;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Cur != null)
            {
                if (keyData == (Keys.Control | Keys.B)) { Format(MdFormat.A.Bold, "**"); return true; }
                if (keyData == (Keys.Control | Keys.I)) { Format(MdFormat.A.Italic, "*"); return true; }
                if (keyData == (Keys.Control | Keys.K)) { if (!BlockFormatting) InsertLink(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- tabs ----------
        TabDoc NewTab()
        {
            var d = new TabDoc();
            d.Box = new MdEditor
            {
                // Both bars always: changing ScrollBars recreates the window; with word wrap the horizontal one stays hidden
                Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Both, WordWrap = ShownWrap, BlockMode = blockMode,
                AcceptsTab = true, HideSelection = false, BorderStyle = BorderStyle.None,
                Font = editorFont, BackColor = P.Editor, ForeColor = P.Text, Visible = false, Formatted = !showMarks
            };
            d.Box.TextChanged += (s, e) =>
            {
                if (d.Box.Styling) return;
                if (!d.Dirty) { d.Dirty = true; strip.Invalidate(); }
                UpdateStatus(); restyleTimer.Stop(); restyleTimer.Start();
            };
            d.Box.HandleCreated += (s, e) => { ApplyZoom(d); Restyle(d); };
            d.Box.KeyUp += (s, e) => UpdateStatus();
            d.Box.MouseUp += (s, e) => UpdateStatus();
            d.Box.HandleCreated += (s, e) => SetWindowTheme(d.Box.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            d.Box.MouseWheel += (s, e) => { if (ModifierKeys == Keys.Control) SetZoom(zoom + (e.Delta > 0 ? 0.1f : -0.1f)); };
            docs.Add(d);
            editorHost.Controls.Add(d.Box);
            d.Box.FilesDropped += OpenDropped;
            d.Box.BlockChanged += () => { if (d == Cur) UpdateFormatItems(); };
            Switch(d);
            return d;
        }

        void Switch(TabDoc d)
        {
            active = d;
            foreach (var x in docs) x.Box.Visible = x == d && !previewOn;
            if (previewOn) ShowPreview(); else d.Box.Focus();
            UpdateTitle(); UpdateStatus(); strip.Invalidate(); UpdateFormatItems();
        }

        // Files dropped anywhere on the window open in tabs. Drag events don't bubble to the form,
        // so every surface registers its own handlers (the editors use WM_DROPFILES, see MdEditor).
        void AcceptFileDrops(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            c.DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) OpenDropped(files); };
        }

        void OpenDropped(string[] files)
        {
            // Defer so the source (e.g. Explorer) isn't blocked while files load or a message box shows
            BeginInvoke((Action)(() => { foreach (var f in files) if (File.Exists(f)) OpenFile(f); Activate(); }));
        }

        void OpenDialog()
        {
            using (var dlg = new OpenFileDialog { Filter = "텍스트 문서 (*.txt)|*.txt|Markdown (*.md)|*.md|모든 파일 (*.*)|*.*", Multiselect = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) foreach (var f in dlg.FileNames) OpenFile(f);
        }

        void OpenFile(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            var existing = docs.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { Switch(existing); return; }
            if (!File.Exists(path)) { MessageBox.Show(this, path + " 파일을 찾을 수 없습니다.", "tnote"); return; }
            var bytes = File.ReadAllBytes(path);
            var enc = DetectEncoding(bytes);
            string text = enc.GetString(bytes);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            string eol = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : text.Contains("\r") ? "\r" : "\r\n";

            var d = Cur != null && Cur.Path == null && !Cur.Dirty && Cur.Box.TextLength == 0 ? Cur : NewTab();
            d.Path = path; d.Enc = enc; d.Eol = eol;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            // .md files open formatted; anything else is plain text, even if it contains * or #
            if (IsMd(path)) d.Box.LoadMarkdown(text, P); else { d.Box.Text = text; d.Box.ClearFormat(P, true); }
            if (showMarks) { string md = d.Box.ToMarkdown(); d.Box.Text = md; }
            d.Box.Select(0, 0);
            Restyle(d);
            d.Box.ClearUndo();
            d.Dirty = false;
            Switch(d);
            AddRecent(path);
        }

        static Encoding DetectEncoding(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(true);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode;
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode;
            try { new UTF8Encoding(false, true).GetString(b); return new UTF8Encoding(false); }
            catch (DecoderFallbackException) { return Encoding.Default; }
        }

        static string EncName(Encoding e)
        {
            if (e is UTF8Encoding) return e.GetPreamble().Length > 0 ? "UTF-8(BOM)" : "UTF-8";
            if (e.CodePage == 1200) return "UTF-16 LE";
            if (e.CodePage == 1201) return "UTF-16 BE";
            return "ANSI";
        }

        static bool IsMd(string path) { return path != null && Regex.IsMatch(path, @"\.(md|markdown)$", RegexOptions.IgnoreCase); }

        // The document as plain text and as Markdown, and whether plain text would lose formatting
        void GetContent(TabDoc d, out string plain, out string md, out bool formatted)
        {
            if (showMarks)
            {
                md = d.Box.Text;
                var fmts = new List<MdFormat.Fmt>();
                plain = MdFormat.Parse(md, fmts);
                formatted = fmts.Count > 0;
            }
            else { plain = d.Box.Text; md = d.Box.ToMarkdown(out formatted); }
        }

        string AskSavePath(TabDoc d, bool markdown, string name)
        {
            using (var dlg = new SaveFileDialog { Filter = "텍스트 문서 (*.txt)|*.txt|Markdown (*.md)|*.md|모든 파일 (*.*)|*.*", FilterIndex = markdown ? 2 : 1, FileName = name, AddExtension = true, DefaultExt = markdown ? "md" : "txt",
                InitialDirectory = d.Path != null ? System.IO.Path.GetDirectoryName(d.Path) : "" })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        // Saving formatted text as a non-Markdown file: Yes = save as Markdown, No = save as text, Cancel
        DialogResult AskKeepFormatting()
        {
            using (var f = new Form { Text = "tnote", FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(520, 200), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Ui.Body, BackColor = P.Menu, ForeColor = P.Text })
            {
                var title = new Label { Text = "서식을 유지하려면 Markdown으로 저장", Font = new Font(Ui.Body.FontFamily, 14f), Left = 24, Top = 18, Width = 470, Height = 32 };
                var body = new Label { Text = "텍스트 파일입니다. 굵게 및 제목 등의 현재 서식을 유지하려면 Markdown 파일(.md)로 저장합니다.", Left = 24, Top = 56, Width = 470, Height = 40 };
                var warn = new Label { Text = "일반 텍스트 파일로 저장하면 모든 서식이 손실됩니다.", Font = new Font(Ui.Body, FontStyle.Bold), Left = 24, Top = 98, Width = 470, Height = 22 };
                var md = new Button { Text = "Markdown 파일로...", Left = 24, Top = 148, Width = 150, Height = 32, DialogResult = DialogResult.Yes, BackColor = P.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var txt = new Button { Text = "텍스트 파일로 저장", Left = 184, Top = 148, Width = 150, Height = 32, DialogResult = DialogResult.No, FlatStyle = FlatStyle.Flat };
                var cancel = new Button { Text = "취소", Left = 344, Top = 148, Width = 150, Height = 32, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
                md.FlatAppearance.BorderSize = 0;
                f.Controls.AddRange(new Control[] { title, body, warn, md, txt, cancel });
                f.AcceptButton = md; f.CancelButton = cancel;
                return f.ShowDialog(this);
            }
        }

        bool Save(TabDoc d, bool saveAs)
        {
            if (d == null) return false;
            string plain, md; bool formatted;
            GetContent(d, out plain, out md, out formatted);
            string path = d.Path;
            if (saveAs || path == null)
            {
                string name = path == null ? "제목 없음" + (formatted ? ".md" : ".txt") : System.IO.Path.GetFileName(path);
                path = AskSavePath(d, formatted && path == null || IsMd(path), name);
                if (path == null) return false;
            }
            bool asMd = IsMd(path);
            if (!asMd && formatted)
            {
                var choice = AskKeepFormatting();
                if (choice == DialogResult.Cancel) return false;
                if (choice == DialogResult.Yes)
                {
                    path = AskSavePath(d, true, System.IO.Path.GetFileNameWithoutExtension(path) + ".md");
                    if (path == null) return false;
                    asMd = true;
                }
            }
            File.WriteAllText(path, (asMd ? md : plain).Replace("\n", d.Eol), d.Enc);
            if (!asMd && formatted) // saved without formatting, so drop it from the editor too
            {
                if (showMarks) { d.Box.Styling = true; d.Box.Text = MdFormat.Escape(plain); d.Box.Styling = false; Restyle(d); }
                else d.Box.ClearFormat(P, true);
            }
            d.Path = path;
            d.Dirty = false;
            strip.Invalidate(); UpdateTitle(); UpdateStatus();
            AddRecent(d.Path);
            return true;
        }

        bool ConfirmDiscard(TabDoc d)
        {
            if (!d.Dirty) return true;
            Switch(d);
            var r = MessageBox.Show(this, d.Title + "의 변경 내용을 저장하시겠습니까?", "tnote", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            return r == DialogResult.No || Save(d, false);
        }

        void CloseTab(TabDoc d)
        {
            if (d == null || !ConfirmDiscard(d)) return;
            int i = docs.IndexOf(d);
            docs.Remove(d);
            editorHost.Controls.Remove(d.Box);
            d.Box.Dispose();
            if (docs.Count == 0) { Close(); return; }
            if (active == d) Switch(docs[Math.Max(0, i - 1)]); else strip.Invalidate();
        }

        void UpdateTitle() { if (Cur != null) Text = Cur.Title + " - tnote"; }

        void UpdateStatus()
        {
            var d = Cur;
            if (d == null) return;
            if (d.Box.Composing) { restyleTimer.Stop(); restyleTimer.Start(); return; } // the timer updates it once the syllable is done
            int sel = d.Box.SelectionStart, line = d.Box.GetLineFromCharIndex(sel);
            int col = sel - d.Box.GetFirstCharIndexFromLine(line);
            status.Pos = string.Format("줄 {0}, 열 {1}", line + 1, col + 1);
            status.Chars = d.Box.SelectionLength > 0
                ? string.Format("{0}/{1} 자", d.Box.SelectionLength, d.Box.TextLength)
                : d.Box.TextLength + " 자";
            status.Kind = d.Path != null && d.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "Markdown" : "일반 텍스트";
            status.Zoom = Math.Round(zoom * 100) + "%";
            status.Eol = d.Eol == "\r\n" ? "Windows (CRLF)" : d.Eol == "\n" ? "Unix (LF)" : "Macintosh (CR)";
            status.Enc = EncName(d.Enc);
            status.Invalidate();
        }

        // ---------- find / replace / goto ----------
        Form findForm;
        void ShowFind(bool replace)
        {
            if (findForm != null && !findForm.IsDisposed) findForm.Close();
            var f = new Form { Text = replace ? "바꾸기" : "찾기", FormBorderStyle = FormBorderStyle.FixedToolWindow, Size = new Size(420, replace ? 170 : 130), StartPosition = FormStartPosition.CenterParent, Owner = this, Font = Ui.Body, BackColor = P.Chrome, ForeColor = P.Text };
            var findBox = new TextBox { Left = 80, Top = 12, Width = 200, Text = Cur.Box.SelectionLength > 0 ? Cur.Box.SelectedText : lastFind };
            var repBox = new TextBox { Left = 80, Top = 42, Width = 200, Visible = replace };
            var mc = new CheckBox { Text = "대/소문자 구분", Left = 10, Top = replace ? 75 : 45, Width = 130, Checked = matchCase };
            var next = new Button { Text = "다음 찾기", Left = 295, Top = 10, Width = 100 };
            var prev = new Button { Text = "이전 찾기", Left = 295, Top = 40, Width = 100, Visible = !replace };
            var rep = new Button { Text = "바꾸기", Left = 295, Top = 40, Width = 100, Visible = replace };
            var repAll = new Button { Text = "모두 바꾸기", Left = 295, Top = 70, Width = 100, Visible = replace };
            f.Controls.AddRange(new Control[] { new Label { Text = "찾을 내용:", Left = 10, Top = 15, Width = 70 }, findBox,
                new Label { Text = "바꿀 내용:", Left = 10, Top = 45, Width = 70, Visible = replace }, repBox, mc, next, prev, rep, repAll });
            Action sync = () => { lastFind = findBox.Text; matchCase = mc.Checked; };
            next.Click += (s, e) => { sync(); FindNext(lastFind, true); };
            prev.Click += (s, e) => { sync(); FindNext(lastFind, false); };
            rep.Click += (s, e) =>
            {
                sync();
                var b = Cur.Box;
                if (b.SelectionLength > 0 && string.Equals(b.SelectedText, lastFind, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                    b.SelectedText = repBox.Text;
                FindNext(lastFind, true);
            };
            repAll.Click += (s, e) =>
            {
                sync();
                if (lastFind.Length == 0) return;
                var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var src = Cur.Box.Text; var sb = new StringBuilder(); int i = 0, n = 0, j;
                while ((j = src.IndexOf(lastFind, i, cmp)) >= 0) { sb.Append(src, i, j - i).Append(repBox.Text); i = j + lastFind.Length; n++; }
                if (n == 0) return;
                // Replace match by match from the end, so the formatting of the rest of the text survives
                var b = Cur.Box; var hits = new List<int>(); i = 0;
                while ((j = src.IndexOf(lastFind, i, cmp)) >= 0) { hits.Add(j); i = j + lastFind.Length; }
                for (int k = hits.Count - 1; k >= 0; k--) { b.Select(hits[k], lastFind.Length); b.SelectedText = repBox.Text; }
            };
            f.AcceptButton = next;
            findForm = f;
            f.Show();
        }

        void FindNext(string what, bool forward)
        {
            if (string.IsNullOrEmpty(what)) { ShowFind(false); return; }
            var b = Cur.Box; var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int idx;
            if (forward)
            {
                idx = b.Text.IndexOf(what, b.SelectionStart + b.SelectionLength, cmp);
                if (idx < 0) idx = b.Text.IndexOf(what, 0, cmp); // wrap around
            }
            else
            {
                int start = b.SelectionStart - 1;
                idx = start < 0 ? -1 : b.Text.LastIndexOf(what, start, cmp);
                if (idx < 0) idx = b.Text.LastIndexOf(what, cmp);
            }
            if (idx < 0) { MessageBox.Show(this, "\"" + what + "\"을(를) 찾을 수 없습니다.", "tnote"); return; }
            b.Select(idx, what.Length); b.ScrollToCaret(); UpdateStatus();
        }

        void GotoLine()
        {
            var b = Cur.Box;
            using (var f = new Form { Text = "줄 이동", FormBorderStyle = FormBorderStyle.FixedDialog, Size = new Size(260, 130), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Ui.Body })
            {
                var num = new NumericUpDown { Left = 10, Top = 30, Width = 220, Minimum = 1, Maximum = Math.Max(1, b.Lines.Length), Value = b.GetLineFromCharIndex(b.SelectionStart) + 1 };
                var ok = new Button { Text = "이동", Left = 150, Top = 58, DialogResult = DialogResult.OK };
                f.Controls.AddRange(new Control[] { new Label { Text = "줄 번호:", Left = 10, Top = 8 }, num, ok });
                f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                b.Select(b.GetFirstCharIndexFromLine((int)num.Value - 1), 0); b.ScrollToCaret(); UpdateStatus();
            }
        }

        // ---------- view ----------
        void ChooseFont()
        {
            using (var dlg = new FontDialog { Font = editorFont })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    editorFont = dlg.Font;
                    SaveSettings();
                    // Setting Font resets all character formatting, so carry it across as Markdown
                    foreach (var d in docs)
                    {
                        bool dirty = d.Dirty;
                        string md = showMarks ? null : d.Box.ToMarkdown();
                        d.Box.Styling = true; d.Box.Font = editorFont; d.Box.Styling = false;
                        if (md != null) d.Box.LoadMarkdown(md, P);
                        Restyle(d); ApplyZoom(d);
                        d.Dirty = dirty;
                    }
                }
        }

        // RichEdit's own zoom, so the font size (and with it heading sizes) stays put
        const int EM_SETZOOM = 0x0400 + 225;
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        void ApplyZoom(TabDoc d) { if (d.Box.IsHandleCreated) SendMessage(d.Box.Handle, EM_SETZOOM, (IntPtr)(int)Math.Round(zoom * 100), (IntPtr)100); }

        void SetZoom(float z)
        {
            zoom = Math.Max(0.1f, Math.Min(5f, z));
            foreach (var d in docs) ApplyZoom(d);
            UpdateStatus(); SaveSettings();
        }

        void SetWrap(bool on)
        {
            wordWrap = on; wrapItem.Checked = on;
            ApplyWrap(); SaveSettings();
        }

        // Block selection mode shows lines unwrapped; the saved setting comes back when it ends
        bool ShownWrap { get { return wordWrap && !blockMode; } }
        void ApplyWrap()
        {
            foreach (var d in docs) { d.Box.WordWrap = ShownWrap; }
        }

        void SetDark(bool on) { dark = on; darkItem.Checked = on; ApplyTheme(); SaveSettings(); }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        void ApplyTheme()
        {
            P = dark ? Palette.Dark : Palette.Light;
            BackColor = P.Border; // shows through the 1px edge padding
            strip.P = P; status.P = P;
            menu.Renderer = new FlatRenderer(P);
            menu.BackColor = P.Chrome;
            editorHost.BackColor = P.Editor;
            foreach (var d in docs)
            {
                d.Box.BackColor = P.Editor; d.Box.ForeColor = P.Text;
                if (d.Box.IsHandleCreated) SetWindowTheme(d.Box.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            strip.Invalidate(); status.Invalidate(); menu.Invalidate();
            RestyleAll();
            if (previewOn) ShowPreview();
        }

        // ---------- formatting ----------
        // Symbol view styles the Markdown source; formatted view only needs colors to follow the theme
        void Restyle(TabDoc d) { if (d.Box.IsHandleCreated) { if (showMarks) d.Box.Restyle(P); else d.Box.Recolor(P); } }
        void RestyleAll() { foreach (var d in docs) Restyle(d); }

        // Switches every tab between formatted text and its Markdown source
        void SetShowMarks(bool on)
        {
            if (on == showMarks) return;
            EnsureEditing();
            foreach (var d in docs)
            {
                bool dirty = d.Dirty;
                if (on) { string md = d.Box.ToMarkdown(); d.Box.Styling = true; d.Box.Text = md; d.Box.Styling = false; d.Box.ClearUndo(); }
                else d.Box.LoadMarkdown(d.Box.Text, P);
                d.Box.Formatted = !on;
                d.Dirty = dirty;
            }
            showMarks = on; marksItem.Checked = on; SaveSettings();
            RestyleAll(); UpdateStatus();
        }

        void Changed() { var d = Cur; if (d != null && !d.Dirty) { d.Dirty = true; strip.Invalidate(); } }

        // Bold, italic, strikethrough, inline code: real formatting, or markers in the symbol view
        void Format(MdFormat.A a, string marker)
        {
            EnsureEditing();
            if (Cur.Box.BlockActive) { Cur.Box.BlockFormat(a, marker, showMarks); Changed(); Cur.Box.Focus(); return; }
            if (showMarks) { WrapSel(marker, marker); return; }
            Cur.Box.ToggleAttr(a, P); Changed(); Cur.Box.Focus();
        }

        // ---------- formatting commands ----------
        void EnsureEditing() { if (previewOn) TogglePreview(); }

        void InsertText(string s) { EnsureEditing(); Cur.Box.SelectedText = s; Cur.Box.Focus(); }

        // Inline markers can nest (~~**x**~~), so toggling one looks through every marker layer
        // around the text instead of only the characters right next to it.
        const string Marks = "*~`";

        static List<KeyValuePair<char, int>> MarkerRuns(string t, int i, int d) // nearest run first
        {
            var r = new List<KeyValuePair<char, int>>();
            while (i >= 0 && i < t.Length && Marks.IndexOf(t[i]) >= 0)
            {
                char c = t[i]; int n = 0;
                while (i >= 0 && i < t.Length && t[i] == c) { n++; i += d; }
                r.Add(new KeyValuePair<char, int>(c, n));
            }
            return r;
        }

        // Edit that removes `open` around t[s, e): replace [from, to) with str. False if that layer isn't there.
        static bool UnwrapIn(string t, int s, int e, string open, out int from, out int to, out string str)
        {
            from = to = 0; str = null;
            char ch = open[0]; int m = open.Length, offL = 0, offR = 0;
            var L = MarkerRuns(t, s - 1, -1); var R = MarkerRuns(t, e, 1);
            for (int j = 0; j < Math.Min(L.Count, R.Count); offL += L[j].Value, offR += R[j].Value, j++)
            {
                if (L[j].Key != ch || R[j].Key != ch) continue;
                int k = Math.Min(L[j].Value, R[j].Value); // * is italic only as a run of 1 or 3; ** needs 2 or more
                if (!(m == 1 ? k == 1 || k >= 3 : k >= m)) return false;
                from = s - offL - m; to = e + offR + m; str = t.Substring(s - offL, e + offR - (s - offL));
                return true;
            }
            return false;
        }

        static readonly Regex LineParts = new Regex(@"^(\s*(?:#{1,6}\s+|[-*+] \[[ xX]\] |[-*+] |\d+\. |> )?\s*)(.*?)(\s*)$");

        // Unwrap edit for one line's content, or false if `open` isn't around it
        static bool LineWrapped(string c, string open, out int from, out int to, out string str)
        {
            from = to = 0; str = null;
            int P = MarkerRuns(c, 0, 1).Sum(r => r.Value), T = MarkerRuns(c, c.Length - 1, -1).Sum(r => r.Value);
            return c.Length > P + T && UnwrapIn(c, P, c.Length - T, open, out from, out to, out str);
        }

        // Inline markers stop at a line break, so a multi-line selection is wrapped line by line, after
        // any list/heading/quote prefix. If every line already has the marker, it's removed from all.
        void WrapLines(int a, int len, string open)
        {
            var b = Cur.Box; int from, to; string str;
            var parts = b.Text.Substring(a, len).Split('\n').Select(l => LineParts.Match(l)).ToList();
            bool all = parts.Any(p => p.Groups[2].Length > 0) && parts.All(p => p.Groups[2].Length == 0 || LineWrapped(p.Groups[2].Value, open, out from, out to, out str));
            string outText = string.Join("\n", parts.Select(p =>
            {
                string c = p.Groups[2].Value;
                if (c.Length > 0 && all) { LineWrapped(c, open, out from, out to, out str); c = c.Substring(0, from) + str + c.Substring(to); }
                else if (c.Length > 0 && !LineWrapped(c, open, out from, out to, out str)) c = open + c + open;
                return p.Groups[1].Value + c + p.Groups[3].Value;
            }));
            b.Select(a, len); b.SelectedText = outText; b.Select(a, outText.Length); b.Focus();
        }

        void WrapSel(string open, string close)
        {
            EnsureEditing();
            var b = Cur.Box; string v = b.Text; int a = b.SelectionStart, len = b.SelectionLength;
            if (open == close) // inline markers can't span a line break, so leave edge whitespace (a selected line's \n) outside
            {
                while (len > 0 && char.IsWhiteSpace(v[a + len - 1])) len--;
                while (len > 0 && char.IsWhiteSpace(v[a])) { a++; len--; }
                b.Select(a, len);
            }
            string sel = b.SelectedText;
            char ch = open[0]; int m = open.Length;
            if (open == close && Marks.IndexOf(ch) >= 0 && sel.Contains("\n")) { WrapLines(a, len, open); return; }
            if (open == close && Marks.IndexOf(ch) >= 0 && open == new string(ch, m))
            {
                int from, to; string str;
                if (UnwrapIn(v, a, a + len, open, out from, out to, out str)) // markers around the selection
                {
                    b.Select(from, to - from); b.SelectedText = str; b.Select(a - m, len);
                }
                else
                {
                    int P = MarkerRuns(sel, 0, 1).Sum(r => r.Value), T = MarkerRuns(sel, sel.Length - 1, -1).Sum(r => r.Value);
                    if (sel.Length > P + T && UnwrapIn(sel, P, sel.Length - T, open, out from, out to, out str)) // markers inside the selection
                    {
                        b.Select(a + from, to - from); b.SelectedText = str; b.Select(a, len - 2 * m);
                    }
                    else { b.SelectedText = open + sel + open; b.Select(a + m, len); }
                }
                b.Focus();
                return;
            }
            if (a >= open.Length && a + len + close.Length <= v.Length && v.Substring(a - open.Length, open.Length) == open && v.Substring(a + len, close.Length) == close)
            {
                b.Select(a - open.Length, len + open.Length + close.Length); b.SelectedText = sel; b.Select(a - open.Length, len);
            }
            else if (sel.Length >= open.Length + close.Length && sel.StartsWith(open) && sel.EndsWith(close))
            {
                b.SelectedText = sel.Substring(open.Length, sel.Length - open.Length - close.Length); b.Select(a, len - open.Length - close.Length);
            }
            else { b.SelectedText = open + sel + close; b.Select(a + open.Length, len); }
            b.Focus();
        }

        void SelectLines(out int start, out int end)
        {
            var b = Cur.Box; string v = b.Text;
            start = b.SelectionStart == 0 ? 0 : v.LastIndexOf('\n', b.SelectionStart - 1) + 1;
            int stop = b.SelectionStart + b.SelectionLength;
            if (b.SelectionLength > 0 && stop > 0 && v[stop - 1] == '\n') stop--;
            end = v.IndexOf('\n', Math.Max(stop, start));
            if (end < 0) end = v.Length;
        }

        void ReplaceLines(Func<string, int, string> f)
        {
            EnsureEditing();
            int a, e; SelectLines(out a, out e);
            var b = Cur.Box;
            var lines = b.Text.Substring(a, e - a).Split('\n');
            string outText = string.Join("\n", lines.Select((l, i) => f(l, i)));
            b.Select(a, e - a); b.SelectedText = outText; b.Select(a, outText.Length); b.Focus();
        }

        static readonly Regex ListPrefix = new Regex(@"^(\s*)([-*+] \[[ xX]\] |[-*+] |\d+\. |> )");

        void ToggleLinePrefix(string prefix)
        {
            EnsureEditing();
            int a, e; SelectLines(out a, out e);
            var lines = Cur.Box.Text.Substring(a, e - a).Split('\n');
            // Compare list kinds, not text: "- " must not match "- [ ] ", or bullet would remove a to-do list instead of converting it
            Func<string, string> kind = l =>
            {
                var m = ListPrefix.Match(l); string p = m.Groups[2].Value;
                return !m.Success ? "" : p.Contains("[") ? "task" : char.IsDigit(p[0]) ? "num" : p[0] == '>' ? "quote" : "bullet";
            };
            Func<string, bool> has = l => kind(l) == kind(prefix);
            bool all = lines.All(has);
            // Edit only each line's prefix, bottom-up so earlier offsets stay valid; the rest keeps its formatting
            var b = Cur.Box; var starts = new int[lines.Length]; int delta = 0;
            for (int i = 0, p = a; i < lines.Length; p += lines[i].Length + 1, i++) starts[i] = p;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var m = ListPrefix.Match(lines[i]);
                int at = starts[i] + (m.Success ? m.Groups[1].Length : 0), old = m.Success ? m.Groups[2].Length : 0;
                string np = all ? "" : prefix == "1. " ? (i + 1) + ". " : prefix;
                b.Select(at, old); b.SelectedText = np; delta += np.Length - old;
            }
            b.Select(a, e - a + delta); b.Focus();
        }

        void SetHeading(int n)
        {
            if (showMarks) { ReplaceLines((l, i) => (n > 0 ? new string('#', n) + " " : "") + Regex.Replace(l, @"^#{1,6}\s+", "")); return; }
            EnsureEditing(); Cur.Box.SetHeading(n); Changed(); Cur.Box.Focus();
        }

        void InsertLink()
        {
            EnsureEditing();
            var b = Cur.Box; string sel = b.SelectedText;
            using (var f = new Form { Text = "링크 삽입", FormBorderStyle = FormBorderStyle.FixedDialog, Size = new Size(360, 130), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Ui.Body })
            {
                var url = new TextBox { Left = 10, Top = 30, Width = 320, Text = "https://" };
                var ok = new Button { Text = "삽입", Left = 250, Top = 58, DialogResult = DialogResult.OK };
                f.Controls.AddRange(new Control[] { new Label { Text = "주소:", Left = 10, Top = 8 }, url, ok });
                f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK || url.Text.Trim().Length == 0) return;
                if (!showMarks) { b.AddLink(url.Text.Trim(), P); Changed(); b.Focus(); return; }
                string text = sel.Length > 0 ? sel : url.Text.Trim();
                int a = b.SelectionStart;
                b.SelectedText = "[" + text + "](" + url.Text.Trim() + ")";
                b.Select(a + 1, text.Length); b.Focus();
            }
        }

        void ClearFormatting()
        {
            EnsureEditing();
            var b = Cur.Box;
            if (b.SelectionLength == 0) { MessageBox.Show(this, "서식을 지울 텍스트를 선택하세요.", "tnote"); return; }
            if (!showMarks) { b.ClearFormat(P); Changed(); return; }
            string s = b.SelectedText;
            s = Regex.Replace(s, @"(?m)^#{1,6}\s+", "");
            s = Regex.Replace(s, @"(?m)^(\s*)([-*+] \[[ xX]\] |[-*+] |\d+\. |> )", "$1");
            s = Regex.Replace(s, @"\*\*(.+?)\*\*|~~(.+?)~~|\*(.+?)\*|`([^`]+)`", "$1$2$3$4");
            s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
            int a = b.SelectionStart; b.SelectedText = s; b.Select(a, s.Length);
        }

        void TogglePreview()
        {
            previewOn = !previewOn;
            previewBtn.Checked = previewOn;
            if (previewOn) ShowPreview();
            else { preview.Visible = false; Switch(Cur); }
        }

        void ShowPreview()
        {
            var d = Cur; if (d == null) return;
            foreach (var x in docs) x.Box.Visible = false;
            preview.Visible = true; preview.BringToFront();
            Func<Color, string> hex = c => string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            preview.DocumentText = "<!doctype html><meta charset=utf-8><meta http-equiv=X-UA-Compatible content=IE=edge><style>body{font:15px 'Malgun Gothic',sans-serif;line-height:1.6;color:" + hex(P.Text) + ";background:" + hex(P.Editor) +
                ";padding:0 8px;margin:0}code{font-family:Consolas,monospace;background:" + hex(P.Hover) + ";padding:1px 4px}pre{background:" + hex(P.Hover) + ";padding:8px}blockquote{margin:0;padding-left:10px;border-left:3px solid " + hex(P.Border) + ";opacity:.8}table{border-collapse:collapse}td,th{border:1px solid " + hex(P.MenuBorder) + ";padding:3px 8px}a{color:" + hex(P.Accent) + "}</style>" +
                MarkdownToHtml(showMarks ? d.Box.Text : d.Box.ToMarkdown());
        }

        static string Esc(string s) { return System.Net.WebUtility.HtmlEncode(s); }

        static string Inline(string s)
        {
            s = Esc(s);
            s = Regex.Replace(s, @"`([^`]+)`", "<code>$1</code>");
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)",
                mt => Regex.IsMatch(mt.Groups[2].Value, "^(https?:|mailto:)", RegexOptions.IgnoreCase)
                    ? "<a href=\"" + mt.Groups[2].Value + "\">" + mt.Groups[1].Value + "</a>" : mt.Groups[1].Value);
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            s = Regex.Replace(s, @"~~(.+?)~~", "<del>$1</del>");
            s = Regex.Replace(s, @"(^|[^*])\*([^*]+)\*", "$1<em>$2</em>");
            return s;
        }

        static string MarkdownToHtml(string src)
        {
            var lines = src.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            Match mt;
            Func<string, bool> isList = l => Regex.IsMatch(l, @"^\s*([-*+]|\d+\.) ");
            Func<string, bool> isRow = l => Regex.IsMatch(l, @"^\|.*\|\s*$");
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.StartsWith("```"))
                {
                    var buf = new List<string>();
                    while (++i < lines.Length && !lines[i].StartsWith("```")) buf.Add(lines[i]);
                    sb.Append("<pre><code>").Append(Esc(string.Join("\n", buf))).Append("</code></pre>");
                }
                else if ((mt = Regex.Match(l, @"^(#{1,6})\s+(.*)")).Success)
                    sb.AppendFormat("<h{0}>{1}</h{0}>", mt.Groups[1].Length, Inline(mt.Groups[2].Value));
                else if (Regex.IsMatch(l, @"^\s*(---|\*\*\*)\s*$")) sb.Append("<hr>");
                else if (isRow(l) && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\|[\s\-:|]+\|\s*$"))
                {
                    Func<string, string, string> row = (r, tag) => "<tr>" + string.Concat(r.Trim().Trim('|').Split('|').Select(c => "<" + tag + ">" + Inline(c.Trim()) + "</" + tag + ">")) + "</tr>";
                    sb.Append("<table>").Append(row(l, "th")); i++;
                    while (i + 1 < lines.Length && isRow(lines[i + 1])) sb.Append(row(lines[++i], "td"));
                    sb.Append("</table>");
                }
                else if (isList(l))
                {
                    bool ordered = Regex.IsMatch(l, @"^\s*\d+\.");
                    sb.Append(ordered ? "<ol>" : "<ul>");
                    while (i < lines.Length && isList(lines[i]))
                    {
                        string t = Regex.Replace(lines[i], @"^\s*([-*+]|\d+\.) ", "");
                        var task = Regex.Match(t, @"^\[([ xX])\] (.*)");
                        sb.Append("<li>").Append(task.Success ? (task.Groups[1].Value != " " ? "☑ " : "☐ ") + Inline(task.Groups[2].Value) : Inline(t)).Append("</li>");
                        i++;
                    }
                    i--; sb.Append(ordered ? "</ol>" : "</ul>");
                }
                else if (l.StartsWith(">"))
                {
                    var buf = new List<string>();
                    while (i < lines.Length && lines[i].StartsWith(">")) buf.Add(Inline(lines[i++].TrimStart('>').TrimStart()));
                    i--; sb.Append("<blockquote>").Append(string.Join("<br>", buf)).Append("</blockquote>");
                }
                else if (l.Trim().Length > 0) sb.Append("<p>").Append(Inline(l)).Append("</p>");
            }
            return sb.ToString();
        }

        // ---------- recent files ----------
        void LoadRecent()
        {
            try { if (File.Exists(SettingsPath)) recent = File.ReadAllLines(SettingsPath).Where(l => l.Length > 0).ToList(); } catch (IOException) { }
            RefreshRecentMenu();
        }

        void AddRecent(string path)
        {
            recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            recent.Insert(0, path);
            if (recent.Count > 10) recent.RemoveRange(10, recent.Count - 10);
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)); File.WriteAllLines(SettingsPath, recent); } catch (IOException) { }
            RefreshRecentMenu();
        }

        void RefreshRecentMenu()
        {
            recentMenu.DropDownItems.Clear();
            foreach (var p in recent) { var path = p; recentMenu.DropDownItems.Add(path, null, (s, e) => OpenFile(path)); }
            recentMenu.Enabled = recent.Count > 0;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
