// mcp-config для claude в %TEMP%\kompas-davinci\: {"mcpServers":{"kompas-mcp":
// {"command":<KompasMcp.exe>,"args":["--attach"]}}}. Сервер всегда в attach-режиме:
// панель работает с КОМПАСом пользователя.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KompasMcp.JsonLib;

namespace KompasMcp.Davinci
{
  public static class McpConfig
  {
    public static string Write(DavinciConfig cfg)
    {
      string exe = cfg.KompasMcpExe;
      if (string.IsNullOrEmpty(exe))
        exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KompasMcp.exe");
      if (!File.Exists(exe))
        throw new ToolException("KompasMcp.exe не найден: " + exe);

      var server = new Dictionary<string, object> { { "command", exe }, { "args", new List<object> { "--attach" } } };
      var servers = new Dictionary<string, object> { { "kompas-mcp", server } };
      var root = new Dictionary<string, object> { { "mcpServers", servers } };

      string dir = Path.Combine(Path.GetTempPath(), "kompas-davinci");
      Directory.CreateDirectory(dir);
      string path = Path.Combine(dir, "mcp-config.json");
      File.WriteAllText(path, Json.Write(root), new UTF8Encoding(false));
      Log.Write("mcp-config: " + path + " -> " + exe);
      return path;
    }
  }
}