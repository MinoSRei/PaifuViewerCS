using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PaifuViewer
{
    /// <summary>
    /// 外部ライブラリなしで動く最小の JSON パーサ。
    /// 戻り値は Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON が空です");
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i != text.Length) throw Err("JSON の末尾に余分な文字があります", i);
            return v;
        }

        static Exception Err(string m, int i) { return new FormatException(m + "（位置 " + i + "）"); }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw Err("JSON が途中で終わっています", i);
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Err("オブジェクトのキーが必要です", i);
                    string key = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Err("':' が必要です", i);
                    i++;
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw Err("',' または '}' が必要です", i);
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw Err("',' または ']' が必要です", i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        static bool Lit(string s, ref int i, string w)
        {
            if (i + w.Length <= s.Length && string.CompareOrdinal(s, i, w, 0, w.Length) == 0) { i += w.Length; return true; }
            return false;
        }

        static string Str(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw Err("文字列が閉じていません", i);
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw Err("文字列が閉じていません", i);
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Err("不正な \\u エスケープです", i);
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw Err("不正なエスケープです", i);
                }
            }
        }

        static object Num(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
            double d;
            if (st == i || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw Err("不正な値です", st);
            return d;
        }
    }

    /// <summary>MiniJson の結果を扱うための小さな変換ヘルパ。</summary>
    public static class J
    {
        public static int I(object o) { return Convert.ToInt32(o, CultureInfo.InvariantCulture); }
        public static List<object> L(object o) { return o as List<object> ?? new List<object>(); }
        public static List<int> Ints(object o) { return L(o).Select(x => I(x)).ToList(); }

        public static string S(object o)
        {
            if (o == null) return "";
            if (o is double) return ((double)o).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        /// <summary>JS の names[q] || "席"+q に相当。</summary>
        public static string Name(string[] names, int q)
        {
            return q < names.Length && !string.IsNullOrEmpty(names[q]) ? names[q] : "席" + q;
        }
    }
}
