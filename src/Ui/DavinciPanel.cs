// Панель чата «Давинчи» (WPF): немодальное окно в процессе KompasMcp.exe --panel на своём
// STA-потоке с Dispatcher. Одно на систему (именованный мьютекс; второй запуск выводит окно
// вперёд). Статический DavinciPanel — потокобезопасный вход для сессии копилота: все вызовы
// маршалятся в поток окна. Само окно — PanelWindow.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KompasMcp.Davinci;
using Shapes = System.Windows.Shapes;

namespace KompasMcp.Ui
{
  // Состояние внешней службы для индикатора в шапке. Hard — отправлять запрос бессмысленно
  // (нет Claude Code, нет связи с API); мягкие ошибки (лимит, модель) запрос не блокируют.
  public sealed class ServiceState
  {
    public readonly bool Ok;
    public readonly string Text;
    public readonly string Detail;
    public readonly bool Hard;

    public ServiceState(bool ok, string text, string detail, bool hard = false)
    {
      Ok = ok;
      Text = text;
      Detail = detail;
      Hard = hard;
    }
  }

  public static class DavinciPanel
  {
    const string MutexName = "KompasMcp.DavinciPanel.Mutex";
    const string ShowEventName = "KompasMcp.DavinciPanel.ShowEvent";
    static readonly object Gate = new object();
    static Mutex single;
    static PanelWindow window;
    static Thread uiThread;
    static EventWaitHandle showEvent;

    // Обработчики панели (вызываются в потоке окна; долгую работу — в фон).
    // SubmitHandler == null — эхо-режим (копилот не подключён).
    public static Action<string> SubmitHandler;
    public static Action AbortHandler;
    public static Func<bool> NewDialogHandler;   // false — сброс сейчас невозможен
    // Проверка Claude для индикатора (вызывается в фоне; true — полная проверка запросом к API).
    // null — индикатор показывает, что копилот не подключён.
    public static Func<bool, ServiceState> ClaudeStatusProvider;

    // Открыть панель (идемпотентно). false — панель уже открыта в ДРУГОМ процессе.
    public static bool Start()
    {
      lock (Gate)
      {
        if (window != null) { Post(w => w.BringUp()); return true; }
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
        PanelWindow w = new PanelWindow();
        w.Closed += delegate { w.Dispatcher.InvokeShutdown(); };
        lock (Gate) { window = w; }
        Thread watcher = new Thread(WatchShow);
        watcher.IsBackground = true;
        watcher.Start();
        w.Show();
        Dispatcher.Run();
        Log.Write("panel: closed");
      }
      catch (Exception e) { Log.Error("panel", e); }
      lock (Gate) { window = null; }
    }

    // Второй процесс просит первый показать панель.
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

    // Ждёт сигнал «показать» (периодический WaitOne — поток завершится после Close()).
    static void WatchShow()
    {
      while (showEvent != null)
      {
        bool signaled = false;
        try { signaled = showEvent.WaitOne(5000); } catch { return; }
        if (signaled) Post(w => w.BringUp());
      }
    }

    static void Post(Action<PanelWindow> action)
    {
      PanelWindow w;
      lock (Gate) { w = window; }
      if (w == null) return;
      try { w.Dispatcher.BeginInvoke((Action)delegate { action(w); }); }
      catch (Exception e) { Log.Error("panel.Post", e); }
    }

    // ---- события сессии (из любого потока) ----

    public static bool Busy { set { Post(w => w.Busy = value); } }
    public static void AppendAssistant(string text) { Post(w => w.AddAssistant(text)); }
    public static void ToolCall(string name) { Post(w => w.ToolCall(name)); }
    public static void ToolResult(bool isError) { Post(w => w.ToolResult(isError)); }
    public static void TurnDone(string summary) { Post(w => w.TurnDone(summary)); }
    public static void Notice(string text) { Post(w => w.AddNotice(text)); }
    public static void Error(string text) { Post(w => w.AddError(text)); }
    public static void RefreshClaudeStatus() { Post(w => w.CheckClaude(false)); }

    // Блокирует до закрытия окна (панельный процесс живёт, пока открыта панель).
    public static void WaitClosed()
    {
      Thread t;
      lock (Gate) { t = uiThread; }
      if (t != null && t.IsAlive) t.Join();
    }

    // Закрыть панель (завершение сервера — stdin закрыт).
    public static void Close()
    {
      Post(w => w.Close());
      if (single != null)
      {
        try { single.ReleaseMutex(); } catch { }
        single = null;
      }
      showEvent = null; // WatchShow завершится на ближайшем таймауте
    }
  }

