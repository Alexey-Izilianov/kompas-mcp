// Тест панели Давинчи (WPF) без показа окна: пустой ввод игнорируется; в эхо-режиме запрос и
// «[эхо]» попадают в ленту; SubmitHandler получает текст, а события из фонового потока
// (шаг инструмента, ответ, итог хода) доходят через Dispatcher; пока идёт запрос, новый не
// отправляется; вложения копируются и уходят блоком [Вложения пользователя]; Markdown ответа
// с заголовком, вложенным списком, таблицей, кодом, цитатой и путём к файлу строится без ошибок.
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KompasMcp.Davinci;
using KompasMcp.Ui;

class PanelTest
{
  static bool fail;

  static int Main()
  {
    Thread t = new Thread(RunChecks);
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
    if (fail) { Console.WriteLine("PANEL TEST FAIL"); return 1; }
    Console.WriteLine("PANEL TEST OK");
    return 0;
  }

  static void Check(string name, bool cond)
  {
    if (!cond) { fail = true; Console.WriteLine("FAIL: " + name); }
    else Console.WriteLine("[" + name + "] ok");
  }

  // Прокачка очереди Dispatcher (окно не показано, Dispatcher.Run не крутится).
  static void Pump(Func<bool> done, double seconds)
  {
    DateTime until = DateTime.UtcNow.AddSeconds(seconds);
    while (!done() && DateTime.UtcNow < until)
    {
      DispatcherFrame frame = new DispatcherFrame();
      Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { frame.Continue = false; });
      Dispatcher.PushFrame(frame);
      Thread.Sleep(20);
    }
  }

  static void RunChecks()
  {
    try { Run(); }
    catch (Exception e) { fail = true; Console.WriteLine("FAIL: исключение " + e); }
  }

  static void Run()
  {
    PanelWindow w = new PanelWindow();

    // 0. смена темы после применения шаблонов (раскладка пустого экрана с примерами):
    //    кисти, найденные шаблонами через ресурсы, WPF замораживает — раньше тут падало
    FrameworkElement root = (FrameworkElement)w.Content;
    root.Measure(new Size(460, 720));
    root.Arrange(new Rect(0, 0, 460, 720));
    root.UpdateLayout();
    bool dark = w.IsDark;
    w.SetTheme(!dark);
    root.UpdateLayout();
    w.SetTheme(dark);
    Check("theme toggle after templates", w.IsDark == dark);

    // 1. пустой ввод — ничего
    w.Submit();
    Check("empty submit ignored", w.ChatText.Length == 0);

    // 2. эхо-режим: запрос и уведомление в ленте, поле очищено
    DavinciPanel.SubmitHandler = null;
    w.InputBox.Text = "привет";
    w.Submit();
    Check("user message", w.ChatText.Contains("Вы: привет"));
    Check("echo notice", w.ChatText.Contains("[эхо]"));
    Check("input cleared", w.InputBox.Text.Length == 0);
    Check("echo not busy", !w.Busy);

    // 3. обработчик получает текст; ответ приходит из фонового потока
    string got = null;
    DavinciPanel.SubmitHandler = delegate(string s)
    {
      got = s;
      ThreadPool.QueueUserWorkItem(delegate
      {
        w.Dispatcher.BeginInvoke((Action)delegate
        {
          w.ToolCall("mcp__kompas-mcp__create_part");
          w.ToolResult(true);
          w.AddAssistant("**Готово**: " + s.ToUpper());
          w.TurnDone(null);
          w.Busy = false;
        });
      });
    };
    w.InputBox.Text = "нарисуй фланец";
    w.Submit();
    Check("handler received", got == "нарисуй фланец");
    Check("busy while running", w.Busy);
    Pump(() => w.ChatText.Contains("Давинчи:"), 3);
    Check("tool step, short name", w.ChatText.Contains("[инструмент] create_part"));
    Check("background answer", w.ChatText.Contains("НАРИСУЙ ФЛАНЕЦ"));
    Check("idle after turn", !w.Busy);

    // 4. пока идёт запрос, второй не отправляется и остаётся в поле
    int calls = 0;
    DavinciPanel.SubmitHandler = delegate { calls++; };
    w.InputBox.Text = "первый";
    w.Submit();
    w.InputBox.Text = "второй";
    w.Submit();
    Check("second submit blocked", calls == 1 && w.InputBox.Text == "второй" && w.ChatText.Contains("ещё работает"));
    w.Busy = false;
    w.InputBox.Text = "";

    // 5. вложения: копия в <workspace>\attach, путь — в сообщении модели
    string srcFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kompas-panel-test-attach.txt");
    System.IO.File.WriteAllText(srcFile, "ТЗ-тест");
    string dest = w.AttachFile(srcFile);
    Check("attach copied", System.IO.File.Exists(dest) && dest.Contains(System.IO.Path.Combine("attach", "")));
    Check("attach pending", w.PendingAttachments.Length == 1);
    string got2 = null;
    DavinciPanel.SubmitHandler = delegate(string s) { got2 = s; };
    w.InputBox.Text = "ознакомься";
    w.Submit();
    Check("message with attachment", got2 != null && got2.Contains("ознакомься")
      && got2.Contains("[Вложения пользователя]") && got2.Contains(dest));
    Check("attachments cleared", w.PendingAttachments.Length == 0);
    w.Busy = false;

    // 6. разметка ответа строится без исключений
    w.AddAssistant("# Заголовок\n- пункт\n  - вложенный\n1. первый\n2. второй\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n" +
      "```\nкод\n```\n> цитата\n\nфайл C:\\tmp\\деталь.m3d, `create_part` и snake_case_name без курсива");
    Check("markdown answer", w.ChatText.Contains("Заголовок"));

    // 7. настройки открываются вместо ленты и закрываются без сохранения
    w.OpenSettings();
    Check("settings open", w.SettingsOpen);
    w.CloseSettings(false);
    Check("settings closed", !w.SettingsOpen);

    DavinciPanel.SubmitHandler = null;
    System.IO.File.Delete(srcFile);
    try { System.IO.File.Delete(dest); } catch { }
    w.Close();

    ConfigChecks();
  }

  // Источник нейросети: старые конфиги по env, окружение Ollama, сохранение без потери ключей.
  static void ConfigChecks()
  {
    DavinciConfig legacy = new DavinciConfig();
    legacy.Env["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:11434";
    Check("legacy env -> ollama", legacy.ResolvedProvider == DavinciConfig.ProviderOllama);

    DavinciConfig ollama = new DavinciConfig { Provider = DavinciConfig.ProviderOllama, Model = "m1" };
    var env = ollama.EffectiveEnv();
    Check("ollama env", env["ANTHROPIC_BASE_URL"] == DavinciConfig.OllamaDefaultUrl
      && env["ANTHROPIC_AUTH_TOKEN"] == "ollama" && env["ANTHROPIC_DEFAULT_HAIKU_MODEL"] == "m1");
    Check("ollama model passed explicitly", ClaudeProbe.EffectiveModel(ollama) == "m1");

    DavinciConfig claude = new DavinciConfig { Provider = DavinciConfig.ProviderClaude };
    Check("claude env clean", !claude.EffectiveEnv().ContainsKey("ANTHROPIC_BASE_URL") && ClaudeProbe.EffectiveModel(claude) == null);

    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "davinci-config-test.json");
    System.IO.File.WriteAllText(tmp, "{\"envOllama\":{\"A\":\"1\"},\"env\":{\"ANTHROPIC_BASE_URL\":\"http://x\",\"HTTPS_PROXY\":\"p\"}}");
    DavinciConfig c = DavinciConfig.Load(tmp);
    Check("load legacy api", c.ResolvedProvider == DavinciConfig.ProviderApi);
    c.Provider = DavinciConfig.ProviderClaude;
    c.Model = "sonnet";
    c.Save();
    DavinciConfig back = DavinciConfig.Load(tmp);
    string text = System.IO.File.ReadAllText(tmp);
    Check("save roundtrip", back.Provider == DavinciConfig.ProviderClaude && back.Model == "sonnet"
      && !back.Env.ContainsKey("ANTHROPIC_BASE_URL") && back.Env["HTTPS_PROXY"] == "p" && text.Contains("envOllama"));
    System.IO.File.Delete(tmp);
  }
}
