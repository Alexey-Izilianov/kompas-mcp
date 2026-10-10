using System;
using System.Collections.Generic;
using System.Text;
using KompasMcp.JsonLib;

namespace KompasMcp
{
  // Справочник инструментов (docs/tools.md) — из реестра и JSON-схем, без ручного дублирования:
  // KompasMcp.exe --tools-md docs\tools.md
  public static class ToolsDoc
  {
    public static string Markdown()
    {
      List<ToolDef> tools = ToolRegistry.All;
      List<string> sections = new List<string>();
      foreach (ToolDef t in tools) if (!sections.Contains(t.Section)) sections.Add(t.Section);

      StringBuilder sb = new StringBuilder();
      sb.Append("# kompas-mcp ").Append(Rpc.ServerVersion).Append(" — справочник инструментов\n\n");
      sb.Append("Файл сформирован командой `KompasMcp.exe --tools-md docs\\tools.md` из реестра сервера: ");
      sb.Append("описания и параметры совпадают с тем, что получает MCP-клиент в `tools/list`. ");
      sb.Append("Всего инструментов: ").Append(tools.Count).Append(".\n\n");
      sb.Append("Единицы: длины — мм, углы — градусы, силы — Н, моменты — Н·м, напряжения — МПа. ");
      sb.Append("Пути — абсолютные или относительно папки сервера; результаты по умолчанию — в `mcp-out` рядом с exe.\n\n");
      sb.Append("## Содержание\n\n");
      foreach (string s in sections)
      {
        List<string> names = new List<string>();
        foreach (ToolDef t in tools) if (t.Section == s) names.Add("[`" + t.Name + "`](#" + t.Name + ")");
        sb.Append("- **").Append(s).Append("** — ").Append(string.Join(", ", names.ToArray())).Append('\n');
      }
      foreach (string s in sections)
      {
        sb.Append("\n## ").Append(s).Append('\n');
        foreach (ToolDef t in tools)
          if (t.Section == s) Tool(sb, t);
      }
      return sb.ToString();
    }

    static void Tool(StringBuilder sb, ToolDef t)
    {
      sb.Append("\n### ").Append(t.Name).Append("\n\n").Append(t.Description).Append("\n\n");
      Dictionary<string, object> schema = Json.Parse(t.SchemaJson) as Dictionary<string, object>;
      object propsObj = null, reqObj = null;
      if (schema != null)
      {
        schema.TryGetValue("properties", out propsObj);
        schema.TryGetValue("required", out reqObj);
      }
      Dictionary<string, object> props = propsObj as Dictionary<string, object>;
      if (props == null || props.Count == 0)
      {
        sb.Append("Параметров нет.\n");
        return;
      }
      List<string> required = new List<string>();
      if (reqObj is List<object>) foreach (object r in (List<object>)reqObj) required.Add(r.ToString());
      sb.Append("| Параметр | Тип | Обяз. | Описание |\n|---|---|---|---|\n");
      foreach (KeyValuePair<string, object> p in props)
      {
        Dictionary<string, object> d = p.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
        sb.Append("| `").Append(p.Key).Append("` | ").Append(TypeOf(d)).Append(" | ")
          .Append(required.Contains(p.Key) ? "да" : "").Append(" | ")
          .Append(Cell(d.ContainsKey("description") ? d["description"].ToString() : "")).Append(" |\n");
      }
    }

    static string TypeOf(Dictionary<string, object> d)
    {
      object e;
      if (d.TryGetValue("enum", out e) && e is List<object>)
      {
        List<string> vals = new List<string>();
        foreach (object v in (List<object>)e) vals.Add(v.ToString());
        return string.Join(" \\| ", vals.ToArray());
      }
      string type = d.ContainsKey("type") ? d["type"].ToString() : "";
      object items;
      if (type == "array" && d.TryGetValue("items", out items) && items is Dictionary<string, object>)
      {
        Dictionary<string, object> it = (Dictionary<string, object>)items;
        if (it.ContainsKey("type")) return "array of " + it["type"];
      }
      return type;
    }

    static string Cell(string s)
    {
      return s.Replace("|", "\\|").Replace("\n", " ");
    }
  }
}
