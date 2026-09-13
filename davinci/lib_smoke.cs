// Smoke библиотеки «Давинчи» (Этап 3): свой видимый экземпляр КОМПАСа,
// попытки подключения Davinci.rtw легаси (ksAttachKompasLibrary) и API-7
// (ProceduresLibraries.Add), затем закрытие своего экземпляра.
// Результат смотрим по выводу и %TEMP%\kompas-davinci\davinci.log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Kompas6API5;
using KompasAPI7;

class LibSmoke
{
  static int Main()
  {
    string rtw = @"C:\Projects\kompas-test\kompas-mcp\davinci\Davinci.rtw";
    string log = Path.Combine(Path.GetTempPath(), "kompas-davinci", "davinci.log");
    if (File.Exists(log)) File.Delete(log);

    var before = new HashSet<int>(Pids());
    Console.WriteLine("kompas pids before: " + string.Join(",", before));

    var k = (KompasObject)Activator.CreateInstance(Type.GetTypeFromProgID("KOMPAS.Application.5"));
    try
    {
      k.Visible = true;

      // Путь 1: легаси .rtw
      int libId = 0;
      try { libId = k.ksAttachKompasLibrary(rtw); }
      catch (Exception e) { Console.WriteLine("legacy attach EXC: " + e.Message); }
      Console.WriteLine("legacy attach libraryId=" + libId);
      if (libId != 0)
      {
        try
        {
          k.ksExecuteKompasLibraryCommand(libId, 2);
          Console.WriteLine("legacy run cmd 2: ok");
        }
        catch (Exception e) { Console.WriteLine("legacy run EXC: " + e.Message); }
        try { k.ksDetachKompasLibrary(libId); } catch { }
      }

      // Путь 2: API-7
      try
      {
        IApplication app7 = (IApplication)k.ksGetApplication7();
        ILibraryManager lm = app7.LibraryManager;
        IProceduresLibraries pl = lm.ProceduresLibraries;
        Console.WriteLine("procedures libraries count before: " + pl.Count);
        ProceduresLibrary added = pl.Add(rtw, "");
        Console.WriteLine("API7 add: " + (added != null ? "OK ref=" + added.Reference : "null"));
      }
      catch (Exception e)
      {
        Console.WriteLine("API7 add EXC: " + e.Message +
          (e.InnerException == null ? "" : " / " + e.InnerException.Message));
      }
    }
    finally
    {
      try { MarshalRelease(k); } catch { }
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

  static void MarshalRelease(object com)
  {
    System.Runtime.InteropServices.Marshal.ReleaseComObject(com);
  }

  static List<int> Pids()
  {
    var list = new List<int>();
    foreach (Process pr in Process.GetProcessesByName("KOMPAS")) list.Add(pr.Id);
    return list;
  }
}