  // Окно чата: шапка (аватар, состояние КОМПАСа, новый диалог, тема), лента сообщений
  // (запрос — пузырём справа, ответ — размеченным текстом, шаги инструментов — свёрнутой
  // строкой хода), поле ввода с вложениями и кнопкой «отправить/остановить».
  public class PanelWindow : Window
  {
    readonly Theme theme = new Theme();
    readonly ScrollViewer scroller;
    readonly StackPanel chat;
    readonly FrameworkElement emptyState;
    readonly TextBox input;
    readonly TextBlock placeholder;
    readonly Border inputCard;
    readonly Button sendButton;
    readonly WrapPanel attachRow;
    readonly TextBlock statusText;          // КОМПАС
    readonly Shapes.Ellipse statusDot;
    readonly TextBlock claudeText;          // Claude
    readonly Shapes.Ellipse claudeDot;
    readonly FrameworkElement claudeRow;
    readonly FrameworkElement bottomBar;
    readonly SettingsPage settingsPage;
    readonly DispatcherTimer claudeTimer;
    ServiceState claudeState;
    bool claudeChecking;
    bool pinned;
    readonly Grid busyLine;
    readonly TranslateTransform busyShift;
    readonly List<string> pendingAttachments = new List<string>();
    readonly StringBuilder plain = new StringBuilder();   // текст ленты (тесты, отладка)
    readonly DispatcherTimer statusTimer;
    ImageSource avatar;
    Turn turn;            // текущий ход: блок шагов инструментов
    bool busy;

    public PanelWindow()
    {
      Title = "Давинчи — копилот КОМПАС-3D";
      // обычное окно; «поверх других окон» — только если включено в настройках
      pinned = Theme.LoadPinned();
      Topmost = pinned;
      UseLayoutRounding = true;
      SnapsToDevicePixels = true;
      AllowDrop = true;
      FontFamily = Markdown.UiFont;
      FontSize = 14;
      MinWidth = 380;
      MinHeight = 440;
      TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
      theme.Apply(Theme.LoadDark());
      Resources.MergedDictionaries.Add(Styles.Load());
      theme.Publish(Resources);
      Background = theme.Window;
      Foreground = theme.Text;
      avatar = LoadAvatar();
      try { Icon = LoadIcon(); } catch (Exception e) { Log.Error("panel.icon", e); }
      PlaceWindow();

      Grid root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // шапка
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // полоса занятости
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // ввод
      Content = root;

      // ---- шапка ----
      Border header = new Border { BorderBrush = theme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 10, 8, 10) };
      DockPanel head = new DockPanel();
      header.Child = head;
      StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      DockPanel.SetDock(tools, Dock.Right);
      Button newChat = IconButton(Icons.NewChat, "Новый диалог");
      newChat.Click += delegate { NewDialog(); };
      Button settings = IconButton(Icons.Settings, "Настройки");
      settings.Click += delegate { OpenSettings(); };
      tools.Children.Add(newChat);
      tools.Children.Add(settings);
      head.Children.Add(tools);
      FrameworkElement av = AvatarCircle(38);
      av.Margin = new Thickness(0, 0, 10, 0);
      av.VerticalAlignment = VerticalAlignment.Center;
      DockPanel.SetDock(av, Dock.Left);
      head.Children.Add(av);
      StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
      titles.Children.Add(new TextBlock { Text = "Давинчи", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = theme.Text });
      titles.Children.Add(StatusRow("КОМПАС: проверяю…", out statusDot, out statusText));
      claudeRow = StatusRow(DavinciPanel.ClaudeStatusProvider == null ? "Claude: копилот не подключён" : "Claude: проверяю…",
        out claudeDot, out claudeText);
      claudeRow.Cursor = Cursors.Hand;
      claudeRow.ToolTip = "Щелчок — проверить ещё раз";
      claudeRow.MouseLeftButtonUp += delegate { CheckClaude(true); };
      titles.Children.Add(claudeRow);
      head.Children.Add(titles);
      root.Children.Add(header);

      // ---- полоса занятости: бегущий отрезок под шапкой ----
      busyLine = new Grid { Height = 2, ClipToBounds = true, Visibility = Visibility.Collapsed };
      Shapes.Rectangle runner = new Shapes.Rectangle { Width = 90, Fill = theme.Accent, HorizontalAlignment = HorizontalAlignment.Left };
      busyShift = new TranslateTransform(-90, 0);
      runner.RenderTransform = busyShift;
      busyLine.Children.Add(runner);
      Grid.SetRow(busyLine, 1);
      root.Children.Add(busyLine);
      busyLine.SizeChanged += delegate { if (busy) StartBusyAnimation(); };

