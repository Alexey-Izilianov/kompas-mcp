// Проверка вложений: файл в workspace\attach -> claude читает его содержимое.
// Кладём attach\tz.txt с кодовым словом, спрашиваем модель — ответ должен
// содержать кодовое слово (значит Read файла из workspace работает без
// дополнительных разрешений).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KompasMcp.Davinci;

class FilesTest
{
  static int Main()
  {
    string ws = Path.Combine(Path.GetTempPath(), "kompas-files-test");
    string attach = Path.Combine(ws, "attach");
    Directory.CreateDirectory(attach);
    string magic = "КОДОВОЕ-СЛОВО-АЛЬФА-7314";
    string path = Path.Combine(attach, "tz.txt");
    File.WriteAllText(path,
      "ТЗ на фланец.\nКодовое слово изделия: " + magic + "\nМатериал: сталь 45.",
      new System.Text.UTF8Encoding(false));

    string root = @"C:\Projects\kompas-test\kompas-mcp";
    int cfgTimeout = 180;
    // mcp-config пишем сами (McpConfig тянет ToolException из сервера)
    string mcpPath = Path.Combine(ws, "mcp-config.json");
    string exe = Path.Combine(root, "KompasMcp.exe");
    string json = "{\"mcpServers\":{\"kompas-mcp\":{\"command\":\"" +
      exe.Replace("\\", "\\\\") + "\",\"args\":[\"--attach\"]}}}";
    File.WriteAllText(mcpPath, json, new System.Text.UTF8Encoding(false));

    var runner = new ClaudeRunner();
    runner.Options = new ClaudeRunnerOptions();
    runner.Options.McpConfigPath = mcpPath;
    runner.Options.Workspace = ws;
    runner.Options.TimeoutSec = cfgTimeout;
    var texts = new List<string>();
    runner.OnEvent += delegate(StreamEvent ev)
    {
      if (ev == null) return;
      if (!string.IsNullOrEmpty(ev.AssistantText)) texts.Add(ev.AssistantText);
      else if (ev.Type == "result" && ev.ResultText != null) texts.Add(ev.ResultText);
    };
    bool done = false;
    runner.OnExit += delegate { done = true; };

    string msg = "Прочитай файл из блока ниже и назови кодовое слово изделия.\n\n" +
      "[Вложения пользователя]\n- " + path + "\n";
    runner.Run(msg, null);

    DateTime until = DateTime.UtcNow.AddSeconds(cfgTimeout + 30);
    while (DateTime.UtcNow < until && !done) Thread.Sleep(200);

    foreach (string t in texts) Console.WriteLine("<< " + t);
    string all = string.Join("\n", texts.ToArray());
    if (all.Contains("АЛЬФА-7314")) { Console.WriteLine("FILES TEST OK"); return 0; }
    Console.WriteLine("FILES TEST FAIL");
    return 1;
  }
}