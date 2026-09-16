using System;
using System.Collections.Generic;
using KompasMcp.JsonLib;

namespace KompasMcp
{
  // Диспетчер MCP stdio: построчный JSON-RPC 2.0 (не LSP-фрейминг).
  // Методы: initialize, ping, tools/list, tools/call; уведомления игнорируются.
  public static class Rpc
  {
    const string ServerName = "kompas-mcp";
    const string ServerVersion = "0.1.0";

    // Возвращает строку-ответ (одна строка JSON) или null, если отвечать не нужно.
    public static string Handle(string line)
    {
      object msg;
      try { msg = Json.Parse(line); }
      catch (Exception e)
      {
        return Error(null, -32700, "Parse error: " + e.Message);
      }
      var dict = msg as Dictionary<string, object>;
      if (dict == null) return Error(null, -32600, "Request must be an object");

      bool hasId = dict.ContainsKey("id");
      string method = GetStr(dict, "method");

      if (!hasId || method == null)
        return null; // уведомление — молча

      object idObj = dict["id"];

      object paramsObj;
      dict.TryGetValue("params", out paramsObj);
      var prms = paramsObj as Dictionary<string, object>;

      try
      {
        object result;
        if (method == "initialize")
        {
          result = Initialize(prms);
        }
        else if (method == "ping")
        {
          result = new Dictionary<string, object>();
        }
        else if (method == "tools/list")
        {
          result = ToolsList();
        }
        else if (method == "tools/call")
        {
          result = ToolsCall(prms);
        }
        else
        {
          return Error(idObj, -32601, "Method not found: " + method);
        }

        var resp = new Dictionary<string, object>();
        resp["jsonrpc"] = "2.0";
        resp["id"] = idObj;
        resp["result"] = result;
        return Json.Write(resp);
      }
      catch (ToolException te)
      {
        return ToolError(idObj, te.Message);
      }
      catch (System.Runtime.InteropServices.COMException ce)
      {
        if (KompasHost.IsRpcDead(ce))
        {
          // КОМПАС закрыли/он упал во время вызова: сбрасываем ссылки и отдаём
          // модели понятный текст вместо внутреннего -32603.
          Log.Error("rpc: КОМПАС недоступен — отвязываюсь (мертвый COM-объект)", ce);
          KompasHost.Detach();
          return ToolError(idObj, "КОМПАС закрыт или недоступен (RPC не отвечает). " +
            "Запустите КОМПАС и повторите — подключение восстановится автоматически.");
        }
        throw;
      }
      catch (Exception e)
      {
        Log.Error("handle " + method, e);
        return Error(idObj, -32603, "Internal error: " + e.Message);
      }
    }

    static object Initialize(Dictionary<string, object> prms)
    {
      string protoVersion = "2024-11-05";
      if (prms != null && prms.ContainsKey("protocolVersion") && prms["protocolVersion"] is string)
        protoVersion = (string)prms["protocolVersion"];

      var toolsCap = new Dictionary<string, object>();
      var caps = new Dictionary<string, object>();
      caps["tools"] = toolsCap;

      var info = new Dictionary<string, object>();
      info["name"] = ServerName;
      info["version"] = ServerVersion;

      var result = new Dictionary<string, object>();
      result["protocolVersion"] = protoVersion;
      result["capabilities"] = caps;
      result["serverInfo"] = info;
      return result;
    }

    static object ToolsList()
    {
      var list = new List<object>();
      foreach (ToolDef t in ToolRegistry.All)
      {
        var d = new Dictionary<string, object>();
        d["name"] = t.Name;
        d["description"] = t.Description;
        d["inputSchema"] = new RawJson(t.SchemaJson);
        list.Add(d);
      }
      var result = new Dictionary<string, object>();
      result["tools"] = list;
      return result;
    }

    static object ToolsCall(Dictionary<string, object> prms)
    {
      if (prms == null) throw new ToolException("tools/call без params");
      string name = GetStr(prms, "name");
      if (name == null) throw new ToolException("tools/call без name");

      ToolDef tool = ToolRegistry.Find(name);
      if (tool == null) throw new ToolException("Неизвестный tool: " + name);

      object argsObj;
      var args = prms.TryGetValue("arguments", out argsObj)
        ? argsObj as Dictionary<string, object> ?? new Dictionary<string, object>()
        : new Dictionary<string, object>();

      Log.Write("call " + name + " " + Json.Write(args));
      object result = tool.Handler(args);
      string text = Json.Write(result);
      Log.Write("call " + name + " -> " + (text.Length > 500 ? text.Substring(0, 500) + "..." : text));

      var content = new List<object>();
      var item = new Dictionary<string, object>();
      item["type"] = "text";
      item["text"] = text;
      content.Add(item);

      var resp = new Dictionary<string, object>();
      resp["content"] = content;
      resp["isError"] = false;
      return resp;
    }

    static string GetStr(Dictionary<string, object> d, string key)
    {
      object v;
      if (d != null && d.TryGetValue(key, out v) && v is string) return (string)v;
      return null;
    }

    static string ToolError(object idObj, string message)
    {
      var content = new List<object>();
      var item = new Dictionary<string, object>();
      item["type"] = "text";
      item["text"] = message;
      content.Add(item);

      var result = new Dictionary<string, object>();
      result["content"] = content;
      result["isError"] = true;

      var resp = new Dictionary<string, object>();
      resp["jsonrpc"] = "2.0";
      resp["id"] = idObj;
      resp["result"] = result;
      return Json.Write(resp);
    }

    static string Error(object idObj, int code, string message)
    {
      var err = new Dictionary<string, object>();
      err["code"] = code;
      err["message"] = message;

      var resp = new Dictionary<string, object>();
      resp["jsonrpc"] = "2.0";
      resp["id"] = idObj;
      resp["error"] = err;
      return Json.Write(resp);
    }
  }
}