      // ---- лента ----
      Grid feed = new Grid();
      chat = new StackPanel { Margin = new Thickness(16, 8, 16, 12) };
      emptyState = BuildEmptyState();
      feed.Children.Add(chat);
      feed.Children.Add(emptyState);
      scroller = new ScrollViewer
      {
        Content = feed,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false
      };
      // колесо всегда листает ленту (иначе его перехватывают поля сообщений)
      scroller.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
      {
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta * 0.6);
        e.Handled = true;
      };
      Grid.SetRow(scroller, 2);
      root.Children.Add(scroller);

      // ---- ввод ----
      StackPanel bottom = new StackPanel { Margin = new Thickness(12, 4, 12, 12) };
      attachRow = new WrapPanel { Margin = new Thickness(2, 0, 0, 2), Visibility = Visibility.Collapsed };
      bottom.Children.Add(attachRow);
      inputCard = new Border
      {
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = theme.Border,
        Background = theme.Surface,
        Padding = new Thickness(12, 9, 8, 8)
      };
      Grid card = new Grid();
      card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      input = new TextBox
      {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 66,   // три строки
        MaxHeight = 240,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = theme.Text,
        CaretBrush = theme.Text,
        SelectionBrush = theme.Selection,
        Padding = new Thickness(0),
        FontSize = 14,
        FocusVisualStyle = null
      };
      placeholder = new TextBlock
      {
        Text = "Опишите задачу…",
        Foreground = theme.Muted,
        IsHitTestVisible = false,
        Margin = new Thickness(2, 0, 0, 0)
      };
      input.TextChanged += delegate { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
      input.PreviewKeyDown += delegate(object s, KeyEventArgs e)
      {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; Submit(); }
      };
      input.GotKeyboardFocus += delegate { inputCard.BorderBrush = theme.FocusBorder; };
      input.LostKeyboardFocus += delegate { inputCard.BorderBrush = theme.Border; };
      card.Children.Add(input);
      card.Children.Add(placeholder);
      DockPanel actions = new DockPanel { Margin = new Thickness(-6, 6, 0, 0), LastChildFill = false };
      Grid.SetRow(actions, 1);
      Button attach = IconButton(Icons.Attach, "Прикрепить файлы");
      attach.Click += delegate { try { AttachFiles(); } catch (Exception e) { Log.Error("panel.Attach", e); } };
      DockPanel.SetDock(attach, Dock.Left);
      actions.Children.Add(attach);
      sendButton = new Button { Style = (Style)Resources["SendButton"] };
      sendButton.Click += delegate { if (busy) Abort(); else Submit(); };
      DockPanel.SetDock(sendButton, Dock.Right);
      actions.Children.Add(sendButton);
      card.Children.Add(actions);
      inputCard.Child = card;
      bottom.Children.Add(inputCard);
      Grid.SetRow(bottom, 3);
      root.Children.Add(bottom);
      bottomBar = bottom;

      // ---- настройки: страница вместо ленты и ввода ----
      settingsPage = new SettingsPage(theme, Resources, CloseSettings) { Visibility = Visibility.Collapsed };
      Grid.SetRow(settingsPage, 2);
      Grid.SetRowSpan(settingsPage, 2);
      root.Children.Add(settingsPage);

