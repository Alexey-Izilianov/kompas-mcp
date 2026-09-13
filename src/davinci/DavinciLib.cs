// Библиотека «Давинчи» для КОМПАС-3D (Этап 3, Hello-scope): COM-классы,
// загружаемые КОМПАСом. InitLibrary приходит ВНУТРИ процесса КОМПАСа →
// регистрируем ApplicationInterface в ROT под моникером KOMPAS_DAVINCHI_<PID>
// — через него KompasMcp.exe --attach работает с видимым КОМПАСом юзера.
// СВОЙ лог в %TEMP%\kompas-davinci\ (Log.cs kompas-mcp пишет рядом с exe).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using Kompas6API5;
using KompasLibrary;

namespace KompasMcp.Davinci
{
  static class DavinciLog
  {
    static readonly object Gate = new object();
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "kompas-davinci");
    static readonly string LogPath = System.IO.Path.Combine(Dir, "davinci.log");

    public static void Write(string message)
    {
      try
      {
        lock (Gate)
        {
          Directory.CreateDirectory(Dir);
          File.AppendAllText(LogPath,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + "\r\n",
            new System.Text.UTF8Encoding(false));
        }
      }
      catch { }
    }

    public static void Error(string where, Exception e) { Write("ERROR in " + where + ": " + e); }
  }

  // Современный путь: КОМПАС создаёт класс по ProgId/CLSID и вызывает
  // IKompasLibrary. Команды: 1 = панель (Этап 4), 2 = диагностика.
  [ComVisible(true)]
  [Guid("E7A45C31-9B2D-4F6A-8C3E-1D50A7B24E90")]
  [ProgId("KompasMcp.Davinci")]
  [ClassInterface(ClassInterfaceType.None)]
  public class DavinciLibrary : IKompasLibrary
  {
    const string RotPrefix = "KOMPAS_DAVINCHI_";
    object kompasApp;
    int rotCookie;

    [DllImport("ole32.dll")]
    static extern int CreateItemMoniker([MarshalAs(UnmanagedType.LPWStr)] string delim,
      [MarshalAs(UnmanagedType.LPWStr)] string item, out ComTypes.IMoniker mk);
    [DllImport("ole32.dll")]
    static extern int GetRunningObjectTable(uint reserved, out ComTypes.IRunningObjectTable rot);

    public int Version { get { return 1; } }

    // В interop это индексированное свойство COM — в C# приходит методом-аксессором.
    public bool get_IsFunctionEnable(ksKompasLibraryFunctionEnum functionId) { return true; }
    public string LibraryName { get { return "Davinci"; } }
    public string DisplayLibraryName { get { return "Давинчи"; } }
    public string LibraryHelpFile { get { return ""; } }
    public int ProtectNumber { get { return 0; } }
    public bool IsOnApplication7 { get { return true; } }

    public bool InitLibrary(object applicationInterface)
    {
      DavinciLog.Write("InitLibrary: " + (applicationInterface == null
        ? "null" : applicationInterface.GetType().FullName));
      kompasApp = applicationInterface;
      RegisterInRot(applicationInterface);
      return true;
    }

    void RegisterInRot(object applicationInterface)
    {
      try
      {
        string name = RotPrefix + Process.GetCurrentProcess().Id;
        ComTypes.IRunningObjectTable rot;
        if (GetRunningObjectTable(0, out rot) != 0 || rot == null)
        {
          DavinciLog.Write("ROT: GetRunningObjectTable failed");
          return;
        }
        ComTypes.IMoniker mk;
        if (CreateItemMoniker("!", name, out mk) != 0 || mk == null)
        {
          DavinciLog.Write("ROT: CreateItemMoniker failed");
          return;
        }
        rotCookie = rot.Register(1 /*ROTFLAGS_REGISTRATIONKEEPSTRONG*/, applicationInterface, mk);
        DavinciLog.Write("ROT: зарегистрирован " + name);
      }
      catch (Exception e) { DavinciLog.Error("RegisterInRot", e); }
    }

    void RevokeRot()
    {
      try
      {
        if (rotCookie != 0)
        {
          ComTypes.IRunningObjectTable rot;
          if (GetRunningObjectTable(0, out rot) == 0 && rot != null) rot.Revoke(rotCookie);
          rotCookie = 0;
          DavinciLog.Write("ROT: revoke");
        }
      }
      catch (Exception e) { DavinciLog.Error("RevokeRot", e); }
    }

    public bool FillLibraryMenu(IKompasLibraryMenu menu)
    {
      DavinciLog.Write("FillLibraryMenu");
      try
      {
        menu.AddSubMenu("Давинчи");
        menu.AddMenuCommand(1, "Панель");
        menu.AddMenuCommand(2, "Диагностика");
        menu.EndSubMenu();
        return true;
      }
      catch (Exception e) { DavinciLog.Error("FillLibraryMenu", e); return false; }
    }

    public int RunLibraryCommand(int command, int demoMode)
    {
      DavinciLog.Write("RunLibraryCommand: " + command + ", demo=" + demoMode);
      if (command == 1) Show(kompasApp, "Панель появится на Этапе 4 (сейчас эхо-режим не подключён)");
      if (command == 2) Show(kompasApp, "Давинчи: библиотека жива, лог в %TEMP%\\kompas-davinci\\davinci.log");
      return 1;
    }

    // ksMessage через лейт-биндинг (KompasObject API-5)
    static void Show(object kompas, string message)
    {
      try
      {
        KompasObject k = kompas as KompasObject;
        if (k != null) k.ksMessage("Давинчи: " + message);
        else DavinciLog.Write("ksMessage: kompas=null");
      }
      catch (Exception e) { DavinciLog.Error("ksMessage", e); }
    }

    public bool GetLibraryCommandState(int command, out bool enable, out int checked_)
    {
      enable = true;
      checked_ = 0;
      return true;
    }

    public string GetDisableReason(int command) { return ""; }
    public bool FillContextPanel(object contextPanel) { return false; }
    public bool ContextPanelStyleComboChanged(string styleComboId, int styleType, int newValue) { return false; }
    public object GetKompasConverter() { return null; }
    public bool CreateMacroFromSample(int macroReference) { return false; }

    public bool BeginUnloadLibrary()
    {
      DavinciLog.Write("BeginUnloadLibrary");
      RevokeRot();
      return true;
    }
  }

  // Легаси-интерфейс (ksAttachKompasLibrary / старые меню). ksKompasLibrary —
  // dispinterface: методы вызываются по имени через IDispatch, поэтому класс
  // AutoDispatch. Эксперимент Этапа 3 покажет, какой из путей грузит v22.
  [ComVisible(true)]
  [Guid("4C8D9F52-1E63-4B7A-9D4F-2A8B5C6E7D10")]
  [ProgId("KompasMcp.DavinciLegacy")]
  [ClassInterface(ClassInterfaceType.AutoDispatch)]
  public class DavinciLegacy : ksKompasLibrary
  {
    public string GetLibraryName() { return "Davinci"; }
    public string DisplayLibraryName() { return "Давинчи"; }
    public string GetHelpFile() { return ""; }
    public short GetProtectNumber() { return 0; }
    public bool IsOnApplication7() { return true; }

    public bool LibInterfaceNotifyEntry(object applicationInterface)
    {
      DavinciLog.Write("legacy LibInterfaceNotifyEntry");
      return true;
    }

    public bool LibInterfaceNotifyDisconnect()
    {
      DavinciLog.Write("legacy LibInterfaceNotifyDisconnect");
      return true;
    }

    public int ExternalGetMenu() { return 1; }

    public string ExternalMenuItem(short index, out short itemType, out short command)
    {
      itemType = 0;
      command = 0;
      if (index == 1) { command = 1; return "Панель"; }
      if (index == 2) { command = 2; return "Диагностика"; }
      return null;
    }

    public void ExternalRunCommand(short command, short mode, object applicationInterface)
    {
      DavinciLog.Write("legacy ExternalRunCommand: " + command + ", mode=" + mode);
      try
      {
        KompasObject k = applicationInterface as KompasObject;
        if (k != null)
          k.ksMessage("Давинчи (legacy): команда " + command + ", лог в %TEMP%\\kompas-davinci");
      }
      catch (Exception e) { DavinciLog.Error("ExternalRunCommand", e); }
    }

    public object GetIKompasConverter() { return null; }
    public bool LibraryCommandState(int command, out bool enable, out int checked_)
    {
      enable = true;
      checked_ = 0;
      return true;
    }
    public string GetDisableReason(int command) { return ""; }
    public bool FillContextPanel(object contextPanel) { return false; }
    public bool ContextPanelStyleComboChanged(string styleComboId, int styleType, int newValue) { return false; }
    public bool CreateMacroFromSample(int macroReference) { return false; }
  }
}