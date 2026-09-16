// Нативно-совместимая библиотека «Давинчи» (.rtw): КОМПАС грузит .rtw через
// LoadLibrary и вызывает native-экспорты (SDK LibraryWizard: LIBRARYID,
// LIBRARYENTRY, LIBRARYNAME(W), LibInterfaceNotifyEntry, LibIsOnApplication7).
// Управляемые методы этого класса получают экспорты на этапе сборки
// (build-native.ps1: ildasm + .export/.vtentry + ilasm), меню библиотеки
// лежит в Win32-ресурсах (lib.rc: MENU 100 + STRINGTABLE). COM-вариант
// (DavinciLibrary) остаётся для совместимости, КОМПАС его не грузит.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Kompas6API5;

namespace KompasMcp.Davinci
{
  static class DavinciNative
  {
    static readonly GCHandle nameW = Pin(Encoding.Unicode.GetBytes("Давинчи\0"), out nameWPtr);
    static readonly GCHandle nameA = Pin(Encoding.ASCII.GetBytes("Davinci\0"), out nameAPtr);
    static readonly IntPtr nameWPtr;
    static readonly IntPtr nameAPtr;
    static object kompasApp;
    static int rotCookie;
    static string ownDir;
    static bool resolverInstalled;

    static GCHandle Pin(byte[] bytes, out IntPtr ptr)
    {
      ptr = Marshal.UnsafeAddrOfPinnedArrayElement(bytes, 0);
      return GCHandle.Alloc(bytes, GCHandleType.Pinned);
    }

    // .rtw загружен КОМПАСом нативно, поэтому CLR ищет зависимые сборки от
    // каталога KOMPAS.Exe, где interop-DLL нет: клик по меню падал с
    // FileNotFoundException до первой строки метода. Ставим resolver на свою
    // папку и предзагружаем interop — вызывается из ранних экспортов (attach).
    static void EnsureResolvers()
    {
      if (resolverInstalled) return;
      resolverInstalled = true;
      try
      {
        ownDir = OwnDirectory();
        DavinciLog.Write("native EnsureResolvers: " + ownDir);
        if (ownDir == null) return;
        AppDomain.CurrentDomain.AssemblyResolve += LoadFromOwnDir;
        foreach (string n in new[] { "KompasLibrary", "Kompas6API5", "Kompas6Constants",
          "Kompas6Constants3D", "KompasAPI7", "KAPITypes" })
        {
          string p = Path.Combine(ownDir, n + ".dll");
          if (File.Exists(p)) Assembly.LoadFrom(p);
        }
      }
      catch (Exception e) { DavinciLog.Error("EnsureResolvers", e); }
    }

    static string OwnDirectory()
    {
      try
      {
        string loc = typeof(DavinciNative).Assembly.Location;
        if (!string.IsNullOrEmpty(loc) && File.Exists(loc)) return Path.GetDirectoryName(loc);
        foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
        {
          if (string.Equals(Path.GetFileNameWithoutExtension(m.FileName), "DavinciNative",
            StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(m.FileName);
        }
      }
      catch (Exception e) { DavinciLog.Error("OwnDirectory", e); }
      return null;
    }

    static Assembly LoadFromOwnDir(object sender, ResolveEventArgs args)
    {
      if (ownDir == null) return null;
      string p = Path.Combine(ownDir, new AssemblyName(args.Name).Name + ".dll");
      return File.Exists(p) ? Assembly.LoadFrom(p) : null;
    }

    // --- экспорты (имена обязаны совпадать с native-именами) ---

    // Идентификатор ресурса библиотеки: меню STRINGTABLE/MENU с этим id.
    public static uint LIBRARYID() { return 100; }

    public static IntPtr LIBRARYNAMEW() { return nameWPtr; }
    public static IntPtr LIBRARYNAME() { return nameAPtr; }
    public static int LIBRARYPROTECTNUMBER() { return 0; }

    // Новое API (API-7): получаем приложение в LibInterfaceNotifyEntry.
    public static int LibIsOnApplication7() { EnsureResolvers(); return 1; }

    // КОМПАС передаёт интерфейс приложения (IDispatch*): запоминаем его для
    // команд и регистрируем в ROT под KOMPAS_DAVINCHI_<PID>.
    public static int LibInterfaceNotifyEntry(IntPtr application)
    {
      EnsureResolvers();
      DavinciLog.Write("native LibInterfaceNotifyEntry: " + application);
      try
      {
        if (application != IntPtr.Zero)
        {
          kompasApp = Marshal.GetObjectForIUnknown(application);
          RegisterInRot(kompasApp);
        }
        return 0;
      }
      catch (Exception e) { DavinciLog.Error("LibInterfaceNotifyEntry", e); return -1; }
    }

    public static int LibInterfaceNotifyDisconnect()
    {
      DavinciLog.Write("native LibInterfaceNotifyDisconnect");
      RevokeRot();
      return 0;
    }

    // Головная функция библиотеки: 1 = панель, 2 = диагностика.
    public static void LIBRARYENTRY(uint comm)
    {
      EnsureResolvers();
      DavinciLog.Write("native LIBRARYENTRY: " + comm);
      try
      {
        if (comm == 1) DavinciLibrary.OpenPanel(kompasApp);
        if (comm == 2)
        {
          string msg = "Давинчи: ROT " + RotPrefix + System.Diagnostics.Process.GetCurrentProcess().Id +
            ", лог %TEMP%\\kompas-davinci\\davinci.log";
          KompasObject k = kompasApp as KompasObject;
          if (k != null) k.ksMessage("Давинчи: " + msg);
          else DavinciLog.Write("диагностика: " + msg);
        }
      }
      catch (Exception e) { DavinciLog.Error("LIBRARYENTRY", e); }
    }

    const string RotPrefix = "KOMPAS_DAVINCHI_";

    [DllImport("ole32.dll")]
    static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")]
    static extern int CreateItemMoniker([MarshalAs(UnmanagedType.LPWStr)] string delim,
      [MarshalAs(UnmanagedType.LPWStr)] string item, out IMoniker mk);

    static void RegisterInRot(object applicationInterface)
    {
      string name = RotPrefix + System.Diagnostics.Process.GetCurrentProcess().Id;
      IRunningObjectTable rot;
      if (GetRunningObjectTable(0, out rot) != 0 || rot == null) return;
      IMoniker mk;
      if (CreateItemMoniker("!", name, out mk) != 0 || mk == null) return;
      rotCookie = rot.Register(1 /*ROTFLAGS_REGISTRATIONKEEPSTRONG*/, applicationInterface, mk);
      DavinciLog.Write("native ROT: зарегистрирован " + name);
    }

    static void RevokeRot()
    {
      try
      {
        if (rotCookie != 0)
        {
          IRunningObjectTable rot;
          if (GetRunningObjectTable(0, out rot) == 0 && rot != null) rot.Revoke(rotCookie);
          rotCookie = 0;
        }
      }
      catch (Exception e) { DavinciLog.Error("RevokeRot", e); }
    }
  }
}