using System;
using System.IO;
using System.Text;
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
      bool panel = false;
      for (int i = 0; i < args.Length; i++) { if (args[i] == "--panel") panel = true; }
      if (panel)
      {
        Log.Write("panel: flag --panel, открываю панель Давинчи");
        Ui.DavinciPanel.Start();
      }

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
        Log.Error("main loop", e);
        Ui.DavinciPanel.Close();
        return 1;
      }
      Ui.DavinciPanel.Close();
      Log.Write("=== kompas-mcp exit ===");
      return 0;
    }

    static int ProcessId()
    {
      return System.Diagnostics.Process.GetCurrentProcess().Id;
    }
  }
}