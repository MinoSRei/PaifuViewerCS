using System;
using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>天鳳6形式の 1 局分のログを、1 手ごとの局面 (Step) の列に展開する。</summary>
    public class RoundBuilder
    {
        class Seat
        {
            public List<int> H;
            public List<object> Dr, Ds;
            public int A, B;
            public int? Dw;
            public List<River> Rv = new List<River>();
            public List<Meld> Ml = new List<Meld>();
            public bool Ri;
        }

        class Parsed
        {
            public char L;
            public List<int> T;
            public int Ci;
        }

        static readonly char[] CallLetters = "cpmak".ToCharArray();

        readonly string[] names;
        readonly int dealer;
        readonly List<int> dora;
        readonly Seat[] ps = new Seat[4];
        readonly int[] sc;
        readonly List<Step> S = new List<Step>();
        readonly List<int> LW = new List<int>(), RW = new List<int>();
        int dn = 1, wall = 70, last = -1, ld, rd;
        bool pend, rin;

        RoundBuilder(List<object> k, string[] names)
        {
            this.names = names;
            dealer = J.I(J.L(k[0])[0]) % 4;
            dora = J.Ints(k[2]);
            for (int q = 0; q < 4; q++)
                ps[q] = new Seat { H = J.Ints(k[4 + 3 * q]), Dr = J.L(k[5 + 3 * q]), Ds = J.L(k[6 + 3 * q]) };
            sc = J.Ints(k[1]).ToArray();
        }

        /// <param name="paishan">雀魂の牌山 136 枚（拡張キー "paishan"）。なければ null。</param>
        public static RoundData Build(List<object> k, string[] names, List<int> paishan = null)
        {
            if (k.Count < 17) throw new FormatException("局データの要素数が不足しています");
            var b = new RoundBuilder(k, names);
            b.Run(k);
            foreach (var s in b.S) s.Jn = s.CalcJn();
            var rd = new RoundData { Steps = b.S, LW = b.LW, RW = b.RW };
            WallAligner.Apply(rd, paishan);
            return rd;
        }

        public static string CallName(char l)
        {
            return l == 'c' ? "チー" : l == 'p' ? "ポン" : l == 'm' ? "大明槓" : "";
        }

        string Nm(int q) { return Tiles.WD[(q - dealer + 4) % 4] + "家 " + J.Name(names, q); }

        void Snap(string msg, int t)
        {
            string ph = t < 0 ? "e"
                : msg.Contains(" ツモ ") || msg.Contains("チー") || msg.Contains("ポン") ? "d"
                : msg.Contains(" 打 ") ? "c" : "w";
            S.Add(new Step
            {
                Msg = msg, T = t, Rin = msg.Contains("槓"), Ld = ld, Rd = rd, Ph = ph,
                Sc = (int[])sc.Clone(), Dn = Math.Min(dn, dora.Count), Wall = wall, Dealer = dealer,
                Pl = ps.Select(p =>
                {
                    var h = new List<int>(p.H);
                    if (p.Dw.HasValue) Tiles.Rm(h, p.Dw.Value);
                    Tiles.Sort(h);
                    return new PlayerState
                    {
                        H = h, Dw = p.Dw, Ri = p.Ri,
                        Rv = p.Rv.Select(r => r.Clone()).ToList(),
                        Ml = p.Ml.Select(m => m.Clone()).ToList()
                    };
                }).ToArray()
            });
        }

        static Parsed ParseCall(string s)
        {
            int x = s.IndexOfAny(CallLetters);
            string rest = s.Remove(x, 1);
            var t = new List<int>();
            for (int j = 0; j + 1 < rest.Length; j += 2) t.Add(int.Parse(rest.Substring(j, 2)));
            return new Parsed { L = s[x], T = t, Ci = x / 2 };
        }

        char DoMeld(int q, string s)
        {
            var z = ParseCall(s);
            var p = ps[q];
            for (int j = 0; j < z.T.Count; j++) if (j != z.Ci) Tiles.Rm(p.H, z.T[j]);
            if (last >= 0 && ps[last].Rv.Count > 0) ps[last].Rv[ps[last].Rv.Count - 1].X = true;
            p.Ml.Add(new Meld { L = z.L, T = z.T, Ci = z.Ci });
            if (z.L == 'm') { pend = true; rin = true; }
            Snap(Nm(q) + " " + CallName(z.L) + " " + Tiles.Tn(z.T[z.Ci]), q);
            return z.L;
        }

        void DoKan(int q, string s)
        {
            var z = ParseCall(s);
            var p = ps[q];
            rin = true;
            if (z.L == 'a')
            {
                foreach (int c in z.T) Tiles.Rm(p.H, c);
                p.Dw = null;
                p.Ml.Add(new Meld { L = z.L, T = z.T, Ci = z.Ci });
                dn++;
            }
            else
            {
                Tiles.Rm(p.H, z.T[z.Ci]);
                if (p.Dw == z.T[z.Ci]) p.Dw = null;
                int j = p.Ml.FindIndex(m => m.L == 'p' && Tiles.Norm(m.T[0]) == Tiles.Norm(z.T[0]));
                var km = new Meld { L = 'k', T = z.T, Ci = z.Ci };
                if (j >= 0) p.Ml[j] = km; else p.Ml.Add(km);
                pend = true;
            }
            Snap(Nm(q) + " " + (z.L == 'a' ? "暗槓" : "加槓") + " " + Tiles.Tn(z.T[z.Ci]), q);
        }

        void Run(List<object> k)
        {
            Snap("配牌", dealer);
            int cur = dealer, g = 0;
            string call = null;
            bool need = true;
            while (g++ < 600)
            {
                var p = ps[cur];
                if (call != null)
                {
                    char l = DoMeld(cur, call);
                    call = null;
                    need = l == 'm';
                    if (l == 'm') p.B++;
                }
                if (need)
                {
                    if (p.A >= p.Dr.Count || !(p.Dr[p.A] is double)) break;
                    int d = J.I(p.Dr[p.A++]);
                    p.H.Add(d);
                    p.Dw = d;
                    wall--;
                    bool fromRin = rin;
                    if (rin) { RW.Add(d); rd++; } else { LW.Add(d); ld++; }
                    rin = false;
                    Snap(Nm(cur) + " ツモ " + Tiles.Tn(d), cur);
                    S[S.Count - 1].Rs = fromRin;
                }
                need = true;
                if (p.B >= p.Ds.Count) break;
                object x = p.Ds[p.B++];
                string xs = x as string;
                if (xs != null && xs.IndexOfAny(new[] { 'a', 'k' }) >= 0) { DoKan(cur, xs); continue; }
                if (xs == null && J.I(x) == 0) continue;

                bool ri = false;
                int v;
                if (xs != null)
                {
                    ri = xs[0] == 'r';
                    if (!int.TryParse(xs.Substring(1), out v)) break;
                }
                else v = J.I(x);
                bool tg = v == 60;
                if (tg && !p.Dw.HasValue) break;
                int tl = tg ? p.Dw.Value : v;
                Tiles.Rm(p.H, tl);
                p.Dw = null;
                if (ri) { p.Ri = true; sc[cur] -= 1000; }
                if (pend) { dn++; pend = false; }
                p.Rv.Add(new River { C = tl, R = ri, G = tg });
                last = cur;
                Snap(Nm(cur) + " 打 " + Tiles.Tn(tl) + (ri ? "（立直）" : ""), cur);

                // ポン・大明槓を優先し、次にチーを、下家から順に探す
                call = null;
                foreach (string re in new[] { "pm", "c" })
                {
                    for (int o = 1; o < 4 && call == null; o++)
                    {
                        int q = (cur + o) % 4;
                        var sp = ps[q];
                        string s = sp.A < sp.Dr.Count ? sp.Dr[sp.A] as string : null;
                        if (s == null || s.IndexOfAny(re.ToCharArray()) < 0) continue;
                        var z = ParseCall(s);
                        if (Tiles.Norm(z.T[z.Ci]) == Tiles.Norm(tl)) { call = s; sp.A++; cur = q; }
                    }
                }
                if (call == null) cur = (cur + 1) % 4;
            }

            var R = J.L(k[k.Count - 1]);
            string msg = R.Count > 0 ? J.S(R[0]) : "";
            if (msg == "和了")
            {
                var outp = new List<string>();
                for (int j = 1; j + 1 < R.Count; j += 2)
                {
                    var d = J.Ints(R[j]);
                    for (int a = 0; a < d.Count && a < sc.Length; a++) sc[a] += d[a];
                    var h = J.L(R[j + 1]);
                    int w = J.I(h[0]), f = J.I(h[1]);
                    outp.Add(Nm(w) + " " + (w == f ? "ツモ" : "ロン（" + Nm(f) + "）") + " " + J.S(h[3]) + "\n"
                             + string.Join("、", h.Skip(4).Select(y => J.S(y))));
                }
                msg = "和了\n" + string.Join("\n", outp);
            }
            else if (R.Count > 1 && R[1] is List<object>)
            {
                var d = J.Ints(R[1]);
                for (int a = 0; a < d.Count && a < sc.Length; a++) sc[a] += d[a];
            }
            Snap(msg, -1);
        }
    }
}
