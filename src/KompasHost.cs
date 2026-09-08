using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Kompas6API5;
using KompasAPI7;

namespace KompasMcp
{
  // Жизненный цикл КОМПАС: MCP держит СВОЙ экземпляр (каждый Activator.CreateInstance
  // поднимает новый процесс KOMPAS.Exe — грабля из memory). PID определяем дифференцированным
  // снапшотом процессов до/после создания, чтобы при Stop убить только свой.
  public static class KompasHost
  {
    static KompasObject kompas;
    static IApplication app7;
    static int pid;
    static HashSet<int> pidsBefore;

    public static bool IsRunning { get { return kompas != null; } }
    public static int Pid { get { return pid; } }

    public static KompasObject Kompas
    {
      get
      {
        if (kompas == null) Start(false);
        return kompas;
      }
    }

    public static IApplication App7
    {
      get
      {
        KompasObject k = Kompas; // гарантирует запуск
        if (app7 == null)
        {
          app7 = (IApplication)k.ksGetApplication7();
          if (app7 == null) throw new ToolException("ksGetApplication7 вернул null");
        }
        return app7;
      }
    }

    public static void Start(bool visible)
    {
      if (kompas != null) { Show(visible); return; }
      Log.Write("start: создаю KOMPAS.Application.5");
      pidsBefore = new HashSet<int>(PidsOfKompas());
      kompas = (KompasObject)Activator.CreateInstance(Type.GetTypeFromProgID("KOMPAS.Application.5"));
      if (kompas == null) throw new ToolException("Activator.CreateInstance вернул null (COM-класс не зарегистрирован? Запустите КОМПАС один раз вручную)");
      try { kompas.Visible = visible; }
      catch (Exception e) { Log.Error("kompas.Visible=" + visible, e); }
      pid = 0;
      foreach (int p in PidsOfKompas())
        if (!pidsBefore.Contains(p)) pid = p;
      Log.Write("start: ok, pid=" + pid);
    }

    public static void Show(bool visible)
    {
      if (kompas == null) return;
      try { app7.Visible = visible; }
      catch { try { kompas.Visible = visible; } catch (Exception e) { Log.Error("Show(" + visible + ")", e); } }
    }

    public static void Stop()
    {
      Log.Write("stop: закрываю свой экземпляр, pid=" + pid);
      if (kompas != null)
      {
        try { if (app7 != null) Marshal.ReleaseComObject(app7); } catch (Exception e) { Log.Error("release app7", e); }
        try { Marshal.ReleaseComObject(kompas); } catch (Exception e) { Log.Error("release kompas", e); }
        kompas = null;
        app7 = null;
      }
      if (pid != 0)
      {
        try
        {
          Process pr = Process.GetProcessById(pid);
          if (!pr.HasExited)
          {
            pr.Kill();
            pr.WaitForExit(5000);
          }
        }
        catch (Exception e) { Log.Error("kill " + pid, e); }
        pid = 0;
      }
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    static List<int> PidsOfKompas()
    {
      var list = new List<int>();
      try
      {
        foreach (Process pr in Process.GetProcessesByName("KOMPAS"))
          list.Add(pr.Id);
      }
      catch (Exception e) { Log.Error("PidsOfKompas", e); }
      return list;
    }

    // ---- информация для tools ----

    public static Dictionary<string, object> Status()
    {
      var st = new Dictionary<string, object>();
      st["running"] = kompas != null;
      st["pid"] = pid;
      if (kompas == null)
      {
        st["visible"] = false;
        st["documents"] = new List<object>();
        return st;
      }
      bool vis = false;
      try { vis = app7 != null && app7.Visible; } catch { }
      st["visible"] = vis;
      var docs = new List<object>();
      try
      {
        var docsCol = App7.Documents;
        int n = docsCol.Count;
        for (int i = 0; i < n; i++)
        {
          var d = docsCol[i];
          var info = new Dictionary<string, object>();
          try { info["name"] = d.Name; }
          catch { info["name"] = "?"; }
          docs.Add(info);
        }
      }
      catch (Exception e) { Log.Error("list docs", e); }
      st["documents"] = docs;
      return st;
    }
  }
}