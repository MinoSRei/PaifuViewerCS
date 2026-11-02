using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaifuViewer
{
    /// <summary>
    /// ボットどうしの自動対戦。東場の 1 局を配牌から打ち切り、1 人あたりの和了率・放銃率・副露率・立直率と流局率を集計する。
    /// ボットの強さの調整（BotParams）に使う。`dotnet run -- --selfplay 4000` で実行できる。
    /// </summary>
    public static class SelfPlay
    {
        public class Stats
        {
            public long Rounds, Seats, Win, DealIn, Called, Riichi, Draw, WinPoints, DealInPoints;
            public long DealInWhileRiichi, DealInToRiichi, DealInToOpen, DealInToDama;   // 放銃の内訳
            public double WinRate { get { return (double)Win / Seats; } }
            public double DealInRate { get { return (double)DealIn / Seats; } }
            public double CallRate { get { return (double)Called / Seats; } }
            public double RiichiRate { get { return (double)Riichi / Seats; } }
            public double DrawRate { get { return (double)Draw / Rounds; } }
        }

        public static Stats Run(int rounds, int seed, BotParams p = null)
        {
            var total = new Stats();
            var lk = new object();
            Parallel.For(0, rounds, () => (rnd: (Random)null, s: new Stats()), (i, _, loc) =>
            {
                var rnd = loc.rnd ?? new Random(seed * 7919 + i);
                var setup = Deal(rnd);
                var g = new SimGame(setup, rnd, p);
                g.Play(null);
                var s = loc.s;
                s.Rounds++;
                s.Seats += 4;
                for (int q = 0; q < 4; q++)
                {
                    if (g.Called[q]) s.Called++;
                    if (g.Riichied[q]) s.Riichi++;
                }
                if (g.Winners.Count == 0) s.Draw++;
                foreach (int w in g.Winners) { s.Win++; s.WinPoints += Math.Max(0, g.Deltas[w]); }
                if (g.Winners.Count > 0 && !g.Winners.Contains(g.From))
                {
                    s.DealIn++;
                    s.DealInPoints += -g.Deltas[g.From];
                    int w0 = g.Winners[0];
                    if (g.IsRiichi(g.From)) s.DealInWhileRiichi++;
                    else if (g.IsRiichi(w0)) s.DealInToRiichi++;
                    else if (g.IsOpen(w0)) s.DealInToOpen++;
                    else s.DealInToDama++;
                }
                return (rnd, s);
            }, loc =>
            {
                lock (lk)
                {
                    var s = loc.s;
                    total.Rounds += s.Rounds; total.Seats += s.Seats; total.Win += s.Win; total.DealIn += s.DealIn;
                    total.Called += s.Called; total.Riichi += s.Riichi; total.Draw += s.Draw;
                    total.WinPoints += s.WinPoints; total.DealInPoints += s.DealInPoints;
                    total.DealInWhileRiichi += s.DealInWhileRiichi; total.DealInToRiichi += s.DealInToRiichi;
                    total.DealInToOpen += s.DealInToOpen; total.DealInToDama += s.DealInToDama;
                }
            });
            return total;
        }

        public static string Report(Stats s)
        {
            var b = new StringBuilder();
            b.AppendLine("局数 " + s.Rounds);
            b.AppendLine("和了率 " + (s.WinRate * 100).ToString("0.0") + "%　平均打点 " + (s.Win == 0 ? 0 : s.WinPoints / s.Win));
            b.AppendLine("放銃率 " + (s.DealInRate * 100).ToString("0.0") + "%　平均放銃点 " + (s.DealIn == 0 ? 0 : s.DealInPoints / s.DealIn));
            if (s.DealIn > 0)
                b.AppendLine("　内訳：自分のリーチ後 " + Pct(s.DealInWhileRiichi, s.DealIn) + "　リーチへ " + Pct(s.DealInToRiichi, s.DealIn)
                    + "　副露へ " + Pct(s.DealInToOpen, s.DealIn) + "　ダマへ " + Pct(s.DealInToDama, s.DealIn));
            b.AppendLine("副露率 " + (s.CallRate * 100).ToString("0.0") + "%");
            b.AppendLine("立直率 " + (s.RiichiRate * 100).ToString("0.0") + "%");
            b.AppendLine("流局率 " + (s.DrawRate * 100).ToString("0.0") + "%");
            return b.ToString();
        }

        static string Pct(long a, long n) { return (100.0 * a / n).ToString("0") + "%"; }

        /// <summary>東場のランダムな配牌（親もランダム）。0 番の家の手牌だけ決め、他家の手牌と牌山は SimGame が配る。</summary>
        static SimSetup Deal(Random rnd)
        {
            var tiles = new List<int>();
            for (int i = 0; i < 34; i++)
                for (int j = 0; j < 4; j++)
                    tiles.Add(j == 0 && i < 27 && i % 9 == 4 ? 50 + i / 9 + 1 : Tiles.Cd(i));
            for (int i = tiles.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int x = tiles[i]; tiles[i] = tiles[j]; tiles[j] = x;
            }
            int dealer = rnd.Next(4);
            var pl = new PlayerState[4];
            for (int q = 0; q < 4; q++)
            {
                pl[q] = new PlayerState { H = tiles.Skip(q * 13).Take(13).ToList() };
                Tiles.Sort(pl[q].H);
            }
            var s = new Step
            {
                Msg = "", T = dealer, Ph = "w", Sc = new[] { 25000, 25000, 25000, 25000 },
                Dn = 1, Wall = 70, Dealer = dealer, Pl = pl, Jn = 1
            };
            var k = new List<object>
            {
                new List<object> { dealer, 0, 0 },
                new List<object> { 25000, 25000, 25000, 25000 },
                new List<object> { tiles[52] },
                new List<object>()
            };
            return new SimSetup(s, k, 0);
        }
    }
}
