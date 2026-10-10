// Разбор событий stream-json (claude -p --output-format stream-json --verbose) парсером Json.cs:
// Dictionary<string,object> / List<object> / string / double / bool / null.
using System;
using System.Collections.Generic;
using KompasMcp.JsonLib;

namespace KompasMcp.Davinci
{
  // Одно событие потока: assistant (текст, tool_use), user (tool_result), result (итог),
  // system (init с session_id).
  public class StreamEvent
  {
    public string Type;            // system | assistant | user | result
    public string Subtype;         // init | success | error_* (result)
    public string SessionId;       // есть в каждом событии
    public bool IsError;
    public string AssistantText;   // склейка text-блоков assistant-сообщения
    public string Model;           // модель сообщения; "<synthetic>" — сообщение claude об ошибке API
    public readonly List<string> ToolNames = new List<string>(); // tool_use сообщения по порядку (mcp__kompas-mcp__ping)
    public string ToolResultText;  // текст tool_result (событие user)
    public bool ToolResultIsError; // tool_result с is_error — инструмент вернул ошибку
    public string ResultText;      // финальный ответ (result)

    static string S(Dictionary<string, object> d, string key)
    {
      object v;
      return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
    }

    static Dictionary<string, object> D(Dictionary<string, object> d, string key)
    {
      object v;
      return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
    }

    static List<object> L(Dictionary<string, object> d, string key)
    {
      object v;
      return d.TryGetValue(key, out v) ? v as List<object> : null;
    }

    static bool IsTrue(Dictionary<string, object> d, string key)
    {
      object v;
      return d.TryGetValue(key, out v) && v is bool && (bool)v;
    }

    public static StreamEvent Parse(string line)
    {
      if (line == null || line.Length == 0) return null;
      object root;
      try { root = Json.Parse(line); }
      catch { return null; }
      Dictionary<string, object> d = root as Dictionary<string, object>;
      if (d == null) return null;

      StreamEvent ev = new StreamEvent();
      ev.Type = S(d, "type") ?? "";
      ev.SessionId = S(d, "session_id");
      ev.Subtype = S(d, "subtype");

      ev.IsError = IsTrue(d, "is_error");
      object o;
      if (d.TryGetValue("result", out o)) ev.ResultText = o as string;

      var msg = D(d, "message");
      if (msg != null)
      {
        ev.Model = S(msg, "model");
        var blocks = L(msg, "content");
        if (blocks != null)
        {
          var texts = new List<string>();
          foreach (object item in blocks)
          {
            var block = item as Dictionary<string, object>;
            if (block == null) continue;
            string bt = S(block, "type");
            if (bt == "text")
            {
              if (ev.Type == "assistant") texts.Add(S(block, "text") ?? "");
            }
            else if (bt == "tool_use")
              ev.ToolNames.Add(S(block, "name") ?? "");
            else if (bt == "tool_result")
            {
              if (IsTrue(block, "is_error")) ev.ToolResultIsError = true;
              var rc = L(block, "content");
              if (rc != null)
              {
                var parts = new List<string>();
                foreach (object ritem in rc)
                {
                  var rblock = ritem as Dictionary<string, object>;
                  if (ritem is string) parts.Add((string)ritem);
                  else if (rblock != null && S(rblock, "type") == "text") parts.Add(S(rblock, "text") ?? "");
                }
                ev.ToolResultText = string.Join("\n", parts.ToArray());
              }
            }
          }
          if (texts.Count > 0) ev.AssistantText = string.Join("\n", texts.ToArray());
        }
      }
      return ev;
    }
  }
}