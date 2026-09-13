// Сессия копилота (Этап 5): связывает панель Давинчи и ClaudeRunner.
// SubmitHandler (UI-поток) ставит сообщение в очередь; runner работает в фоне,
// события stream-json сокращённо попадают в лог панели через DavinciPanel.AppendLog.
// Диалог непрерывный: следующее сообщение резюмится по session_id.
using System;
using System.Threading;
using KompasMcp.Ui;

namespace KompasMcp.Davinci
{
  public static class DavinciSession
  {
    static readonly object Gate = new object();
    static DavinciConfig cfg;
    static string mcpConfigPath;
    static ClaudeRunner runner;
    static string lastSessionId;

    public static bool Busy { get { lock (Gate) { return runner != null && runner.Running; } } }
    public static string LastSessionId { get { lock (Gate) { return lastSessionId; } } }

    static string SystemPrompt()
    {
      return
        "Ты — копилот «Давинчи» внутри КОМПАС-3D. Работай инструментами MCP-сервера kompas-mcp.\n" +
        "Правила:\n" +
        "1. Перед любыми операциями документ должен существовать: создай его (create_drawing/create_part) " +
        "или открой существующий (kompas_open) — не работай с несуществующим документом.\n" +
        "2. НЕ вызывай kompas_stop — это отключает тебя от КОМПАСа пользователя.\n" +
        "3. НЕ сохраняй документы без явной просьбы пользователя.\n" +
        "4. Готовые артефакты (скрипты, чертежи, отчёты) пиши файлами в рабочую папку mcp-out.\n" +
        "5. Отвечай по-русски, кратко и по делу; для длинных шагов используй инструменты, а не рассказы.";
    }

    // Инициализация при старте сервера с --panel: конфиг + mcp-config.
    public static void Init()
    {
      cfg = DavinciConfig.Load();
      mcpConfigPath = McpConfig.Write(cfg);
      Log.Write("session: init ok");
    }

    public static void Submit(string text)
    {
      if (mcpConfigPath == null) Init();
      lock (Gate)
      {
        if (runner != null && runner.Running)
        {
          DavinciPanel.AppendLog("[давинчи] предыдущий запрос ещё выполняется — подожди или нажми «Прервать»");
          return;
        }
        runner = new ClaudeRunner();
        runner.Options = new ClaudeRunnerOptions();
        runner.Options.McpConfigPath = mcpConfigPath;
        runner.Options.Workspace = cfg.Workspace;
        runner.Options.SystemPrompt = SystemPrompt();
        runner.Options.ClaudeCmd = cfg.ClaudeCmd;
        runner.Options.Model = cfg.Model;
        runner.Options.TimeoutSec = cfg.TimeoutSec;
        runner.OnEvent += OnEvent;
        runner.OnStderr += OnStderr;
        runner.OnError += OnError;
        runner.OnExit += OnExit;
        string resume = lastSessionId;
        string message = text;
        // Запуск в фоне: UI-поток не ждёт
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
          try { runner.Run(message, resume); }
          catch (Exception e) { Log.Error("session.Run", e); DavinciPanel.AppendLog("[ошибка запуска] " + e.Message); }
        });
      }
    }

    public static void Abort()
    {
      ClaudeRunner r;
      lock (Gate) { r = runner; }
      if (r == null || !r.Running) { DavinciPanel.AppendLog("[давинчи] нечего прерывать"); return; }
      DavinciPanel.AppendLog("[давинчи] прерываю…");
      System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { r.Stop(); } catch (Exception e) { Log.Error("abort", e); } });
    }

    static void OnEvent(StreamEvent ev)
    {
      if (ev == null) return;
      if (ev.SessionId != null) lock (Gate) { lastSessionId = ev.SessionId; }
      // ассистентский текст — главное содержимое ответа
      if (!string.IsNullOrEmpty(ev.AssistantText))
        DavinciPanel.AppendAssistant(ev.AssistantText);
      else if (ev.Type == "assistant" && !string.IsNullOrEmpty(ev.ToolName))
        DavinciPanel.AppendLog("[тул] " + ev.ToolName);
      else if (ev.Type == "result")
      {
        string tail = "";
        if (ev.TotalCostUsd > 0) tail += "  ($" + ev.TotalCostUsd.ToString("0.0000") + ")";
        if (ev.NumTurns > 0) tail += ", оборотов: " + ev.NumTurns;
        if (!string.IsNullOrEmpty(ev.ResultText))
          DavinciPanel.AppendLog("[итог] " + ev.ResultText + tail);
        else
          DavinciPanel.AppendLog("[завершено]" + tail);
      }
      else if (ev.IsError)
        DavinciPanel.AppendLog("[ошибка модели] " + (ev.ResultText ?? ""));
    }

    static void OnStderr(string line)
    {
      // stderr claude пишется только в лог-файл — шум в панель не тащим
    }

    static void OnError(Exception e)
    {
      DavinciPanel.AppendLog("[ошибка runner] " + e.Message);
    }

    static void OnExit()
    {
      DavinciPanel.Busy = false;
      DavinciPanel.AppendLog("[готово]");
    }
  }
}