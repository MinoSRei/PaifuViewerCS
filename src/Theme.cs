using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PaifuViewer
{
    /// <summary>配色とフォント。元 HTML の CSS 変数（ライトテーマ）に対応。</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("#e9f1ee"), Hdr = Hex("#dcebe6"), Side = Hex("#eaf3f0"), Felt = Hex("#cfe6da"),
            Box = Hex("#f6fbf9"), Line = Hex("#b2c9c1"), Fg = Hex("#17302b"), Acc = Hex("#12805f");

        public static readonly Color Table = Hex("#2f7a5f"), TableEdge = Hex("#24604b"), AccSoft = Hex("#cdeee1"),
            AccHover = Hex("#1aa57c"), TileBack = Hex("#e3a33b"), TileBackEdge = Hex("#b97c1f");

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

        public static GraphicsPath Rounded(RectangleF r, float rad)
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

    /// <summary>ちらつき防止付きの描画用パネル。</summary>
    public class Canvas : Panel
    {
        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>
    /// 麻雀王国の牌画像（tiles フォルダ）を読み込む。赤5 は配布がないため 5 の画像を赤く着色して作る。
    /// </summary>
    public static class TileImages
    {
        static readonly string[] Honors = { "ji_e", "ji_s", "ji_w", "ji_n", "no", "ji_h", "ji_c" };
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        static readonly string dir = FindDir();

        static string FindDir()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var d in new[] { Path.Combine(b, "tiles"), Path.Combine(b, @"..\tiles"), Path.Combine(b, @"..\..\..\tiles"), "tiles" })
                if (File.Exists(Path.Combine(d, "p_ms1_1.gif"))) return Path.GetFullPath(d);
            return null;
        }

        static string BaseName(int n)
        {
            int s = n / 10, k = n % 10;
            return s == 4 ? "p_" + Honors[k - 1] : "p_" + "mps"[s - 1] + "s" + k;
        }

        /// <param name="side">true なら横向き（左倒し）の画像。</param>
        public static Bitmap Get(int c, bool side)
        {
            if (dir == null) return null;
            string key = c + (side ? "s" : "u");
            Bitmap im;
            if (cache.TryGetValue(key, out im)) return im;
            string path = Path.Combine(dir, BaseName(Tiles.Norm(c)) + (side ? "_3" : "_1") + ".gif");
            im = null;
            if (File.Exists(path))
            {
                using (var src = Image.FromFile(path)) im = new Bitmap(src);
                if (c > 50) MakeRed(im);
            }
            cache[key] = im;
            return im;
        }

        /// <summary>黒〜灰色の図柄を赤に置き換える（赤ドラ用）。</summary>
        static void MakeRed(Bitmap b)
        {
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    var p = b.GetPixel(x, y);
                    if (p.A < 128 || (p.R > p.G + 40 && p.R > p.B + 40)) continue;
                    int mx = Math.Max(p.R, Math.Max(p.G, p.B)), mn = Math.Min(p.R, Math.Min(p.G, p.B)), l = (p.R + p.G + p.B) / 3;
                    if (l >= 140 || mx - mn > 70) continue;
                    b.SetPixel(x, y, Color.FromArgb(p.A, 175 + l * 70 / 140, l * 35 / 100, l * 35 / 100));
                }
        }
    }

    /// <summary>牌 1 枚の描画。画像がない場合は文字で描く。</summary>
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

        /// <param name="r">牌の外形。side=true の場合は横向き（幅&gt;高さ）の外形。</param>
        /// <param name="dim">半透明（鳴かれた牌・選べない牌）。</param>
        /// <param name="grey">暗く表示（ツモ切り）。</param>
        /// <param name="mark">アクセント色の枠（ホバー・対象牌）。</param>
        public static void Draw(Graphics g, int c, RectangleF r, bool dim = false, bool grey = false, bool mark = false, bool side = false)
        {
            var im = TileImages.Get(c, side);
            if (im != null)
            {
                if (dim || grey)
                {
                    float k = grey ? 0.8f : 1f;
                    var cm = new ColorMatrix { Matrix00 = k, Matrix11 = k, Matrix22 = k, Matrix33 = dim ? 0.4f : 1f };
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(im, new[] { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom) },
                            new RectangleF(0, 0, im.Width, im.Height), GraphicsUnit.Pixel, ia);
                    }
                }
                else g.DrawImage(im, r);
            }
            else if (side)
            {
                var st = g.Save();
                g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
                g.RotateTransform(-90);
                DrawText(g, c, new RectangleF(-r.Height / 2, -r.Width / 2, r.Height, r.Width), dim, grey);
                g.Restore(st);
            }
            else DrawText(g, c, r, dim, grey);

            if (mark)
            {
                using (var pen = new Pen(Theme.Acc, 3f))
                using (var path = Theme.Rounded(r, 4))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>伏せ牌。</summary>
        public static void DrawBack(Graphics g, RectangleF r)
        {
            using (var path = Theme.Rounded(r, 4))
            using (var b = new SolidBrush(Theme.TileBack))
            using (var pen = new Pen(Theme.TileBackEdge, 1.5f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
        }

        static void DrawText(Graphics g, int c, RectangleF r, bool dim, bool grey)
        {
            int al = dim ? 100 : 255;
            float fs = r.Width * 0.5f;
            using (var path = Theme.Rounded(r, 3))
            {
                using (var b = new SolidBrush(Color.FromArgb(al, grey ? Theme.TileGrey : Theme.TileBg))) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(al, Theme.TileBorder), 1f)) g.DrawPath(pen, path);
            }
            int n = Tiles.Norm(c), s = n / 10;
            if (s == 4)
            {
                using (var b = new SolidBrush(Color.FromArgb(al, Theme.Ink)))
                    g.DrawString(Tiles.H7[n - 41], Theme.Px(fs, FontStyle.Bold), b, r, TightCenter);
                return;
            }
            var col = c > 50 ? Theme.Aka : s == 1 ? Theme.S1 : s == 2 ? Theme.S2 : Theme.S3;
            float sfs = fs * 0.62f, top = r.Y + (r.Height - fs * 1.05f - sfs * 1.05f) / 2;
            using (var b = new SolidBrush(Color.FromArgb(al, col)))
            {
                g.DrawString((n % 10).ToString(), Theme.Px(fs, FontStyle.Bold), b, new RectangleF(r.X - 4, top, r.Width + 8, fs * 1.05f), TightCenter);
                g.DrawString("萬筒索"[s - 1].ToString(), Theme.Px(sfs), b, new RectangleF(r.X - 4, top + fs * 1.05f, r.Width + 8, sfs * 1.05f), TightCenter);
            }
        }
    }
}
