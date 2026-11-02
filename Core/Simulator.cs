using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PaifuViewer
{
    /// <summary>試行結果の集計（視点の家から見た局の終わり方と収支）。</summary>
    public class SimOutcome
    {
        public int N, Win, DealIn, OtherTsumo, Yoko, Draw;
        public long Delta;

        public double Rate(int x) { return N == 0 ? 0 : (double)x / N; }
        public double WinRate { get { return Rate(Win); } }
        public double DealInRate { get { return Rate(DealIn); } }
        public double DrawRate { get { return Rate(Draw); } }
        public double MeanDelta { get { return N == 0 ? 0 : (double)Delta / N; } }

        public void Add(SimOutcome o)
        {
            N += o.N; Win += o.Win; DealIn += o.DealIn; OtherTsumo += o.OtherTsumo; Yoko += o.Yoko; Draw += o.Draw; Delta += o.Delta;
        }
    }

    /// <summary>視点の家が選べる操作 1 つと、その操作をしたときの結果。</summary>
    public class SimCandidate
    {
        public string Label;
        public int? Tile;              // 表示する牌（打牌・鳴き・ロンの対象）
        public SimOutcome R = new SimOutcome();
        internal Action<SimGame> Act;
    }

    public class SimResult
    {
        public int Viewer;
        public string Note = "";
        public SimOutcome Overall = new SimOutcome();
        public List<SimCandidate> Cands = new List<SimCandidate>();
        public bool Done;
    }

    /// <summary>
    /// モンテカルロ・シミュレーション。視点の家から見えない牌（他家の手牌・牌山・裏ドラ）を毎回並べ替え、
    /// 4 人とも簡単なボット（牌効率で打牌、リーチ者がいればテンパイ以外はベタオリ、役牌のみポン）で局の終わりまで打つ。
    /// </summary>
    public static class Simulator
    {
        /// <summary>
        /// 局面 s から trials 回ずつ試行する。視点の家が操作できる局面では、操作の候補ごとにも試行する。
        /// progress には完了した割合（0..1）と途中結果を渡す。
        /// </summary>
        public static SimResult Run(Step s, List<object> k, int viewer, int trials, CancellationToken ct, Action<double, SimResult> progress = null, bool overallOnly = false)
        {
            var setup = new SimSetup(s.Clone(), k, viewer);
            var res = new SimResult { Viewer = viewer };
            if (s.Ph == "e") { res.Note = "局終了"; res.Done = true; return res; }
            if (!overallOnly) res.Cands = Candidates(setup);
            var jobs = new List<KeyValuePair<SimOutcome, Action<SimGame>>> { new KeyValuePair<SimOutcome, Action<SimGame>>(res.Overall, null) };
            jobs.AddRange(res.Cands.Select(c => new KeyValuePair<SimOutcome, Action<SimGame>>(c.R, c.Act)));

            // 途中経過を見せるため、少しずつ全候補を回す
            const int chunk = 50;
            int total = trials * jobs.Count, done = 0;
            for (int start = 0; start < trials; start += chunk)
            {
                int n = Math.Min(chunk, trials - start);
                var parts = new SimOutcome[jobs.Count * n];
                Parallel.For(0, parts.Length, new ParallelOptions { CancellationToken = ct }, () => new Random(Guid.NewGuid().GetHashCode()), (ix, _, rnd) =>
                {
                    var job = jobs[ix / n];
                    var g = new SimGame(setup, rnd);
                    g.Play(job.Value);
                    parts[ix] = g.Result;
                    return rnd;
                }, _ => { });
                lock (res)
                {
                    for (int ix = 0; ix < parts.Length; ix++) jobs[ix / n].Key.Add(parts[ix]);
                }
                done += parts.Length;
                if (progress != null) progress((double)done / total, res);
            }
            res.Done = true;
            return res;
        }

        static List<SimCandidate> Candidates(SimSetup st)
        {
            var o = new List<SimCandidate>();
            var s = st.S;
            int v = st.Viewer;
            var P = s.Pl[v];
            if (s.Ph == "d" && s.T == v)
            {
                var all = new List<int>(P.H);
                if (P.Dw.HasValue) all.Add(P.Dw.Value);
                if (P.Dw.HasValue && Tiles.Agari(all) && SimGame.EvalStatic(st, v, all, P.Ml, P.Dw.Value, true, s.Wall, s.Rs, P.Ri, false) != null)
                    o.Add(new SimCandidate { Label = "ツモ和了", Tile = P.Dw, Act = g => g.ForceTsumo() });
                if (P.Ri) return o;   // リーチ中はツモ切りのみ（候補が 1 つなら比べるものがない）
                var codes = all.Distinct().OrderBy(Tiles.Key).ToList();
                var ok = codes.Where(c => s.Kk == null || !s.Kk.Contains(Tiles.Norm(c))).ToList();
                if (ok.Count == 0) ok = codes;
                foreach (int c in ok)
                {
                    int cc = c;
                    o.Add(new SimCandidate { Label = "打 " + Tiles.Tn(c) + (c > 50 ? "(赤)" : ""), Tile = c, Act = g => g.ForceDiscard(cc, false) });
                }
                if (P.Ml.All(m => m.L == 'a') && s.Sc[v] >= 1000 && s.Wall >= 4)
                    foreach (int c in ok)
                    {
                        var rest = new List<int>(all);
                        Tiles.Rm(rest, c);
                        if (!Tiles.Tenpai(rest)) continue;
                        int cc = c;
                        o.Add(new SimCandidate { Label = "立直 打 " + Tiles.Tn(c) + (c > 50 ? "(赤)" : ""), Tile = c, Act = g => g.ForceDiscard(cc, true) });
                    }
            }
            else if (s.Ph == "c" && s.T != v && s.T >= 0)
            {
                int q = s.T, dc = s.Pl[q].Rv.Last().C, nn = Tiles.Norm(dc);
                var calls = new List<SimCandidate>();
                var h = P.H;
                if (Tiles.Agari(new List<int>(h) { dc }))
                {
                    var hand = new List<int>(h) { dc };
                    if (SimGame.EvalStatic(st, v, hand, P.Ml, dc, false, s.Wall, false, P.Ri, false) != null)
                        calls.Add(new SimCandidate { Label = "ロン", Tile = dc, Act = g => g.ForceResponse(SimGame.Resp.Ron, null) });
                }
                if (!P.Ri && s.Wall > 0)
                {
                    var sm = h.Where(c => Tiles.Norm(c) == nn).Take(2).ToList();
                    if (sm.Count == 2) calls.Add(new SimCandidate { Label = "ポン", Tile = dc, Act = g => g.ForceResponse(SimGame.Resp.Pon, sm) });
                    if ((q + 1) % 4 == v && nn < 40)
                    {
                        int su = nn / 10, rk = nn % 10;
                        foreach (var ab in new[] { new[] { -2, -1 }, new[] { -1, 1 }, new[] { 1, 2 } })
                        {
                            int x = rk + ab[0], y = rk + ab[1];
                            if (x < 1 || y > 9) continue;
                            int? u = Tiles.Pk(h, su * 10 + x), w = Tiles.Pk(h, su * 10 + y);
                            if (!u.HasValue || !w.HasValue) continue;
                            var use = new List<int> { u.Value, w.Value };
                            calls.Add(new SimCandidate { Label = "チー " + Tiles.Tn(u.Value) + Tiles.Tn(w.Value), Tile = dc, Act = g => g.ForceResponse(SimGame.Resp.Chi, use) });
                        }
                    }
                }
                if (calls.Count > 0)
                {
                    o.Add(new SimCandidate { Label = "スルー", Tile = dc, Act = g => g.ForceResponse(SimGame.Resp.Pass, null) });
                    o.AddRange(calls);
                }
            }
            return o;
        }
    }

    /// <summary>試行に共通する情報（局の条件・開始局面・視点の家から見えない牌）。</summary>
    class SimSetup
    {
        public Step S;
        public int Viewer, RoundWind, Honba, Pot;
        public List<int> DoraInd;
        public List<int> Pool;   // 視点の家から見えない牌

        public SimSetup(Step s, List<object> k, int viewer)
        {
            S = s;
            Viewer = viewer;
            var k0 = J.L(k[0]);
            RoundWind = (J.I(k0[0]) / 4) % 4;
            Honba = J.I(k0[1]);
            Pot = (J.I(k0[2]) + s.Pl.Count(p => p.Ri)) * 1000;
            DoraInd = J.Ints(k[2]).Take(s.Dn).ToList();

            // 136 枚（赤5 は各色 1 枚）から、視点の家に見えている牌を除く
            var pool = new List<int>();
            for (int i = 0; i < 34; i++)
                for (int j = 0; j < 4; j++)
                {
                    int c = Tiles.Cd(i);
                    pool.Add(j == 0 && i < 27 && i % 9 == 4 ? 50 + i / 9 + 1 : c);
                }
            var seen = new List<int>();
            var P = s.Pl[viewer];
            seen.AddRange(P.H);
            if (P.Dw.HasValue) seen.Add(P.Dw.Value);
            foreach (var p in s.Pl)
            {
                foreach (var r in p.Rv) if (!r.X) seen.Add(r.C);
                foreach (var m in p.Ml) seen.AddRange(m.T);
            }
            seen.AddRange(DoraInd);
            foreach (int c in seen) Tiles.Rm(pool, c);
            Pool = pool;
        }
    }

    /// <summary>1 回の試行。</summary>
    class SimGame
    {
        public enum Resp { Bot, Pass, Ron, Pon, Chi }

        enum Ph { Draw, Turn, Respond, End }

        readonly SimSetup st;
        readonly Random rnd;
        readonly int v, dealer;
        readonly List<int>[] H = new List<int>[4];     // 門前の手牌（ツモ牌を含む）
        readonly List<int>[] Rv = new List<int>[4];    // 河（鳴かれた牌も含む。現物の判定用）
        readonly List<Meld>[] Ml = new List<Meld>[4];
        readonly bool[] Ri = new bool[4], Ipp = new bool[4], Furi = new bool[4];
        readonly int[] RiAt = new int[4];             // リーチ宣言時の各家の河の枚数（それ以降の他家の打牌は現物）
        readonly int[][] RiRv = new int[4][];
        readonly int[] D = new int[4];
        readonly List<int> wall = new List<int>(), ura = new List<int>();
        int wi, wallLeft, pot, t, tile;
        bool rinNext, rs, afterCall;
        List<int> kk;
        Ph ph;
        Resp forced = Resp.Bot;
        List<int> forcedUse;

        public SimOutcome Result = new SimOutcome { N = 1 };

        public SimGame(SimSetup st, Random rnd)
        {
            this.st = st;
            this.rnd = rnd;
            var s = st.S;
            v = st.Viewer;
            dealer = s.Dealer;
            pot = st.Pot;
            var pool = new List<int>(st.Pool);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int x = pool[i]; pool[i] = pool[j]; pool[j] = x;
            }
            int pi = 0;
            for (int q = 0; q < 4; q++)
            {
                var P = s.Pl[q];
                int size = P.H.Count + (P.Dw.HasValue ? 1 : 0);
                if (q == v)
                {
                    H[q] = new List<int>(P.H);
                    if (P.Dw.HasValue) H[q].Add(P.Dw.Value);
                }
                else
                {
                    H[q] = pool.Skip(pi).Take(size).ToList();
                    pi += size;
                }
                Rv[q] = P.Rv.Select(r => r.C).ToList();
                Ml[q] = new List<Meld>(P.Ml);
                Ri[q] = P.Ri;
                if (P.Ri)
                {
                    int ri = P.Rv.FindIndex(r => r.R);
                    RiAt[q] = ri < 0 ? 0 : ri;
                    RiRv[q] = s.Pl.Select(p => Math.Max(0, ri < 0 ? 0 : ri - (P.Rv.Count - p.Rv.Count))).ToArray();
                    Ipp[q] = P.Rv.Count > 0 && P.Rv.Last().R;
                }
            }
            wallLeft = s.Wall;
            wall.AddRange(pool.Skip(pi).Take(wallLeft));
            ura.AddRange(pool.Skip(pi + wall.Count).Take(5));
            while (ura.Count < 5) ura.Add(pool[rnd.Next(pool.Count)]);
            t = s.T;
            kk = s.Kk;
            if (s.Ph == "w") { ph = Ph.Draw; rinNext = s.Rin; }
            else if (s.Ph == "d")
            {
                ph = Ph.Turn;
                rs = s.Rs;
                // 他家のツモ牌は手牌の最後の牌とみなす
                var P = s.Pl[t];
                afterCall = !P.Dw.HasValue;
                tile = afterCall ? 0 : t == v ? P.Dw.Value : H[t].Last();
            }
            else { ph = Ph.Respond; tile = s.Pl[t].Rv.Last().C; }
        }

        // =====================================================================
        // 視点の家の操作（候補）
        // =====================================================================

        public void ForceTsumo() { TsumoWin(t); }
        public void ForceDiscard(int c, bool riichi) { Discard(t, c, riichi); }
        public void ForceResponse(Resp r, List<int> use) { forced = r; forcedUse = use; }

        public void Play(Action<SimGame> first)
        {
            if (first != null) first(this);
            int guard = 0;
            while (ph != Ph.End && guard++ < 1000)
            {
                if (ph == Ph.Draw) DoDraw();
                else if (ph == Ph.Turn) DoTurn();
                else DoRespond();
            }
        }

        // =====================================================================
        // 進行
        // =====================================================================

        void DoDraw()
        {
            if (wi >= wall.Count) { Ryuukyoku(); return; }
            tile = wall[wi++];
            wallLeft--;
            rs = rinNext;
            rinNext = false;
            afterCall = false;
            kk = null;
            H[t].Add(tile);
            ph = Ph.Turn;
        }

        void DoTurn()
        {
            int q = t;
            if (!afterCall && Tiles.Agari(H[q]) && Eval(q, H[q], tile, true) != null) { TsumoWin(q); return; }
            bool riichi;
            int c = ChooseDiscard(q, out riichi);
            Discard(q, c, riichi);
        }

        void Discard(int q, int c, bool riichi)
        {
            Tiles.Rm(H[q], c);
            Rv[q].Add(c);
            if (Ri[q]) Ipp[q] = false;
            else Furi[q] = false;
            if (riichi)
            {
                Ri[q] = true;
                Ipp[q] = true;
                RiAt[q] = Rv[q].Count - 1;
                RiRv[q] = Rv.Select(r => r.Count).ToArray();
                RiRv[q][q] = Rv[q].Count - 1;
                D[q] -= 1000;
                pot += 1000;
            }
            tile = c;
            t = q;
            kk = null;
            ph = Ph.Respond;
        }

        void DoRespond()
        {
            int q = t, c = tile;
            var fr = forced;
            forced = Resp.Bot;

            // ロン（複数人のロンはすべて和了とする）
            var winners = new List<int>();
            for (int k = 1; k < 4; k++)
            {
                int r = (q + k) % 4;
                var hand = new List<int>(H[r]) { c };
                if (!Tiles.Agari(hand)) continue;
                bool want = r == v && fr != Resp.Bot ? fr == Resp.Ron : true;
                if (want && !Furiten(r) && Eval(r, hand, c, false) != null) winners.Add(r);
                else Furi[r] = true;   // 見逃し（リーチ中は以後ずっとフリテン）
            }
            if (winners.Count > 0) { RonWin(winners, q, c); return; }

            // 鳴き
            if (wallLeft > 0)
            {
                if (fr == Resp.Pon || fr == Resp.Chi) { Call(v, q, fr == Resp.Pon ? 'p' : 'c', forcedUse, c); return; }
                for (int k = 1; k < 4; k++)
                {
                    int r = (q + k) % 4;
                    if (r == v && fr == Resp.Pass) continue;
                    if (Ri[r] || !Yakuhai(r, c)) continue;
                    var sm = H[r].Where(x => Tiles.Norm(x) == Tiles.Norm(c)).Take(2).ToList();
                    if (sm.Count == 2) { Call(r, q, 'p', sm, c); return; }
                }
            }

            if (wallLeft <= 0) { Ryuukyoku(); return; }
            t = (q + 1) % 4;
            ph = Ph.Draw;
        }

        void Call(int r, int from, char l, List<int> use, int c)
        {
            foreach (int x in use) Tiles.Rm(H[r], x);
            var tl = new List<int>(use);
            tl.Insert(0, c);
            Ml[r].Add(new Meld { L = l, T = tl, Ci = 0 });
            for (int q = 0; q < 4; q++) Ipp[q] = false;
            int nn = Tiles.Norm(c);
            kk = new List<int> { nn };
            if (l == 'c')
            {
                var us = use.Select(Tiles.Norm).ToList();
                if (us.All(x => x > nn) && nn % 10 + 3 <= 9) kk.Add(nn + 3);
                if (us.All(x => x < nn) && nn % 10 - 3 >= 1) kk.Add(nn - 3);
            }
            t = r;
            afterCall = true;
            ph = Ph.Turn;
        }

        bool Yakuhai(int q, int c)
        {
            int n = Tiles.Norm(c);
            if (n < 41) return false;
            int w = n - 41;
            return w >= 4 || w == st.RoundWind || w == (q - dealer + 4) % 4;
        }

        bool Furiten(int q)
        {
            if (Furi[q]) return true;
            var a = Counts(H[q]);
            for (int j = 0; j < 34; j++)
            {
                if (!Rv[q].Any(x => Tiles.Ix(x) == j)) continue;
                a[j]++;
                bool w = Shanten.Calc(a, Ml[q].Count) < 0;
                a[j]--;
                if (w) return true;
            }
            return false;
        }

        // =====================================================================
        // 和了・流局
        // =====================================================================

        public static ScoreResult EvalStatic(SimSetup st, int q, List<int> hand, List<Meld> ml, int winTile, bool tsumo, int wallLeft, bool rs, bool riichi, bool ipp, List<int> ura = null)
        {
            var w = new WinContext
            {
                Hand = hand, Melds = ml, WinTile = winTile, Tsumo = tsumo,
                SeatWind = (q - st.S.Dealer + 4) % 4, RoundWind = st.RoundWind,
                Riichi = riichi, Ippatsu = ipp,
                Haitei = tsumo && wallLeft <= 0 && !rs, Houtei = !tsumo && wallLeft <= 0,
                Rinshan = tsumo && rs,
                DoraInd = st.DoraInd,
                UraInd = ura ?? new List<int>()
            };
            return Scoring.Evaluate(w);
        }

        ScoreResult Eval(int q, List<int> hand, int winTile, bool tsumo)
        {
            return EvalStatic(st, q, hand, Ml[q], winTile, tsumo, wallLeft, rs, Ri[q], Ipp[q], ura.Take(st.DoraInd.Count).ToList());
        }

        void TsumoWin(int q)
        {
            var r = Eval(q, H[q], tile, true);
            if (r == null) { ph = Ph.End; Result.Draw = 1; return; }
            string _;
            var d = Scoring.Deltas(r, q, q, dealer, st.Honba, pot, out _);
            for (int k = 0; k < 4; k++) D[k] += d[k];
            if (q == v) Result.Win = 1; else Result.OtherTsumo = 1;
            Finish();
        }

        void RonWin(List<int> winners, int from, int c)
        {
            bool first = true;
            foreach (int r in winners)
            {
                var hand = new List<int>(H[r]) { c };
                var sr = Eval(r, hand, c, false);
                string _;
                var d = Scoring.Deltas(sr, r, from, dealer, first ? st.Honba : 0, first ? pot : 0, out _);
                for (int k = 0; k < 4; k++) D[k] += d[k];
                first = false;
            }
            if (winners.Contains(v)) Result.Win = 1;
            else if (from == v) Result.DealIn = 1;
            else Result.Yoko = 1;
            Finish();
        }

        void Ryuukyoku()
        {
            var ten = Enumerable.Range(0, 4).Where(q => Shanten.Calc(H[q], Ml[q].Count) == 0).ToList();
            if (ten.Count > 0 && ten.Count < 4)
                for (int q = 0; q < 4; q++) D[q] += ten.Contains(q) ? 3000 / ten.Count : -3000 / (4 - ten.Count);
            Result.Draw = 1;
            Finish();
        }

        void Finish()
        {
            Result.Delta = D[v];
            ph = Ph.End;
        }

        // =====================================================================
        // ボットの打牌
        // =====================================================================

        static int[] Counts(List<int> h)
        {
            var a = new int[34];
            foreach (int c in h) a[Tiles.Ix(c)]++;
            return a;
        }

        /// <summary>q の家から見えている牌の種類ごとの枚数（自分の手牌・全員の河と副露・ドラ表示牌）。</summary>
        int[] Visible(int q)
        {
            var a = Counts(H[q]);
            for (int p = 0; p < 4; p++)
            {
                foreach (int c in Rv[p]) a[Tiles.Ix(c)]++;
                foreach (var m in Ml[p]) foreach (int c in m.T) a[Tiles.Ix(c)]++;
            }
            foreach (int c in st.DoraInd) a[Tiles.Ix(c)]++;
            return a;
        }

        int ChooseDiscard(int q, out bool riichi)
        {
            riichi = false;
            var h = H[q];
            if (Ri[q] && !afterCall) return tile;
            int melds = Ml[q].Count;
            var a = Counts(h);
            var codes = h.Distinct().ToList();
            if (kk != null && q == t)
            {
                var ok = codes.Where(c => !kk.Contains(Tiles.Norm(c))).ToList();
                if (ok.Count > 0) codes = ok;
            }
            var sh = new Dictionary<int, int>();
            foreach (int c in codes)
            {
                int i = Tiles.Ix(c);
                a[i]--;
                sh[c] = Shanten.Calc(a, melds);
                a[i]++;
            }
            int min = sh.Values.Min();

            var threats = Enumerable.Range(0, 4).Where(p => p != q && Ri[p]).ToList();
            if (threats.Count > 0 && min >= 1)
            {
                // ベタオリ：危険度の低い牌、同じなら向聴数を保てる牌
                var vis = Visible(q);
                return codes.OrderBy(c => threats.Sum(p => Danger(p, c, vis))).ThenBy(c => sh[c]).ThenBy(c => Keep(q, c, a)).First();
            }

            var best = codes.Where(c => sh[c] == min).ToList();
            int pick;
            if (best.Count > 1 && min <= 2)
            {
                var vis = Visible(q);
                var uk = new Dictionary<int, int>();
                foreach (int c in best)
                {
                    int i = Tiles.Ix(c);
                    a[i]--;
                    uk[c] = Ukeire(a, melds, min, vis);
                    a[i]++;
                }
                pick = best.OrderByDescending(c => uk[c]).ThenBy(c => Keep(q, c, a)).First();
            }
            else pick = best.OrderBy(c => Keep(q, c, a)).First();

            if (min == 0 && !Ri[q] && Ml[q].All(m => m.L == 'a') && wallLeft >= 4 && st.S.Sc[q] + D[q] >= 1000) riichi = true;
            return pick;
        }

        /// <summary>向聴数が進む牌の残り枚数（見えている枚数を除く）。</summary>
        static int Ukeire(int[] a, int melds, int cur, int[] vis)
        {
            int n = 0;
            for (int j = 0; j < 34; j++)
            {
                if (vis[j] >= 4 || !Near(a, j)) continue;
                a[j]++;
                if (Shanten.Calc(a, melds) < cur) n += 4 - vis[j];
                a[j]--;
            }
            return n;
        }

        static bool Near(int[] a, int j)
        {
            if (a[j] > 0) return true;
            if (j >= 27) return false;
            int r = j % 9;
            for (int d = -2; d <= 2; d++)
                if (d != 0 && r + d >= 0 && r + d <= 8 && a[j + d] > 0) return true;
            return false;
        }

        /// <summary>手に残したい度合い（小さいほど先に切る）。</summary>
        int Keep(int q, int c, int[] a)
        {
            int i = Tiles.Ix(c), n = a[i], v0;
            if (i >= 27)
                v0 = n >= 2 ? 40 : Yakuhai(q, Tiles.Cd(i)) ? 6 : 2;
            else
            {
                int r = i % 9;
                v0 = 10 + (n - 1) * 8 + (r >= 2 && r <= 6 ? 3 : r == 1 || r == 7 ? 1 : 0);
                for (int d = -2; d <= 2; d++)
                    if (d != 0 && r + d >= 0 && r + d <= 8) v0 += a[i + d] * (Math.Abs(d) == 1 ? 4 : 2);
            }
            if (st.DoraInd.Any(x => Scoring.DoraOf(x) == i)) v0 += 6;
            if (c > 50) v0 += 6;
            return v0;
        }

        /// <summary>リーチした家 p に対して牌 c を切る危険度（現物 0、字牌・筋は低め、無筋の中張牌が最も高い）。</summary>
        int Danger(int p, int c, int[] vis)
        {
            int i = Tiles.Ix(c);
            var safe = new HashSet<int>(Rv[p].Select(Tiles.Ix));
            for (int o = 0; o < 4; o++)
                if (o != p && RiRv[p] != null)
                    for (int j = RiRv[p][o]; j < Rv[o].Count; j++) safe.Add(Tiles.Ix(Rv[o][j]));
            if (safe.Contains(i)) return 0;
            if (i >= 27) return vis[i] >= 3 ? 1 : vis[i] == 2 ? 3 : 6;
            int r = i % 9, b = i - r;
            bool lo = r - 3 < 0 || safe.Contains(b + r - 3), hi = r + 3 > 8 || safe.Contains(b + r + 3);
            bool term = r == 0 || r == 8, two = r == 1 || r == 7;
            if (lo && hi) return term ? 2 : two ? 3 : 4;
            return term ? 6 : two ? 8 : 12;
        }
    }
}
