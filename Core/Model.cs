using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>河の 1 枚。R=立直宣言牌, G=ツモ切り, X=鳴かれた牌。</summary>
    public class River
    {
        public int C;
        public bool R, G, X;
        public River Clone() { return (River)MemberwiseClone(); }
    }

    /// <summary>副露。L: c=チー p=ポン m=大明槓 a=暗槓 k=加槓、Ci=鳴いた牌の位置。</summary>
    public class Meld
    {
        public char L;
        public List<int> T;
        public int Ci;
        public Meld Clone() { return new Meld { L = L, T = new List<int>(T), Ci = Ci }; }
    }

    public class PlayerState
    {
        public List<int> H = new List<int>();     // 手牌（ツモ牌を除きソート済み）
        public int? Dw;                            // ツモ牌
        public List<River> Rv = new List<River>();
        public List<Meld> Ml = new List<Meld>();
        public bool Ri;

        public PlayerState Clone()
        {
            return new PlayerState
            {
                H = new List<int>(H),
                Dw = Dw,
                Rv = Rv.Select(r => r.Clone()).ToList(),
                Ml = Ml.Select(m => m.Clone()).ToList(),
                Ri = Ri
            };
        }
    }

    /// <summary>1 手ごとの局面。Ph: d=打牌待ち c=鳴き判断待ち w=ツモ待ち e=局終了。</summary>
    public class Step
    {
        public string Msg;
        public int T;
        public bool Rin;          // 次のツモが嶺上牌か
        public bool Rs;           // この局面のツモが嶺上牌だったか（嶺上開花の判定用）
        public List<int> Kk;      // 鳴いた直後に切れない牌（喰い替え禁止、Norm 値）
        public int Ld, Rd;        // 牌山・嶺上から何枚ツモったか
        public string Ph;
        public int[] Sc;
        public int Dn, Wall, Dealer;
        public PlayerState[] Pl;
        public int Jn;

        public Step Clone()
        {
            var s = (Step)MemberwiseClone();
            s.Sc = (int[])Sc.Clone();
            s.Pl = Pl.Select(p => p.Clone()).ToArray();
            return s;
        }

        public int CalcJn() { return Pl.Sum(p => p.Rv.Count) / 4 + 1; }
    }

    public class RoundData
    {
        public List<Step> Steps;
        public List<int> LW, RW;  // 通常ツモ列・嶺上ツモ列（牌山データがあれば牌山の最後まで）
        public bool HasWall;
        public List<int> Wall;    // 牌山 136 枚（ツモ順に並べ替え済み。牌山データがなければ null）
        public int WallStart;     // Wall のうち最初の通常ツモの位置（それより前は配牌）
        public int[] RinshanPos;  // 王牌（Wall の末尾 14 枚）のうち嶺上牌の位置。推定できなければ null
        public int[] DoraPos, UraPos;        // 王牌のうちドラ表示牌・裏ドラ表示牌の位置（牌山と照合できたときのみ）
        public List<int> DoraInd, UraInd;    // 牌山から読んだドラ表示牌・裏ドラ表示牌（カンで増える分まで 5 枚ずつ）
        public string WallStatus = "牌山データなし";
    }
}
