// Панель чата «Давинчи» (Этап 4, эхо-режим): немодальная WinForms-форма,
// живёт в процессе KompasMcp.exe на отдельном STA-потоке. Single-instance
// (named mutex + вывод существующего окна на передний план). Реальный запуск
// claude подключается на Этапе 5 через DavinciPanel.SubmitHandler; пока он
// null — введённый текст эхом возвращается в лог.
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace KompasMcp.Ui
{
  public static class DavinciPanel
  {
    const string MutexName = "KompasMcp.DavinciPanel.Mutex";
    static readonly object Gate = new object();
    static Mutex single;
    static PanelForm form;
    static Thread uiThread;

    // Этап 5: обработчик отправки (вызывается в UI-потоке; долгую работу — в фон).
    // null => эхо-режим.
    public static Action<string> SubmitHandler;

    public static bool IsOpen { get { lock (Gate) { return form != null; } } }

    // Открыть панель (идемпотентно). Возвращает false, если панель уже открыта
    // в ДРУГОМ процессе (single-instance,mutex занят).
    public static bool Start()
    {
      lock (Gate)
      {
        if (form != null) { BringToFront(); return true; }
        if (single == null)
        {
          bool created;
          single = new Mutex(true, MutexName, out created);
          if (!created)
          {
            Log.Write("panel: already running in another process");
            return false;
          }
        }
        uiThread = new Thread(UiMain);
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();
        Log.Write("panel: ui thread started");
        return true;
      }
    }

    static void UiMain()
    {
      try
      {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        PanelForm f = new PanelForm();
        lock (Gate) { form = f; }
        Application.Run(f);
        Log.Write("panel: closed");
      }
      catch (Exception e) { Log.Error("panel", e); }
      lock (Gate) { form = null; }
    }

    static void BringToFront()
    {
      try
      {
        PanelForm f = form;
        if (f != null) f.BeginInvoke((MethodInvoker)delegate { f.BringToFront(); f.Activate(); });
      }
      catch (Exception e) { Log.Error("panel.BringToFront", e); }
    }

    // Потокобезопасное добавление строки в лог (вызывается из любого потока).
    public static void AppendLog(string text)
    {
      PanelForm f;
      lock (Gate) { f = form; }
      if (f == null) return;
      try { f.BeginInvoke((MethodInvoker)delegate { f.AppendLogLine(text); }); }
      catch (Exception e) { Log.Error("panel.AppendLog", e); }
    }

    // Закрыть панель (вызывается при завершении сервера — stdin закрыт).
    public static void Close()
    {
      PanelForm f;
      lock (Gate) { f = form; }
      if (f == null) return;
      try { f.BeginInvoke((MethodInvoker)delegate { f.Close(); }); }
      catch (Exception e) { Log.Error("panel.Close", e); }
      if (single != null)
      {
        try { single.ReleaseMutex(); } catch { }
        single = null;
      }
    }
  }

  public class PanelForm : Form
  {
    readonly TextBox logBox;
    readonly TextBox inputBox;
    readonly Button sendButton;
    readonly Label statusLabel;
    readonly System.Windows.Forms.Timer statusTimer;

    public PanelForm()
    {
      Text = "Давинчи — копилот КОМПАС-3D";
      FormBorderStyle = FormBorderStyle.SizableToolWindow;
      StartPosition = FormStartPosition.Manual;
      Screen sc = Screen.FromPoint(Cursor.Position);
      if (sc == null) sc = Screen.PrimaryScreen;
      System.Drawing.Rectangle wa = sc.WorkingArea;
      Width = Math.Max(420, wa.Width / 4);
      Height = Math.Max(360, wa.Height / 2);
      Location = new Point(wa.Right - Width - 20, wa.Bottom - Height - 20);
      TopMost = true;

      statusLabel = new Label();
      statusLabel.Dock = DockStyle.Top;
      statusLabel.Height = 20;
      statusLabel.TextAlign = ContentAlignment.MiddleLeft;
      statusLabel.Text = "КОМПАС: …";

      logBox = new TextBox();
      logBox.Dock = DockStyle.Fill;
      logBox.Multiline = true;
      logBox.ReadOnly = true;
      logBox.ScrollBars = ScrollBars.Vertical;
      logBox.Font = new Font("Segoe UI", 9f);
      logBox.TabStop = false;

      inputBox = new TextBox();
      inputBox.Dock = DockStyle.Bottom;
      inputBox.Multiline = true;
      inputBox.Height = 60;
      inputBox.Font = new Font("Segoe UI", 9f);

      sendButton = new Button();
      sendButton.Dock = DockStyle.Bottom;
      sendButton.Text = "Отправить (Ctrl+Enter)";
      sendButton.Height = 30;

      // порядок Dock: сначала нижние, потом Fill
      Controls.Add(logBox);
      Controls.Add(inputBox);
      Controls.Add(sendButton);
      Controls.Add(statusLabel);

      sendButton.Click += delegate { Submit(); };
      inputBox.KeyDown += delegate(object s, KeyEventArgs e)
      {
        if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); }
      };

      statusTimer = new System.Windows.Forms.Timer();
      statusTimer.Interval = 2000;
      statusTimer.Tick += delegate { RefreshStatus(); };
      statusTimer.Start();
      RefreshStatus();
    }

    // Отправка сообщения: handler Этапа 5 или эхо.
    public void Submit()
    {
      string text = inputBox.Text.Trim();
      if (text.Length == 0) return;
      inputBox.Clear();
      AppendLogLine("> " + text);
      Action<string> handler = DavinciPanel.SubmitHandler;
      if (handler != null)
      {
        try { handler(text); }
        catch (Exception e)
        {
          Log.Error("panel.Submit", e);
          AppendLogLine("[ошибка обработчика: " + e.Message + "]");
        }
      }
      else
      {
        AppendLogLine("[эхо] (Этап 4: реальный запуск модели будет подключён на Этапе 5)");
      }
    }

    public void AppendLogLine(string line)
    {
      if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine);
      logBox.AppendText(line);
      logBox.SelectionStart = logBox.TextLength;
      logBox.ScrollToCaret();
    }

    // Тестовые аксессоры (логика эхо проверяется без показа формы)
    public TextBox InputBox { get { return inputBox; } }
    public string LogText { get { return logBox.Text; } }

    void RefreshStatus()
    {
      try
      {
        var st = KompasHost.Status();
        bool running = st.ContainsKey("running") && (bool)st["running"];
        string mode = st.ContainsKey("mode") ? Convert.ToString(st["mode"]) : "?";
        string docs = "?";
        if (running)
        {
          object d = st.ContainsKey("documents") ? st["documents"] : null;
          System.Collections.ICollection col = d as System.Collections.ICollection;
          docs = col != null ? Convert.ToString(col.Count) : "?";
        }
        statusLabel.Text = "КОМПАС: " + (running ? "подключён" : "не запущен")
          + " (режим " + mode + ", документов " + docs + ")";
      }
      catch (Exception e)
      {
        statusLabel.Text = "КОМПАС: статус недоступен";
        Log.Error("panel.RefreshStatus", e);
      }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      // закрытие панели не убивает сервер: панель можно снова открыть
      base.OnFormClosing(e);
    }
  }
}