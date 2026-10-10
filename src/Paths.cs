using System;
using System.IO;

namespace KompasMcp
{
  // Пути артефактов. КОМПАС резолвит относительные пути от СВОЕГО cwd (у COM-сервера он свой),
  // а cwd сервера зависит от клиента — поэтому относительные пути считаются от папки exe.
  public static class Paths
  {
    // Дефолт: каталог mcp-out рядом с exe (без привязки к конкретному пользователю).
    public static string Out(string fileName)
    {
      string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-out");
      try { Directory.CreateDirectory(dir); } catch { }
      return Path.Combine(dir, fileName);
    }

    public static string Abs(string path)
    {
      if (string.IsNullOrEmpty(path)) return path;
      return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
    }
  }
}
