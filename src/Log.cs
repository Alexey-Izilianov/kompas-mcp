using System;
using System.IO;
using System.Text;

namespace KompasMcp
{
  // Файловый лог: stdout занят протоколом, поэтому диагностика только сюда.
  public static class Log
  {
    static readonly object Gate = new object();
    static string path;

    public static string Path
    {
      get
      {
        if (path == null)
        {
          string dir = AppDomain.CurrentDomain.BaseDirectory;
          path = System.IO.Path.Combine(dir, "kompas-mcp.log");
        }
        return path;
      }
    }

    public static void Write(string message)
    {
      try
      {
        lock (Gate)
        {
          File.AppendAllText(Path,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " + message + "\r\n",
            new UTF8Encoding(false));
        }
      }
      catch { }
    }

    public static void Error(string where, Exception e)
    {
      Write("ERROR in " + where + ": " + e);
    }
  }
}