// Панель чата «Давинчи»: немодальная WinForms-форма, живёт в процессе
// KompasMcp.exe на отдельном STA-потоке. Single-instance (named mutex +
// вывод существующего окна на передний план). Отправка — Enter в поле ввода
// (Shift+Enter — перенос строки). Тёмная/светлая тема переключается кнопкой
// в шапке. Сообщения гоняются headless-клиенту через DavinciPanel.SubmitHandler.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KompasMcp.Ui
{
  public static class DavinciPanel
  {
    const string MutexName = "KompasMcp.DavinciPanel.Mutex";
    const string ShowEventName = "KompasMcp.DavinciPanel.ShowEvent";
    static readonly object Gate = new object();
    static Mutex single;
    static PanelForm form;
    static Thread uiThread;
    static EventWaitHandle showEvent;

    // Обработчик отправки (вызывается в UI-потоке; долгую работу — в фон).
    // null => эхо-режим.
    public static Action<string> SubmitHandler;

    public static bool IsOpen { get { lock (Gate) { return form != null; } } }

    // Открыть панель (идемпотентно). Возвращает false, если панель уже открыта
    // в ДРУГОМ процессе (single-instance, mutex занят).
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
            Log.Write("panel: already running in another process — сигнал показать");
            SignalShow();
            return false;
          }
          bool evCreated;
          showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out evCreated);
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
        Thread watcher = new Thread(WatchShow);
        watcher.IsBackground = true;
        watcher.Start();
        Application.Run(f);
        Log.Write("panel: closed");
      }
      catch (Exception e) { Log.Error("panel", e); }
      lock (Gate) { form = null; }
    }

    // Второй процесс просит первый показать скрытую панель.
    static void SignalShow()
    {
      try
      {
        bool created;
        using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out created))
          ev.Set();
      }
      catch (Exception e) { Log.Error("panel.SignalShow", e); }
    }

    // Ждёт сигнал «показать» от второго процесса (периодический WaitOne,
    // чтобы поток мог завершиться после Close()).
    static void WatchShow()
    {
      while (showEvent != null)
      {
        bool signaled = false;
        try { signaled = showEvent.WaitOne(5000); } catch { return; }
        if (!signaled) continue;
        PanelForm f;
        lock (Gate) { f = form; }
        if (f == null) continue;
        try
        {
          f.BeginInvoke((MethodInvoker)delegate
          {
            try { f.Show(); f.WindowState = FormWindowState.Normal; f.TopMost = true; f.Activate(); }
            catch { }
          });
        }
        catch { }
      }
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

    // Блокирует до фактического закрытия формы (главный поток панельного
    // процесса живёт ровно столько, сколько открыта панель).
    public static void WaitClosed()
    {
      Thread t;
      lock (Gate) { t = uiThread; }
      if (t != null && t.IsAlive) t.Join();
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
      showEvent = null; // поток WatchShow завершится на ближайшем таймауте
    }
  }

  public class PanelForm : Form
  {
    readonly RichTextBox logBox;
    readonly TextBox inputBox;
    readonly Button sendButton;
    readonly Button abortButton;
    readonly Button attachButton;
    readonly Button themeButton;
    readonly Label statusLabel;
    readonly Label titleLabel;
    readonly Label attachmentsLabel;
    readonly Panel header;
    readonly List<string> pendingAttachments = new List<string>();
    readonly System.Windows.Forms.Timer statusTimer;
    bool busy;
    bool dark;

    // цвета темы (переключаются ApplyTheme)
    Color chatText, whoUser, whoAssistant, serviceColor, logBack, headerBack, inputBack, inputFore;

    public bool Busy
    {
      get { return busy; }
      set
      {
        busy = value;
        try
        {
          sendButton.Enabled = !busy;
          sendButton.Text = busy ? "Работаю…" : "Отправить";
          abortButton.Enabled = busy;
        }
        catch { }
      }
    }

    public PanelForm()
    {
      Text = "Давинчи — копилот КОМПАС-3D";
      FormBorderStyle = FormBorderStyle.Sizable;
      MaximizeBox = true;
      MinimizeBox = true;
      ShowInTaskbar = true;
      StartPosition = FormStartPosition.Manual;
      Screen sc = Screen.FromPoint(Cursor.Position);
      if (sc == null) sc = Screen.PrimaryScreen;
      System.Drawing.Rectangle wa = sc.WorkingArea;
      Width = Math.Max(560, wa.Width / 3);
      Height = Math.Max(480, wa.Height * 2 / 3);
      MinimumSize = new Size(520, 420);
      Location = new Point(wa.Right - Width - 20, wa.Bottom - Height - 20);
      TopMost = true;
      Icon icon = LoadDavinciIcon();
      if (icon != null) Icon = icon;
      Font = new Font("Segoe UI", 9.5f);

      // Шапка: аватар | название + статус | кнопка темы
      header = new Panel();
      header.Dock = DockStyle.Top;
      header.Height = 56;
      header.Padding = new Padding(8, 6, 6, 6);

      Button themeBtn = new Button();
      themeBtn.Dock = DockStyle.Right;
      themeBtn.Width = 44;
      themeBtn.FlatStyle = FlatStyle.Flat;
      themeBtn.FlatAppearance.BorderSize = 0;
      themeBtn.TextAlign = ContentAlignment.MiddleCenter;
      themeBtn.Click += delegate { ToggleTheme(); };

      PictureBox avatar = new PictureBox();
      avatar.Dock = DockStyle.Left;
      avatar.Width = 44;
      avatar.SizeMode = PictureBoxSizeMode.Zoom;
      Image av = LoadAvatar();
      if (av != null) avatar.Image = av;

      Panel headText = new Panel();
      headText.Dock = DockStyle.Fill;
      statusLabel = new Label();
      statusLabel.Dock = DockStyle.Fill;
      statusLabel.TextAlign = ContentAlignment.MiddleLeft;
      statusLabel.Text = "КОМПАС: …";
      statusLabel.Font = new Font("Segoe UI", 8.5f);
      titleLabel = new Label();
      titleLabel.Dock = DockStyle.Top;
      titleLabel.Height = 24;
      titleLabel.TextAlign = ContentAlignment.MiddleLeft;
      titleLabel.Text = "Давинчи — копилот КОМПАС-3D";
      titleLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
      // порядок Dock: Fill добавляется первым (иначе налезает на аватар)
      headText.Controls.Add(statusLabel);
      headText.Controls.Add(titleLabel);

      // порядок добавления в шапку: Fill первым, потом края
      header.Controls.Add(headText);
      header.Controls.Add(avatar);
      header.Controls.Add(themeBtn);
      themeButton = themeBtn;

      logBox = new RichTextBox();
      logBox.Dock = DockStyle.Fill;
      logBox.ReadOnly = true;
      logBox.BorderStyle = BorderStyle.None;
      logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
      logBox.Font = new Font("Segoe UI", 9.5f);
      logBox.TabStop = false;
      logBox.WordWrap = true;
      logBox.HideSelection = false;
      logBox.Padding = new Padding(10, 8, 10, 8);

      inputBox = new TextBox();
      inputBox.Dock = DockStyle.Bottom;
      inputBox.Multiline = true;
      inputBox.AcceptsReturn = true;
      inputBox.ScrollBars = ScrollBars.Vertical;
      inputBox.Height = 64;
      inputBox.Font = new Font("Segoe UI", 9.5f);
      // Enter (без Shift) — отправка
      inputBox.KeyDown += delegate(object s, KeyEventArgs e)
      {
        if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Submit(); }
      };

      // список вложений над вводом (скрыт, пока вложений нет)
      attachmentsLabel = new Label();
      attachmentsLabel.Dock = DockStyle.Bottom;
      attachmentsLabel.Height = 18;
      attachmentsLabel.TextAlign = ContentAlignment.MiddleLeft;
      attachmentsLabel.AutoEllipsis = true;
      attachmentsLabel.Font = new Font("Segoe UI", 8.5f);
      attachmentsLabel.Padding = new Padding(10, 0, 0, 0);
      attachmentsLabel.Visible = false;

      // нижний ряд кнопок, прижат вправо; масштабируется с окном
      FlowLayoutPanel toolRow = new FlowLayoutPanel();
      toolRow.Dock = DockStyle.Bottom;
      toolRow.FlowDirection = FlowDirection.RightToLeft;
      toolRow.AutoSize = true;
      toolRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
      toolRow.Padding = new Padding(8);
      toolRow.WrapContents = false;

      sendButton = new Button();
      sendButton.Text = "Отправить";
      sendButton.AutoSize = true;
      sendButton.MinimumSize = new Size(110, 30);
      sendButton.FlatStyle = FlatStyle.Flat;
      sendButton.FlatAppearance.BorderSize = 0;

      abortButton = new Button();
      abortButton.Text = "Прервать";
      abortButton.AutoSize = true;
      abortButton.MinimumSize = new Size(88, 30);
      abortButton.Enabled = false;

      attachButton = new Button();
      attachButton.Text = "Прикрепить";
      attachButton.AutoSize = true;
      attachButton.MinimumSize = new Size(92, 30);

      // RightToLeft: первый добавленный — крайний правый
      toolRow.Controls.Add(sendButton);
      toolRow.Controls.Add(abortButton);
      toolRow.Controls.Add(attachButton);

      // порядок Dock: последние добавленные — у внешнего края
      Controls.Add(logBox);
      Controls.Add(inputBox);
      Controls.Add(attachmentsLabel);
      Controls.Add(toolRow);
      Controls.Add(header);

      sendButton.Click += delegate { Submit(); };
      attachButton.Click += delegate { try { AttachFiles(); } catch (Exception e) { Log.Error("panel.Attach", e); } };
      abortButton.Click += delegate
      {
        try { KompasMcp.Davinci.DavinciSession.Abort(); } catch (Exception e) { Log.Error("panel.Abort", e); }
      };

      statusTimer = new System.Windows.Forms.Timer();
      statusTimer.Interval = 2000;
      statusTimer.Tick += delegate { RefreshStatus(); };
      statusTimer.Start();

      SetTheme(false);
      RefreshStatus();
    }

    // ---- тема ----

    void ToggleTheme()
    {
      SetTheme(!dark);
    }

    void SetTheme(bool makeDark)
    {
      dark = makeDark;
      if (dark)
      {
        logBack = Color.FromArgb(30, 30, 34);
        headerBack = Color.FromArgb(39, 39, 44);
        chatText = Color.FromArgb(226, 226, 226);
        whoUser = Color.FromArgb(126, 176, 255);
        whoAssistant = Color.FromArgb(92, 219, 170);
        serviceColor = Color.FromArgb(150, 150, 150);
        inputBack = Color.FromArgb(44, 44, 50);
        inputFore = Color.FromArgb(230, 230, 230);
        sendButton.BackColor = Color.FromArgb(30, 130, 96);
        sendButton.ForeColor = Color.White;
      }
      else
      {
        logBack = Color.White;
        headerBack = Color.FromArgb(240, 242, 245);
        chatText = Color.FromArgb(25, 25, 25);
        whoUser = Color.FromArgb(47, 84, 150);
        whoAssistant = Color.FromArgb(0, 128, 96);
        serviceColor = Color.FromArgb(130, 130, 130);
        inputBack = Color.White;
        inputFore = Color.FromArgb(25, 25, 25);
        sendButton.BackColor = Color.FromArgb(0, 128, 96);
        sendButton.ForeColor = Color.White;
      }
      BackColor = logBack;
      logBox.BackColor = logBack;
      inputBox.BackColor = inputBack;
      inputBox.ForeColor = inputFore;
      attachmentsLabel.ForeColor = dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(90, 90, 90);
      header.BackColor = headerBack;
      statusLabel.ForeColor = dark ? Color.FromArgb(165, 165, 165) : Color.FromArgb(80, 80, 80);
      titleLabel.ForeColor = dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30);
      themeButton.Text = dark ? "☀" : "☾";
      themeButton.ForeColor = dark ? Color.FromArgb(235, 220, 130) : Color.FromArgb(60, 60, 70);
      // перерисовать содержимое чата в новых цветах не выйдет (текст уже
      // отрисован) — новые сообщения пойдут в актуальной теме
    }

    // Отправка сообщения: DavinciSession или эхо.
    // Вложения передаются модели блоком [Вложения пользователя] с путями
    // (файлы копируются в <workspace>\attach — внутри рабочей папки claude).
    public void Submit()
    {
      string text = inputBox.Text.Trim();
      if (text.Length == 0 && pendingAttachments.Count == 0) return;
      string message = text;
      if (pendingAttachments.Count > 0)
      {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(text);
        sb.Append("\n\n[Вложения пользователя]\n");
        foreach (string p in pendingAttachments)
          sb.Append("- ").Append(p).Append('\n');
        message = sb.ToString();
        pendingAttachments.Clear();
        RefreshAttachmentsLabel();
      }
      inputBox.Clear();
      AppendUserMessage(text);
      Action<string> handler = DavinciPanel.SubmitHandler;
      if (handler != null)
      {
        Busy = true;
        try { handler(message); }
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

    // ---- вложения ----

    // Диалог выбора файлов; копии складываются в <workspace>\attach.
    public string[] AttachFiles()
    {
      using (OpenFileDialog dlg = new OpenFileDialog())
      {
        dlg.Title = "Прикрепить файлы (ТЗ, референсы, документы)";
        dlg.Multiselect = true;
        if (dlg.ShowDialog(this) == DialogResult.OK)
          foreach (string src in dlg.FileNames)
            AttachFile(src);
      }
      return pendingAttachments.ToArray();
    }

    // Копирует файл в папку вложений и запоминает путь (при совпадении имён — суффикс (1)).
    public string AttachFile(string src)
    {
      string dir = KompasMcp.Davinci.DavinciSession.AttachmentDir();
      string name = Path.GetFileName(src);
      string baseName = Path.GetFileNameWithoutExtension(name);
      string ext = Path.GetExtension(name);
      string dest = Path.Combine(dir, name);
      int n = 1;
      while (File.Exists(dest))
      {
        dest = Path.Combine(dir, baseName + "(" + n + ")" + ext);
        n++;
      }
      File.Copy(src, dest);
      pendingAttachments.Add(dest);
      AppendLogLine("[файл] " + name + " → " + dest);
      RefreshAttachmentsLabel();
      return dest;
    }

    void RefreshAttachmentsLabel()
    {
      if (pendingAttachments.Count == 0) { attachmentsLabel.Visible = false; return; }
      attachmentsLabel.Text = "Вложения: " + string.Join("; ", pendingAttachments);
      attachmentsLabel.Visible = true;
    }

    // Тестовый доступ к списку вложений.
    public string[] PendingAttachments { get { return pendingAttachments.ToArray(); } }

    // ---- иконка и аватар ----

    static Icon LoadDavinciIcon()
    {
      try
      {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string p = Path.Combine(exeDir, "avatar.ico");
        if (!File.Exists(p)) p = Path.Combine(Path.Combine(exeDir, "davinci"), "avatar.ico");
        if (File.Exists(p)) return new Icon(p, 32, 32);
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
      }
      catch (Exception e) { Log.Error("panel.icon", e); return null; }
    }

    static Image LoadAvatar()
    {
      try
      {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string p = Path.Combine(exeDir, "avatar.png");
        if (!File.Exists(p)) p = Path.Combine(Path.Combine(exeDir, "davinci"), "avatar.png");
        if (File.Exists(p)) return Image.FromFile(p);
      }
      catch (Exception e) { Log.Error("panel.avatar", e); }
      return null;
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
      logBox.SelectionFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
      logBox.SelectionColor = whoColor;
      logBox.AppendText(who);
      logBox.SelectionFont = logBox.Font;
      logBox.SelectionColor = chatText;
      logBox.AppendText(Environment.NewLine + text);
      ScrollEnd();
    }

    public void AppendUserMessage(string text)
    {
      AppendStyled("Ты", whoUser, text);
    }

    public void AppendAssistantMessage(string text)
    {
      AppendStyled("Давинчи", whoAssistant, text);
    }

    // Служебная строка (тулы, итоги) — серым мелким.
    public void AppendLogLine(string line)
    {
      if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine);
      logBox.SelectionStart = logBox.TextLength;
      logBox.SelectionFont = new Font("Segoe UI", 8.5f);
      logBox.SelectionColor = serviceColor;
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
        if (running)
          statusLabel.Text = "КОМПАС: подключён (режим " + mode + ", документов " + docs + ")";
        else if (st.ContainsKey("note"))
          statusLabel.Text = "КОМПАС: " + Convert.ToString(st["note"]);
        else
          statusLabel.Text = "КОМПАС: не запущен (режим " + mode + ")";
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