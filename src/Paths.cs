using System;
using System.IO;

namespace KompasMcp
{
  // Дефолтные пути артефактов: каталог mcp-out рядом с exe (без привязки к конкретному пользователю).
  public static class Paths
  {
    public static string Out(string fileName)
    {
      string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-out");
      try { Directory.CreateDirectory(dir); } catch { }
      return Path.Combine(dir, fileName);
    }
  }
}