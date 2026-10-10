// Конфиг Давинчи: davinci.json рядом с exe (или в davinci\ рядом с exe). Нет файла — значения
// по умолчанию. Источник нейросети (provider): claude — вход пользователя в Claude Code; ollama —
// локальный сервер Ollama; api — другой Anthropic-совместимый API. Из него собирается окружение
// claude (EffectiveEnv); env — дополнительные переменные поверх (прокси и т. п.). Старые конфиги
// без provider определяются по ANTHROPIC_BASE_URL в env. Save сохраняет неизвестные ключи файла.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KompasMcp.JsonLib;

namespace KompasMcp.Davinci
{
  public class DavinciConfig
  {
    public const string ProviderClaude = "claude", ProviderOllama = "ollama", ProviderApi = "api";
    public const string OllamaDefaultUrl = "http://127.0.0.1:11434";

    public string KompasMcpExe;   // путь к KompasMcp.exe (команда mcp-config)
    public string Provider = "";  // "" — определить по env (старые конфиги)
    public string BaseUrl;        // ollama / api
    public string ApiKey;         // ollama (по умолчанию "ollama") / api
    public string Model;          // "" = модель по умолчанию
    public string Workspace;      // рабочий каталог claude (mcp-out)
    public int TimeoutSec = 900;
    public string ClaudeCmd = "claude.cmd";
    // Дополнительное окружение для claude. Нужно, потому что панельный процесс
    // наследует окружение КОМПАСа, а не терминала пользователя.
    public Dictionary<string, string> Env = new Dictionary<string, string>();
    public string Path;           // файл, из которого загружен (и куда сохраняется)
    Dictionary<string, object> raw = new Dictionary<string, object>();

    // Переменные, которыми управляет provider: при явном источнике в env их быть не должно.
    static readonly string[] Managed = { "ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_MODEL",
      "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL" };

    // Источник с учётом старых конфигов: адрес API в env — ollama или сторонний API.
    public string ResolvedProvider
    {
      get
      {
        if (!string.IsNullOrEmpty(Provider)) return Provider;
        string url;
        if (!Env.TryGetValue("ANTHROPIC_BASE_URL", out url) || string.IsNullOrEmpty(url)) return ProviderClaude;
        return IsOllamaUrl(url) ? ProviderOllama : ProviderApi;
      }
    }

    public static bool IsOllamaUrl(string url)
    {
      return url.Contains(":11434") || url.ToLowerInvariant().Contains("ollama");
    }

    // Окружение claude: переменные источника, поверх — env.
    public Dictionary<string, string> EffectiveEnv()
    {
      Dictionary<string, string> e = new Dictionary<string, string>();
      if (!string.IsNullOrEmpty(Provider) && Provider != ProviderClaude)
      {
        bool ollama = Provider == ProviderOllama;
        string url = string.IsNullOrEmpty(BaseUrl) && ollama ? OllamaDefaultUrl : BaseUrl;
        if (!string.IsNullOrEmpty(url)) e["ANTHROPIC_BASE_URL"] = url;
        string key = string.IsNullOrEmpty(ApiKey) && ollama ? "ollama" : ApiKey;
        // ключ Anthropic — x-api-key; у прокси и Ollama — Bearer-токен
        if (!string.IsNullOrEmpty(key)) e[key.StartsWith("sk-ant-") ? "ANTHROPIC_API_KEY" : "ANTHROPIC_AUTH_TOKEN"] = key;
        // фоновые вызовы claude идут к «haiku»/«sonnet» — на стороннем сервере это та же модель
        if (!string.IsNullOrEmpty(Model))
          foreach (string n in new[] { "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL" })
            e[n] = Model;
      }
      foreach (KeyValuePair<string, string> kv in Env) e[kv.Key] = kv.Value;
      return e;
    }

    public DavinciConfig Clone()
    {
      DavinciConfig c = (DavinciConfig)MemberwiseClone();
      c.Env = new Dictionary<string, string>(Env);
      c.raw = new Dictionary<string, object>(raw);
      return c;
    }

    // davinci.json рядом с exe, иначе в папке davinci\ рядом с exe (туда же сохраняется новый)
    public static DavinciConfig Load()
    {
      string exeDir = AppDomain.CurrentDomain.BaseDirectory;
      string path = System.IO.Path.Combine(exeDir, "davinci.json");
      string alt = System.IO.Path.Combine(System.IO.Path.Combine(exeDir, "davinci"), "davinci.json");
      if (!File.Exists(path) && (File.Exists(alt) || Directory.Exists(System.IO.Path.GetDirectoryName(alt)))) path = alt;
      return Load(path);
    }

