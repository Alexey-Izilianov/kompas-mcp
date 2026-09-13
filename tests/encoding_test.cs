// Проверка кодировки stdin: русское сообщение -> claude -> модель возвращает
// его же по-русски. Если stdin ушёл в OEM-кодировке, модель ответит мусором
// или не сможет процитировать слово.
using System;
using System.Collections.Generic;
using System.Threading;
using KompasMcp.Davinci;

class EncodingTest
{
  static int Main()
  {
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
      if (!string.IsNullOrEmpty(ev.AssistantText)) texts.Add(ev.AssistantText);
      else if (ev.Type == "result" && ev.ResultText != null) texts.Add(ev.ResultText);
    };
    bool done = false;
    runner.OnExit += delegate { done = true; };

    runner.Run("Ответь ровно одним словом без изменений: здравствуйте", null);

    DateTime until = DateTime.UtcNow.AddSeconds(cfg.TimeoutSec + 30);
    while (DateTime.UtcNow < until && !done) Thread.Sleep(200);

    foreach (string t in texts) Console.WriteLine("<< " + t);
    string all = string.Join("\n", texts.ToArray());
    if (all.Contains("здравствуйте")) { Console.WriteLine("ENCODING TEST OK"); return 0; }
    Console.WriteLine("ENCODING TEST FAIL");
    return 1;
  }
}