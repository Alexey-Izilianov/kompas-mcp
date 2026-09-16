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
      Log.Write("=== kompas-mcp started, pid=" + ProcessId() + " ===");
      try { Console.OutputEncoding = new UTF8Encoding(false); }
      catch (Exception e) { Log.Error("OutputEncoding", e); }
      KompasHost.ConfigureFromArgs(args);
      if (HasFlag(args, "--panel")) return PanelMain();
      StdioLoop();
      Ui.DavinciPanel.Close();
      Log.Write("=== kompas-mcp exit ===");
      return 0;
    }

    static bool HasFlag(string[] args, string flag)
    {
      for (int i = 0; i < args.Length; i++)
        if (args[i] == flag) return true;
      return false;
    }

    // Панельный режим: процесс порождён меню «Давинчи → Панель» из КОМПАСа
    // (stdin у него может отсутствовать), поэтому живём, пока открыта панель,
    // а stdio-луп обслуживаем в фоне.
    static int PanelMain()
    {
      Log.Write("panel: flag --panel, открываю панель Давинчи");
      try
      {
        Davinci.DavinciSession.Init();
        Ui.DavinciPanel.SubmitHandler = Davinci.DavinciSession.Submit;
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
            if (response != null)
            {
              stdout.WriteLine(response);
              stdout.Flush();
            }
          }
        }
      }
      catch (Exception e)
      {
        // в панельном режиме stdin может отсутствовать вовсе — панель не трогаем
        Log.Error("stdio loop", e);
      }
    }

    static int ProcessId()
    {
      return System.Diagnostics.Process.GetCurrentProcess().Id;
    }
  }
}