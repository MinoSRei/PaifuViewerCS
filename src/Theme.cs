using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PaifuViewer
{
    /// <summary>配色とフォント。元 HTML の CSS 変数（ライトテーマ）に対応。</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("#e9f1ee"), Hdr = Hex("#dcebe6"), Side = Hex("#eaf3f0"), Felt = Hex("#cfe6da"),
            Box = Hex("#f6fbf9"), Line = Hex("#b2c9c1"), Fg = Hex("#17302b"), Acc = Hex("#12805f");

        public static readonly Color TileBg = Hex("#f4efe6"), TileGrey = Hex("#cfc8b8"), TileBorder = Hex("#c9c1b0"),
            Ink = Hex("#222222"), S1 = Hex("#b3261e"), S2 = Hex("#1b57b8"), S3 = Hex("#17743f"), Aka = Hex("#dd3333");

        static Color Hex(string h) { return ColorTranslator.FromHtml(h); }

        public static readonly string Family = PickFamily();
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        static string PickFamily()
        {
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name));
            foreach (var n in new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic" })
                if (installed.Contains(n)) return n;
            return SystemFonts.DefaultFont.FontFamily.Name;
        }

        static Font Get(float size, FontStyle st, GraphicsUnit unit)
        {
            string key = size.ToString("F2") + "/" + (int)st + "/" + (int)unit;
            Font f;
            if (!cache.TryGetValue(key, out f)) cache[key] = f = new Font(Family, size, st, unit);
            return f;
        }

        /// <summary>描画用（ピクセル指定）。</summary>
        public static Font Px(float px, FontStyle st = FontStyle.Regular) { return Get(px, st, GraphicsUnit.Pixel); }

        /// <summary>コントロール用（ポイント指定）。</summary>
        public static Font Ui(float pt, FontStyle st = FontStyle.Regular) { return Get(pt, st, GraphicsUnit.Point); }
    }

    /// <summary>ちらつき防止付きの描画用パネル。</summary>
    public class Canvas : Panel
    {
        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>牌 1 枚の描画（元 HTML の .t 要素に相当）。</summary>
    public static class TileRenderer
    {
        public static readonly StringFormat CenterFormat = MakeFormat(false);
        static readonly StringFormat TightCenter = MakeFormat(true);

        static StringFormat MakeFormat(bool typographic)
        {
            var f = typographic ? (StringFormat)StringFormat.GenericTypographic.Clone() : new StringFormat();
            f.Alignment = StringAlignment.Center;
            f.LineAlignment = StringAlignment.Center;
            // LineLimit が付いていると行高より低い枠では文字が描かれないため外す
            f.FormatFlags = (f.FormatFlags | StringFormatFlags.NoClip | StringFormatFlags.NoWrap) & ~StringFormatFlags.LineLimit;
            return f;
        }

        /// <param name="r">牌の外形。rotated=true の場合は横向き（幅&gt;高さ）の外形を渡す。</param>
        /// <param name="fs">数字のフォントサイズ(px)。</param>
        /// <param name="dim">半透明（鳴かれた牌）。</param>
        /// <param name="grey">灰色背景（ツモ切り）。</param>
        /// <param name="mark">アクセント色の枠（立直宣言牌・ホバー）。</param>
        public static void Draw(Graphics g, int c, RectangleF r, float fs, bool dim = false, bool grey = false,
            bool mark = false, bool sharp = false, bool rotated = false)
        {
            if (!rotated) { DrawUpright(g, c, r, fs, dim, grey, mark, sharp); return; }
            var st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(90);
            DrawUpright(g, c, new RectangleF(-r.Height / 2, -r.Width / 2, r.Height, r.Width), fs, dim, grey, mark, sharp);
            g.Restore(st);
        }

        static void DrawUpright(Graphics g, int c, RectangleF r, float fs, bool dim, bool grey, bool mark, bool sharp)
        {
            int al = dim ? 89 : 255;
            using (var path = Rounded(r, sharp ? 0 : 3))
            {
                using (var b = new SolidBrush(Color.FromArgb(al, grey ? Theme.TileGrey : Theme.TileBg))) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(al, Theme.TileBorder), 1f)) g.DrawPath(pen, path);
            }
            if (c > 50)
            {
                using (var pen = new Pen(Color.FromArgb(al, Theme.Aka), 2f))
                {
                    var ir = r;
                    ir.Inflate(-2f, -2f);
                    g.DrawRectangle(pen, ir.X, ir.Y, ir.Width, ir.Height);
                }
            }

            int n = Tiles.Norm(c), s = n / 10;
            if (s == 4)
            {
                using (var b = new SolidBrush(Color.FromArgb(al, Theme.Ink)))
                    g.DrawString(Tiles.H7[n - 41], Theme.Px(fs, FontStyle.Bold), b, r, TightCenter);
            }
            else
            {
                var col = s == 1 ? Theme.S1 : s == 2 ? Theme.S2 : Theme.S3;
                float sfs = fs * 0.62f, total = fs * 1.05f + sfs * 1.05f, top = r.Y + (r.Height - total) / 2;
                using (var b = new SolidBrush(Color.FromArgb(al, col)))
                {
                    g.DrawString((n % 10).ToString(), Theme.Px(fs, FontStyle.Bold), b,
                        new RectangleF(r.X - 4, top, r.Width + 8, fs * 1.05f), TightCenter);
                    g.DrawString("萬筒索"[s - 1].ToString(), Theme.Px(sfs), b,
                        new RectangleF(r.X - 4, top + fs * 1.05f, r.Width + 8, sfs * 1.05f), TightCenter);
                }
            }

            if (mark)
            {
                using (var pen = new Pen(Theme.Acc, 2f))
                {
                    var o = r;
                    o.Inflate(1f, 1f);
                    g.DrawRectangle(pen, o.X, o.Y, o.Width, o.Height);
                }
            }
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
