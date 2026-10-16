// Notepad clone modeled on Windows 11 Notepad. Feature set comes from NotepadXamlUI.winmd
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

namespace NotepadClone
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
        public static readonly Font Body = new Font("Segoe UI Variable Text", 10.5f);
        public static readonly Font Small = new Font("Segoe UI Variable Text", 9f);
        public static Font Icons(float size) { return new Font("Segoe Fluent Icons", size); }
        public static readonly Font Icon10 = Icons(10f), Icon8 = Icons(7.5f), Icon7 = Icons(6f);
        public const string Add = "", Close = "", ChromeClose = "", Min = "", Max = "", Restore = "",
            Bold = "", Italic = "", Strike = "", Link = "", List = "", Table = "", Chevron = "",
            Settings = "", Eye = "", Clear = "", Page = "", Dot = "";

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
                TextRenderer.DrawText(g, d.Title, Ui.Body, tr, P.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
            int[] x = { 24, 104, Math.Max(200, w / 2 - 196), w - 388, w - 320, w - 152 };
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
        public enum K { Heading, Quote, Bold, Italic, Strike, Code, Dim, Accent, Link }
        public struct Span { public int A, N, Level; public K Kind; }

        static readonly Regex Head = new Regex(@"^(#{1,6}\s+)(.*)$"), Hr = new Regex(@"^\s*(---+|\*\*\*+|___+)\s*$"),
            Item = new Regex(@"^\s*([-*+]|\d+\.)( \[[ xX]\])? "), Sep = new Regex(@"^\s*\|[\s\-:|]+\|?\s*$"),
            Code = new Regex(@"`[^`]+`"), Link = new Regex(@"\[([^\]]+)\]\([^)\s]+\)"),
            Bold = new Regex(@"\*\*(?!\s).+?\*\*"), Strike = new Regex(@"~~(?!\s).+?~~"), Italic = new Regex(@"(?<![*\w])\*(?![\s*])[^*]+?\*(?!\*)");

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
                    Add(r, a, m.Groups[1].Length, K.Dim);
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
                Add(r, a + m.Index, m.Length, K.Code); Add(r, a + m.Index, 1, K.Dim); Add(r, a + m.Index + m.Length - 1, 1, K.Dim);
                for (int j = m.Index; j < m.Index + m.Length; j++) masked[j] = ' ';
            }
            string t = new string(masked);
            foreach (Match m in Link.Matches(t)) { Add(r, a + m.Index, m.Length, K.Dim); Add(r, a + m.Groups[1].Index, m.Groups[1].Length, K.Link); }
            Pair(r, a, t, Bold, 2, K.Bold);
            Pair(r, a, t, Strike, 2, K.Strike);
            Pair(r, a, t, Italic, 1, K.Italic);
        }

        static void Pair(List<Span> r, int a, string t, Regex re, int mark, K k)
        {
            foreach (Match m in re.Matches(t))
            {
                Add(r, a + m.Index, m.Length, k);
                Add(r, a + m.Index, mark, K.Dim); Add(r, a + m.Index + m.Length - mark, mark, K.Dim);
            }
        }

        static void Add(List<Span> r, int a, int n, K k, int level = 0) { if (n > 0) r.Add(new Span { A = a, N = n, Kind = k, Level = level }); }
    }

    // Plain-text editor that shows Markdown formatting in place. Styling goes through the
    // Text Object Model with undo suspended, so it never dirties the document or the undo stack.
    class MdEditor : RichTextBox
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string name);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, out IntPtr l);
        const int EM_GETOLEINTERFACE = 0x0400 + 60, tomTrue = -1, tomSuspend = -9999995, tomResume = -9999994;
        static readonly float[] HeadScale = { 1f, 1.7f, 1.45f, 1.25f, 1.12f, 1.05f, 1f };
        public const int MaxStyledLength = 300000;

        public bool Styling;
        dynamic tom;

        public MdEditor() { Multiline = true; DetectUrls = false; AutoWordSelection = false; }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams; // RichEdit 4.1+ supports suspending undo
                if (LoadLibrary("msftedit.dll") != IntPtr.Zero) cp.ClassName = "RICHEDIT50W";
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) { tom = null; base.OnHandleCreated(e); }

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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert)) { PastePlain(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
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

        public void Restyle(bool on, Palette P)
        {
            string s = Text;
            if (s.Length > MaxStyledLength) on = false;
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
                    f.Name = Font.Name; f.Size = size; f.ForeColor = Rgb(P.Text); f.BackColor = Rgb(P.Editor);
                    if (!on) return;
                    int dim = Rgb(Mix(P.Text, P.Editor, 0.55f));
                    foreach (var sp in MdSpans.Parse(s))
                    {
                        f = doc.Range(sp.A, sp.A + sp.N).Font;
                        switch (sp.Kind)
                        {
                            case MdSpans.K.Heading: f.Bold = tomTrue; f.Size = size * HeadScale[sp.Level]; break;
                            case MdSpans.K.Quote: f.Italic = tomTrue; f.ForeColor = Rgb(P.Muted); break;
                            case MdSpans.K.Bold: f.Bold = tomTrue; break;
                            case MdSpans.K.Italic: f.Italic = tomTrue; break;
                            case MdSpans.K.Strike: f.StrikeThrough = tomTrue; break;
                            case MdSpans.K.Code: f.Name = "Consolas"; f.BackColor = Rgb(P.Hover); break;
                            case MdSpans.K.Dim: f.ForeColor = dim; break;
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
    }

    class MainForm : Form
    {
        readonly TabStrip strip = new TabStrip();
        readonly StatusPanel status = new StatusPanel();
        readonly Panel editorHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 0, 0) };
        readonly List<TabDoc> docs = new List<TabDoc>();
        readonly ToolStripMenuItem recentMenu = new ToolStripMenuItem("최근 파일");
        MenuStrip menu;
        ToolStripMenuItem wrapItem, statusItem, darkItem, previewBtn, liveItem;
        readonly List<ToolStripItem> iconItems = new List<ToolStripItem>();
        Font editorFont = new Font("Consolas", 11f);
        float zoom = 1f;
        bool wordWrap = true, dark, previewOn, liveFormat = true;
        readonly Timer restyleTimer = new Timer { Interval = 150 };
        Palette P = Palette.Light;
        TabDoc active;
        readonly WebBrowser preview = new WebBrowser { Dock = DockStyle.Fill, IsWebBrowserContextMenuEnabled = false, AllowWebBrowserDrop = false, Visible = false };
        static readonly string SettingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadClone", "recent.txt");
        List<string> recent = new List<string>();
        string lastFind = "";
        bool matchCase;

        public MainForm(string[] args)
        {
            Text = "메모장";
            Size = new Size(1000, 640);
            MinimumSize = new Size(480, 300);
            KeyPreview = true;
            DoubleBuffered = true;
            Font = Ui.Body;
            Padding = new Padding(1, 0, 1, 1); // edge strips the form hit-tests as resize borders

            strip.Docs = () => docs; strip.Active = () => active; strip.Select = Switch; strip.CloseDoc = CloseTab; strip.NewTab = () => NewTab();
            menu = BuildMenu();
            MainMenuStrip = menu;
            editorHost.Controls.Add(preview);
            Controls.Add(editorHost);
            Controls.Add(status);
            Controls.Add(menu);
            Controls.Add(strip);
            LoadRecent();

            if (args.Length > 0) foreach (var a in args) OpenFile(a);
            if (docs.Count == 0) NewTab();
            restyleTimer.Tick += (s, e) =>
            {
                if (Cur != null && Cur.Box.Composing) return; // try again on the next tick, once the syllable is done
                restyleTimer.Stop(); if (Cur != null) { Restyle(Cur); UpdateStatus(); }
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
            edit.DropDownItems.Add(Item("잘라내기", Keys.Control | Keys.X, (s, e) => Cur.Box.Cut()));
            edit.DropDownItems.Add(Item("복사", Keys.Control | Keys.C, (s, e) => Cur.Box.Copy()));
            edit.DropDownItems.Add(Item("붙여넣기", Keys.Control | Keys.V, (s, e) => Cur.Box.PastePlain()));
            // Shown as Del but not bound: a bound Delete shortcut reaches the menu before the RichTextBox,
            // so the key would only ever clear a selection and never delete the next character.
            var del = Item("삭제", Keys.None, (s, e) => Cur.Box.SelectedText = "");
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
            statusItem = Item("상태 표시줄", Keys.None, (s, e) => { status.Visible = !status.Visible; statusItem.Checked = status.Visible; });
            statusItem.Checked = true;
            wrapItem = Item("자동 줄 바꿈", Keys.None, (s, e) => SetWrap(!wordWrap));
            wrapItem.Checked = wordWrap;
            darkItem = Item("어두운 테마", Keys.None, (s, e) => SetDark(!dark));
            liveItem = Item("서식 표시", Keys.None, (s, e) => { liveFormat = !liveFormat; liveItem.Checked = liveFormat; RestyleAll(); });
            liveItem.Checked = liveFormat;
            previewBtn = Item("서식 미리 보기", Keys.Control | Keys.Shift | Keys.M, (s, e) => TogglePreview());
            view.DropDownItems.AddRange(new ToolStripItem[] { statusItem, wrapItem, darkItem, new ToolStripSeparator(), liveItem, previewBtn });

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
                using (var b = new SolidBrush(P.Text))
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
            table.DropDownItems.Add(Item("인라인 코드", Keys.None, (s, e) => WrapSel("`", "`")));
            table.DropDownItems.Add(Item("코드 블록", Keys.None, (s, e) => WrapSel("```\r\n", "\r\n```")));
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

            m.Items.AddRange(new ToolStripItem[] {
                formatSpacer, head, list,
                IconBtn(Ui.Bold, "굵게 (Ctrl+B)", (s, e) => WrapSel("**", "**")),
                IconBtn(Ui.Italic, "기울임꼴 (Ctrl+I)", (s, e) => WrapSel("*", "*")),
                IconBtn(Ui.Strike, "취소선", (s, e) => WrapSel("~~", "~~")),
                IconBtn(Ui.Link, "링크 (Ctrl+K)", (s, e) => InsertLink()),
                table,
                IconBtn(Ui.Clear, "서식 지우기", (s, e) => ClearFormatting()),
                settings, eye });
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
                if (keyData == (Keys.Control | Keys.B)) { WrapSel("**", "**"); return true; }
                if (keyData == (Keys.Control | Keys.I)) { WrapSel("*", "*"); return true; }
                if (keyData == (Keys.Control | Keys.K)) { InsertLink(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- tabs ----------
        TabDoc NewTab()
        {
            var d = new TabDoc();
            d.Box = new MdEditor
            {
                Dock = DockStyle.Fill, ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both, WordWrap = wordWrap,
                AcceptsTab = true, HideSelection = false, BorderStyle = BorderStyle.None,
                Font = ZoomedFont(), BackColor = P.Editor, ForeColor = P.Text, Visible = false
            };
            d.Box.TextChanged += (s, e) =>
            {
                if (d.Box.Styling) return;
                if (!d.Dirty) { d.Dirty = true; strip.Invalidate(); }
                UpdateStatus(); restyleTimer.Stop(); restyleTimer.Start();
            };
            d.Box.HandleCreated += (s, e) => Restyle(d);
            d.Box.KeyUp += (s, e) => UpdateStatus();
            d.Box.MouseUp += (s, e) => UpdateStatus();
            d.Box.HandleCreated += (s, e) => SetWindowTheme(d.Box.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            d.Box.MouseWheel += (s, e) => { if (ModifierKeys == Keys.Control) SetZoom(zoom + (e.Delta > 0 ? 0.1f : -0.1f)); };
            docs.Add(d);
            editorHost.Controls.Add(d.Box);
            Switch(d);
            return d;
        }

        void Switch(TabDoc d)
        {
            active = d;
            foreach (var x in docs) x.Box.Visible = x == d && !previewOn;
            if (previewOn) ShowPreview(); else d.Box.Focus();
            UpdateTitle(); UpdateStatus(); strip.Invalidate();
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
            if (!File.Exists(path)) { MessageBox.Show(this, path + " 파일을 찾을 수 없습니다.", "메모장"); return; }
            var bytes = File.ReadAllBytes(path);
            var enc = DetectEncoding(bytes);
            string text = enc.GetString(bytes);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            string eol = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : text.Contains("\r") ? "\r" : "\r\n";

            var d = Cur != null && Cur.Path == null && !Cur.Dirty && Cur.Box.TextLength == 0 ? Cur : NewTab();
            d.Path = path; d.Enc = enc; d.Eol = eol;
            d.Box.Text = text.Replace("\r\n", "\n").Replace("\r", "\n");
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

        bool Save(TabDoc d, bool saveAs)
        {
            if (d == null) return false;
            if (saveAs || d.Path == null)
            {
                using (var dlg = new SaveFileDialog { Filter = "텍스트 문서 (*.txt)|*.txt|Markdown (*.md)|*.md|모든 파일 (*.*)|*.*", FileName = d.Path == null ? "*.txt" : d.Title })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    d.Path = dlg.FileName;
                }
            }
            File.WriteAllText(d.Path, d.Box.Text.Replace("\r\n", "\n").Replace("\n", d.Eol), d.Enc);
            d.Dirty = false;
            strip.Invalidate(); UpdateTitle(); UpdateStatus();
            AddRecent(d.Path);
            return true;
        }

        bool ConfirmDiscard(TabDoc d)
        {
            if (!d.Dirty) return true;
            Switch(d);
            var r = MessageBox.Show(this, d.Title + "의 변경 내용을 저장하시겠습니까?", "메모장", MessageBoxButtons.YesNoCancel);
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

        void UpdateTitle() { if (Cur != null) Text = Cur.Title + " - 메모장"; }

        void UpdateStatus()
        {
            var d = Cur;
            if (d == null) return;
            if (d.Box.Composing) { restyleTimer.Stop(); restyleTimer.Start(); return; } // the timer updates it once the syllable is done
            int sel = d.Box.SelectionStart, line = d.Box.GetLineFromCharIndex(sel);
            int col = sel - d.Box.GetFirstCharIndexFromLine(line);
            status.Pos = string.Format("줄 {0}, 열 {1}", line + 1, col + 1);
            status.Chars = d.Box.SelectionLength > 0
                ? string.Format("{0}/{1}자", d.Box.SelectionLength, d.Box.TextLength)
                : d.Box.TextLength + "자";
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
                sb.Append(src, i, src.Length - i);
                Cur.Box.SelectAll(); Cur.Box.SelectedText = sb.ToString();
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
            if (idx < 0) { MessageBox.Show(this, "\"" + what + "\"을(를) 찾을 수 없습니다.", "메모장"); return; }
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
                if (dlg.ShowDialog(this) == DialogResult.OK) { editorFont = dlg.Font; SetZoom(zoom); }
        }

        Font ZoomedFont() { return new Font(editorFont.FontFamily, editorFont.Size * zoom, editorFont.Style); }

        void SetZoom(float z)
        {
            zoom = Math.Max(0.1f, Math.Min(5f, z));
            foreach (var d in docs) { d.Box.Font = ZoomedFont(); Restyle(d); }
            UpdateStatus();
        }

        void SetWrap(bool on)
        {
            wordWrap = on; wrapItem.Checked = on;
            foreach (var d in docs) { d.Box.WordWrap = on; d.Box.ScrollBars = on ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both; }
        }

        void SetDark(bool on) { dark = on; darkItem.Checked = on; ApplyTheme(); }

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

        // ---------- live formatting ----------
        void Restyle(TabDoc d) { if (d.Box.IsHandleCreated) d.Box.Restyle(liveFormat, P); }
        void RestyleAll() { foreach (var d in docs) Restyle(d); }

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

        void WrapSel(string open, string close)
        {
            EnsureEditing();
            var b = Cur.Box; string v = b.Text; int a = b.SelectionStart, len = b.SelectionLength; string sel = b.SelectedText;
            char ch = open[0]; int m = open.Length;
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
            ReplaceLines((l, i) => all ? ListPrefix.Replace(l, "$1") : (prefix == "1. " ? (i + 1) + ". " : prefix) + ListPrefix.Replace(l, "$1"));
        }

        void SetHeading(int n)
        {
            ReplaceLines((l, i) => (n > 0 ? new string('#', n) + " " : "") + Regex.Replace(l, @"^#{1,6}\s+", ""));
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
            if (b.SelectionLength == 0) { MessageBox.Show(this, "서식을 지울 텍스트를 선택하세요.", "메모장"); return; }
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
            preview.DocumentText = "<!doctype html><meta charset=utf-8><meta http-equiv=X-UA-Compatible content=IE=edge><style>body{font:15px 'Segoe UI Variable Text','Segoe UI','Malgun Gothic',sans-serif;line-height:1.6;color:" + hex(P.Text) + ";background:" + hex(P.Editor) +
                ";padding:0 8px;margin:0}code{font-family:Consolas,monospace;background:" + hex(P.Hover) + ";padding:1px 4px}pre{background:" + hex(P.Hover) + ";padding:8px}blockquote{margin:0;padding-left:10px;border-left:3px solid " + hex(P.Border) + ";opacity:.8}table{border-collapse:collapse}td,th{border:1px solid " + hex(P.MenuBorder) + ";padding:3px 8px}a{color:" + hex(P.Accent) + "}</style>" +
                MarkdownToHtml(d.Box.Text);
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
