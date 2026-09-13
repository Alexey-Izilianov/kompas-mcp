// Генерация mcp-config для claude (Этап 5): {"mcpServers":{"kompas-mcp":
// {"command":<KompasMcp.exe>,"args":["--attach"]}}} в %TEMP%\kompas-davinci\.
// Сервер в mcp-config всегда в attach-режиме: панель работает с КОМПАСом юзера.
using System;
using System.Collections.Generic;
using System.IO;
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

      var server = new Dictionary<string, object>();
      server["command"] = exe;
      server["args"] = new List<object> { "--attach" };

      var servers = new Dictionary<string, object>();
      servers["kompas-mcp"] = server;
      var root = new Dictionary<string, object>();
      root["mcpServers"] = servers;

      string dir = Path.Combine(Path.GetTempPath(), "kompas-davinci");
      Directory.CreateDirectory(dir);
      string path = Path.Combine(dir, "mcp-config.json");
      File.WriteAllText(path, Json.Write(root), new System.Text.UTF8Encoding(false));
      Log.Write("mcp-config: " + path + " -> " + exe);
      return path;
    }
  }
}