    public static DavinciConfig Load(string path)
    {
      var cfg = new DavinciConfig();
      cfg.Path = path;
      if (!File.Exists(path))
      {
        Log.Write("config: davinci.json не найден, значения по умолчанию");
        return cfg;
      }
      try
      {
        var d = Json.Parse(File.ReadAllText(path, new UTF8Encoding(false))) as Dictionary<string, object>;
        if (d == null) { Log.Write("config: davinci.json не объект, значения по умолчанию"); return cfg; }
        cfg.raw = d;
        object v;
        if (d.TryGetValue("kompasMcpExe", out v)) cfg.KompasMcpExe = Convert.ToString(v);
        if (d.TryGetValue("provider", out v)) cfg.Provider = Convert.ToString(v);
        if (d.TryGetValue("baseUrl", out v)) cfg.BaseUrl = Convert.ToString(v);
        if (d.TryGetValue("apiKey", out v)) cfg.ApiKey = Convert.ToString(v);
        if (d.TryGetValue("model", out v)) cfg.Model = Convert.ToString(v);
        if (d.TryGetValue("workspace", out v)) cfg.Workspace = Convert.ToString(v);
        if (d.TryGetValue("timeoutSec", out v)) cfg.TimeoutSec = (int)AsDouble(v, 900);
        if (d.TryGetValue("claudeCmd", out v)) cfg.ClaudeCmd = Convert.ToString(v);
        var env = d.TryGetValue("env", out v) ? v as Dictionary<string, object> : null;
        if (env != null)
          foreach (KeyValuePair<string, object> kv in env)
            cfg.Env[kv.Key] = Convert.ToString(kv.Value);
        Log.Write("config: davinci.json загружен (" + path + ")");
      }
      catch (Exception e)
      {
        Log.Error("config", e);
      }
      return cfg;
    }

    // Запись в Path: известные ключи обновляются, остальные (например, отключённые блоки)
    // остаются как были. При явном источнике управляемые им переменные из env убираются.
    public void Save()
    {
      if (!string.IsNullOrEmpty(Provider))
        foreach (string n in Managed) Env.Remove(n);
      Dictionary<string, object> d = new Dictionary<string, object>(raw);
      d["kompasMcpExe"] = KompasMcpExe ?? "";
      d["provider"] = Provider ?? "";
      d["baseUrl"] = BaseUrl ?? "";
      d["apiKey"] = ApiKey ?? "";
      d["model"] = Model ?? "";
      d["workspace"] = Workspace ?? "";
      d["timeoutSec"] = (double)TimeoutSec;
      d["claudeCmd"] = ClaudeCmd ?? "claude.cmd";
      Dictionary<string, object> env = new Dictionary<string, object>();
      foreach (KeyValuePair<string, string> kv in Env) env[kv.Key] = kv.Value;
      d["env"] = env;
      raw = d;
      string dir = System.IO.Path.GetDirectoryName(Path);
      if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
      StringBuilder sb = new StringBuilder();
      Pretty(sb, d, "");
      sb.Append('\n');
      File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
      Log.Write("config: davinci.json сохранён (" + Path + ")");
    }

    // JSON с отступами (файл правят руками): объекты построчно, остальное — Json.Write.
    static void Pretty(StringBuilder sb, object v, string indent)
    {
      var obj = v as Dictionary<string, object>;
      if (obj == null) { sb.Append(Json.Write(v)); return; }
      if (obj.Count == 0) { sb.Append("{}"); return; }
      sb.Append("{\n");
      int i = 0;
      foreach (KeyValuePair<string, object> kv in obj)
      {
        sb.Append(indent).Append("  ").Append(Json.Write(kv.Key)).Append(": ");
        Pretty(sb, kv.Value, indent + "  ");
        sb.Append(++i < obj.Count ? ",\n" : "\n");
      }
      sb.Append(indent).Append('}');
    }

    static double AsDouble(object v, double def)
    {
      if (v is double) return (double)v;
      double d;
      if (v != null && double.TryParse(Convert.ToString(v).Replace(',', '.'),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
        return d;
      return def;
    }
  }
}
