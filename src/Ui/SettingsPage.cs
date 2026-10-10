// Страница настроек панели «Давинчи»: источник нейросети и модель, работа копилота, окно.
// Всё применяется кнопкой «Сохранить»; «Проверить» — пробный запрос с введёнными, ещё не
// сохранёнными параметрами. Список моделей Ollama берётся с сервера (/api/tags).
using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KompasMcp.Davinci;
using KompasMcp.JsonLib;

namespace KompasMcp.Ui
{
  sealed class SettingsPage : Grid
  {
    static readonly string[] ProviderIds = { DavinciConfig.ProviderClaude, DavinciConfig.ProviderOllama, DavinciConfig.ProviderApi };
    static readonly string[] ProviderNames = { "Claude Code", "Ollama", "Другой API" };
    static readonly string[] ProviderHints = {
      "Ваш вход в Claude Code — как claude в терминале.",
      "Сервер Ollama на этом компьютере (локальные и облачные модели).",
      "Anthropic-совместимый API: адрес и ключ." };
    static readonly string[] ThemeIds = { "system", "light", "dark" };
    static readonly string[] ThemeNames = { "Как в Windows", "Светлая", "Тёмная" };

    readonly Theme theme;
    readonly ResourceDictionary res;
    readonly Action<bool> closed;   // true — сохранено
    readonly Button[] providerButtons = new Button[3];
    readonly Button[] themeButtons = new Button[3];
    readonly TextBlock providerHint, testResult;
    readonly FrameworkElement urlRow, keyRow;
    readonly TextBox url, model, workspace, timeout, claudeCmd;
    readonly PasswordBox key;
    readonly TextBlock modelPlaceholder;
    readonly WrapPanel modelChips;
    readonly Button testButton;
    readonly CheckBox pin;
    DavinciConfig cfg;
    string provider;
    int chipsRequest;

    public DavinciConfig Result { get; private set; }
    public string ThemeMode { get; private set; }
    public bool Pinned { get { return pin.IsChecked == true; } }

    public SettingsPage(Theme t, ResourceDictionary resources, Action<bool> onClosed)
    {
      theme = t;
      res = resources;
      closed = onClosed;
      RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      // заголовок страницы
      StackPanel top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 16, 4) };
      Button back = new Button { Style = (Style)res["IconButton"], Content = Styles.Icon(Icons.Back, t.Muted, 18), ToolTip = "Назад без сохранения" };
      back.Click += delegate { closed(false); };
      top.Children.Add(back);
      top.Children.Add(new TextBlock { Text = "Настройки", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = t.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
      Children.Add(top);

      StackPanel body = new StackPanel { Margin = new Thickness(18, 4, 18, 16) };
      ScrollViewer scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
      Grid.SetRow(scroll, 1);
      Children.Add(scroll);

      // ---- нейросеть ----
      body.Children.Add(Section("Нейросеть"));
      body.Children.Add(Label("Источник"));
      body.Children.Add(Choices(ProviderNames, providerButtons, i => SetProvider(ProviderIds[i])));
      providerHint = new TextBlock { FontSize = 12, Foreground = t.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
      body.Children.Add(providerHint);

      url = Field();
      url.LostFocus += delegate { if (provider == DavinciConfig.ProviderOllama) LoadOllamaModels(); };
      urlRow = Row("Адрес сервера", url);
      body.Children.Add(urlRow);
      key = new PasswordBox { Style = (Style)res["SecretField"] };
      keyRow = Row("Ключ API", key);
      body.Children.Add(keyRow);

      body.Children.Add(Label("Модель"));
      model = Field();
      modelPlaceholder = new TextBlock { Text = "по умолчанию", Foreground = t.Muted, FontSize = 13, IsHitTestVisible = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
      model.TextChanged += delegate { modelPlaceholder.Visibility = model.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; MarkChips(); };
      Grid modelBox = new Grid();
      modelBox.Children.Add(model);
      modelBox.Children.Add(modelPlaceholder);
      body.Children.Add(modelBox);
      modelChips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
      body.Children.Add(modelChips);

      StackPanel test = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
      testButton = new Button { Style = (Style)res["Choice"], Content = Text("Проверить") };
      testButton.Click += delegate { Test(); };
      test.Children.Add(testButton);
      testResult = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0), MaxWidth = 300 };
      test.Children.Add(testResult);
      body.Children.Add(test);

      // ---- работа ----
      body.Children.Add(Section("Работа"));
      workspace = Field();
      body.Children.Add(Row("Рабочая папка (файлы, вложения)", workspace));
      timeout = Field();
      timeout.Width = 90;
      timeout.HorizontalAlignment = HorizontalAlignment.Left;
      body.Children.Add(Row("Лимит времени на запрос, мин", timeout));
      claudeCmd = Field();
      body.Children.Add(Row("Команда Claude Code", claudeCmd));

      // ---- окно ----
      body.Children.Add(Section("Окно"));
      body.Children.Add(Label("Тема"));
      body.Children.Add(Choices(ThemeNames, themeButtons, i => SetThemeMode(ThemeIds[i])));
      pin = new CheckBox { Style = (Style)res["Check"], Content = Text("Поверх других окон"), Margin = new Thickness(0, 12, 0, 0) };
      body.Children.Add(pin);

      // кнопки
      Border bar = new Border { BorderBrush = t.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 16, 12) };
      StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      Button cancel = new Button { Style = (Style)res["Choice"], Content = Text("Отмена"), Margin = new Thickness(0, 0, 8, 0) };
      cancel.Click += delegate { closed(false); };
      Button save = new Button { Style = (Style)res["AccentButton"], Content = new TextBlock { Text = "Сохранить", FontSize = 13 } };
      save.Click += delegate { Save(); };
      buttons.Children.Add(cancel);
      buttons.Children.Add(save);
      bar.Child = buttons;
      Grid.SetRow(bar, 2);
      Children.Add(bar);
    }

