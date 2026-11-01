using System;
using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>卓上に出す操作ボタン。Q はボタンを置く家（-1 は卓中央）。</summary>
    public class BoardButton
    {
        public int Q;
        public string Label;
        public Action Act;
    }

    /// <summary>分岐中の局面で選べる操作。</summary>
    public class BranchActions
    {
        public string Text = "";
        public int DiscardQ = -1;     // 打牌できる家
        public bool[] HandOk;         // 手牌の各牌を切れるか
        public bool DrawnOk;          // ツモ牌を切れるか
        public int TargetQ = -1;      // 鳴き・ロン対象の打牌をした家
        public List<BoardButton> Buttons = new List<BoardButton>();
        public List<int> Unseen;      // 牌山が尽きたときのツモ候補
        public List<Action> HandActions;   // 手牌の各牌を切る操作
        public Action DrawnAction;         // ツモ切り
        public Action<int> PickAction;     // ツモ候補から引く
    }

    /// <summary>
    /// 牌譜ビューアの状態と操作（UI 非依存）。元 HTML の load / setRound / go / 分岐処理に対応。
    /// </summary>
    public class ViewerState
    {
        static readonly Random rnd = new Random();

        public string[] Names = new string[0];
        public List<object> Rounds = new List<object>();
        public List<object> K;
        public List<object> Paishan = new List<object>();   // 拡張キー "paishan"（局ごとの牌山）
        public RoundData RD;
        public List<Step> Steps;
        public int I, Rc;
        public int? View;

        public List<Step> BR;
        public int Bi;
        public bool Rii;
        public string Bn = "";
        public BranchActions Acts = new BranchActions();
        public List<KeyValuePair<int, string>> Events = new List<KeyValuePair<int, string>>();

        public bool Loaded { get { return K != null; } }
        public bool Branching { get { return BR != null; } }
        public Step Cs { get { return BR != null ? BR[Bi] : Steps[I]; } }
        public int Cx { get { return BR != null ? Bi : I; } }
        public int Count { get { return BR != null ? BR.Count : Steps.Count; } }
        public List<Step> Log { get { return BR ?? Steps; } }
        public Step Last { get { return Steps[Steps.Count - 1]; } }

        // =====================================================================
        // 読み込み・移動
        // =====================================================================

        public void Load(string json)
        {
            var dict = MiniJson.Parse(json) as Dictionary<string, object>;
            object logO, nameO;
            if (dict == null || !dict.TryGetValue("log", out logO) || J.L(logO).Count == 0)
                throw new FormatException("天鳳6形式の牌譜ではありません");
            var rounds = J.L(logO);
            var names = dict.TryGetValue("name", out nameO) ? J.L(nameO).Select(x => J.S(x)).ToArray() : new string[0];
            object wallO;
            var paishan = dict.TryGetValue("paishan", out wallO) ? J.L(wallO) : new List<object>();
            // 1 局目を先に展開して、壊れたデータなら現在の表示を保つ
            RoundBuilder.Build(J.L(rounds[0]), names);
            Names = names;
            Rounds = rounds;
            Paishan = paishan;
            View = null;
            SetRound(0);
        }

        public void SetRound(int r)
        {
            BR = null;
            Rc = r;
            K = J.L(Rounds[r]);
            RD = RoundBuilder.Build(K, Names, r < Paishan.Count ? J.Ints(Paishan[r]) : null);
            Steps = RD.Steps;
            I = 0;
            if (!View.HasValue) View = Math.Max(0, Array.FindIndex(Names, x => !string.IsNullOrEmpty(x) && x != "AI"));
            int n = Steps.Count - 1;
            Events = new List<KeyValuePair<int, string>>();
            for (int j = 0; j < Steps.Count; j++)
            {
                var s = Steps[j];
                string kind = j == 0 ? "配牌"
                    : j == n ? (s.Msg.StartsWith("和了") ? "和了" : "流局")
                    : s.Msg.Contains("チー") || s.Msg.Contains("ポン") || s.Msg.Contains("槓") ? "鳴き"
                    : s.Msg.Contains("立直") ? "リーチ" : "";
                if (kind != "") Events.Add(new KeyValuePair<int, string>(j, s.Jn + "巡 " + kind));
            }
            Refresh();
        }

        public void Go(int n)
        {
            if (!Loaded) return;
            if (BR != null) Bi = Math.Max(0, Math.Min(BR.Count - 1, n));
            else I = Math.Max(0, Math.Min(Steps.Count - 1, n));
            Refresh();
        }

        public void Jump(int d)
        {
            if (BR != null || !Loaded) return;
            int t = Steps[I].Jn + d, j = Steps.FindIndex(x => x.Jn == t);
            if (j >= 0) Go(j);
        }

        // =====================================================================
        // 表示ヘルパ
        // =====================================================================

        public string Nmq(int q) { return J.Name(Names, q); }
        public static string Wn(int q, Step s) { return Tiles.WD[(q - s.Dealer + 4) % 4] + "家"; }
        public string N(int q) { return Wn(q, Cs) + " " + Nmq(q); }

        public static string Rname(List<object> k)
        {
            var k0 = J.L(k[0]);
            int r = J.I(k0[0]);
            return Tiles.WD[(r / 4) % 4].ToString() + (r % 4 + 1) + "局 " + J.I(k0[1]) + "本場";
        }

        public string RoundName { get { return Rname(K); } }
        public List<int> Dora(Step s) { return J.Ints(K[2]).Take(s.Dn).ToList(); }
        public int Kyotaku(Step s) { return (J.I(J.L(K[0])[2]) + s.Pl.Count(p => p.Ri)) * 1000; }
        public static string FirstLine(string m) { return m.Split('\n')[0]; }

        // =====================================================================
        // 分岐（牌譜と別の展開を卓上で操作して進める）
        // =====================================================================

        public void StartBranch()
        {
            var s = Steps[I];
            if (s.Ph == "e") return;
            BR = new List<Step> { s.Clone() };
            Bi = 0;
            Rii = false;
            Bn = "";
            if (BR[0].Ph == "w") Nxt(); else Refresh();
        }

        public void EndBranch()
        {
            BR = null;
            Rii = false;
            Refresh();
        }

        void Refresh() { Acts = ComputeActions(Cs); }

        Step Nx(string m, int t, string ph)
        {
            BR.RemoveRange(Bi + 1, BR.Count - Bi - 1);
            var n = BR[Bi].Clone();
            n.Msg = m;
            n.T = t;
            n.Ph = ph;
            Bn = "";
            n.Rin = false;
            n.Jn = n.CalcJn();
            BR.Add(n);
            Bi++;
            return n;
        }

        void Note(string m) { Bn = m; Refresh(); }
        void Fin(string m) { Nx(m, -1, "e"); Refresh(); }

        void Disc(int q, int c, bool w)
        {
            var p0 = Cs.Pl[q];
            if (p0.Ri && !w) { Note("リーチ後はツモ切りのみ可能です"); return; }
            bool r = Rii;
            if (r)
            {
                var rest = new List<int>(p0.H);
                if (p0.Dw.HasValue) rest.Add(p0.Dw.Value);
                Tiles.Rm(rest, c);
                if (!Tiles.Tenpai(rest)) { Note("その牌を切ってもテンパイになりません"); return; }
            }
            var n = Nx(N(q) + " 打 " + Tiles.Tn(c) + (r ? "（立直）" : ""), q, "c");
            var p = n.Pl[q];
            if (w) p.Dw = null;
            else
            {
                Tiles.Rm(p.H, c);
                if (p.Dw.HasValue) { p.H.Add(p.Dw.Value); Tiles.Sort(p.H); p.Dw = null; }
            }
            p.Rv.Add(new River { C = c, R = r, G = w });
            if (r) { p.Ri = true; n.Sc[q] -= 1000; }
            Rii = false;
            Refresh();
        }

        void DoCall(int q, char l, List<int> use, int dc)
        {
            int t = Cs.T, d = (t - q + 4) % 4;
            int ci = l == 'c' || d == 3 ? 0 : d == 2 ? 1 : l == 'p' ? 2 : 3;
            var tl = new List<int>(use);
            tl.Insert(ci, dc);
            var n = Nx(N(q) + " " + RoundBuilder.CallName(l) + " " + Tiles.Tn(dc), q, l == 'm' ? "w" : "d");
            var p = n.Pl[q];
            foreach (int c in use) Tiles.Rm(p.H, c);
            var rv = n.Pl[t].Rv;
            rv[rv.Count - 1].X = true;
            p.Ml.Add(new Meld { L = l, T = tl, Ci = ci });
            if (l == 'm') { n.Dn++; n.Rin = true; Nxt(); }
            else Refresh();
        }

        void DoKan(int q, int k)
        {
            var pp = Cs.Pl[q];
            int m = pp.Ml.FindIndex(x => x.L == 'p' && Tiles.Norm(x.T[0]) == k);
            var n = Nx(N(q) + " " + (m >= 0 ? "加槓 " : "暗槓 ") + Tiles.Tn(k), q, "w");
            var p = n.Pl[q];
            if (p.Dw.HasValue) { p.H.Add(p.Dw.Value); p.Dw = null; }
            if (m >= 0)
            {
                int c = p.H.First(x => Tiles.Norm(x) == k);
                Tiles.Rm(p.H, c);
                var t = new List<int>(p.Ml[m].T) { c };
                p.Ml[m] = new Meld { L = 'k', T = t, Ci = p.Ml[m].Ci };
            }
            else
            {
                var u = p.H.Where(x => Tiles.Norm(x) == k).ToList();
                foreach (int c in u) Tiles.Rm(p.H, c);
                p.Ml.Add(new Meld { L = 'a', T = u, Ci = 0 });
            }
            Tiles.Sort(p.H);
            n.Dn++;
            n.Rin = true;
            Nxt();
        }

        void Pass()
        {
            var s = Cs;
            if (s.Wall <= 0) { Fin("流局（海底）"); return; }
            int q = (s.T + 1) % 4;
            Nx("見送り → " + N(q) + " のツモ", q, "w");
            Nxt();
        }

        void DrawFromWall(int c)
        {
            var s0 = Cs;
            int q = s0.T;
            bool rn = s0.Rin;
            var n = Nx(N(q) + " ツモ " + Tiles.Tn(c), q, "d");
            n.Pl[q].Dw = c;
            n.Wall--;
            if (rn) n.Rd++; else n.Ld++;
            Refresh();
        }

        /// <summary>牌譜の牌山どおりに次のツモを行う。牌山が尽きていれば選択待ちのまま表示する。</summary>
        void Nxt()
        {
            var s = Cs;
            var src = s.Rin ? RD.RW : RD.LW;
            int idx = s.Rin ? s.Rd : s.Ld;
            if (idx < src.Count) DrawFromWall(src[idx]); else Refresh();
        }

        List<int> Unseen(Step s)
        {
            var a = new int[34];
            var red = new HashSet<int>();
            Action<int> add = c => { a[Tiles.Ix(c)]++; if (c > 50) red.Add(c); };
            foreach (var p in s.Pl)
            {
                p.H.ForEach(add);
                if (p.Dw.HasValue) add(p.Dw.Value);
                foreach (var r in p.Rv) if (!r.X) add(r.C);
                foreach (var m in p.Ml) m.T.ForEach(add);
            }
            foreach (int c in Dora(s)) add(c);
            var o = new List<int>();
            for (int j = 0; j < 34; j++)
            {
                if (a[j] >= 4) continue;
                o.Add(Tiles.Cd(j));
                if (j < 27 && j % 9 == 4 && !red.Contains(50 + j / 9 + 1)) o.Add(50 + j / 9 + 1);
            }
            return o;
        }

        static bool TenpaiAfter(List<int> all, int c)
        {
            var rest = new List<int>(all);
            Tiles.Rm(rest, c);
            return Tiles.Tenpai(rest);
        }

        /// <summary>現在の局面で選べる合法手（打牌できる牌・卓上に出すボタン）を求める。</summary>
        BranchActions ComputeActions(Step s)
        {
            var A = new BranchActions();
            if (BR == null)
            {
                A.Text = "任意の局面で「分岐開始」を押すと、卓上の牌やボタンを直接操作して牌譜と別の展開を確認できます（ツモは牌譜の牌山どおり）。" + (RD.HasWall ? "" : "牌山データがないため、牌譜にないツモは未使用牌から選びます。");
                return A;
            }
            Action<int, string, Action> B = (bq, label, f) => A.Buttons.Add(new BoardButton { Q = bq, Label = label, Act = f });

            if (s.Ph == "d")
            {
                int q = s.T;
                var P = s.Pl[q];
                var all = new List<int>(P.H);
                if (P.Dw.HasValue) all.Add(P.Dw.Value);
                A.DiscardQ = q;
                A.HandOk = P.H.Select(c => !P.Ri && (!Rii || TenpaiAfter(all, c))).ToArray();
                A.DrawnOk = P.Dw.HasValue && (!Rii || TenpaiAfter(all, P.Dw.Value));
                A.Text = N(q) + "の手番：卓上の手牌をクリックして打牌"
                    + (Rii ? "（リーチ宣言中：テンパイを保てる牌のみ選べます）" : P.Ri ? "（リーチ中のためツモ切りのみ）" : "");

                if (Tiles.Agari(all)) B(q, "ツモ", () => Fin(N(q) + " ツモ和了（役・点数は未計算）"));
                if (!P.Ri && s.Wall > 0)
                {
                    var cn = new SortedDictionary<int, int>();
                    foreach (int c in all)
                    {
                        int nn = Tiles.Norm(c), cur;
                        cn.TryGetValue(nn, out cur);
                        cn[nn] = cur + 1;
                    }
                    foreach (var kv in cn)
                    {
                        int kk = kv.Key;
                        if (kv.Value == 4 || P.Ml.Any(m => m.L == 'p' && Tiles.Norm(m.T[0]) == kk))
                            B(q, (kv.Value == 4 ? "暗槓 " : "加槓 ") + Tiles.Tn(kk), () => DoKan(q, kk));
                    }
                }
                if (!P.Ri && P.Ml.All(m => m.L == 'a') && s.Sc[q] >= 1000 && s.Wall >= 4 && all.Any(c => TenpaiAfter(all, c)))
                    B(q, Rii ? "リーチ取消" : "リーチ", () => { Rii = !Rii; Refresh(); });

                A.HandActions = P.H.Select(c => (Action)(() => Disc(q, c, false))).ToList();
                if (P.Dw.HasValue) { int dw = P.Dw.Value; A.DrawnAction = () => Disc(q, dw, true); }
            }
            else if (s.Ph == "c")
            {
                int q = s.T;
                int dc = s.Pl[q].Rv[s.Pl[q].Rv.Count - 1].C, nn = Tiles.Norm(dc);
                A.TargetQ = q;
                A.Text = N(q) + "の " + Tiles.Tn(dc) + " に対する操作：鳴く家の手牌の上のボタンを押すか、中央の「スルー」で次へ";
                for (int k = 1; k < 4; k++)
                {
                    int r = (q + k) % 4;
                    var P = s.Pl[r];
                    if (Tiles.Agari(new List<int>(P.H) { dc }))
                        B(r, "ロン", () => Fin(N(r) + " ロン和了（" + N(q) + "から／役・点数は未計算）"));
                    if (P.Ri || s.Wall <= 0) continue;
                    var sm = P.H.Where(c => Tiles.Norm(c) == nn).ToList();
                    if (sm.Count >= 2) { var use = sm.Take(2).ToList(); B(r, "ポン", () => DoCall(r, 'p', use, dc)); }
                    if (sm.Count >= 3) { var use = sm.Take(3).ToList(); B(r, "カン", () => DoCall(r, 'm', use, dc)); }
                    if (k == 1 && nn < 40)
                    {
                        int su = nn / 10, rk = nn % 10;
                        foreach (var ab in new[] { new[] { -2, -1 }, new[] { -1, 1 }, new[] { 1, 2 } })
                        {
                            int x = rk + ab[0], y = rk + ab[1];
                            if (x < 1 || y > 9) continue;
                            int? u = Tiles.Pk(P.H, su * 10 + x), v = Tiles.Pk(P.H, su * 10 + y);
                            if (!u.HasValue || !v.HasValue) continue;
                            var use = new List<int> { u.Value, v.Value };
                            B(r, "チー " + Tiles.Tn(u.Value) + Tiles.Tn(v.Value), () => DoCall(r, 'c', use, dc));
                        }
                    }
                }
                B(-1, "スルー", Pass);
            }
            else if (s.Ph == "w")
            {
                A.Unseen = Unseen(s);
                A.PickAction = c => DrawFromWall(c);
                A.Text = (RD.HasWall ? "牌山が尽きたため、" : "牌山データがなく牌譜のツモも尽きたため、") + N(s.T) + "のツモ牌を卓上の一覧から選んでください";
            }
            else A.Text = "局終了（" + FirstLine(s.Msg) + "）";
            return A;
        }

        /// <summary>牌山切れのツモ候補からランダムに 1 枚引く。</summary>
        public void PickRandom()
        {
            var u = Acts.Unseen;
            if (u != null && u.Count > 0) DrawFromWall(u[rnd.Next(u.Count)]);
        }
    }
}
