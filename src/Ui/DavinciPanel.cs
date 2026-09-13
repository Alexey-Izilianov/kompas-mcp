// Панель чата «Давинчи» (Этап 4, эхо-режим): немодальная WinForms-форма,
// живёт в процессе KompasMcp.exe на отдельном STA-потоке. Single-instance
// (named mutex + вывод существующего окна на передний план). Реальный запуск
// claude подключается на Этапе 5 через DavinciPanel.SubmitHandler; пока он
// null — введённый текст эхом возвращается в лог.
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
    readonly Button hideButton;
    readonly Label statusLabel;
    readonly Label attachmentsLabel;
    readonly List<string> pendingAttachments = new List<string>();
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
      Icon icon = LoadDavinciIcon();
      if (icon != null) Icon = icon;

      // Шапка: аватар + строка статуса КОМПАСа
      Panel header = new Panel();
      header.Dock = DockStyle.Top;
      header.Height = 34;
      PictureBox avatar = new PictureBox();
      avatar.Dock = DockStyle.Left;
      avatar.Width = 32;
      avatar.SizeMode = PictureBoxSizeMode.Zoom;
      Image av = LoadAvatar();
      if (av != null) avatar.Image = av;

      statusLabel = new Label();
      statusLabel.Dock = DockStyle.Fill;
      statusLabel.TextAlign = ContentAlignment.MiddleLeft;
      statusLabel.Text = "КОМПАС: …";
      header.Controls.Add(avatar);
      header.Controls.Add(statusLabel);

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
      sendButton.Text = "Отправить (Ctrl+Enter)";
      sendButton.Width = 170;

      abortButton = new Button();
      abortButton.Text = "Прервать";
      abortButton.Width = 90;
      abortButton.Enabled = false;

      attachButton = new Button();
      attachButton.Text = "Прикрепить";
      attachButton.Width = 100;

      hideButton = new Button();
      hideButton.Text = "Скрыть";
      hideButton.Width = 80;

      // нижний ряд кнопок: слева направо Прикрепить | Скрыть | Отправить | Прервать
      Panel toolRow = new Panel();
      toolRow.Dock = DockStyle.Bottom;
      toolRow.Height = 32;
      toolRow.Controls.Add(abortButton);
      toolRow.Controls.Add(sendButton);
      toolRow.Controls.Add(hideButton);
      toolRow.Controls.Add(attachButton);
      abortButton.Dock = DockStyle.Left;
      sendButton.Dock = DockStyle.Left;
      hideButton.Dock = DockStyle.Left;
      attachButton.Dock = DockStyle.Left;

      // список вложений над вводом (скрыт, пока вложений нет)
      attachmentsLabel = new Label();
      attachmentsLabel.Dock = DockStyle.Bottom;
      attachmentsLabel.Height = 18;
      attachmentsLabel.TextAlign = ContentAlignment.MiddleLeft;
      attachmentsLabel.AutoEllipsis = true;
      attachmentsLabel.Font = statusFont;
      attachmentsLabel.ForeColor = Color.FromArgb(90, 90, 90);
      attachmentsLabel.Visible = false;

      // порядок Dock: последние добавленные — у внешнего края
      Controls.Add(logBox);
      Controls.Add(inputBox);
      Controls.Add(attachmentsLabel);
      Controls.Add(toolRow);
      Controls.Add(header);

      sendButton.Click += delegate { Submit(); };
      attachButton.Click += delegate { try { AttachFiles(); } catch (Exception e) { Log.Error("panel.Attach", e); } };
      hideButton.Click += delegate { Hide(); };
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