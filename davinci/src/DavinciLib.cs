// Общий код библиотеки «Давинчи»: журнал %TEMP%\kompas-davinci\davinci.log (Log.cs сервера
// пишет рядом с exe, а библиотека живёт в процессе КОМПАСа) и запуск панели KompasMcp.exe.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KompasMcp.Davinci
{
  static class DavinciLog
  {
    static readonly object Gate = new object();
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "kompas-davinci");
    static readonly string LogPath = Path.Combine(Dir, "davinci.log");

    public static void Write(string message)
    {
      try
      {
        lock (Gate)
        {
          Directory.CreateDirectory(Dir);
          File.AppendAllText(LogPath,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + "\r\n",
            new UTF8Encoding(false));
        }
      }
      catch { }
    }

    public static void Error(string where, Exception e) { Write("ERROR in " + where + ": " + e); }
  }

  static class DavinciLauncher
  {
    // Запускает KompasMcp.exe --panel, привязанный к ЭТОМУ КОМПАСу через ROT-моникер.
    // Если панель уже открыта, мьютекс внутри KompasMcp выведет её вперёд, а дубль завершится.
    // Возвращает текст ошибки для пользователя или null.
    public static string OpenPanel(string rotName)
    {
      string exe = ResolveExe();
      if (exe == null) return "не найден KompasMcp.exe — проверьте davinci.json рядом с библиотекой";
      string args = "--panel --attach --rot-name " + rotName;
      DavinciLog.Write("panel: start \"" + exe + "\" " + args);
      try
      {
        // KompasMcp.exe — консольное приложение: без CreateNoWindow рядом висит пустая консоль
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        return null;
      }
      catch (Exception e)
      {
        DavinciLog.Error("OpenPanel", e);
        return "не удалось запустить панель: " + e.Message;
      }
    }

    // Путь к KompasMcp.exe: ключ kompasMcpExe в davinci.json рядом с библиотекой, затем
    // KompasMcp.exe в родительской папке и в самой папке. AppDomain.BaseDirectory внутри
    // КОМПАСа — папка KOMPAS.Exe, поэтому отталкиваемся от своей сборки.
    public static string ResolveExe()
    {
      try
      {
        string dllDir = Path.GetDirectoryName(typeof(DavinciLauncher).Assembly.Location);
        string jsonPath = Path.Combine(dllDir, "davinci.json");
        if (File.Exists(jsonPath))
        {
          string path = JsonString(File.ReadAllText(jsonPath), "kompasMcpExe");
          if (path != null && File.Exists(path)) return path;
          DavinciLog.Write("ResolveExe: в davinci.json нет существующего kompasMcpExe: " + jsonPath);
        }
        foreach (string p in new[] { Path.Combine(Path.GetDirectoryName(dllDir), "KompasMcp.exe"),
                                     Path.Combine(dllDir, "KompasMcp.exe") })
          if (File.Exists(p)) return p;
      }
      catch (Exception e) { DavinciLog.Error("ResolveExe", e); }
      return null;
    }

    // Строковое значение ключа из плоского JSON: полный парсер (Json.cs) в .rtw не входит.
    static string JsonString(string json, string key)
    {
      int k = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
      int colon = k >= 0 ? json.IndexOf(':', k) : -1;
      int q1 = colon >= 0 ? json.IndexOf('"', colon + 1) : -1;
      int q2 = q1 >= 0 ? json.IndexOf('"', q1 + 1) : -1;
      return q2 > q1 ? json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\\", "\\") : null;
    }
  }
}
