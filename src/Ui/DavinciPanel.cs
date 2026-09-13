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

    // Индикатор занятости из любого потока (runner/сессия).
    public static bool Busy
    {
      set
      {
        PanelForm f;
        lock (Gate) { f = form; }
        if (f == null) return;
        try { f.BeginInvoke((MethodInvoker)delegate { f.Busy = value; }); }
        catch (Exception e) { Log.Error("panel.Busy", e); }
      }
    }

    // Чат-сообщения из любого потока (runner/сессия).
    public static void AppendUser(string text) { ChatAppend(f => f.AppendUserMessage(text), text); }
    public static void AppendAssistant(string text) { ChatAppend(f => f.AppendAssistantMessage(text), text); }

    static void ChatAppend(Action<PanelForm> apply, string text)
    {
      PanelForm f;
      lock (Gate) { f = form; }
      if (f == null) return;
      try { f.BeginInvoke((MethodInvoker)delegate { apply(f); }); }
      catch (Exception e) { Log.Error("panel.ChatAppend", e); }
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
    readonly RichTextBox logBox;
    readonly TextBox inputBox;
    readonly Button sendButton;
    readonly Button abortButton;
    readonly Label statusLabel;
    readonly System.Windows.Forms.Timer statusTimer;
    readonly Font plainFont;
    readonly Font whoFont;
    readonly Font statusFont;
    bool busy;

    public bool Busy
    {
      get { return busy; }
      set
      {
        busy = value;
        try
        {
          sendButton.Enabled = !busy;
          sendButton.Text = busy ? "Работаю…" : "Отправить (Ctrl+Enter)";
        }
        catch { }
      }
    }

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

      logBox = new RichTextBox();
      logBox.Dock = DockStyle.Fill;
      logBox.ReadOnly = true;
      logBox.BorderStyle = BorderStyle.None;
      logBox.BackColor = Color.White;
      logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
      logBox.Font = new Font("Segoe UI", 9.5f);
      logBox.TabStop = false;
      logBox.WordWrap = true;
      logBox.HideSelection = false;
      plainFont = logBox.Font;
      whoFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
      statusFont = new Font("Segoe UI", 8.5f);

      inputBox = new TextBox();
      inputBox.Dock = DockStyle.Bottom;
      inputBox.Multiline = true;
      inputBox.Height = 60;
      inputBox.Font = new Font("Segoe UI", 9f);

      sendButton = new Button();
      sendButton.Dock = DockStyle.Bottom;
      sendButton.Text = "Отправить (Ctrl+Enter)";
      sendButton.Height = 30;

      abortButton = new Button();
      abortButton.Dock = DockStyle.Bottom;
      abortButton.Text = "Прервать";
      abortButton.Height = 28;
      abortButton.Enabled = false;

      // порядок Dock: сначала нижние, потом Fill
      Controls.Add(logBox);
      Controls.Add(inputBox);
      Controls.Add(sendButton);
      Controls.Add(abortButton);
      Controls.Add(statusLabel);

      sendButton.Click += delegate { Submit(); };
      abortButton.Click += delegate
      {
        try { KompasMcp.Davinci.DavinciSession.Abort(); } catch (Exception e) { Log.Error("panel.Abort", e); }
      };
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

    // Отправка сообщения: DavinciSession (Этап 5) или эхо.
    public void Submit()
    {
      string text = inputBox.Text.Trim();
      if (text.Length == 0) return;
      inputBox.Clear();
      AppendUserMessage(text);
      Action<string> handler = DavinciPanel.SubmitHandler;
      if (handler != null)
      {
        Busy = true;
        try { handler(text); }
        catch (Exception e)
        {
          Log.Error("panel.Submit", e);
          AppendLogLine("[ошибка обработчика: " + e.Message + "]");
          Busy = false;
        }
      }
      else
      {
        AppendLogLine("[эхо] (копилот не подключён: нет davinci.json / Init)");
      }
    }

    // ---- чат-формат ----

    void BeginParagraph()
    {
      if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine + Environment.NewLine);
      logBox.SelectionStart = logBox.TextLength;
    }

    void AppendStyled(string who, Color whoColor, string text)
    {
      BeginParagraph();
      logBox.SelectionFont = whoFont;
      logBox.SelectionColor = whoColor;
      logBox.AppendText(who);
      logBox.SelectionFont = plainFont;
      logBox.SelectionColor = Color.FromArgb(25, 25, 25);
      logBox.AppendText(Environment.NewLine + text);
      ScrollEnd();
    }

    public void AppendUserMessage(string text)
    {
      AppendStyled("Ты", Color.FromArgb(47, 84, 150), text);
    }

    public void AppendAssistantMessage(string text)
    {
      AppendStyled("Давинчи", Color.FromArgb(0, 128, 96), text);
    }

    // Служебная строка (тулы, итоги) — серым мелким.
    public void AppendLogLine(string line)
    {
      if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine);
      logBox.SelectionStart = logBox.TextLength;
      logBox.SelectionFont = statusFont;
      logBox.SelectionColor = Color.FromArgb(130, 130, 130);
      logBox.AppendText(line);
      ScrollEnd();
    }

    void ScrollEnd()
    {
      logBox.SelectionStart = logBox.TextLength;
      logBox.SelectionLength = 0;
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