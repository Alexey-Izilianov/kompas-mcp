// E2E smoke (Этап 6): живой видимый КОМПАС (kompas_spawn.exe) -> claude CLI
// через runner с mcp-config (KompasMcp.exe --attach) -> модель вызывает
// kompas_status -> в result должен быть mode:"attach" и pid КОМПАСа.
// КОМПАС должен остаться жив после завершения claude.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using KompasMcp.Davinci;

class E2eTest
{
  static int Main()
  {
    int kompasPid = SpawnKompas();
    if (kompasPid == 0) { Console.WriteLine("E2E FAIL: КОМПАС не запустился"); return 1; }
    Console.WriteLine("kompas pid=" + kompasPid);

    DavinciConfig cfg = new DavinciConfig();
    cfg.KompasMcpExe = @"C:\Projects\kompas-test\kompas-mcp\KompasMcp.exe";
    cfg.Workspace = @"C:\Projects\kompas-test\kompas-mcp\tests";
    cfg.TimeoutSec = 180;
    string mcpPath = McpConfig.Write(cfg);

    var runner = new ClaudeRunner();
    runner.Options = new ClaudeRunnerOptions();
    runner.Options.McpConfigPath = mcpPath;
    runner.Options.Workspace = cfg.Workspace;
    runner.Options.TimeoutSec = cfg.TimeoutSec;
    var texts = new List<string>();
    runner.OnEvent += delegate(StreamEvent ev)
    {
      if (ev == null) return;
      if (!string.IsNullOrEmpty(ev.AssistantText)) texts.Add("A: " + ev.AssistantText);
      else if (ev.Type == "assistant" && !string.IsNullOrEmpty(ev.ToolName)) texts.Add("[тул] " + ev.ToolName);
      else if (ev.Type == "result" && ev.ResultText != null) texts.Add("R: " + ev.ResultText);
    };
    bool done = false;
    runner.OnExit += delegate { done = true; };

    Console.WriteLine("запуск claude: статус КОМПАСа...");
    runner.Run("Вызови инструмент kompas_status и напиши одной строкой значение mode и pid из ответа. Ничего больше не делай.", null);

    DateTime until = DateTime.UtcNow.AddSeconds(cfg.TimeoutSec + 30);
    while (DateTime.UtcNow < until && !done) Thread.Sleep(1000);

    foreach (string t in texts) Console.WriteLine(t);

    bool ok = texts.Count > 0;
    string all = string.Join("\n", texts.ToArray());
    if (ok) ok = all.Contains("attach");
    if (ok) ok = all.Contains(kompasPid.ToString());

    bool alive = PidsAlive().Contains(kompasPid);
    Console.WriteLine("kompas alive after claude: " + alive);

    try { Process.GetProcessById(kompasPid).Kill(); } catch { }
    if (ok && alive) { Console.WriteLine("E2E TEST OK"); return 0; }
    Console.WriteLine("E2E TEST FAIL");
    return 1;
  }

  static int SpawnKompas()
  {
    string exe = @"C:\Projects\kompas-test\kompas-mcp\tests\kompas_spawn.exe";
    if (!File.Exists(exe)) return 0;
    ProcessStartInfo psi = new ProcessStartInfo(exe);
    psi.UseShellExecute = false;
    psi.RedirectStandardOutput = true;
    Process pr = Process.Start(psi);
    string line;
    while ((line = pr.StandardOutput.ReadLine()) != null)
    {
      if (line.StartsWith("PID="))
      {
        string s = line.Substring(4);
        int pid;
        if (int.TryParse(s, out pid)) return pid;
      }
    }
    return 0;
  }

  static List<int> PidsAlive()
  {
    var list = new List<int>();
    foreach (Process pr in Process.GetProcessesByName("KOMPAS")) list.Add(pr.Id);
    return list;
  }
}