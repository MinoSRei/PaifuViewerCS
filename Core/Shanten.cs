using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PaifuViewer
{
    /// <summary>
    /// 向聴数（-1 が和了形、0 がテンパイ）。種類ごとの枚数 a[34] と副露数から求める。
    /// 一般形は色ごとに面子・塔子・雀頭の取り方を列挙してメモ化し、色をまたいで組み合わせる。
    /// </summary>
    public static class Shanten
    {
        // 色ごとの枚数（5 進数）→ 取り得る (面子, 塔子, 雀頭) を m*100+t*10+p で並べたもの
        static readonly ConcurrentDictionary<int, int[]> memo = new ConcurrentDictionary<int, int[]>();

        public static int Calc(int[] a, int melds)
        {
            int s = Normal(a, melds);
            if (melds == 0)
            {
                s = System.Math.Min(s, Chiitoi(a));
                s = System.Math.Min(s, Kokushi(a));
            }
            return s;
        }

        public static int Calc(List<int> tiles, int melds)
        {
            var a = new int[34];
            foreach (int c in tiles) a[Tiles.Ix(c)]++;
            return Calc(a, melds);
        }

        static int Chiitoi(int[] a)
        {
            int pairs = 0, kinds = 0;
            for (int i = 0; i < 34; i++)
            {
                if (a[i] > 0) kinds++;
                if (a[i] >= 2) pairs++;
            }
            return 6 - pairs + System.Math.Max(0, 7 - kinds);
        }

        static readonly int[] Yao = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };

        static int Kokushi(int[] a)
        {
            int kinds = 0;
            bool pair = false;
            foreach (int i in Yao)
            {
                if (a[i] > 0) kinds++;
                if (a[i] >= 2) pair = true;
            }
            return 13 - kinds - (pair ? 1 : 0);
        }

        static int Normal(int[] a, int melds)
        {
            int need = 4 - melds;
            // st[m, t, p]：これまでの色で (面子 m, 塔子 t, 雀頭 p) が取れるか（m, t は need で頭打ち）
            var st = new bool[5, 9, 2];
            st[0, 0, 0] = true;
            for (int g = 0; g < 4; g++)
            {
                var opts = Group(a, g);
                var nx = new bool[5, 9, 2];
                for (int m = 0; m <= need; m++)
                    for (int t = 0; t <= 8; t++)
                        for (int p = 0; p < 2; p++)
                        {
                            if (!st[m, t, p]) continue;
                            foreach (int o in opts)
                            {
                                int om = o / 100, ot = o / 10 % 10, op = o % 10;
                                if (p + op > 1) continue;
                                nx[System.Math.Min(need, m + om), System.Math.Min(8, t + ot), p + op] = true;
                            }
                        }
                st = nx;
            }
            int best = 0;
            for (int m = 0; m <= need; m++)
                for (int t = 0; t <= 8; t++)
                    for (int p = 0; p < 2; p++)
                        if (st[m, t, p]) best = System.Math.Max(best, 2 * m + System.Math.Min(t, need - m) + p);
            return 2 * need - best;
        }

        static int[] Group(int[] a, int g)
        {
            int off = g * 9, len = g < 3 ? 9 : 7, key = g < 3 ? 0 : 1 << 24;
            int k = 0;
            for (int i = 0; i < len; i++) k = k * 5 + a[off + i];
            return memo.GetOrAdd(key | k, _ =>
            {
                var b = new int[len];
                System.Array.Copy(a, off, b, 0, len);
                var o = new HashSet<int>();
                Rec(b, 0, 0, 0, 0, o, g == 3);
                return Pareto(o);
            });
        }

        static void Rec(int[] a, int i, int m, int t, int p, HashSet<int> o, bool honor)
        {
            while (i < a.Length && a[i] == 0) i++;
            if (i >= a.Length) { o.Add(m * 100 + System.Math.Min(t, 8) * 10 + p); return; }
            int n = a.Length;
            if (a[i] >= 3) { a[i] -= 3; Rec(a, i, m + 1, t, p, o, honor); a[i] += 3; }
            if (!honor && i + 2 < n && a[i + 1] > 0 && a[i + 2] > 0)
            {
                a[i]--; a[i + 1]--; a[i + 2]--;
                Rec(a, i, m + 1, t, p, o, honor);
                a[i]++; a[i + 1]++; a[i + 2]++;
            }
            if (a[i] >= 2)
            {
                a[i] -= 2;
                Rec(a, i, m, t + 1, p, o, honor);
                if (p == 0) Rec(a, i, m, t, 1, o, honor);
                a[i] += 2;
            }
            if (!honor && i + 1 < n && a[i + 1] > 0) { a[i]--; a[i + 1]--; Rec(a, i, m, t + 1, p, o, honor); a[i]++; a[i + 1]++; }
            if (!honor && i + 2 < n && a[i + 2] > 0) { a[i]--; a[i + 2]--; Rec(a, i, m, t + 1, p, o, honor); a[i]++; a[i + 2]++; }
            a[i]--; Rec(a, i + 1, m, t, p, o, honor); a[i]++;
        }

        /// <summary>他の取り方に面子・塔子・雀頭のすべてで劣るものを除く。</summary>
        static int[] Pareto(HashSet<int> o)
        {
            var r = new List<int>();
            foreach (int x in o)
            {
                bool dom = false;
                foreach (int y in o)
                {
                    if (y == x) continue;
                    if (y / 100 >= x / 100 && y / 10 % 10 >= x / 10 % 10 && y % 10 >= x % 10) { dom = true; break; }
                }
                if (!dom) r.Add(x);
            }
            return r.ToArray();
        }
    }
}