    // Заполнить из текущих настроек (копия — до «Сохранить» ничего не меняется).
    public void Load(DavinciConfig current, string themeMode, bool pinned)
    {
      cfg = current;
      string p = current.ResolvedProvider;
      Dictionary<string, string> env = current.EffectiveEnv();
      string v;
      url.Text = !string.IsNullOrEmpty(current.BaseUrl) ? current.BaseUrl
        : env.TryGetValue("ANTHROPIC_BASE_URL", out v) ? v : "";
      key.Password = !string.IsNullOrEmpty(current.ApiKey) ? current.ApiKey
        : env.TryGetValue("ANTHROPIC_API_KEY", out v) ? v : env.TryGetValue("ANTHROPIC_AUTH_TOKEN", out v) && v != "ollama" ? v : "";
      model.Text = !string.IsNullOrEmpty(current.Model) ? current.Model : ClaudeProbe.EffectiveModel(current) ?? "";
      workspace.Text = !string.IsNullOrEmpty(current.Workspace) ? current.Workspace
        : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-out");
      timeout.Text = Math.Max(1, (int)Math.Round(current.TimeoutSec / 60.0)).ToString();
      claudeCmd.Text = string.IsNullOrEmpty(current.ClaudeCmd) ? "claude.cmd" : current.ClaudeCmd;
      pin.IsChecked = pinned;
      SetThemeMode(themeMode);
      testResult.Text = "";
      provider = null;
      SetProvider(p);
    }

    void SetProvider(string id)
    {
      if (provider == id) return;
      provider = id;
      int i = Array.IndexOf(ProviderIds, id);
      for (int k = 0; k < providerButtons.Length; k++) providerButtons[k].Tag = k == i ? "on" : null;
      providerHint.Text = ProviderHints[Math.Max(0, i)];
      bool claude = id == DavinciConfig.ProviderClaude;
      urlRow.Visibility = claude ? Visibility.Collapsed : Visibility.Visible;
      keyRow.Visibility = id == DavinciConfig.ProviderApi ? Visibility.Visible : Visibility.Collapsed;
      modelPlaceholder.Text = claude ? "по умолчанию" : "имя модели";
      if (id == DavinciConfig.ProviderOllama && url.Text.Trim().Length == 0) url.Text = DavinciConfig.OllamaDefaultUrl;
      testResult.Text = "";
      if (claude) SetChips(new[] { "", "sonnet", "opus", "haiku" });
      else if (id == DavinciConfig.ProviderOllama) LoadOllamaModels();
      else SetChips(new string[0]);
    }

    void SetThemeMode(string mode)
    {
      ThemeMode = Array.IndexOf(ThemeIds, mode) >= 0 ? mode : "system";
      for (int k = 0; k < themeButtons.Length; k++) themeButtons[k].Tag = ThemeIds[k] == ThemeMode ? "on" : null;
    }

    // ---- модели ----

    void SetChips(IList<string> names)
    {
      modelChips.Children.Clear();
      foreach (string n in names)
      {
        string name = n;
        Button chip = new Button { Style = (Style)res["Choice"], Content = Text(name.Length == 0 ? "по умолчанию" : name, 12), Margin = new Thickness(0, 0, 6, 6) };
        chip.Click += delegate { model.Text = name; };
        modelChips.Children.Add(chip);
      }
      MarkChips();
    }

    void MarkChips()
    {
      foreach (UIElement e in modelChips.Children)
      {
        Button b = e as Button;
        TextBlock tb = b == null ? null : b.Content as TextBlock;
        if (tb == null) continue;
        string name = tb.Text == "по умолчанию" ? "" : tb.Text;
        b.Tag = name == model.Text.Trim() ? "on" : null;
      }
    }

