using System.Collections.Generic;
using System.Linq;

namespace PaifuViewer
{
    /// <summary>
    /// 牌コード（天鳳6形式: 11-19 萬, 21-29 筒, 31-39 索, 41-47 字, 51-53 赤5）の扱いと和了判定。
    /// </summary>
    public static class Tiles
    {
        public const string WD = "東南西北";
        public static readonly string[] H7 = { "東", "南", "西", "北", "白", "發", "中" };

        public static int Norm(int c) { return c > 50 ? (c - 50) * 10 + 5 : c; }
        public static double Key(int c) { return Norm(c) + (c > 50 ? 0.5 : 0); }
        public static void Sort(List<int> h) { h.Sort((a, b) => Key(a).CompareTo(Key(b))); }

        /// <summary>同じコードを優先して 1 枚取り除く。なければ赤/通常を同一視して取り除く。</summary>
        public static void Rm(List<int> h, int c)
        {
            int i = h.IndexOf(c);
            if (i < 0) i = h.FindIndex(x => Norm(x) == Norm(c));
            if (i >= 0) h.RemoveAt(i);
        }

        public static string Tn(int c)
        {
            int n = Norm(c);
            return n > 40 ? H7[n - 41] : (n % 10).ToString() + "mps"[n / 10 - 1];
        }

        /// <summary>牌コード → 0..33 の種類インデックス。</summary>
        public static int Ix(int c)
        {
            int n = Norm(c);
            return n > 40 ? 27 + n - 41 : (n / 10 - 1) * 9 + n % 10 - 1;
        }

        /// <summary>0..33 の種類インデックス → 牌コード。</summary>
        public static int Cd(int i) { return i < 27 ? (i / 9 + 1) * 10 + i % 9 + 1 : i - 27 + 41; }

        /// <summary>手牌から種類 n の牌を 1 枚選ぶ（同コード優先）。</summary>
        public static int? Pk(List<int> h, int n)
        {
            foreach (int c in h) if (c == n) return c;
            foreach (int c in h) if (Norm(c) == n) return c;
            return null;
        }

        static bool Mj(int[] a, int i)
        {
            while (i < 34 && a[i] == 0) i++;
            if (i >= 34) return true;
            if (a[i] >= 3)
            {
                a[i] -= 3; bool r = Mj(a, i); a[i] += 3;
                if (r) return true;
            }
            if (i < 27 && i % 9 < 7 && a[i + 1] > 0 && a[i + 2] > 0)
            {
                a[i]--; a[i + 1]--; a[i + 2]--;
                bool r = Mj(a, i);
                a[i]++; a[i + 1]++; a[i + 2]++;
                if (r) return true;
            }
            return false;
        }

        public static bool Agari(List<int> ts)
        {
            var a = new int[34];
            foreach (int c in ts) a[Ix(c)]++;
            if (ts.Count == 14)
            {
                if (a.Count(x => x == 2) == 7) return true;
                int[] k = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
                if (k.All(j => a[j] > 0) && k.Any(j => a[j] == 2)) return true;
            }
            for (int j = 0; j < 34; j++)
            {
                if (a[j] < 2) continue;
                a[j] -= 2; bool r = Mj(a, 0); a[j] += 2;
                if (r) return true;
            }
            return false;
        }

        public static bool Tenpai(List<int> ts)
        {
            var a = new int[34];
            foreach (int c in ts) a[Ix(c)]++;
            for (int j = 0; j < 34; j++)
            {
                if (a[j] >= 4) continue;
                var t = new List<int>(ts) { Cd(j) };
                if (Agari(t)) return true;
            }
            return false;
        }
    }
}
