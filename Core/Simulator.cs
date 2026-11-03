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
    /// 4 人ともボット（牌効率で打牌、打点と危険度で押し引き、役牌・タンヤオ・染め手の鳴き）で局の終わりまで打つ。
    /// </summary>
    public static class Simulator
    {
        /// <summary>
        /// 局面 s から trials 回ずつ試行する。視点の家が操作できる局面では、操作の候補ごとにも試行する。
        /// progress には完了した割合（0..1）と途中結果を渡す。
        /// </summary>
        public static SimResult Run(Step s, List<object> k, int viewer, int trials, CancellationToken ct, Action<double, SimResult> progress = null, bool overallOnly = false)
        {
            SimSetup setup;
            List<KeyValuePair<SimOutcome, Action<SimGame>>> jobs;
            var res = Prepare(s, k, viewer, overallOnly, out setup, out jobs);
            if (res.Done) return res;

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

        /// <summary>
        /// Run と同じ試行を 1 本のスレッドで少しずつ行い、およそ sliceMs ごとに await で処理を返す。
        /// ブラウザ（WebAssembly）では裏で計算するスレッドがないため、こうして画面が固まらないようにする。
        /// </summary>
        public static async Task<SimResult> RunAsync(Step s, List<object> k, int viewer, int trials, CancellationToken ct, Action<double, SimResult> progress = null, bool overallOnly = false, int sliceMs = 25)
        {
            SimSetup setup;
            List<KeyValuePair<SimOutcome, Action<SimGame>>> jobs;
            var res = Prepare(s, k, viewer, overallOnly, out setup, out jobs);
            if (res.Done) return res;

            var rnd = new Random(Guid.NewGuid().GetHashCode());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int chunk = 50;
            int total = trials * jobs.Count, done = 0;
            for (int start = 0; start < trials; start += chunk)
            {
                int n = Math.Min(chunk, trials - start);
                foreach (var job in jobs)
                    for (int j = 0; j < n; j++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var g = new SimGame(setup, rnd);
                        g.Play(job.Value);
                        job.Key.Add(g.Result);
                        done++;
                        if (sw.ElapsedMilliseconds < sliceMs) continue;
                        if (progress != null) progress((double)done / total, res);
                        await Task.Delay(1, ct);
                        sw.Restart();
                    }
            }
            res.Done = true;
            if (progress != null) progress(1, res);
            return res;
        }

        static SimResult Prepare(Step s, List<object> k, int viewer, bool overallOnly, out SimSetup setup, out List<KeyValuePair<SimOutcome, Action<SimGame>>> jobs)
        {
            setup = new SimSetup(s.Clone(), k, viewer);
            var res = new SimResult { Viewer = viewer };
            jobs = null;
            if (s.Ph == "e") { res.Note = "局終了"; res.Done = true; return res; }
            if (!overallOnly) res.Cands = Candidates(setup);
            jobs = new List<KeyValuePair<SimOutcome, Action<SimGame>>> { new KeyValuePair<SimOutcome, Action<SimGame>>(res.Overall, null) };
            jobs.AddRange(res.Cands.Select(c => new KeyValuePair<SimOutcome, Action<SimGame>>(c.R, c.Act)));
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

    /// <summary>
    /// ボットの判断のしきい値。自動対戦（SelfPlay）の成績が雀魂の上位卓（雀聖〜魂天）の平均に近づくように調整した値。
    /// </summary>
    public class BotParams
    {
        public int CallMaxShanten = 3;       // 鳴いた後の向聴数がこれ以下なら（役のある）鳴きを考える
        public int HonitsuMin = 8;           // 染め手で鳴くのに必要な、その色と字牌の枚数
        public bool Atozuke = true;          // 役牌の対子を残したまま先に鳴く（後付け）
        public double PushHan1 = 2;          // 1 向聴で押す見込みの飜数
        public int PushWall1 = 16;           // 1 向聴で押す残り枚数の下限
        public int PushDanger1 = 8;          // 1 向聴で押すとき切ってよい危険度の上限
        public int TenpaiFoldValue = 2000;   // テンパイでもこの打点未満・待ちが少なく危険な牌なら降りる
        public int TenpaiFoldWaits = 3;
        public int TenpaiFoldDanger = 10;
        public int DamaValue = 5200;         // 役ありのテンパイでこの打点以上ならダマ
        public int ChaseWaits = 5;           // 他家のリーチに追いかけリーチするのに必要な待ちの残り枚数
        public double Open3Threat = 0.8;     // 3 副露の他家を、リーチの何人分の脅威とみなすか
        public double Open2Threat = 0.35;    // 2 副露で 8 巡目以降の他家
        public double Open1Threat = 0;       // 1 副露で 10 巡目以降の他家
        public bool ReadOpen = true;         // 副露の内容（タンヤオ・染め手）から安全な牌を読む
        public double ThreatMin = 0.5;       // 脅威の合計がこれ以上なら押し引きを考える

        public static BotParams Default = new BotParams();
    }

    /// <summary>1 回の試行。</summary>
    class SimGame
    {
        public enum Resp { Bot, Pass, Ron, Pon, Chi }

        enum Ph { Draw, Turn, Respond, End }

        // 鳴き手の方針（役の当て）
        enum Plan { Any, Tanyao, Honitsu, None }

        readonly SimSetup st;
        readonly Random rnd;
        readonly BotParams P;
        readonly int v, dealer;
        readonly List<int>[] H = new List<int>[4];     // 門前の手牌（ツモ牌を含む）
        readonly List<int>[] Rv = new List<int>[4];    // 河（鳴かれた牌も含む。現物の判定用）
        readonly List<Meld>[] Ml = new List<Meld>[4];
        readonly bool[] Ri = new bool[4], Ipp = new bool[4], Furi = new bool[4];
        readonly int[][] RiRv = new int[4][];          // リーチ宣言時の各家の河の枚数（それ以降の打牌は現物）
        readonly int[] D = new int[4];
        readonly List<int> wall = new List<int>(), ura = new List<int>();
        int wi, wallLeft, pot, t, tile;
        bool rinNext, rs, afterCall;
        List<int> kk;
        Ph ph;
        Resp forced = Resp.Bot;
        List<int> forcedUse;

        public SimOutcome Result = new SimOutcome { N = 1 };

        // 自動対戦の集計用
        public readonly bool[] Called = new bool[4], Riichied = new bool[4];
        public List<int> Winners = new List<int>();
        public int From = -1;
        public int[] Deltas { get { return D; } }
        public bool IsRiichi(int q) { return Ri[q]; }
        public bool IsOpen(int q) { return Ml[q].Any(m => m.L != 'a'); }

        public SimGame(SimSetup st, Random rnd, BotParams p = null)
        {
            this.st = st;
            this.rnd = rnd;
            P = p ?? BotParams.Default;
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
                var PS = s.Pl[q];
                int size = PS.H.Count + (PS.Dw.HasValue ? 1 : 0);
                if (q == v)
                {
                    H[q] = new List<int>(PS.H);
                    if (PS.Dw.HasValue) H[q].Add(PS.Dw.Value);
                }
                else
                {
                    H[q] = pool.Skip(pi).Take(size).ToList();
                    pi += size;
                }
                Rv[q] = PS.Rv.Select(r => r.C).ToList();
                Ml[q] = new List<Meld>(PS.Ml);
                Ri[q] = PS.Ri;
                if (PS.Ri)
                {
                    int ri = Math.Max(0, PS.Rv.FindIndex(r => r.R));
                    RiRv[q] = s.Pl.Select(o => Math.Max(0, ri - (PS.Rv.Count - o.Rv.Count))).ToArray();
                    Ipp[q] = PS.Rv.Count > 0 && PS.Rv.Last().R;
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
                var PS = s.Pl[t];
                afterCall = !PS.Dw.HasValue;
                tile = afterCall ? 0 : t == v ? PS.Dw.Value : H[t].Last();
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
                Riichied[q] = true;
                Ipp[q] = true;
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

            // 鳴き（ポンを優先し、チーは下家のみ）
            if (wallLeft > 0)
            {
                if (fr == Resp.Pon || fr == Resp.Chi) { Call(v, q, fr == Resp.Pon ? 'p' : 'c', forcedUse, c); return; }
                for (int k = 1; k < 4; k++)
                {
                    int r = (q + k) % 4;
                    if (r == v && fr == Resp.Pass) continue;
                    var sm = H[r].Where(x => Tiles.Norm(x) == Tiles.Norm(c)).Take(2).ToList();
                    if (sm.Count == 2 && WantCall(r, 'p', sm, c)) { Call(r, q, 'p', sm, c); return; }
                }
                int n1 = (q + 1) % 4, nn = Tiles.Norm(c);
                if (!(n1 == v && fr == Resp.Pass) && nn < 40)
                {
                    int su = nn / 10, rk = nn % 10;
                    List<int> bestUse = null;
                    foreach (var ab in new[] { new[] { -2, -1 }, new[] { -1, 1 }, new[] { 1, 2 } })
                    {
                        int x = rk + ab[0], y = rk + ab[1];
                        if (x < 1 || y > 9) continue;
                        int? u = Tiles.Pk(H[n1], su * 10 + x), w = Tiles.Pk(H[n1], su * 10 + y);
                        if (!u.HasValue || !w.HasValue) continue;
                        var use = new List<int> { u.Value, w.Value };
                        if (WantCall(n1, 'c', use, c)) { bestUse = use; break; }
                    }
                    if (bestUse != null) { Call(n1, q, 'c', bestUse, c); return; }
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
            Called[r] = true;
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
            Winners.Add(q);
            From = q;
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
            Winners.AddRange(winners);
            From = from;
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
        // ボット：役の方針
        // =====================================================================

        static int[] Counts(List<int> h)
        {
            var a = new int[34];
            foreach (int c in h) a[Tiles.Ix(c)]++;
            return a;
        }

        static bool Simple(int i) { return i < 27 && i % 9 != 0 && i % 9 != 8; }

        bool YakuhaiIx(int q, int i) { return i >= 27 && Yakuhai(q, Tiles.Cd(i)); }

        /// <summary>副露の家の役の当て。門前は Any（リーチできる）、役のない鳴き手は None。</summary>
        Plan PlanOf(int q, List<Meld> ml, out int suit)
        {
            suit = -1;
            if (ml.All(m => m.L == 'a')) return Plan.Any;
            if (ml.Any(m => YakuhaiIx(q, Tiles.Ix(m.T[0])))) return Plan.Any;
            if (P.Atozuke && H[q].GroupBy(Tiles.Ix).Any(g => g.Count() >= 2 && YakuhaiIx(q, g.Key))) return Plan.Any;
            if (ml.All(m => m.T.All(c => Simple(Tiles.Ix(c))))) return Plan.Tanyao;
            var suits = ml.Select(m => Tiles.Ix(m.T[0])).Where(i => i < 27).Select(i => i / 9).Distinct().ToList();
            if (suits.Count <= 1)
            {
                suit = suits.Count == 1 ? suits[0] : DominantSuit(H[q]);
                return Plan.Honitsu;
            }
            return Plan.None;
        }

        static int DominantSuit(List<int> h)
        {
            var n = new int[3];
            foreach (int c in h) { int i = Tiles.Ix(c); if (i < 27) n[i / 9]++; }
            return Array.IndexOf(n, n.Max());
        }

        /// <summary>方針に合わない牌を数えない枚数配列（その方針で和了るための向聴数を測る）。</summary>
        static int[] Masked(int[] a, Plan plan, int suit)
        {
            if (plan == Plan.Any || plan == Plan.None) return a;
            var b = (int[])a.Clone();
            for (int i = 0; i < 34; i++)
                if (plan == Plan.Tanyao ? !Simple(i) : i < 27 && i / 9 != suit) b[i] = 0;
            return b;
        }

        static int PlanShanten(int[] a, int melds, Plan plan, int suit)
        {
            return Shanten.Calc(Masked(a, plan, suit), melds);
        }

        /// <summary>
        /// 鳴くか：役（役牌・タンヤオ・染め手）の当てがあり、鳴いて向聴数が進み、十分に早くなる場合。
        /// 他家がリーチしていて鳴いても遠いときは鳴かない。
        /// </summary>
        bool WantCall(int r, char l, List<int> use, int c)
        {
            if (Ri[r] || wallLeft <= 0) return false;
            int ci = Tiles.Ix(c);
            var a = Counts(H[r]);
            int melds = Ml[r].Count;
            int raw = Shanten.Calc(a, melds);
            if (raw < 0) return false;

            // 鳴いた後の手
            var a2 = (int[])a.Clone();
            foreach (int x in use) a2[Tiles.Ix(x)]--;
            var ml2 = new List<Meld>(Ml[r]) { new Meld { L = l, T = new List<int>(use) { c } } };
            int suit;
            var plan = PlanOf(r, ml2, out suit);
            if (plan == Plan.None) return false;
            bool yakuhaiPon = l == 'p' && YakuhaiIx(r, ci);
            if (plan == Plan.Honitsu)
            {
                int n = 0;
                for (int i = 0; i < 34; i++) if (i >= 27 || i / 9 == suit) n += a[i];
                n += Ml[r].Sum(m => m.T.Count);
                if (n < P.HonitsuMin) return false;
            }
            // 鳴いた後に 1 枚切った最良の向聴数
            int after = 99;
            for (int i = 0; i < 34; i++)
            {
                if (a2[i] == 0) continue;
                a2[i]--;
                after = Math.Min(after, PlanShanten(a2, melds + 1, plan, suit));
                a2[i]++;
            }
            bool threat = Threat(r) >= P.ThreatMin;
            if (threat && after >= 1) return false;
            if (yakuhaiPon) return after <= raw;
            int before = melds == 0 ? raw : PlanShanten(a, melds, plan, suit);
            return after < before && after <= P.CallMaxShanten;
        }

        // =====================================================================
        // ボット：打牌・押し引き
        // =====================================================================

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

        /// <summary>他家 p を何人分のリーチ相当の脅威とみなすか。</summary>
        double ThreatOf(int p)
        {
            if (Ri[p]) return 1;
            int open = Ml[p].Count(m => m.L != 'a');
            if (open >= 3) return P.Open3Threat;
            if (open == 2 && Rv[p].Count >= 8) return P.Open2Threat;
            if (open == 1 && Rv[p].Count >= 10) return P.Open1Threat;
            return 0;
        }

        double Threat(int q)
        {
            double s = 0;
            for (int p = 0; p < 4; p++) if (p != q) s += ThreatOf(p);
            return s;
        }

        double DangerSum(int q, int c, int[] vis)
        {
            double s = 0;
            for (int p = 0; p < 4; p++)
            {
                if (p == q) continue;
                double w = ThreatOf(p);
                if (w > 0) s += w * Danger(p, c, vis) * (Ri[p] ? 1 : OpenFactor(p, c));
            }
            return s;
        }

        /// <summary>
        /// 副露している家の鳴きから見た牌 c の危険度の倍率。副露がすべて中張牌ならタンヤオとみて么九牌は安全寄り、
        /// 副露が 1 色（と字牌）なら染め手とみて他の色は安全寄り、その色と字牌は危険寄りにする。
        /// </summary>
        double OpenFactor(int p, int c)
        {
            if (!P.ReadOpen) return 1;
            var ms = Ml[p].Where(m => m.L != 'a').ToList();
            int i = Tiles.Ix(c);
            if (ms.Count == 0) return 1;
            if (ms.Any(m => YakuhaiIx(p, Tiles.Ix(m.T[0])))) return 1;
            if (ms.All(m => m.T.All(x => Simple(Tiles.Ix(x))))) return Simple(i) ? 1.2 : 0.3;
            var suits = ms.Select(m => Tiles.Ix(m.T[0])).Where(x => x < 27).Select(x => x / 9).Distinct().ToList();
            if (suits.Count == 1) return i < 27 && i / 9 != suits[0] ? 0.2 : 1.4;
            return 1;
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
            int suit;
            var plan = PlanOf(q, Ml[q], out suit);
            var sh = new Dictionary<int, int>();
            foreach (int c in codes)
            {
                int i = Tiles.Ix(c);
                a[i]--;
                sh[c] = PlanShanten(a, melds, plan, suit);
                a[i]++;
            }
            int min = sh.Values.Min();
            var vis = Visible(q);
            double threat = Threat(q);
            var dg = codes.ToDictionary(c => c, c => threat > 0 ? DangerSum(q, c, vis) : 0);

            // 攻めるときの打牌：向聴数 → 受け入れ枚数 → 危険度 → 残したい度合い
            var best = codes.Where(c => sh[c] == min).ToList();
            var uk = new Dictionary<int, int>();
            if (min <= 2 && best.Count > 1)
                foreach (int c in best)
                {
                    int i = Tiles.Ix(c);
                    a[i]--;
                    uk[c] = Ukeire(Masked(a, plan, suit), melds, min, vis);
                    a[i]++;
                }
            int pick = best.OrderByDescending(c => uk.ContainsKey(c) ? uk[c] : 0).ThenBy(c => dg[c]).ThenBy(c => Keep(q, c, a)).First();

            if (threat >= P.ThreatMin && !Push(q, min, plan, suit, pick, dg[pick], a, vis))
            {
                // ベタオリ：危険度の低い牌、同じなら向聴数を保てる牌
                return codes.OrderBy(c => dg[c]).ThenBy(c => sh[c]).ThenBy(c => Keep(q, c, a)).First();
            }

            if (min == 0 && !Ri[q] && Ml[q].All(m => m.L == 'a') && wallLeft >= 4 && st.S.Sc[q] + D[q] >= 1000)
            {
                riichi = !Dama(q, pick, a, vis);
                // 待ちが少ないときは追いかけリーチせず、役がなければ安全な牌だけ押す
                if (riichi && threat >= P.ThreatMin && WaitsAfter(q, pick, a, vis) < P.ChaseWaits)
                {
                    riichi = false;
                    int i = Tiles.Ix(pick);
                    a[i]--;
                    bool yaku = TenpaiValue(q, a, vis, false) > 0;
                    a[i]++;
                    if (!yaku && dg[pick] > 2)
                        return codes.OrderBy(c => dg[c]).ThenBy(c => sh[c]).ThenBy(c => Keep(q, c, a)).First();
                }
            }
            return pick;
        }

        /// <summary>脅威があるときに押すか。テンパイは打点・待ち・危険度、1 向聴は打点の見込みと巡目で決める。</summary>
        bool Push(int q, int min, Plan plan, int suit, int pick, double danger, int[] a, int[] vis)
        {
            if (min <= 0)
            {
                int i = Tiles.Ix(pick);
                a[i]--;
                int waits = Ukeire(Masked(a, plan, suit), Ml[q].Count, 0, vis);
                int value = TenpaiValue(q, a, vis, Ml[q].All(m => m.L == 'a'));
                a[i]++;
                if (value == 0) return danger <= 2;   // 役なし（形式テンパイ）は安全な牌だけ
                return !(value < P.TenpaiFoldValue && waits <= P.TenpaiFoldWaits && danger >= P.TenpaiFoldDanger);
            }
            if (min == 1)
                return HanEstimate(q, a, plan) >= P.PushHan1 && wallLeft >= P.PushWall1 && danger <= P.PushDanger1;
            return false;
        }

        int WaitsAfter(int q, int pick, int[] a, int[] vis)
        {
            int i = Tiles.Ix(pick);
            a[i]--;
            int w = Ukeire(a, Ml[q].Count, 0, vis);
            a[i]++;
            return w;
        }

        /// <summary>役ありのテンパイでダマにするか（ダマで高い、または待ちがない）。</summary>
        bool Dama(int q, int pick, int[] a, int[] vis)
        {
            int i = Tiles.Ix(pick);
            a[i]--;
            int dama = TenpaiValue(q, a, vis, false);
            int waits = Ukeire(a, Ml[q].Count, 0, vis);
            a[i]++;
            return waits == 0 || dama >= P.DamaValue;
        }

        /// <summary>テンパイの手（13 枚の枚数配列）の和了打点の平均（ロン、リーチするなら立直 1 飜込み）。役がなければ 0。</summary>
        int TenpaiValue(int q, int[] a, int[] vis, bool riichi)
        {
            var hand = new List<int>();
            for (int j = 0; j < 34; j++) for (int k = 0; k < a[j]; k++) hand.Add(Tiles.Cd(j));
            // 赤5 は手牌どおりに戻す
            foreach (int c in H[q].Where(x => x > 50))
            {
                int ix = hand.IndexOf(Tiles.Norm(c));
                if (ix >= 0 && a[Tiles.Ix(c)] > 0) hand[ix] = c;
            }
            long sum = 0;
            int n = 0;
            bool oya = q == dealer;
            for (int j = 0; j < 34; j++)
            {
                if (vis[j] >= 4) continue;
                a[j]++;
                bool win = Shanten.Calc(a, Ml[q].Count) < 0;
                a[j]--;
                if (!win) continue;
                var w = new List<int>(hand) { Tiles.Cd(j) };
                var r = EvalStatic(st, q, w, Ml[q], Tiles.Cd(j), false, wallLeft, false, riichi, false);
                int left = 4 - vis[j];
                n += left;
                if (r != null) sum += (long)left * r.Base * (oya ? 6 : 4);
            }
            return n == 0 ? 0 : (int)(sum / n);
        }

        /// <summary>1 向聴の手の飜数の見込み（ドラ・赤・役牌・リーチ・タンヤオ・染め手）。</summary>
        double HanEstimate(int q, int[] a, Plan plan)
        {
            double han = 0;
            foreach (int d in st.DoraInd)
            {
                int di = Scoring.DoraOf(d);
                han += a[di] + Ml[q].Sum(m => m.T.Count(c => Tiles.Ix(c) == di));
            }
            han += H[q].Count(c => c > 50) + Ml[q].Sum(m => m.T.Count(c => c > 50));
            for (int i = 27; i < 34; i++) if (YakuhaiIx(q, i) && a[i] >= 3) han += 1;
            han += Ml[q].Count(m => YakuhaiIx(q, Tiles.Ix(m.T[0])));
            bool menzen = Ml[q].All(m => m.L == 'a');
            if (menzen) han += 1;   // リーチ
            if (plan == Plan.Tanyao) han += 1;
            if (plan == Plan.Honitsu) han += menzen ? 3 : 2;
            if (menzen && Enumerable.Range(0, 34).All(i => a[i] == 0 || Simple(i))) han += 1;
            return han;
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

        /// <summary>
        /// 他家 p に対して牌 c を切る危険度（現物 0、字牌・筋・壁は低め、無筋の中張牌が最も高い）。
        /// リーチしていない家に対しては、その家の河だけを現物とする。
        /// </summary>
        int Danger(int p, int c, int[] vis)
        {
            int i = Tiles.Ix(c);
            var safe = new HashSet<int>(Rv[p].Select(Tiles.Ix));
            if (RiRv[p] != null && Ri[p])
                for (int o = 0; o < 4; o++)
                    if (o != p)
                        for (int j = RiRv[p][o]; j < Rv[o].Count; j++) safe.Add(Tiles.Ix(Rv[o][j]));
            if (safe.Contains(i)) return 0;
            if (i >= 27) return vis[i] >= 3 ? 1 : vis[i] == 2 ? 3 : 6;
            int r = i % 9, b = i - r;
            bool lo = r - 3 < 0 || safe.Contains(b + r - 3), hi = r + 3 > 8 || safe.Contains(b + r + 3);
            // 壁：隣の牌が 4 枚見えていれば、その側の両面待ちはない
            bool wallLo = r - 1 < 0 || vis[i - 1] >= 4;
            bool wallHi = r + 1 > 8 || vis[i + 1] >= 4;
            lo |= wallLo;
            hi |= wallHi;
            bool term = r == 0 || r == 8, two = r == 1 || r == 7;
            if (lo && hi) return term ? 2 : two ? 3 : 4;
            if (lo || hi) return term ? 5 : two ? 7 : 9;
            return term ? 6 : two ? 8 : 12;
        }
    }
}
