// Минимальный JSON-парсер/сериализатор без внешних зависимостей.
// Модель: Dictionary<string,object> / List<object> / string / double / bool / null / RawJson.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KompasMcp.JsonLib
{
  // Обёртка для вставки готового JSON без экранирования (схемы tools).
  public class RawJson
  {
    public readonly string Text;
    public RawJson(string text) { Text = text; }
  }

  public static class Json
  {
    public static object Parse(string text)
    {
      int pos = 0;
      object v = ParseValue(text, ref pos);
      SkipWs(text, ref pos);
      if (pos != text.Length) throw new Exception("Лишние символы после JSON на позиции " + pos);
      return v;
    }

    public static string Write(object o)
    {
      StringBuilder sb = new StringBuilder();
      WriteValue(o, sb);
      return sb.ToString();
    }

    // ---- парсинг ----

    static void SkipWs(string s, ref int i)
    {
      while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
    }

    static object ParseValue(string s, ref int i)
    {
      SkipWs(s, ref i);
      if (i >= s.Length) throw new Exception("Неожиданный конец JSON");
      char c = s[i];
      if (c == '{') return ParseObj(s, ref i);
      if (c == '[') return ParseArr(s, ref i);
      if (c == '"') return ParseStr(s, ref i);
      if (c == 't') { Expect(s, ref i, "true"); return true; }
      if (c == 'f') { Expect(s, ref i, "false"); return false; }
      if (c == 'n') { Expect(s, ref i, "null"); return null; }
      return ParseNum(s, ref i);
    }

    static void Expect(string s, ref int i, string lit)
    {
      if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
        throw new Exception("Ожидалось '" + lit + "' на позиции " + i);
      i += lit.Length;
    }

    static Dictionary<string, object> ParseObj(string s, ref int i)
    {
      var d = new Dictionary<string, object>();
      i++; // {
      SkipWs(s, ref i);
      if (i < s.Length && s[i] == '}') { i++; return d; }
      while (true)
      {
        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != '"') throw new Exception("Ожидался ключ объекта на позиции " + i);
        string key = ParseStr(s, ref i);
        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != ':') throw new Exception("Ожидалось ':' на позиции " + i);
        i++;
        d[key] = ParseValue(s, ref i);
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ',') { i++; continue; }
        if (i < s.Length && s[i] == '}') { i++; return d; }
        throw new Exception("Ожидался ',' или '}' на позиции " + i);
      }
    }

    static List<object> ParseArr(string s, ref int i)
    {
      var list = new List<object>();
      i++; // [
      SkipWs(s, ref i);
      if (i < s.Length && s[i] == ']') { i++; return list; }
      while (true)
      {
        list.Add(ParseValue(s, ref i));
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ',') { i++; continue; }
        if (i < s.Length && s[i] == ']') { i++; return list; }
        throw new Exception("Ожидался ',' или ']' на позиции " + i);
      }
    }

    static string ParseStr(string s, ref int i)
    {
      var sb = new StringBuilder();
      i++; // "
      while (i < s.Length)
      {
        char c = s[i++];
        if (c == '"') return sb.ToString();
        if (c == '\\')
        {
          if (i >= s.Length) break;
          char e = s[i++];
          if (e == '"') sb.Append('"');
          else if (e == '\\') sb.Append('\\');
          else if (e == '/') sb.Append('/');
          else if (e == 'b') sb.Append('\b');
          else if (e == 'f') sb.Append('\f');
          else if (e == 'n') sb.Append('\n');
          else if (e == 'r') sb.Append('\r');
          else if (e == 't') sb.Append('\t');
          else if (e == 'u')
          {
            if (i + 4 > s.Length) throw new Exception("Обрезанный \\u-эскейп");
            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            i += 4;
          }
          else throw new Exception("Неизвестный эскейп '\\" + e + "'");
        }
        else sb.Append(c);
      }
      throw new Exception("Незакрытая строка");
    }

    static double ParseNum(string s, ref int i)
    {
      int start = i;
      while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
      double v;
      if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        throw new Exception("Некорректное число на позиции " + start);
      return v;
    }

    // ---- сериализация ----

    static void WriteValue(object o, StringBuilder sb)
    {
      if (o == null) { sb.Append("null"); return; }
      if (o is RawJson) { sb.Append(((RawJson)o).Text); return; }
      if (o is string) { WriteStr((string)o, sb); return; }
      if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
      if (o is int) { sb.Append(((int)o).ToString(CultureInfo.InvariantCulture)); return; }
      if (o is long) { sb.Append(((long)o).ToString(CultureInfo.InvariantCulture)); return; }
      if (o is double)
      {
        double d = (double)o;
        if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
          sb.Append(d.ToString("0", CultureInfo.InvariantCulture));
        else
          sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        return;
      }
      if (o is Dictionary<string, object>)
      {
        sb.Append('{');
        bool first = true;
        foreach (var kv in (Dictionary<string, object>)o)
        {
          if (!first) sb.Append(',');
          first = false;
          WriteStr(kv.Key, sb);
          sb.Append(':');
          WriteValue(kv.Value, sb);
        }
        sb.Append('}');
        return;
      }
      if (o is IEnumerable)
      {
        sb.Append('[');
        bool first = true;
        foreach (object item in (IEnumerable)o)
        {
          if (!first) sb.Append(',');
          first = false;
          WriteValue(item, sb);
        }
        sb.Append(']');
        return;
      }
      WriteStr(o.ToString(), sb);
    }

    static void WriteStr(string s, StringBuilder sb)
    {
      sb.Append('"');
      foreach (char c in s)
      {
        if (c == '"') sb.Append("\\\"");
        else if (c == '\\') sb.Append("\\\\");
        else if (c == '\b') sb.Append("\\b");
        else if (c == '\f') sb.Append("\\f");
        else if (c == '\n') sb.Append("\\n");
        else if (c == '\r') sb.Append("\\r");
        else if (c == '\t') sb.Append("\\t");
        else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        else sb.Append(c);
      }
      sb.Append('"');
    }
  }
}