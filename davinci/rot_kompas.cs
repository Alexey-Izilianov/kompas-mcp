// ROT + КОМПАС: свой видимый экземпляр, пауза, перечисление ROT — что
// КОМПАС-3D v22 регистрирует в Running Object Table. Потом закрытие.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using Kompas6API5;

class RotKompas
{
  [DllImport("ole32.dll")]
  static extern int GetRunningObjectTable(uint reserved, out ComTypes.IRunningObjectTable rot);
  [DllImport("ole32.dll")]
  static extern int CreateBindCtx(uint reserved, out ComTypes.IBindCtx ctx);

  static int Main()
  {
    var before = new HashSet<int>(Pids());
    Console.WriteLine("kompas pids before: " + string.Join(",", before));

    DumpRot("до запуска КОМПАСа");

    var k = (KompasObject)Activator.CreateInstance(Type.GetTypeFromProgID("KOMPAS.Application.5"));
    try
    {
      k.Visible = true;
      Console.WriteLine("kompas started, waiting 10s...");
      System.Threading.Thread.Sleep(10000);
      DumpRot("с КОМПАСом");
    }
    finally
    {
      try { Marshal.ReleaseComObject(k); } catch { }
      foreach (int p in Pids())
        if (!before.Contains(p))
        {
          try { Process.GetProcessById(p).Kill(); } catch { }
        }
      Console.WriteLine("kompas closed");
    }
    return 0;
  }

  static void DumpRot(string when)
  {
    Console.WriteLine("=== ROT (" + when + ") ===");
    try
    {
      ComTypes.IRunningObjectTable rot;
      if (GetRunningObjectTable(0, out rot) != 0) { Console.WriteLine("GetRunningObjectTable failed"); return; }
      ComTypes.IEnumMoniker e;
      rot.EnumRunning(out e);
      e.Reset();
      var mons = new ComTypes.IMoniker[64];
      IntPtr fetched = Marshal.AllocHGlobal(4);
      ComTypes.IBindCtx bctx;
      CreateBindCtx(0, out bctx);
      int total = 0;
      while (true)
      {
        e.Next(64, mons, fetched);
        int n = Marshal.ReadInt32(fetched);
        if (n == 0) break;
        for (int i = 0; i < n; i++)
        {
          string dn;
          try { mons[i].GetDisplayName(bctx, null, out dn); }
          catch { dn = "(ошибка display name)"; }
          Console.WriteLine("  " + dn);
          total++;
        }
      }
      Marshal.FreeHGlobal(fetched);
      Console.WriteLine("  всего: " + total);
    }
    catch (Exception ex) { Console.WriteLine("ROT EXC: " + ex.Message); }
  }

  static List<int> Pids()
  {
    var list = new List<int>();
    foreach (Process pr in Process.GetProcessesByName("KOMPAS")) list.Add(pr.Id);
    return list;
  }
}