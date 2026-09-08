using System;
using System.Collections.Generic;
using System.Globalization;

namespace KompasMcp
{
  // Ошибка выполнения tool'а: клиент получает result.isError=true, а не протокольную ошибку.
  public class ToolException : Exception
  {
    public ToolException(string message) : base(message) { }
  }

  public class ToolDef
  {
    public string Name;
    public string Description;
    public string SchemaJson;   // JSON-схема входных аргументов
    public Func<Dictionary<string, object>, object> Handler;
  }

  public static class ToolRegistry
  {
    static readonly List<ToolDef> tools = new List<ToolDef>();

    static ToolRegistry()
    {
      Tools.InfraTools.Register();
      Tools.Tools2D.Register();
      Tools.Tools3D.Register();
      Tools.ToolsCalc.Register();
      Tools.ToolsStd.Register();
      Tools.ToolsGear.Register();
      Tools.ToolsSpring.Register();
    }

    public static void Add(string name, string description, string schemaJson,
                           Func<Dictionary<string, object>, object> handler)
    {
      // Канонизация схемы: валидный JSON одной строкой (verbatim-строки содержат переводы строк,
      // недопустимые в построчном протоколе). Битая схема = ошибка при старте, а не на вызове.
      string canonical;
      try
      {
        object parsed = JsonLib.Json.Parse(schemaJson);
        Dictionary<string, object> obj = parsed as Dictionary<string, object>;
        // MCP-клиент требует inputSchema.type == "object"; схемы без типа канонизируем
        if (obj != null && !obj.ContainsKey("type")) obj["type"] = "object";
        canonical = JsonLib.Json.Write(parsed);
      }
      catch (Exception e)
      {
        throw new Exception("Битая inputSchema у tool '" + name + "': " + e.Message + " | схема: " + schemaJson);
      }
      tools.Add(new ToolDef { Name = name, Description = description, SchemaJson = canonical, Handler = handler });
    }

    public static ToolDef Find(string name)
    {
      foreach (ToolDef t in tools)
        if (t.Name == name) return t;
      return null;
    }

    public static List<ToolDef> All { get { return tools; } }

    // ---- хелперы извлечения аргументов ----

    public static string GetStr(Dictionary<string, object> a, string key, string def)
    {
      object v;
      if (a != null && a.TryGetValue(key, out v) && v is string) return (string)v;
      return def;
    }

    public static string GetStr(Dictionary<string, object> a, string key)
    {
      string s = GetStr(a, key, null);
      if (s == null) throw new ToolException("Отсутствует обязательный строковый аргумент '" + key + "'");
      return s;
    }

    public static double GetDbl(Dictionary<string, object> a, string key)
    {
      object v;
      if (a == null || !a.TryGetValue(key, out v) || v == null)
        throw new ToolException("Отсутствует обязательный числовой аргумент '" + key + "'");
      return ToDbl(key, v);
    }

    public static double GetDbl(Dictionary<string, object> a, string key, double def)
    {
      object v;
      if (a == null || !a.TryGetValue(key, out v) || v == null) return def;
      return ToDbl(key, v);
    }

    static double ToDbl(string key, object v)
    {
      if (v is double) return (double)v;
      if (v is int) return (int)v;
      if (v is long) return (long)v;
      if (v is bool) return (bool)v ? 1 : 0;
      double d;
      if (double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
      throw new ToolException("Аргумент '" + key + "' не число: " + v);
    }

    public static int GetInt(Dictionary<string, object> a, string key)
    {
      return (int)Math.Round(GetDbl(a, key));
    }

    public static int GetInt(Dictionary<string, object> a, string key, int def)
    {
      object v;
      if (a == null || !a.TryGetValue(key, out v) || v == null) return def;
      return (int)Math.Round(GetDbl(a, key));
    }

    public static bool GetBool(Dictionary<string, object> a, string key, bool def)
    {
      object v;
      if (a == null || !a.TryGetValue(key, out v) || v == null) return def;
      if (v is bool) return (bool)v;
      return GetDbl(a, key) != 0;
    }
  }
}