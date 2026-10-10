using System;
using System.IO;
using System.Text;
using System.Threading;
using KompasMcp.JsonLib;

namespace KompasMcp
{
  // Точка входа: бесконечный stdio-луп MCP. Каждый вызов — одна строка JSON на stdin,
  // ответ — одна строка JSON на stdout. Любой посторонний вывод в stdout запрещён:
  // диагностика только через Log (kompas-mcp.log).
  class Program
  {
    [STAThread]
    static int Main(string[] args)
    {
      try { Console.OutputEncoding = new UTF8Encoding(false); }
      catch (Exception e) { Log.Error("OutputEncoding", e); }
      if (HasFlag(args, "--version"))
      {
        Console.WriteLine(Rpc.ServerName + " " + Rpc.ServerVersion);
        return 0;
      }
      int md = Array.IndexOf(args, "--tools-md");
      if (md >= 0)
      {
        // справочник инструментов из реестра: docs/tools.md всегда совпадает со схемами
        string text = ToolsDoc.Markdown();
        if (md + 1 < args.Length) File.WriteAllText(args[md + 1], text, new UTF8Encoding(false));
        else Console.Write(text);
        return 0;
      }
      Log.Write("=== kompas-mcp " + Rpc.ServerVersion + " started, pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " ===");
      KompasHost.ConfigureFromArgs(args);
      if (HasFlag(args, "--panel")) return PanelMain();
      StdioLoop();
      KompasHost.Shutdown();
      Ui.DavinciPanel.Close();
      Log.Write("=== kompas-mcp exit ===");
      return 0;
    }

    static bool HasFlag(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }

    // Панельный режим: процесс запущен меню «Давинчи → Панель» из КОМПАСа и stdin может
    // отсутствовать, поэтому живём, пока открыта панель, а stdio-луп крутится в фоне.
    static int PanelMain()
    {
      Log.Write("panel: flag --panel, открываю панель Давинчи");
      try
      {
        Davinci.DavinciSession.Init();
        Ui.DavinciPanel.SubmitHandler = Davinci.DavinciSession.Submit;
        Ui.DavinciPanel.AbortHandler = Davinci.DavinciSession.Abort;
        Ui.DavinciPanel.NewDialogHandler = Davinci.DavinciSession.Reset;
        Ui.DavinciPanel.ClaudeStatusProvider = Davinci.DavinciSession.CheckClaude;
        Log.Write("panel: копилот подключён (DavinciSession)");
      }
      catch (Exception e)
      {
        Log.Error("panel init", e);
        Ui.DavinciPanel.SubmitHandler = null; // эхо-режим
      }
      if (!Ui.DavinciPanel.Start())
      {
        // панель уже открыта в другом процессе — там её вывели на передний план
        Log.Write("panel: уже запущена в другом процессе, выходим");
        return 0;
      }
      Thread stdio = new Thread(StdioLoop);
      stdio.IsBackground = true;
      stdio.Start();
      Ui.DavinciPanel.WaitClosed();
      Davinci.DavinciSession.Shutdown();
      Log.Write("=== kompas-mcp exit ===");
      return 0;
    }

    static void StdioLoop()
    {
      try
      {
        using (var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
        using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })
        {
          string line;
          while ((line = stdin.ReadLine()) != null)
          {
            line = line.Trim();
            if (line.Length == 0) continue;
            string response;
            try { response = Rpc.Handle(line); }
            catch (Exception e)
            {
              Log.Error("outer handle", e);
              response = null;
            }
            if (response != null) stdout.WriteLine(response);   // AutoFlush
          }
        }
      }
      catch (Exception e)
      {
        // в панельном режиме stdin может отсутствовать вовсе — панель не трогаем
        Log.Error("stdio loop", e);
      }
    }
  }
}