    // Модели с сервера Ollama (локальные и добавленные облачные) — в фоне.
    void LoadOllamaModels()
    {
      string baseUrl = url.Text.Trim();
      int request = ++chipsRequest;
      SetChips(new string[0]);
      System.Threading.ThreadPool.QueueUserWorkItem(delegate
      {
        List<string> names = new List<string>();
        try
        {
          HttpWebRequest req = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + "/api/tags");
          req.Timeout = 3000;
          using (WebResponse resp = req.GetResponse())
          using (System.IO.StreamReader r = new System.IO.StreamReader(resp.GetResponseStream()))
          {
            var d = Json.Parse(r.ReadToEnd()) as Dictionary<string, object>;
            object models;
            if (d != null && d.TryGetValue("models", out models) && models is List<object>)
              foreach (object m in (List<object>)models)
              {
                var md = m as Dictionary<string, object>;
                object n;
                if (md != null && md.TryGetValue("name", out n)) names.Add(Convert.ToString(n));
              }
          }
        }
        catch { }
        Dispatcher.BeginInvoke((Action)delegate { if (request == chipsRequest && provider == DavinciConfig.ProviderOllama) SetChips(names); });
      });
    }

    // ---- проверка и сохранение ----

    // Настройки из полей; null и текст ошибки — если поля заполнены неверно.
    DavinciConfig Build(out string error)
    {
      error = null;
      DavinciConfig c = cfg.Clone();
      c.Provider = provider;
      c.BaseUrl = provider == DavinciConfig.ProviderClaude ? "" : url.Text.Trim();
      c.ApiKey = provider == DavinciConfig.ProviderApi ? key.Password.Trim() : "";
      c.Model = model.Text.Trim();
      c.Workspace = workspace.Text.Trim();
      c.ClaudeCmd = claudeCmd.Text.Trim().Length > 0 ? claudeCmd.Text.Trim() : "claude.cmd";
      int minutes;
      if (!int.TryParse(timeout.Text.Trim(), out minutes) || minutes < 1 || minutes > 600)
        error = "Лимит времени — целое число минут от 1 до 600";
      else c.TimeoutSec = minutes * 60;
      if (provider != DavinciConfig.ProviderClaude && c.BaseUrl.Length == 0) error = "Укажите адрес сервера";
      else if (provider != DavinciConfig.ProviderClaude && c.Model.Length == 0) error = "Укажите модель";
      return error == null ? c : null;
    }

    void Test()
    {
      string error;
      DavinciConfig c = Build(out error);
      if (c == null) { ShowResult(false, error); return; }
      testButton.IsEnabled = false;
      testResult.Foreground = theme.Muted;
      testResult.Text = "Проверяю…";
      System.Threading.ThreadPool.QueueUserWorkItem(delegate
      {
        ServiceState s;
        try { s = ClaudeProbe.TestNow(c); }
        catch (Exception e) { s = new ServiceState(false, e.Message, null); }
        Dispatcher.BeginInvoke((Action)delegate
        {
          testButton.IsEnabled = true;
          ShowResult(s.Ok, s.Text);
          testResult.ToolTip = s.Detail;
        });
      });
    }

    void ShowResult(bool ok, string text)
    {
      testResult.Foreground = ok ? theme.Ok : theme.Error;
      testResult.Text = text;
    }

    void Save()
    {
      string error;
      DavinciConfig c = Build(out error);
      if (c == null) { ShowResult(false, error); return; }
      Result = c;
      closed(true);
    }

    // ---- разметка ----

    TextBlock Section(string text)
    {
      return new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = theme.Muted, Margin = new Thickness(0, 18, 0, 2) };
    }

    TextBlock Label(string text)
    {
      return new TextBlock { Text = text, FontSize = 12.5, Foreground = theme.Text, Margin = new Thickness(0, 10, 0, 5) };
    }

    FrameworkElement Row(string label, Control field)
    {
      StackPanel p = new StackPanel();
      p.Children.Add(Label(label));
      p.Children.Add(field);
      return p;
    }

    TextBox Field()
    {
      return new TextBox { Style = (Style)res["Field"] };
    }

    TextBlock Text(string text, double size = 13)
    {
      return new TextBlock { Text = text, FontSize = size, Foreground = theme.Text };
    }

    WrapPanel Choices(string[] names, Button[] buttons, Action<int> pick)
    {
      WrapPanel p = new WrapPanel();
      for (int i = 0; i < names.Length; i++)
      {
        int k = i;
        buttons[i] = new Button { Style = (Style)res["Choice"], Content = Text(names[i]), Margin = new Thickness(0, 0, 6, 6) };
        buttons[i].Click += delegate { pick(k); };
        p.Children.Add(buttons[i]);
      }
      return p;
    }
  }
}
