// Конфиг Давинчи (Этап 5): davinci.json рядом с exe (или в davinci\ рядом с exe).
// Отсутствие файла — не ошибка: используются значения по умолчанию.
using System;
using System.Collections.Generic;
using System.IO;
using KompasMcp.JsonLib;

namespace KompasMcp.Davinci
{
  public class DavinciConfig
  {
    public string KompasMcpExe;   // путь к KompasMcp.exe (команда mcp-config)
    public string Model;          // "" = модель по умолчанию
    public string Workspace;      // рабочий каталог claude (mcp-out)
    public int TimeoutSec = 900;
    public string ClaudeCmd = "claude.cmd";

    public static DavinciConfig Load()
    {
      var cfg = new DavinciConfig();
      string exeDir = AppDomain.CurrentDomain.BaseDirectory;
      string path = Path.Combine(exeDir, "davinci.json");
      if (!File.Exists(path))
      {
        string alt = Path.Combine(Path.Combine(exeDir, "davinci"), "davinci.json");
        if (File.Exists(alt)) path = alt;
        else
        {
          Log.Write("config: davinci.json не найден, дефолты");
          return cfg;
        }
      }
      try
      {
        object o = Json.Parse(File.ReadAllText(path, new System.Text.UTF8Encoding(false)));
        var d = o as Dictionary<string, object>;
        if (d == null) { Log.Write("config: davinci.json не объект, дефолты"); return cfg; }
        object v;
        if (d.TryGetValue("kompasMcpExe", out v)) cfg.KompasMcpExe = Convert.ToString(v);
        if (d.TryGetValue("model", out v)) cfg.Model = Convert.ToString(v);
        if (d.TryGetValue("workspace", out v)) cfg.Workspace = Convert.ToString(v);
        if (d.TryGetValue("timeoutSec", out v)) cfg.TimeoutSec = (int)AsDouble(v, 900);
        if (d.TryGetValue("claudeCmd", out v)) cfg.ClaudeCmd = Convert.ToString(v);
        Log.Write("config: davinci.json загружен (" + path + ")");
      }
      catch (Exception e)
      {
        Log.Error("config", e);
      }
      return cfg;
    }

    static double AsDouble(object v, double def)
    {
      double d;
      if (v is double) return (double)v;
      if (v != null && double.TryParse(Convert.ToString(v).Replace(',', '.'),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
        return d;
      return def;
    }
  }
}