// Smoke №2 (AddIn-механизм): свой видимый КОМПАС, ждём 15 с (AutoConnect
// подхватывает AddIns при старте), смотрим davinci.log, потом закрываем.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Kompas6API5;

class LibSmoke2
{
  static int Main()
  {
    string log = Path.Combine(Path.GetTempPath(), "kompas-davinci", "davinci.log");
    if (File.Exists(log)) File.Delete(log);

    var before = new HashSet<int>(Pids());
    Console.WriteLine("kompas pids before: " + string.Join(",", before));

    var k = (KompasObject)Activator.CreateInstance(Type.GetTypeFromProgID("KOMPAS.Application.5"));
    try
    {
      k.Visible = true;
      Console.WriteLine("kompas started, waiting 15s for AddIns...");
      Thread.Sleep(15000);
    }
    finally
    {
      try { System.Runtime.InteropServices.Marshal.ReleaseComObject(k); } catch { }
      foreach (int p in Pids())
        if (!before.Contains(p))
        {
          try { Process.GetProcessById(p).Kill(); } catch { }
        }
      Console.WriteLine("kompas closed");
    }

    Console.WriteLine("=== davinci.log ===");
    if (File.Exists(log)) Console.WriteLine(File.ReadAllText(log));
    else Console.WriteLine("(лог не создан)");
    return 0;
  }

  static List<int> Pids()
  {
    var list = new List<int>();
    foreach (Process pr in Process.GetProcessesByName("KOMPAS")) list.Add(pr.Id);
    return list;
  }
}