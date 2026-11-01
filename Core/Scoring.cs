using System;
using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>和了の状況（手牌・副露・和了牌と場の条件）。</summary>
    public class WinContext
    {
        public List<int> Hand;          // 門前の手牌（和了牌を含む）
        public List<Meld> Melds = new List<Meld>();
        public int WinTile;
        public bool Tsumo;
        public int SeatWind, RoundWind; // 0=東 .. 3=北
        public bool Riichi, DoubleRiichi, Ippatsu, Haitei, Houtei, Rinshan, Tenhou, Chiihou;
        public List<int> DoraInd = new List<int>(), UraInd;   // UraInd が null なら裏ドラ不明
    }

    /// <summary>点数計算の結果。Points は Tsumo なら [親の支払い, 子の支払い]（親の和了は子の支払いのみ）。</summary>
    public class ScoreResult
    {
        public List<KeyValuePair<string, int>> Yaku = new List<KeyValuePair<string, int>>();
        public int Han, Fu, Yakuman, Base;
        public string Limit = "";
        public bool UraUnknown;

        public string YakuText()
        {
            return string.Join("、", Yaku.Select(y => y.Key + (Yakuman > 0 ? (y.Value > 1 ? "(ダブル役満)" : "(役満)") : "(" + y.Value + "飜)")));
        }

        public string HanFu()
        {
            if (Yakuman > 0) return Yakuman > 1 ? Yakuman + "倍役満" : "役満";
            return (Limit != "" && Han >= 5 ? "" : Fu + "符") + Han + "飜" + (Limit != "" ? " " + Limit : "");
        }
    }

    /// <summary>
    /// 和了形の役・符・飜・点数を求める（喰いタン・赤あり。数え役満あり。包・切り上げ満貫なし）。
    /// 役がなければ null を返す。
    /// </summary>
    public static class Scoring
    {
        // 面子: Kind 0=順子 1=刻子 2=槓子。Ix は先頭の種類インデックス
        struct G
        {
            public int Kind, Ix;
            public bool Open;
            public G(int kind, int ix, bool open) { Kind = kind; Ix = ix; Open = open; }
        }

        static bool Yao(int i) { return i >= 27 || i % 9 == 0 || i % 9 == 8; }
        static bool Honor(int i) { return i >= 27; }
        static bool GroupHasYao(G g) { return g.Kind == 0 ? Yao(g.Ix) || Yao(g.Ix + 2) : Yao(g.Ix); }

        /// <summary>ドラ表示牌 → ドラの種類インデックス。</summary>
        public static int DoraOf(int ind)
        {
            int i = Tiles.Ix(ind);
            if (i < 27) return i / 9 * 9 + (i % 9 + 1) % 9;
            if (i < 31) return 27 + (i - 27 + 1) % 4;
            return 31 + (i - 31 + 1) % 3;
        }

        public static ScoreResult Evaluate(WinContext w)
        {
            var a = new int[34];
            foreach (int c in w.Hand) a[Tiles.Ix(c)]++;
            int wi = Tiles.Ix(w.WinTile);
            bool menzen = w.Melds.All(m => m.L == 'a');
            var meldGroups = w.Melds.Select(m => new G(m.L == 'c' ? 0 : m.L == 'p' ? 1 : 2,
                m.L == 'c' ? m.T.Min(t => Tiles.Ix(t)) : Tiles.Ix(m.T[0]), m.L != 'a')).ToList();

            ScoreResult best = null;
            Action<ScoreResult> take = r =>
            {
                if (r == null) return;
                if (best == null || r.Base > best.Base || r.Base == best.Base && r.Han > best.Han) best = r;
            };

            // 役満の特殊形
            if (w.Melds.Count == 0 && w.Hand.Count == 14)
            {
                int[] k13 = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
                if (k13.All(j => a[j] > 0) && k13.Sum(j => a[j]) == 14)
                {
                    var r = new ScoreResult();
                    if (a[wi] == 2) r.Yaku.Add(new KeyValuePair<string, int>("国士無双十三面待ち", 2));
                    else r.Yaku.Add(new KeyValuePair<string, int>("国士無双", 1));
                    AddSituationalYakuman(w, r);
                    take(Finish(r, w));
                }
                if (a.Count(x => x == 2) == 7) take(Chiitoi(w, a));
            }

            // 一般形：雀頭と面子の分け方をすべて試す
            int need = 4 - w.Melds.Count;
            for (int p = 0; p < 34; p++)
            {
                if (a[p] < 2) continue;
                a[p] -= 2;
                var outs = new List<List<G>>();
                Split(a, 0, new List<G>(), outs);
                a[p] += 2;
                foreach (var gs in outs)
                {
                    if (gs.Count != need) continue;
                    // 和了牌がどの面子（または雀頭）を完成させたか
                    if (p == wi) take(Standard(w, a, p, gs, meldGroups, -1, menzen));
                    for (int g = 0; g < gs.Count; g++)
                    {
                        var x = gs[g];
                        if (x.Kind == 0 ? wi >= x.Ix && wi <= x.Ix + 2 : wi == x.Ix)
                            take(Standard(w, a, p, gs, meldGroups, g, menzen));
                    }
                }
            }
            return best;
        }

        static void Split(int[] a, int i, List<G> cur, List<List<G>> outs)
        {
            while (i < 34 && a[i] == 0) i++;
            if (i >= 34) { outs.Add(new List<G>(cur)); return; }
            if (a[i] >= 3)
            {
                a[i] -= 3; cur.Add(new G(1, i, false));
                Split(a, i, cur, outs);
                cur.RemoveAt(cur.Count - 1); a[i] += 3;
            }
            if (i < 27 && i % 9 < 7 && a[i + 1] > 0 && a[i + 2] > 0)
            {
                a[i]--; a[i + 1]--; a[i + 2]--; cur.Add(new G(0, i, false));
                Split(a, i, cur, outs);
                cur.RemoveAt(cur.Count - 1); a[i]++; a[i + 1]++; a[i + 2]++;
            }
        }

        static void Y(ScoreResult r, string name, int han) { r.Yaku.Add(new KeyValuePair<string, int>(name, han)); }

        /// <summary>天和・地和（役満のときに足す）。</summary>
        static void AddSituationalYakuman(WinContext w, ScoreResult r)
        {
            if (w.Tenhou) Y(r, "天和", 1);
            if (w.Chiihou) Y(r, "地和", 1);
        }

        /// <summary>立直・一発・ツモ・海底などの状況役（1 飜役）。</summary>
        static void AddSituational(WinContext w, ScoreResult r, bool menzen)
        {
            if (w.DoubleRiichi) Y(r, "ダブル立直", 2);
            else if (w.Riichi) Y(r, "立直", 1);
            if (w.Ippatsu) Y(r, "一発", 1);
            if (menzen && w.Tsumo) Y(r, "門前清自摸和", 1);
            if (w.Rinshan) Y(r, "嶺上開花", 1);
            else if (w.Haitei) Y(r, "海底摸月", 1);
            if (w.Houtei) Y(r, "河底撈魚", 1);
        }

        static ScoreResult Chiitoi(WinContext w, int[] a)
        {
            var r = new ScoreResult { Fu = 25 };
            var ix = Enumerable.Range(0, 34).Where(j => a[j] > 0).ToList();
            if (ix.All(Honor)) { Y(r, "字一色", 1); AddSituationalYakuman(w, r); return Finish(r, w); }
            if (w.Tenhou || w.Chiihou) { AddSituationalYakuman(w, r); return Finish(r, w); }
            AddSituational(w, r, true);
            Y(r, "七対子", 2);
            if (ix.All(j => !Yao(j))) Y(r, "断幺九", 1);
            if (ix.All(Yao)) Y(r, "混老頭", 2);
            AddFlush(r, ix, true);
            return Finish(r, w);
        }

        static void AddFlush(ScoreResult r, List<int> ix, bool menzen)
        {
            var suits = ix.Where(j => !Honor(j)).Select(j => j / 9).Distinct().ToList();
            if (suits.Count != 1) return;
            if (ix.Any(Honor)) Y(r, "混一色", menzen ? 3 : 2);
            else Y(r, "清一色", menzen ? 6 : 5);
        }

        /// <param name="wg">和了牌で完成した面子の添字（-1 は単騎）。</param>
        static ScoreResult Standard(WinContext w, int[] a, int p, List<G> hand, List<G> melds, int wg, bool menzen)
        {
            int wi = Tiles.Ix(w.WinTile);
            // ロンで完成した刻子は明刻扱い
            var gs = hand.Select((g, j) => j == wg && g.Kind == 1 && !w.Tsumo ? new G(1, g.Ix, true) : g).ToList();
            gs.AddRange(melds);
            var all = gs;
            var ix = Enumerable.Range(0, 34).Where(j => a[j] > 0).Concat(melds.SelectMany(g => g.Kind == 0 ? new[] { g.Ix, g.Ix + 1, g.Ix + 2 } : new[] { g.Ix })).Distinct().ToList();
            int seatW = 27 + w.SeatWind, roundW = 27 + w.RoundWind;
            var trip = all.Where(g => g.Kind >= 1).ToList();
            int closedTrip = trip.Count(g => !g.Open);
            int kans = all.Count(g => g.Kind == 2);

            // 待ちの形
            string wait = "tanki";
            if (wg >= 0)
            {
                var x = hand[wg];
                if (x.Kind == 1) wait = "shanpon";
                else if (wi == x.Ix + 1) wait = "kanchan";
                else if (wi == x.Ix && x.Ix % 9 == 6 || wi == x.Ix + 2 && x.Ix % 9 == 0) wait = "penchan";
                else wait = "ryanmen";
            }

            // ---- 役満 ----
            var r = new ScoreResult();
            if (closedTrip == 4) Y(r, wait == "tanki" ? "四暗刻単騎" : "四暗刻", wait == "tanki" ? 2 : 1);
            int dragons = trip.Count(g => g.Ix >= 31), winds = trip.Count(g => g.Ix >= 27 && g.Ix <= 30);
            if (dragons == 3) Y(r, "大三元", 1);
            if (winds == 4) Y(r, "大四喜", 2);
            else if (winds == 3 && p >= 27 && p <= 30) Y(r, "小四喜", 1);
            if (ix.All(Honor)) Y(r, "字一色", 1);
            int[] green = { 19, 20, 21, 23, 25, 32 };
            if (ix.All(j => green.Contains(j))) Y(r, "緑一色", 1);
            if (ix.All(j => Yao(j) && !Honor(j))) Y(r, "清老頭", 1);
            if (kans == 4) Y(r, "四槓子", 1);
            if (menzen && w.Melds.Count == 0 && ix.All(j => !Honor(j)) && ix.Select(j => j / 9).Distinct().Count() == 1)
            {
                int s0 = ix[0] / 9 * 9;
                int[] cnt = Enumerable.Range(0, 9).Select(k => a[s0 + k]).ToArray();
                int[] baseC = { 3, 1, 1, 1, 1, 1, 1, 1, 3 };
                if (cnt.Select((c, k) => c - baseC[k]).All(d => d >= 0))
                {
                    cnt[wi - s0]--;
                    Y(r, cnt.SequenceEqual(baseC) ? "純正九蓮宝燈" : "九蓮宝燈", cnt.SequenceEqual(baseC) ? 2 : 1);
                }
            }
            if (r.Yaku.Count > 0 || w.Tenhou || w.Chiihou) { AddSituationalYakuman(w, r); return Finish(r, w); }

            // ---- 符 ----
            bool yakuPair = p >= 31 || p == seatW || p == roundW;
            bool pinfu = menzen && w.Melds.Count == 0 && all.All(g => g.Kind == 0) && !yakuPair && wait == "ryanmen";
            int fu = 20;
            if (!pinfu)
            {
                foreach (var g in all)
                {
                    if (g.Kind == 0) continue;
                    int f = 2 * (Yao(g.Ix) ? 2 : 1) * (g.Open ? 1 : 2) * (g.Kind == 2 ? 4 : 1);
                    fu += f;
                }
                if (p >= 31) fu += 2;
                if (p == seatW) fu += 2;
                if (p == roundW) fu += 2;
                if (wait == "kanchan" || wait == "penchan" || wait == "tanki") fu += 2;
                if (w.Tsumo) fu += 2;
            }
            if (menzen && !w.Tsumo) fu += 10;
            if (!menzen && fu == 20) fu = 30;
            r.Fu = (fu + 9) / 10 * 10;

            // ---- 通常役 ----
            AddSituational(w, r, menzen);
            if (pinfu) Y(r, "平和", 1);
            if (ix.All(j => !Yao(j))) Y(r, "断幺九", 1);
            if (menzen)
            {
                var seqs = all.Where(g => g.Kind == 0).GroupBy(g => g.Ix).Select(x => x.Count()).ToList();
                int peiko = seqs.Sum(c => c / 2);
                if (peiko >= 2) Y(r, "二盃口", 3);
                else if (peiko == 1) Y(r, "一盃口", 1);
            }
            foreach (var g in trip)
            {
                if (g.Ix == 31) Y(r, "役牌 白", 1);
                if (g.Ix == 32) Y(r, "役牌 發", 1);
                if (g.Ix == 33) Y(r, "役牌 中", 1);
                if (g.Ix == seatW) Y(r, "自風 " + Tiles.WD[w.SeatWind], 1);
                if (g.Ix == roundW) Y(r, "場風 " + Tiles.WD[w.RoundWind], 1);
            }
            var seqIx = all.Where(g => g.Kind == 0).Select(g => g.Ix).ToList();
            if (Enumerable.Range(0, 7).Any(k => seqIx.Contains(k) && seqIx.Contains(9 + k) && seqIx.Contains(18 + k)))
                Y(r, "三色同順", menzen ? 2 : 1);
            if (Enumerable.Range(0, 3).Any(s => seqIx.Contains(s * 9) && seqIx.Contains(s * 9 + 3) && seqIx.Contains(s * 9 + 6)))
                Y(r, "一気通貫", menzen ? 2 : 1);
            bool allYaoGroups = all.All(GroupHasYao) && Yao(p);
            if (allYaoGroups && seqIx.Count > 0)
            {
                if (ix.Any(Honor)) Y(r, "混全帯幺九", menzen ? 2 : 1);
                else Y(r, "純全帯幺九", menzen ? 3 : 2);
            }
            if (trip.Count == 4) Y(r, "対々和", 2);
            if (closedTrip == 3) Y(r, "三暗刻", 2);
            var tripIx = trip.Select(g => g.Ix).ToList();
            if (Enumerable.Range(0, 9).Any(k => tripIx.Contains(k) && tripIx.Contains(9 + k) && tripIx.Contains(18 + k)))
                Y(r, "三色同刻", 2);
            if (kans == 3) Y(r, "三槓子", 2);
            if (dragons == 2 && p >= 31) Y(r, "小三元", 2);
            if (ix.All(Yao)) Y(r, "混老頭", 2);
            AddFlush(r, ix, menzen);
            return Finish(r, w);
        }

        /// <summary>ドラを数え、飜・基本点を確定する。役がなければ null。</summary>
        static ScoreResult Finish(ScoreResult r, WinContext w)
        {
            if (r.Yaku.Count == 0) return null;
            if (r.Yaku.Any(y => IsYakuman(y.Key)))
            {
                r.Yaku = r.Yaku.Where(y => IsYakuman(y.Key)).ToList();
                r.Yakuman = r.Yaku.Sum(y => y.Value);
                r.Base = 8000 * r.Yakuman;
                return r;
            }
            var tiles = new List<int>(w.Hand);
            foreach (var m in w.Melds) tiles.AddRange(m.T);
            int dora = w.DoraInd.Sum(d => tiles.Count(t => Tiles.Ix(t) == DoraOf(d)));
            int aka = tiles.Count(t => t > 50);
            int ura = 0;
            if ((w.Riichi || w.DoubleRiichi) && w.UraInd != null) ura = w.UraInd.Sum(d => tiles.Count(t => Tiles.Ix(t) == DoraOf(d)));
            r.UraUnknown = (w.Riichi || w.DoubleRiichi) && w.UraInd == null;
            if (dora > 0) Y(r, "ドラ", dora);
            if (aka > 0) Y(r, "赤ドラ", aka);
            if (ura > 0) Y(r, "裏ドラ", ura);
            r.Han = r.Yaku.Sum(y => y.Value);
            int b = r.Fu * (1 << (2 + Math.Min(r.Han, 20)));
            if (r.Han >= 13) { r.Base = 8000; r.Limit = "数え役満"; }
            else if (r.Han >= 11) { r.Base = 6000; r.Limit = "三倍満"; }
            else if (r.Han >= 8) { r.Base = 4000; r.Limit = "倍満"; }
            else if (r.Han >= 6) { r.Base = 3000; r.Limit = "跳満"; }
            else if (r.Han >= 5 || b >= 2000) { r.Base = 2000; r.Limit = "満貫"; }
            else r.Base = b;
            return r;
        }

        static readonly HashSet<string> YakumanNames = new HashSet<string>
        {
            "国士無双", "国士無双十三面待ち", "四暗刻", "四暗刻単騎", "大三元", "小四喜", "大四喜", "字一色", "緑一色",
            "清老頭", "四槓子", "九蓮宝燈", "純正九蓮宝燈", "天和", "地和"
        };
        static bool IsYakuman(string n) { return YakumanNames.Contains(n); }

        static int Up(int x) { return (x + 99) / 100 * 100; }

        /// <summary>
        /// 点数の移動を求める。from はロンの放銃者（ツモなら winner と同じ）。
        /// 本場は 1 本につき 300 点、供託は和了者が受け取る。
        /// </summary>
        public static int[] Deltas(ScoreResult r, int winner, int from, int dealer, int honba, int kyotaku, out string text)
        {
            var d = new int[4];
            bool oya = winner == dealer;
            if (from != winner)
            {
                int pay = Up(r.Base * (oya ? 6 : 4));
                d[from] -= pay + honba * 300;
                d[winner] += pay + honba * 300;
                text = pay + "点";
            }
            else if (oya)
            {
                int each = Up(r.Base * 2);
                for (int q = 0; q < 4; q++) if (q != winner) { d[q] -= each + honba * 100; d[winner] += each + honba * 100; }
                text = each + "点∀";
            }
            else
            {
                int ko = Up(r.Base), ko2 = Up(r.Base * 2);
                for (int q = 0; q < 4; q++)
                {
                    if (q == winner) continue;
                    int x = (q == dealer ? ko2 : ko) + honba * 100;
                    d[q] -= x; d[winner] += x;
                }
                text = ko + "-" + ko2 + "点";
            }
            d[winner] += kyotaku;
            return d;
        }
    }
}
