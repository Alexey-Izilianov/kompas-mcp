using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KompasMcp.JsonLib;

namespace KompasMcp
{
  // Диспетчер MCP stdio: построчный JSON-RPC 2.0 (не LSP-фрейминг).
  // Методы: initialize, ping, tools/list, tools/call; уведомления игнорируются.
  public static class Rpc
  {
    public const string ServerName = "kompas-mcp";
    public const string ServerVersion = "1.0.0";

    // Возвращает строку-ответ (одна строка JSON) или null, если отвечать не нужно.
    public static string Handle(string line)
    {
      object msg;
      try { msg = Json.Parse(line); }
      catch (Exception e) { return Error(null, -32700, "Parse error: " + e.Message); }
      var dict = msg as Dictionary<string, object>;
      if (dict == null) return Error(null, -32600, "Request must be an object");

      string method = GetStr(dict, "method");
      if (!dict.ContainsKey("id") || method == null) return null;   // уведомление — молча
      object id = dict["id"];
      var prms = (dict.ContainsKey("params") ? dict["params"] : null) as Dictionary<string, object>;

      try
      {
        object result;
        switch (method)
        {
          case "initialize": result = Initialize(prms); break;
          case "ping": result = new Dictionary<string, object>(); break;
          case "tools/list": result = ToolsList(); break;
          case "tools/call": result = ToolsCall(prms); break;
          default: return Error(id, -32601, "Method not found: " + method);
        }
        return Response(id, "result", result);
      }
      catch (ToolException te)
      {
        return Response(id, "result", Content(te.Message, true));
      }
      catch (Exception e)
      {
        var ce = e as COMException;
        if (ce != null && KompasHost.IsRpcDead(ce))
        {
          // КОМПАС закрыли или он упал во время вызова: сбрасываем ссылки и отдаём
          // модели понятный текст вместо внутреннего -32603.
          Log.Error("rpc: КОМПАС недоступен — отвязываюсь (мертвый COM-объект)", ce);
          KompasHost.Detach();
          return Response(id, "result", Content("КОМПАС закрыт или недоступен (RPC не отвечает). " +
            "Запустите КОМПАС и повторите — подключение восстановится автоматически.", true));
        }
        Log.Error("handle " + method, e);
        return Error(id, -32603, "Internal error: " + e.Message);
      }
    }

    static object Initialize(Dictionary<string, object> prms)
    {
      string protoVersion = GetStr(prms, "protocolVersion") ?? "2024-11-05";
      return new Dictionary<string, object>
      {
        { "protocolVersion", protoVersion },
        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
        { "serverInfo", new Dictionary<string, object> { { "name", ServerName }, { "version", ServerVersion } } },
      };
    }

    // Реестр не меняется после статического конструктора ToolRegistry — список строится один раз.
    static List<object> toolsList;

    static object ToolsList()
    {
      if (toolsList == null)
      {
        var list = new List<object>();
        foreach (ToolDef t in ToolRegistry.All)
          list.Add(new Dictionary<string, object>
          {
            { "name", t.Name }, { "description", t.Description }, { "inputSchema", new RawJson(t.SchemaJson) },
          });
        toolsList = list;
      }
      return new Dictionary<string, object> { { "tools", toolsList } };
    }

    static object ToolsCall(Dictionary<string, object> prms)
    {
      if (prms == null) throw new ToolException("tools/call без params");
      string name = GetStr(prms, "name");
      if (name == null) throw new ToolException("tools/call без name");
      ToolDef tool = ToolRegistry.Find(name);
      if (tool == null) throw new ToolException("Неизвестный tool: " + name);

      var args = (prms.ContainsKey("arguments") ? prms["arguments"] : null) as Dictionary<string, object>
        ?? new Dictionary<string, object>();
      Log.Write("call " + name + " " + Json.Write(args));
      object result = tool.Handler(args);
      var multi = result as Tools.ToolContent;
      if (multi != null)
      {
        Log.Write("call " + name + " -> " + multi.Items.Count + " элемент(а) содержимого");
        return new Dictionary<string, object> { { "content", multi.Items }, { "isError", false } };
      }
      string text = Json.Write(result);
      Log.Write("call " + name + " -> " + (text.Length > 500 ? text.Substring(0, 500) + "..." : text));
      return Content(text, false);
    }

    static Dictionary<string, object> Content(string text, bool isError)
    {
      var item = new Dictionary<string, object> { { "type", "text" }, { "text", text } };
      return new Dictionary<string, object> { { "content", new List<object> { item } }, { "isError", isError } };
    }

    static string Response(object id, string key, object value)
    {
      return Json.Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { key, value } });
    }

    static string Error(object id, int code, string message)
    {
      return Response(id, "error", new Dictionary<string, object> { { "code", code }, { "message", message } });
    }

    static string GetStr(Dictionary<string, object> d, string key)
    {
      object v;
      return d != null && d.TryGetValue(key, out v) ? v as string : null;
    }
  }
}
