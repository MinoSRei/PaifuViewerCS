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

        // 王牌のうちドラ表示牌・裏ドラ表示牌の位置（1 枚目、カンで増える 2 枚目以降の順）。
        // 雀魂の牌山で確認した並び。牌譜のドラ表示牌と一致した場合のみ使う
        static readonly int[] DoraPattern = { 9, 7, 5, 3, 1 }, UraPattern = { 8, 6, 4, 2, 0 };

        /// <param name="dora">牌譜に記録されたドラ表示牌（照合に使う）。</param>
        /// <param name="ura">牌譜に記録された裏ドラ表示牌（リーチ和了の局のみ。カン裏があればそれも照合する）。</param>
        public static void Apply(RoundData rd, List<int> paishan, List<int> dora, List<int> ura)
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
                bool doraOk = Matches(dead, DoraPattern, dora) && (ura.Count == 0 || Matches(dead, UraPattern, ura));
                if (doraOk)
                {
                    rd.DoraPos = DoraPattern;
                    rd.UraPos = UraPattern;
                    rd.DoraInd = DoraPattern.Select(j => dead[j]).ToList();
                    rd.UraInd = UraPattern.Select(j => dead[j]).ToList();
                }
                rd.WallStatus = "牌山データあり（分岐のツモも牌山どおり"
                    + (pat == null ? "。嶺上牌は牌譜の分のみ" : guessed ? "。嶺上牌の位置は推定" : "")
                    + (doraOk ? "。裏ドラ・カンドラも牌山から" : "") + "）";
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

        /// <summary>王牌の pos の位置に、記録された表示牌 rec が順に並んでいるか。</summary>
        static bool Matches(List<int> dead, int[] pos, List<int> rec)
        {
            return rec.Count > 0 && rec.Count <= pos.Length && rec.Select((c, j) => dead[pos[j]] == c).All(x => x);
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
