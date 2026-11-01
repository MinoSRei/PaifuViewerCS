using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>
    /// 雀魂の牌山（paishan、136 枚）を牌譜の実際のツモ順と突き合わせ、
    /// 分岐で使うツモ列（通常・嶺上）を牌山の最後まで延長する。
    /// 牌山の並び順は牌譜側で検証し、一致しなければ使わない。
    /// </summary>
    public static class WallAligner
    {
        public const int Total = 136, Dead = 14, Haipai = 52;

        // 王牌 14 枚のうち嶺上牌が取られる位置の候補（王牌内の添字）。牌譜の嶺上ツモと一致するものを採用する
        static readonly int[][] RinshanPatterns =
        {
            new[] { 13, 12, 11, 10 },
            new[] { 0, 1, 2, 3 },
            new[] { 12, 13, 10, 11 },
            new[] { 1, 0, 3, 2 },
        };

        public static void Apply(RoundData rd, List<int> paishan)
        {
            if (paishan == null || paishan.Count == 0) { rd.WallStatus = "牌山データなし"; return; }
            if (paishan.Count != Total) { rd.WallStatus = "牌山データの枚数が不正（" + paishan.Count + "枚）のため未使用"; return; }

            foreach (bool reverse in new[] { false, true })
            {
                var seq = reverse ? Enumerable.Reverse(paishan).ToList() : paishan;
                int o = FindDraws(seq, rd.LW);
                if (o < 0) continue;

                var dead = seq.Skip(Total - Dead).ToList();
                var pat = RinshanPatterns.FirstOrDefault(pt => rd.RW.Count <= pt.Length && rd.RW.Select((c, j) => dead[pt[j]] == c).All(x => x));
                bool guessed = rd.RW.Count == 0;
                rd.LW = seq.Skip(o).Take(Total - Dead - o).ToList();
                if (pat != null) rd.RW = pat.Select(j => dead[j]).ToList();
                rd.HasWall = true;
                rd.Wall = seq;
                rd.WallStart = o;
                rd.RinshanPos = pat;
                rd.WallStatus = "牌山データあり（分岐のツモも牌山どおり"
                    + (pat == null ? "。嶺上牌は牌譜の分のみ" : guessed ? "。嶺上牌の位置は推定" : "") + "）";
                return;
            }
            rd.WallStatus = "牌山データが牌譜のツモと一致しないため未使用";
        }

        /// <summary>牌譜の通常ツモ列が牌山の生牌部分に連続して現れる位置。配牌直後（52 枚目）を優先する。</summary>
        static int FindDraws(List<int> seq, List<int> lw)
        {
            if (lw.Count == 0) return -1;
            int limit = Total - Dead;
            if (Matches(seq, lw, Haipai, limit)) return Haipai;
            for (int o = 0; o + lw.Count <= limit; o++)
                if (Matches(seq, lw, o, limit)) return o;
            return -1;
        }

        static bool Matches(List<int> seq, List<int> lw, int o, int limit)
        {
            if (o + lw.Count > limit) return false;
            for (int j = 0; j < lw.Count; j++)
                if (seq[o + j] != lw[j]) return false;
            return true;
        }
    }
}
