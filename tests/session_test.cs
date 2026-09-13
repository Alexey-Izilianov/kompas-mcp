// Тест Этапа 5: DavinciConfig.Load (дефолты при отсутствии davinci.json),
// McpConfig.Write (валидный JSON с kompas-mcp --attach, файл в %TEMP%).
using System;
using System.IO;
using KompasMcp.JsonLib;
using KompasMcp.Davinci;

class SessionTest
{
  static bool fail;

  static int Main()
  {
    // 1. Load без davinci.json рядом с exe (tests\ без конфига) — дефолты
    DavinciConfig cfg = DavinciConfig.Load();
    Check("default claudeCmd", cfg.ClaudeCmd == "claude.cmd");
    Check("default timeout", cfg.TimeoutSec == 900);

    // 2. McpConfig.Write с явным exe
    cfg.KompasMcpExe = @"C:\Projects\kompas-test\kompas-mcp\KompasMcp.exe";
    string path = McpConfig.Write(cfg);
    Check("mcp-config written", File.Exists(path));
    string json = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
    Check("mcp-config content", json.Contains("kompas-mcp") && json.Contains("--attach") &&
      json.Contains("KompasMcp.exe"));

    // 3. JSON валиден (парсер обратно)
    object o = Json.Parse(json);
    var root = o as System.Collections.Generic.Dictionary<string, object>;
    Check("mcp-config parses", root != null && root.ContainsKey("mcpServers"));

    if (fail) { Console.WriteLine("SESSION TEST FAIL"); return 1; }
    Console.WriteLine("SESSION TEST OK");
    return 0;
  }

  static void Check(string name, bool cond)
  {
    if (!cond) { fail = true; Console.WriteLine("FAIL: " + name); }
    else Console.WriteLine("[" + name + "] ok");
  }
}