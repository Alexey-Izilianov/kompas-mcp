// Тест панели Давинчи (Этап 4, эхо-режим) без показа окна:
// пустой ввод игнорируется, Submit кладёт "> текст" + "[эхо]" в лог,
// SubmitHandler получает текст и работает из другого потока (AppendLog).
using System;
using System.Threading;
using System.Windows.Forms;
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

  static void RunChecks()
  {
    Application.EnableVisualStyles();
    PanelForm f = new PanelForm();
    IntPtr forceHandle = f.Handle; // без Show дескриптор не создаётся, BeginInvoke падает

    // 1. пустой ввод — ничего
    f.Submit();
    Check("empty submit no echo", f.LogText.Length == 0);

    // 2. чат-формат: сообщение юзера + эхо-строка
    f.InputBox.Text = "привет";
    f.Submit();
    Check("user block", f.LogText.Contains("Ты") && f.LogText.Contains("привет"));
    Check("echo notice", f.LogText.Contains("[эхо]"));
    Check("input cleared", f.InputBox.Text.Length == 0);

    // 3. SubmitHandler: получает текст, отвечает из чужого потока
    string got = null;
    DavinciPanel.SubmitHandler = delegate(string s)
    {
      got = s;
      // имитация ответа из фонового потока (BeginInvoke формы — тот же
      // механизм, что у DavinciPanel.AppendLog; сам DavinciPanel.form тут
      // null, т.к. панель создана не через DavinciPanel.Start)
      ThreadPool.QueueUserWorkItem(delegate
      {
        f.BeginInvoke((MethodInvoker)delegate { f.AppendLogLine("[ответ] " + s.ToUpper()); });
      });
    };
    f.InputBox.Text = "нарисуй фланец";
    f.Submit();
    Check("handler received", got == "нарисуй фланец");

    // дождаться фонового AppendLog (BeginInvoke в UI-поток, а он крутится только
    // в message loop — вместо Run прокачаем очередь вручную)
    DateTime until = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < until && !f.LogText.Contains("[ответ]"))
    {
      Application.DoEvents();
      Thread.Sleep(50);
    }
    Check("background AppendLog", f.LogText.Contains("[ответ] НАРИСУЙ ФЛАНЕЦ"));
    DavinciPanel.SubmitHandler = null;
    f.Dispose();
  }
}