      // перетаскивание файлов в окно — вложения
      DragOver += delegate(object s, DragEventArgs e)
      {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
      };
      Drop += delegate(object s, DragEventArgs e)
      {
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null) return;
        foreach (string f in files)
          try { if (File.Exists(f)) AttachFile(f); } catch (Exception ex) { Log.Error("panel.Drop", ex); }
      };

      SourceInitialized += delegate { ApplyTitleBar(); };
      Loaded += delegate { input.Focus(); };
      // положение — только у показанного окна (тест создаёт окно без показа)
      Closing += delegate { if (IsLoaded) Theme.SaveBounds(WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds); };

      statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
      statusTimer.Tick += delegate { RefreshStatus(); };
      statusTimer.Start();
      // Claude: при открытии — полная проверка (видно исчерпанный лимит, неверную модель),
      // дальше раз в 20 с — быстрая (claude найден, API отвечает)
      claudeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
      claudeTimer.Tick += delegate { CheckClaude(false); };
      claudeTimer.Start();

      UpdateSendButton();
      RefreshStatus();
      CheckClaude(true);
    }

    StackPanel StatusRow(string text, out Shapes.Ellipse dot, out TextBlock label)
    {
      StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent };
      dot = new Shapes.Ellipse { Width = 7, Height = 7, Fill = theme.Idle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) };
      label = new TextBlock { FontSize = 12, Text = text, Foreground = theme.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
      row.Children.Add(dot);
      row.Children.Add(label);
      return row;
    }

    // ---- состояние ----

    public bool Busy
    {
      get { return busy; }
      set
      {
        busy = value;
        UpdateSendButton();
        if (busy)
        {
          busyLine.Visibility = Visibility.Visible;
          StartBusyAnimation();
        }
        else
        {
          busyShift.BeginAnimation(TranslateTransform.XProperty, null);
          busyLine.Visibility = Visibility.Collapsed;
          if (turn != null && !turn.Done) turn.Finish("остановлено");
        }
      }
    }

    // бегущий отрезок: новый проход на каждый запуск и при смене ширины окна
    void StartBusyAnimation()
    {
      double width = busyLine.ActualWidth > 0 ? busyLine.ActualWidth : ActualWidth;
      busyShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
      {
        From = -90,
        To = width,
        Duration = TimeSpan.FromSeconds(1.4),
        RepeatBehavior = RepeatBehavior.Forever
      });
    }

    void UpdateSendButton()
    {
      sendButton.Content = busy
        ? Styles.Icon(Icons.Stop, theme.AccentText, 16, true)
        : Styles.Icon(Icons.Send, theme.AccentText, 17);
      sendButton.ToolTip = busy ? "Остановить" : "Отправить (Enter)";
    }

    // Вывести окно вперёд (повторный вызов меню). Незакреплённое окно поднимается кратким
    // Topmost и возвращается в обычный режим — дальше перекрывается другими окнами как обычно.
    public void BringUp()
    {
      Show();
      if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
      Activate();
      if (!pinned) { Topmost = true; Topmost = false; }
    }

    void SetPinned(bool value)
    {
      pinned = value;
      Topmost = value;
      Theme.SavePinned(value);
    }

    // ---- настройки ----

    internal void OpenSettings()
    {
      DavinciConfig cfg;
      try { cfg = KompasMcp.Davinci.DavinciSession.Config(); }
      catch (Exception e) { Log.Error("panel.settings", e); AddError("Не удалось прочитать настройки: " + e.Message); return; }
      settingsPage.Load(cfg, Theme.LoadMode(), pinned);
      scroller.Visibility = Visibility.Collapsed;
      bottomBar.Visibility = Visibility.Collapsed;
      settingsPage.Visibility = Visibility.Visible;
    }

    internal bool SettingsOpen { get { return settingsPage.Visibility == Visibility.Visible; } }

    internal void CloseSettings(bool saved)
    {
      if (saved)
      {
        try
        {
          KompasMcp.Davinci.DavinciSession.Apply(settingsPage.Result);
          Theme.SaveMode(settingsPage.ThemeMode);
          SetTheme(Theme.IsDarkMode(settingsPage.ThemeMode));
          SetPinned(settingsPage.Pinned);
          if (busy) AddNotice("Настройки сохранены — применятся со следующего запроса.");
        }
        catch (Exception e)
        {
          Log.Error("panel.settings.save", e);
          AddError("Не удалось сохранить настройки: " + e.Message);
        }
        CheckClaude(true);
      }
      settingsPage.Visibility = Visibility.Collapsed;
      scroller.Visibility = Visibility.Visible;
      bottomBar.Visibility = Visibility.Visible;
      input.Focus();
    }

    // ---- индикатор Claude ----

    // Проверка в фоне (HTTP с таймаутом); результат — в потоке окна.
    public void CheckClaude(bool deep)
    {
      Func<bool, ServiceState> provider = DavinciPanel.ClaudeStatusProvider;
      if (provider == null)
      {
        ShowClaude(new ServiceState(false, "Claude: копилот не подключён", "Нет davinci.json или ошибка инициализации — см. kompas-mcp.log"));
        return;
      }
      if (claudeChecking) return;
      claudeChecking = true;
      if (deep || claudeState == null) { claudeText.Text = "Claude: проверяю…"; claudeDot.Fill = theme.Idle; }
      System.Threading.ThreadPool.QueueUserWorkItem(delegate
      {
        ServiceState s;
        try { s = provider(deep); }
        catch (Exception e) { Log.Error("panel.claude", e); s = new ServiceState(false, "Claude: проверка не удалась", e.Message); }
        Dispatcher.BeginInvoke((Action)delegate { claudeChecking = false; ShowClaude(s); });
      });
    }

    void ShowClaude(ServiceState s)
    {
      claudeState = s;
      claudeText.Text = s.Text;
      claudeDot.Fill = s.Ok ? theme.Ok : theme.Error;
      claudeText.Foreground = s.Ok ? theme.Muted : theme.Error;
      claudeRow.ToolTip = (string.IsNullOrEmpty(s.Detail) ? "" : s.Detail + "\n\n") + "Щелчок — проверить ещё раз";
    }

    // ---- отправка ----

    // Вложения передаются модели блоком [Вложения пользователя] с путями
    // (файлы скопированы в <workspace>\attach — внутри рабочей папки claude).
    public void Submit()
    {
      string text = input.Text.Trim();
      if (text.Length == 0 && pendingAttachments.Count == 0) return;
      if (busy) { AddNotice("Давинчи ещё работает — дождитесь ответа или остановите запрос."); return; }
      if (claudeState != null && !claudeState.Ok && claudeState.Hard && DavinciPanel.ClaudeStatusProvider != null)
      {
        // состояние могло устареть — быстрая повторная проверка (claude в PATH, ответ API)
        ServiceState s;
        try { s = DavinciPanel.ClaudeStatusProvider(false); } catch (Exception e) { Log.Error("panel.claude", e); s = claudeState; }
        ShowClaude(s);
        if (!s.Ok && s.Hard) { AddError(s.Text + ". Запрос не отправлен — текст остался в поле ввода."); return; }
      }
      string[] files = pendingAttachments.ToArray();
      string message = text;
      if (files.Length > 0)
      {
        StringBuilder sb = new StringBuilder(text);
        sb.Append("\n\n[Вложения пользователя]\n");
        foreach (string p in files) sb.Append("- ").Append(p).Append('\n');
        message = sb.ToString();
        pendingAttachments.Clear();
        RefreshAttachments();
      }
      input.Clear();
      AddUser(text, files);
      Action<string> handler = DavinciPanel.SubmitHandler;
      if (handler == null)
      {
        AddNotice("[эхо] копилот не подключён (нет davinci.json или ошибка инициализации)");
        return;
      }
      turn = null;
      Busy = true;
      StartTurn();
      try { handler(message); }
      catch (Exception e)
      {
        Log.Error("panel.Submit", e);
        AddError("Не удалось отправить запрос: " + e.Message);
        Busy = false;
      }
    }

    void Abort()
    {
      Action a = DavinciPanel.AbortHandler;
      if (a == null) return;
      try { a(); } catch (Exception e) { Log.Error("panel.Abort", e); }
    }

    void NewDialog()
    {
      if (busy) { AddNotice("Дождитесь ответа или остановите запрос, затем начните новый диалог."); return; }
      Func<bool> h = DavinciPanel.NewDialogHandler;
      if (h != null && !h()) return;
      chat.Children.Clear();
      plain.Length = 0;
      turn = null;
      emptyState.Visibility = Visibility.Visible;
      input.Focus();
    }

    // ---- лента ----

    public void AddUser(string text, string[] files)
    {
      StackPanel content = new StackPanel();
      if (text.Length > 0) content.Children.Add(SelectableText(text));
      foreach (string f in files)
        content.Children.Add(new TextBlock
        {
          Text = "Вложение: " + System.IO.Path.GetFileName(f),
          FontSize = 12,
          Foreground = theme.Muted,
          Margin = new Thickness(0, text.Length > 0 ? 4 : 0, 0, 0)
        });
      Border bubble = new Border
      {
        Background = theme.UserBubble,
        CornerRadius = new CornerRadius(12, 12, 4, 12),
        Padding = new Thickness(12, 8, 12, 8),
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(56, 14, 0, 2),
        Child = content
      };
      Append(bubble, true);
      plain.Append("Вы: ").Append(text).Append('\n');
    }

    public void AddAssistant(string text)
    {
      if (string.IsNullOrEmpty(text)) return;
      if (turn == null || !turn.HasAnswer)
      {
        // подпись — один раз на ход
        StackPanel who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
        who.Children.Add(AvatarCircle(20));
        who.Children.Add(new TextBlock
        {
          Text = "Давинчи",
          FontSize = 12.5,
          FontWeight = FontWeights.SemiBold,
          Foreground = theme.Muted,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(7, 0, 0, 0)
        });
        Append(who, false);
        if (turn != null) turn.HasAnswer = true;
      }
      RichTextBox body = new RichTextBox
      {
        Document = Markdown.Build(text, theme),
        IsReadOnly = true,
        IsDocumentEnabled = true,
        IsReadOnlyCaretVisible = false,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = theme.Text,
        SelectionBrush = theme.Selection,
        Padding = new Thickness(0),
        Margin = new Thickness(-2, 0, 0, 4),
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        FocusVisualStyle = null
      };
      Append(body, false);
      plain.Append("Давинчи: ").Append(text).Append('\n');
    }

    public void AddNotice(string text)
    {
      Append(new TextBlock
      {
        Text = text,
        FontSize = 12,
        Foreground = theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 2)
      }, false);
      plain.Append(text).Append('\n');
    }

    public void AddError(string text)
    {
      Border b = new Border
      {
        BorderBrush = theme.Error,
        BorderThickness = new Thickness(2, 0, 0, 0),
        Padding = new Thickness(10, 2, 0, 2),
        Margin = new Thickness(0, 8, 0, 2),
        Child = new TextBlock { Text = text, FontSize = 12.5, Foreground = theme.Error, TextWrapping = TextWrapping.Wrap }
      };
      Append(b, false);
      plain.Append("Ошибка: ").Append(text).Append('\n');
    }

    // ---- ход: шаги инструментов одной строкой, раскрывается щелчком ----

    void StartTurn()
    {
      turn = new Turn(theme);
      Append(turn.Root, false);
    }

    public void ToolCall(string name)
    {
      if (turn == null || turn.Done) StartTurn();
      string shortName = ShortToolName(name);
      bool stick = AtBottom();
      turn.Add(shortName);
      if (stick) ScrollToEnd();
      plain.Append("[инструмент] ").Append(shortName).Append('\n');
    }

    public void ToolResult(bool isError)
    {
      if (turn != null && isError) turn.MarkLastFailed();
    }

    public void TurnDone(string summary)
    {
      if (turn == null) return;
      turn.Finish(summary);
    }

    // mcp__kompas-mcp__create_part -> create_part
    static string ShortToolName(string name)
    {
      if (string.IsNullOrEmpty(name)) return "?";
      if (name.StartsWith("mcp__"))
      {
        int k = name.IndexOf("__", 5, StringComparison.Ordinal);
        if (k > 0) return name.Substring(k + 2);
      }
      return name;
    }

    // Новые элементы встают перед строкой хода, пока ход идёт: она всегда внизу.
    void Append(UIElement element, bool forceScroll)
    {
      bool stick = forceScroll || AtBottom();
      emptyState.Visibility = Visibility.Collapsed;
      int at = chat.Children.Count;
      if (turn != null && !turn.Done && chat.Children.Contains(turn.Root) && element != turn.Root)
        at = chat.Children.IndexOf(turn.Root);
      chat.Children.Insert(at, element);
      if (stick) ScrollToEnd();
    }

    bool AtBottom()
    {
      return scroller.VerticalOffset >= scroller.ScrollableHeight - 48;
    }

    void ScrollToEnd()
    {
      Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { scroller.ScrollToEnd(); });
    }

    TextBox SelectableText(string text)
    {
      return new TextBox
      {
        Text = text,
        IsReadOnly = true,
        IsReadOnlyCaretVisible = false,
        TextWrapping = TextWrapping.Wrap,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = theme.Text,
        SelectionBrush = theme.Selection,
        Padding = new Thickness(0),
        FontSize = 14,
        FocusVisualStyle = null
      };
    }

    FrameworkElement BuildEmptyState()
    {
      StackPanel p = new StackPanel
      {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 380,
        Margin = new Thickness(24, 32, 24, 24)
      };
      FrameworkElement av = AvatarCircle(72);
      av.HorizontalAlignment = HorizontalAlignment.Center;
      p.Children.Add(av);
      p.Children.Add(new TextBlock
      {
        Text = "Давинчи",
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Foreground = theme.Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 12, 0, 4)
      });
      p.Children.Add(new TextBlock
      {
        Text = "Опишите деталь, чертёж или расчёт — я выполню это в открытом КОМПАСе.",
        FontSize = 13,
        Foreground = theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 0, 0, 18)
      });
      foreach (string sample in new[] {
        "Сделай втулку Ø60×40 с отверстием Ø30 из стали 45 и скажи массу",
        "Начерти зубчатое колесо m=2, z=40 на листе А3",
        "Подбери посадку H7/p6 для Ø30 и объясни её" })
      {
        string text = sample;
        Button b = new Button
        {
          Style = (Style)Resources["PromptButton"],
          Content = new TextBlock { Text = text, FontSize = 13, Foreground = theme.Text, TextWrapping = TextWrapping.Wrap },
          Margin = new Thickness(0, 0, 0, 8)
        };
        b.Click += delegate { input.Text = text; input.CaretIndex = text.Length; input.Focus(); };
        p.Children.Add(b);
      }
      return p;
    }

    Button IconButton(string icon, string tip)
    {
      return new Button
      {
        Style = (Style)Resources["IconButton"],
        Content = Styles.Icon(icon, theme.Muted, 18),
        ToolTip = tip,
        VerticalAlignment = VerticalAlignment.Center
      };
    }

    FrameworkElement AvatarCircle(double size)
    {
      Shapes.Ellipse e = new Shapes.Ellipse { Width = size, Height = size };
      if (avatar != null) e.Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill };
      else e.Fill = theme.Accent;
      return e;
    }

    // ---- вложения ----

    // Диалог выбора файлов; копии складываются в <workspace>\attach.
    public string[] AttachFiles()
    {
      Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
      {
        Title = "Прикрепить файлы (ТЗ, эскизы, модели)",
        Multiselect = true
      };
      if (dlg.ShowDialog(this) == true)
        foreach (string src in dlg.FileNames) AttachFile(src);
      return pendingAttachments.ToArray();
    }

    // Копирует файл в папку вложений (при совпадении имён — суффикс (1)) и запоминает путь.
    public string AttachFile(string src)
    {
      string dir = KompasMcp.Davinci.DavinciSession.AttachmentDir();
      string name = System.IO.Path.GetFileName(src);
      string baseName = System.IO.Path.GetFileNameWithoutExtension(name);
      string ext = System.IO.Path.GetExtension(name);
      string dest = System.IO.Path.Combine(dir, name);
      for (int n = 1; File.Exists(dest); n++)
        dest = System.IO.Path.Combine(dir, baseName + "(" + n + ")" + ext);
      File.Copy(src, dest);
      pendingAttachments.Add(dest);
      RefreshAttachments();
      return dest;
    }

    public string[] PendingAttachments { get { return pendingAttachments.ToArray(); } }

    void RefreshAttachments()
    {
      attachRow.Children.Clear();
      foreach (string path in pendingAttachments)
      {
        string p = path;
        StackPanel chip = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Children.Add(new TextBlock
        {
          Text = System.IO.Path.GetFileName(p),
          FontSize = 12,
          Foreground = theme.Text,
          VerticalAlignment = VerticalAlignment.Center,
          MaxWidth = 220,
          TextTrimming = TextTrimming.CharacterEllipsis,
          ToolTip = p
        });
        Button remove = new Button
        {
          Style = (Style)Resources["IconButton"],
          Padding = new Thickness(3),
          Margin = new Thickness(4, 0, 0, 0),
          Content = Styles.Icon(Icons.Close, theme.Muted, 12),
          ToolTip = "Убрать"
        };
        remove.Click += delegate { pendingAttachments.Remove(p); RefreshAttachments(); };
        chip.Children.Add(remove);
        attachRow.Children.Add(new Border
        {
          BorderBrush = theme.Border,
          BorderThickness = new Thickness(1),
          CornerRadius = new CornerRadius(6),
          Padding = new Thickness(8, 2, 2, 2),
          Margin = new Thickness(0, 0, 6, 6),
          Child = chip
        });
      }
      attachRow.Visibility = pendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- тема и окно ----

    internal void SetTheme(bool dark)
    {
      theme.Apply(dark);
      ApplyTitleBar();
    }

    internal bool IsDark { get { return theme.Dark; } }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Тёмный заголовок окна в тёмной теме (Windows 10 2004+ / 11; на старых — без эффекта).
    void ApplyTitleBar()
    {
      try
      {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = theme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
      }
      catch { }
    }

    void PlaceWindow()
    {
      Rect wa = SystemParameters.WorkArea;
      Rect? saved = Theme.LoadBounds();
      if (saved.HasValue && saved.Value.Width >= MinWidth && saved.Value.Height >= MinHeight
          && saved.Value.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
               SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)))
      {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = saved.Value.X; Top = saved.Value.Y; Width = saved.Value.Width; Height = saved.Value.Height;
        return;
      }
      Width = Math.Min(460, wa.Width - 40);
      Height = Math.Min(720, wa.Height - 40);
      WindowStartupLocation = WindowStartupLocation.Manual;
      Left = wa.Right - Width - 20;
      Top = wa.Bottom - Height - 20;
    }

    void RefreshStatus()
    {
      try
      {
        Dictionary<string, object> st = KompasHost.Status();
        bool running = st.ContainsKey("running") && (bool)st["running"];
        if (running)
        {
          System.Collections.ICollection docs = st.ContainsKey("documents") ? st["documents"] as System.Collections.ICollection : null;
          int n = docs == null ? 0 : docs.Count;
          statusText.Text = "КОМПАС подключён · документов: " + n;
          statusDot.Fill = theme.Ok;
        }
        else
        {
          statusText.Text = st.ContainsKey("note") ? "КОМПАС " + Convert.ToString(st["note"]) : "КОМПАС не найден — запустите КОМПАС";
          statusDot.Fill = theme.Idle;
        }
      }
      catch (Exception e)
      {
        statusText.Text = "КОМПАС: состояние недоступно";
        statusDot.Fill = theme.Idle;
        Log.Error("panel.RefreshStatus", e);
      }
    }

    static string AssetPath(string name)
    {
      string exeDir = AppDomain.CurrentDomain.BaseDirectory;
      string p = System.IO.Path.Combine(exeDir, name);
      if (!File.Exists(p)) p = System.IO.Path.Combine(System.IO.Path.Combine(exeDir, "davinci"), name);
      return File.Exists(p) ? p : null;
    }

    static ImageSource LoadAvatar()
    {
      try
      {
        string p = AssetPath("avatar.png");
        if (p == null) return null;
        BitmapImage img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(p);
        img.EndInit();
        img.Freeze();
        return img;
      }
      catch (Exception e) { Log.Error("panel.avatar", e); return null; }
    }

    static ImageSource LoadIcon()
    {
      string p = AssetPath("avatar.ico");
      if (p == null) return null;
      return BitmapFrame.Create(new Uri(p), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    // Текст ленты (тесты).
    public string ChatText { get { return plain.ToString(); } }
    public TextBox InputBox { get { return input; } }

    // Строка хода: «Выполняю: create_part» во время работы, затем «Шагов: 6 · 41 с»;
    // щелчок раскрывает список инструментов (ошибочные — цветом ошибки).
    sealed class Turn
    {
      public readonly Border Root;
      readonly Theme theme;
      readonly TextBlock header;
      readonly ContentControl chevron;
      readonly StackPanel items;
      readonly DateTime started = DateTime.Now;
      TextBlock last;
      int count;
      bool expanded;
      public bool Done { get; private set; }
      public bool HasAnswer;

      public Turn(Theme t)
      {
        theme = t;
        header = new TextBlock { FontSize = 12.5, Foreground = t.Muted, Text = "Думаю…", VerticalAlignment = VerticalAlignment.Center };
        chevron = new ContentControl { Content = Styles.Icon(Icons.ChevronRight, t.Muted, 12), Margin = new Thickness(0, 0, 5, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(chevron);
        row.Children.Add(header);
        items = new StackPanel { Margin = new Thickness(17, 4, 0, 2), Visibility = Visibility.Collapsed };
        StackPanel body = new StackPanel();
        body.Children.Add(row);
        body.Children.Add(items);
        Root = new Border { Child = body, Margin = new Thickness(0, 10, 0, 2), Background = Brushes.Transparent };
        Root.MouseLeftButtonUp += delegate { if (count > 0) Toggle(); };
      }

      public void Add(string tool)
      {
        count++;
        last = new TextBlock { Text = tool, FontFamily = Markdown.MonoFont, FontSize = 12, Foreground = theme.Muted, Margin = new Thickness(0, 1, 0, 1) };
        items.Children.Add(last);
        chevron.Visibility = Visibility.Visible;
        Root.Cursor = Cursors.Hand;
        header.Text = "Выполняю: " + tool;
      }

      public void MarkLastFailed()
      {
        if (last == null) return;
        last.Foreground = theme.Error;
        last.Text += " — ошибка";
      }

      public void Finish(string summary)
      {
        if (Done) return;
        Done = true;
        int sec = (int)Math.Round((DateTime.Now - started).TotalSeconds);
        string time = sec < 60 ? sec + " с" : (sec / 60) + " мин " + (sec % 60) + " с";
        string text = count > 0 ? "Шагов: " + count + " · " + time : "Готово · " + time;
        if (!string.IsNullOrEmpty(summary)) text += " · " + summary;
        header.Text = text;
      }

      void Toggle()
      {
        expanded = !expanded;
        items.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        chevron.Content = Styles.Icon(expanded ? Icons.ChevronDown : Icons.ChevronRight, theme.Muted, 12);
      }
    }
  }
}
