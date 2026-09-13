// Runner-тест без КОМПАСа (Этап 2): spawn claude -p через ClaudeRunner,
// ping через kompas-mcp, --resume, Stop (taskkill). Сборка: build_runner_test.ps1.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using KompasMcp.Davinci;

namespace KompasMcp.Davinci.Test
{
  static class RunnerTest
  {
    static int failures;

    static void Check(string name, bool ok)
    {
      Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
      if (!ok) failures++;
    }

    static List<StreamEvent> RunOnce(ClaudeRunnerOptions opts, string msg, string resume,
      out string sessionId, int waitMs)
    {
      var evs = new List<StreamEvent>();
      var r = new ClaudeRunner { Options = opts };
      var done = new ManualResetEvent(false);
      r.OnEvent += delegate(StreamEvent e) { lock (evs) evs.Add(e); };
      r.OnExit += delegate { done.Set(); };
      r.OnError += delegate(Exception e) { Console.WriteLine("[runner error] " + e.Message); };
      r.Run(msg, resume);
      if (!done.WaitOne(waitMs))
      {
        Console.WriteLine("[timeout] стоп по таймауту");
        r.Stop();
        done.WaitOne(15000);
      }
      sessionId = r.SessionId;
      return evs;
    }

    static bool AnyToolUse(List<StreamEvent> evs, out string toolResult)
    {
      toolResult = null;
      bool saw = false;
      lock (evs)
        foreach (StreamEvent e in evs)
        {
          if (e.ToolName != null) saw = true;
          if (e.ToolResultText != null) toolResult = e.ToolResultText;
        }
      return saw;
    }

    static int Main(string[] args)
    {
      Console.OutputEncoding = new UTF8Encoding(false);
      string root = AppDomain.CurrentDomain.BaseDirectory;                 // tests\
      string serverExe = Path.GetFullPath(Path.Combine(root, "..", "KompasMcp.exe"));
      Check("server exe exists", File.Exists(serverExe));

      string cfgDir = Path.Combine(Path.GetTempPath(), "kompas-davinci-test");
      Directory.CreateDirectory(cfgDir);
      string cfgPath = Path.Combine(cfgDir, "mcp-config.json");
      File.WriteAllText(cfgPath,
        "{\"mcpServers\":{\"kompas-mcp\":{\"command\":\"" + serverExe.Replace("\\", "\\\\") + "\"}}}",
        new UTF8Encoding(false));

      var opts = new ClaudeRunnerOptions { McpConfigPath = cfgPath, Workspace = root };

      // --- Тест A: первый запрос, tool ping ---
      string sid1;
      var evsA = RunOnce(opts,
        "Вызови tool ping MCP-сервера kompas-mcp и процитируй его ответ одним предложением.",
        null, out sid1, 180000);
      string toolResultA;
      bool sawPing = AnyToolUse(evsA, out toolResultA);
      StreamEvent resultA = FindResult(evsA);
      Check("A: session_id получен", !string.IsNullOrEmpty(sid1));
      Check("A: tool_use был", sawPing);
      Check("A: tool_result содержит ok", toolResultA != null && toolResultA.Contains("ok"));
      Check("A: result success", resultA != null && resultA.Subtype == "success" && !resultA.IsError);
      Check("A: финальный текст не пуст", resultA != null && !string.IsNullOrEmpty(resultA.ResultText));

      // --- Тест B: resume без вызова tools ---
      string sid2;
      var evsB = RunOnce(opts,
        "Без вызова tools: какой JSON вернул ping, который ты вызывал ранее в этой сессии? Ответь одной строкой.",
        sid1, out sid2, 180000);
      string toolResultB;
      bool sawToolB = AnyToolUse(evsB, out toolResultB);
      StreamEvent resultB = FindResult(evsB);
      Check("B: session_id совпал", sid2 == sid1);
      Check("B: без tool_use", !sawToolB);
      Check("B: result success", resultB != null && resultB.Subtype == "success");
      Check("B: помнит ping", resultB != null && resultB.ResultText != null &&
        (resultB.ResultText.Contains("ok") || resultB.ResultText.Contains("true")));
      if (resultB != null) Console.WriteLine("[B text] " + resultB.ResultText);

      // --- Тест C: Stop (taskkill /T /F) ---
      var rC = new ClaudeRunner { Options = opts };
      var doneC = new ManualResetEvent(false);
      rC.OnExit += delegate { doneC.Set(); };
      rC.Run("Не вызывай tools. Пиши очень длинный рассказ о фланцах: не меньше 1500 слов.", null);
      Thread.Sleep(4000);
      Check("C: процесс был запущен", rC.Pid != 0);
      rC.Stop();
      Check("C: процесс убит за 15с", doneC.WaitOne(15000) && !rC.Running);

      Console.WriteLine(failures == 0 ? "RUNNER TEST OK" : "RUNNER TEST FAILED: " + failures);
      return failures == 0 ? 0 : 1;
    }

    static StreamEvent FindResult(List<StreamEvent> evs)
    {
      lock (evs)
        foreach (StreamEvent e in evs)
          if (e.Type == "result") return e;
      return null;
    }
  }
}