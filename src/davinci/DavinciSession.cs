// Сессия копилота: связывает панель Давинчи и ClaudeRunner.
// SubmitHandler (UI-поток) ставит сообщение в очередь; runner работает в фоне,
// события stream-json уходят в панель: текст ответа, шаги инструментов, итог хода.
// Диалог непрерывный: следующее сообщение резюмится по session_id; Reset — новый диалог.
using System;
using System.IO;
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
    static bool answered;     // в текущем ходе уже был текст ответа (итог result его повторяет)
    static bool failed;       // в текущем ходе уже показана ошибка API
    static bool aborted;
    static string lastApiError;  // ошибка API последнего запроса — для индикатора, до успешного ответа

    // Индикатор Claude в панели: проверка окружения и API плюс ошибка последнего запроса.
    // Полная проверка (deep) с успешным ответом модели снимает запомненную ошибку.
    public static ServiceState CheckClaude(bool deep)
    {
      ServiceState s = ClaudeProbe.Check(Cfg(), deep);
      string err;
      lock (Gate)
      {
        if (deep && s.Ok) lastApiError = null;
        err = lastApiError;
      }
      if (s.Ok && err != null)
        return new ServiceState(false, "Claude: " + ClaudeProbe.Friendly(err), s.Detail + "\nпоследний запрос: " + err);
      return s;
    }

    static string SystemPrompt()
    {
      return
        "Ты — копилот «Давинчи» внутри КОМПАС-3D. Работай инструментами MCP-сервера kompas-mcp.\n" +
        "Правила:\n" +
        "1. Перед любыми операциями документ должен существовать: создай его (create_drawing/create_part) " +
        "или открой существующий (open_part/open_assembly) — не работай с несуществующим документом.\n" +
        "2. НЕ вызывай kompas_stop — это отключает тебя от КОМПАСа пользователя.\n" +
        "3. НЕ сохраняй документы без явной просьбы пользователя.\n" +
        "4. Готовые артефакты (скрипты, чертежи, отчёты) пиши файлами в рабочую папку mcp-out.\n" +
        "5. Отвечай по-русски, кратко и по делу; для длинных шагов используй инструменты, а не рассказы.\n" +
        "6. Если в сообщении есть блок [Вложения пользователя] — сначала прочитай перечисленные файлы " +
        "(это ТЗ, референсы и справочные материалы юзера) и строй работу с их учётом.\n" +
        "7. 3D-операции возвращают mass_kg — масса ОБЯЗАНА измениться; если тул вернул ошибку «масса не изменилась», " +
        "не повторяй вызов с теми же аргументами — измени эскиз/плоскость/смещение.\n" +
        "8. После каждой неудачи с плоскостями XOZ/YOZ переключайся на XOY со смещением (offset).\n" +
        "9. Чертежи колёс делай готовыми инструментами (gear_drawing, bevel_gear_drawing, worm_wheel_drawing) — " +
        "они строят разрез по ГОСТ. Если разрез тела вращения с отверстием чертишь вручную: сквозь отверстие " +
        "видны рёбра торцов и фасок — проводи их сплошной основной линией от верхней половины разреза до нижней; " +
        "штрихуй только сечение материала, отверстие не штрихуется.\n" +
        "10. Модель по чертежу с картинки. (а) Читай изображение инструментом image_view: сначала целиком с сеткой, " +
        "затем увеличенно каждую область с размерами и выносками, пока все числа не прочитаны уверенно. " +
        "(б) До построения выпиши пользователю ведомость: виды, каждый размер с элементом, к которому он относится, " +
        "количества одинаковых элементов (пересчитай отверстия, пазы, приливы по изображению), их угловое положение. " +
        "(в) Если данных не хватает (один вид без толщин и глубин, нечитаемое число) — спроси пользователя и не строй " +
        "наугад; если пользователь разрешил допущения, перечисли их явно. (г) После построения сверь: render_png_3d " +
        "в том же виде, что на чертеже, и image_view обоих изображений — количество, положение и пропорции элементов; " +
        "расхождения исправь.\n" +
        "11. Чертёж по модели: сохрани деталь и вызови part_drawing (section — для деталей с отверстиями и полостями); " +
        "размеры элементов наноси через view_project (точки модели → координаты вида) и lin_dim/diam_dim/rad_dim; " +
        "сборочный чертёж — assembly_drawing.";
    }

    static DavinciConfig Cfg() { return cfg ?? (cfg = DavinciConfig.Load()); }

    // Рабочий каталог claude: workspace из конфига (по умолчанию mcp-out), относительный —
    // от папки exe. Вложения пользователя — в <workspace>\attach.
    static string WorkspaceDir()
    {
      string ws = Paths.Abs(string.IsNullOrEmpty(Cfg().Workspace) ? "mcp-out" : Cfg().Workspace);
      Directory.CreateDirectory(ws);
      return ws;
    }

    public static string AttachmentDir()
    {
      string dir = Path.Combine(WorkspaceDir(), "attach");
      Directory.CreateDirectory(dir);
      return dir;
    }

    // Инициализация при старте сервера с --panel: конфиг + mcp-config.
    public static void Init()
    {
      cfg = DavinciConfig.Load();
      mcpConfigPath = McpConfig.Write(cfg);
      Log.Write("session: init ok");
    }

    // Копия текущих настроек — для страницы настроек панели.
    public static DavinciConfig Config()
    {
      return Cfg().Clone();
    }

    // Сохранить настройки и применить со следующего запроса (идущий запрос не трогаем).
    public static void Apply(DavinciConfig c)
    {
      c.Save();
      lock (Gate)
      {
        cfg = c;
        mcpConfigPath = McpConfig.Write(c);
        lastApiError = null;
      }
      Log.Write("session: настройки применены, источник=" + c.ResolvedProvider + ", модель=" + (ClaudeProbe.EffectiveModel(c) ?? "по умолчанию"));
    }

    public static void Submit(string text)
    {
      if (mcpConfigPath == null) Init();
      lock (Gate)
      {
        if (runner != null && runner.Running)
        {
          DavinciPanel.Notice("Предыдущий запрос ещё выполняется — дождитесь ответа или остановите его.");
          return;
        }
        answered = false;
        failed = false;
        aborted = false;
        runner = new ClaudeRunner();
        runner.Options = new ClaudeRunnerOptions
        {
          McpConfigPath = mcpConfigPath,
          Workspace = WorkspaceDir(),
          SystemPrompt = SystemPrompt(),
          ClaudeCmd = cfg.ClaudeCmd,
          Model = ClaudeProbe.EffectiveModel(cfg),
          TimeoutSec = cfg.TimeoutSec,
          Env = cfg.EffectiveEnv(),
        };
        runner.OnEvent += OnEvent;
        runner.OnError += OnError;
        runner.OnExit += OnExit;
        ClaudeRunner r = runner;
        string resume = lastSessionId;
        // запуск в фоне: UI-поток не ждёт
        ThreadPool.QueueUserWorkItem(delegate
        {
          try { r.Run(text, resume); }
          catch (Exception e)
          {
            Log.Error("session.Run", e);
            DavinciPanel.Error("Не удалось запустить Claude Code: " + e.Message);
            DavinciPanel.Busy = false;
          }
        });
      }
    }

    public static void Abort()
    {
      ClaudeRunner r;
      lock (Gate) { r = runner; }
      if (r == null || !r.Running) return;
      aborted = true;
      DavinciPanel.Notice("Останавливаю запрос…");
      ThreadPool.QueueUserWorkItem(delegate { try { r.Stop(); } catch (Exception e) { Log.Error("abort", e); } });
    }

    // Новый диалог: следующий запрос начнёт разговор заново. false — идёт запрос.
    public static bool Reset()
    {
      lock (Gate)
      {
        if (runner != null && runner.Running) return false;
        lastSessionId = null;
      }
      Log.Write("session: новый диалог");
      return true;
    }

    // Останов копилота при выходе панельного процесса: гасим claude, если он жив.
    public static void Shutdown()
    {
      ClaudeRunner r;
      lock (Gate) { r = runner; }
      if (r == null || !r.Running) return;
      try { r.Stop(); }
      catch (Exception e) { Log.Error("session.Shutdown", e); }
    }

    static void OnEvent(StreamEvent ev)
    {
      if (ev == null) return;
      if (ev.SessionId != null) lock (Gate) { lastSessionId = ev.SessionId; }
      if (ev.Type == "assistant")
      {
        // "<synthetic>" — claude сам сообщает, что API не ответил (лимит, модель, связь)
        if (ev.Model == "<synthetic>")
          ApiFailed(ev.AssistantText);
        else if (!string.IsNullOrEmpty(ev.AssistantText))
        {
          answered = true;
          DavinciPanel.AppendAssistant(ev.AssistantText);
        }
        foreach (string tool in ev.ToolNames) DavinciPanel.ToolCall(tool);
      }
      else if (ev.Type == "user" && ev.ToolResultIsError)
        DavinciPanel.ToolResult(true);
      else if (ev.Type == "result")
      {
        // итог повторяет последний текст ответа — показываем его, только если текста не было
        if (ev.IsError) ApiFailed(string.IsNullOrEmpty(ev.ResultText) ? ev.Subtype : ev.ResultText);
        else if (!failed && !answered && !string.IsNullOrEmpty(ev.ResultText))
          DavinciPanel.AppendAssistant(ev.ResultText);
        if (!failed && !ev.IsError) lock (Gate) { lastApiError = null; }
        // стоимость не показываем: при стороннем ANTHROPIC_BASE_URL claude считает её по тарифам Anthropic
        DavinciPanel.TurnDone(failed ? "ошибка" : null);
        DavinciPanel.RefreshClaudeStatus();
      }
    }

    static void ApiFailed(string text)
    {
      if (failed) return;
      failed = true;
      string raw = string.IsNullOrEmpty(text) ? "нет ответа" : text.Trim();
      lock (Gate) { lastApiError = raw; }
      DavinciPanel.Error("Claude не ответил: " + ClaudeProbe.Friendly(raw) + (raw.Length <= 300 ? "\n" + raw : ""));
      DavinciPanel.RefreshClaudeStatus();
    }

    static void OnError(Exception e)
    {
      DavinciPanel.Error("Сбой запуска Claude Code: " + e.Message);
    }

    static void OnExit()
    {
      if (aborted) DavinciPanel.TurnDone("остановлено");
      DavinciPanel.Busy = false;
    }
  }
}