using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using Kompas6API5;
using KompasAPI7;

namespace KompasMcp
{
  // Жизненный цикл КОМПАС: MCP держит СВОЙ экземпляр (каждый Activator.CreateInstance
  // поднимает новый процесс KOMPAS.Exe — грабля из memory). PID определяем дифференцированным
  // снапшотом процессов до/после создания, чтобы при Stop убить только свой.
  //
  // Режим attach (флаги --attach --rot-name KOMPAS_DAVINCHI_<PID>): работаем с видимым
  // КОМПАСом юзера через объект, зарегистрированный библиотекой «Давинчи» в ROT
  // (Этап 3). Start/Show запрещены, Stop=Detach (только отпускаем COM-ссылки).
  public static class KompasHost
  {
    static KompasObject kompas;
    static IApplication app7;
    static int pid;
    static HashSet<int> pidsBefore;
    static bool attach;
    static string rotName;
    static ComTypes.IBindCtx attachBindCtx;

    public static bool IsRunning { get { return kompas != null; } }
    public static bool IsAttach { get { return attach; } }
    public static int Pid { get { return pid; } }

    public static KompasObject Kompas
    {
      get
      {
        if (kompas == null)
        {
          if (attach)
          {
            TryAttach();
            if (kompas == null)
              throw new ToolException("attach: КОМПАС не найден в ROT — запущен ли КОМПАС у юзера? " +
                "(библиотека «Давинчи» ищет моникер " + (rotName ?? "KOMPAS_DAVINCHI_<PID>") +
                ", затем активный KOMPAS.Application.5)");
          }
          else Start(false);
        }
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
      if (attach) throw new ToolException("attach-режим: запуск собственного экземпляра КОМПАСа запрещён (работаем с КОМПАСом юзера)");
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
      if (attach) throw new ToolException("attach-режим: окно КОМПАСа юзера не изменяется");
      if (kompas == null) return;
      try { app7.Visible = visible; }
      catch { try { kompas.Visible = visible; } catch (Exception e) { Log.Error("Show(" + visible + ")", e); } }
    }

    public static void Stop()
    {
      if (attach) { Detach(); return; }
      Log.Write("stop: закрываю свой экземпляр, pid=" + pid);
      ReleaseRefs();
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
      ResetDocumentStatics();
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    // ---- attach (Давинчи) ----

    const string RotPrefix = "KOMPAS_DAVINCHI_";

    public static void ConfigureFromArgs(string[] args)
    {
      for (int i = 0; i < args.Length; i++)
      {
        if (args[i] == "--attach") attach = true;
        else if (args[i] == "--rot-name" && i + 1 < args.Length) { rotName = args[i + 1]; i++; }
      }
      if (attach)
      {
        Log.Write("attach: режим включён, rotName=" + (rotName ?? "(auto)"));
        TryAttach();
      }
    }

    // Порядок: точный rotName → любой моникер KOMPAS_DAVINCHI_* (после
    // перезапуска КОМПАСа pid у нового процесса другой) → активный
    // KOMPAS.Application.5 (регистрация самого КОМПАСа в ROT).
    static void TryAttach()
    {
      try
      {
        if (attachBindCtx == null)
        {
          ComTypes.IBindCtx bc;
          if (CreateBindCtx(0, out bc) != 0 || bc == null)
            throw new ToolException("CreateBindCtx failed");
          attachBindCtx = bc;
        }
        ComTypes.IRunningObjectTable rot;
        if (GetRunningObjectTable(0, out rot) != 0 || rot == null)
          throw new ToolException("GetRunningObjectTable failed");
        if (rotName != null)
        {
          string wanted = rotName;
          if (AttachByDisplayName(rot, name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)))
            return;
          Log.Write("attach: моникер " + rotName + " не найден (КОМПАС перезапускался?) — ищу любой KOMPAS_DAVINCHI_*");
        }
        if (AttachByDisplayName(rot, name => name.StartsWith(RotPrefix, StringComparison.OrdinalIgnoreCase)))
          return;
        // КОМПАС-3D v22 сам регистрирует себя в ROT под моникерами !{6B0B5194-...}
        // (KOMPAS.Application.5) и !{8C3719B5-...} (KOMPAS.Application.7).
        TryGetActiveKompas();
        if (kompas == null)
          Log.Write("attach: подходящий моникер в ROT не найден");
      }
      catch (Exception e)
      {
        Log.Error("attach", e);
      }
    }

    // Перебирает ROT и подключается к первому моникеру, чьё имя прошло match
    // (имя — часть display name после '!'). Возвращает true, если подключились.
    static bool AttachByDisplayName(ComTypes.IRunningObjectTable rot, Predicate<string> match)
    {
      ComTypes.IEnumMoniker enumMk;
      rot.EnumRunning(out enumMk);
      if (enumMk == null) throw new ToolException("ROT: EnumRunning вернул null");
      enumMk.Reset();
      ComTypes.IMoniker[] mks = new ComTypes.IMoniker[1];
      IntPtr pFetched = Marshal.AllocHGlobal(4);
      try
      {
        while (enumMk.Next(1, mks, pFetched) == 0 && Marshal.ReadInt32(pFetched) == 1)
        {
          string name;
          try { mks[0].GetDisplayName(attachBindCtx, null, out name); }
          catch { continue; }
          if (name == null) continue;
          int bang = name.IndexOf('!');
          if (bang >= 0) name = name.Substring(bang + 1);
          if (!match(name)) continue;
          object obj;
          try { rot.GetObject(mks[0], out obj); }
          catch (Exception e) { Log.Error("attach: GetObject(" + name + ")", e); continue; }
          KompasObject k = obj as KompasObject;
          if (k == null)
          {
            Log.Write("attach: объект в ROT (" + name + ") не KompasObject");
            continue;
          }
          kompas = k;
          int tail;
          if (name.Length > RotPrefix.Length &&
              int.TryParse(name.Substring(RotPrefix.Length), out tail))
            pid = tail;
          Log.Write("attach: подключён, rotName=" + name + ", pid=" + pid);
          return true;
        }
      }
      finally { Marshal.FreeHGlobal(pFetched); }
      return false;
    }

    // Подключение к активному КОМПАСу через ROT-регистрацию самого КОМПАСа.
    // PID достаём из снапшота процессов (если запущен ровно один экземпляр).
    static void TryGetActiveKompas()
    {
      try
      {
        object o = Marshal.GetActiveObject("KOMPAS.Application.5");
        KompasObject k = o as KompasObject;
        if (k == null)
        {
          Log.Write("attach: GetActiveObject вернул объект не KompasObject");
          return;
        }
        kompas = k;
        var pids = PidsOfKompas();
        pid = pids.Count == 1 ? pids[0] : 0;
        rotName = "KOMPAS_ACTIVE";
        Log.Write("attach: подключён через GetActiveObject (KOMPAS.Application.5), pid=" + pid);
      }
      catch (Exception e)
      {
        Log.Write("attach: GetActiveObject не нашёл активный КОМПАС: " + e.Message);
      }
    }

    // Умерший COM-объект КОМПАСа: процесс закрыт или упал (RPC недоступен).
    // По такому исключению надо Detach — следующий вызов переподключится к
    // живому КОМПАСу, а не будет долбиться в мёртвую ссылку.
    public static bool IsRpcDead(Exception e)
    {
      System.Runtime.InteropServices.COMException ce =
        e as System.Runtime.InteropServices.COMException;
      if (ce == null) return false;
      return ce.ErrorCode == unchecked((int)0x800706BA) // сервер RPC недоступен (процесс умер)
        || ce.ErrorCode == unchecked((int)0x80010108)  // объект отключён от серверов RPC
        || ce.ErrorCode == unchecked((int)0x800706BE); // сбой при удалённом вызове
    }

    public static void Detach()
    {
      Log.Write("attach: detach — COM-ссылки отпущены, КОМПАС юзера не трогаем");
      ReleaseRefs();
      pid = 0;
      ResetDocumentStatics();
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    static void ReleaseRefs()
    {
      if (app7 != null)
      {
        try { Marshal.ReleaseComObject(app7); } catch (Exception e) { Log.Error("release app7", e); }
        app7 = null;
      }
      if (kompas != null)
      {
        try { Marshal.ReleaseComObject(kompas); } catch (Exception e) { Log.Error("release kompas", e); }
        kompas = null;
      }
    }

    [DllImport("ole32.dll")]
    static extern int GetRunningObjectTable(uint reserved, out ComTypes.IRunningObjectTable prot);
    [DllImport("ole32.dll")]
    static extern int CreateBindCtx(uint reserved, out ComTypes.IBindCtx ppbc);

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

    // Сброс статических полей документов (Tools2D/Tools3D держат COM-ссылки
    // на закрытый документ — после Stop/Detach они мертвы).
    static void ResetDocumentStatics()
    {
      try { Tools.Tools2D.Reset(); } catch (Exception e) { Log.Error("Tools2D.Reset", e); }
      try { Tools.Tools3D.Reset(); } catch (Exception e) { Log.Error("Tools3D.Reset", e); }
    }

    public static Dictionary<string, object> Status()
    {
      var st = new Dictionary<string, object>();
      st["mode"] = attach ? "attach" : "own";
      st["running"] = kompas != null;
      st["pid"] = pid;
      if (attach) st["rotName"] = rotName;
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
      catch (Exception e)
      {
        if (IsRpcDead(e))
        {
          // КОМПАС закрыли/он упал: сбрасываем мёртвые COM-ссылки — панель
          // покажет «закрыт», а следующий вызов тулов переподключится сам.
          Log.Error("status: КОМПАС недоступен — отвязываюсь (мертвый COM-объект)", e);
          Detach();
          st["running"] = false;
          st["pid"] = 0;
          st["visible"] = false;
          st["note"] = "закрыт — подключение восстановится автоматически";
          st["documents"] = docs;
          return st;
        }
        Log.Error("list docs", e);
      }
      st["documents"] = docs;
      return st;
    }
  }
}