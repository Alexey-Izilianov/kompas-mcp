// Состояние Claude для индикатора панели: найден ли Claude Code, отвечает ли API, какая модель.
// Окружение — как его получит claude (ClaudeRunner): env из davinci.json, затем переменные
// пользователя и машины из реестра, затем окружение процесса.
//  - быстрая проверка: claude в PATH + адрес API отвечает по HTTP (любой ответ = связь есть);
//  - полная (deep): для стороннего ANTHROPIC_BASE_URL — запрос на 1 токен к /v1/messages:
//    так видны исчерпанный лимит, неизвестная модель, неверный ключ. Для своего входа Claude Code
//    (подписка) прямой запрос невозможен — только быстрая проверка.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using KompasMcp.JsonLib;
using KompasMcp.Ui;
using Microsoft.Win32;

namespace KompasMcp.Davinci
{
  public static class ClaudeProbe
  {
    const string DefaultApi = "https://api.anthropic.com";

    // .NET Framework по умолчанию может предложить серверу устаревший TLS — включаем 1.2
    static ClaudeProbe()
    {
      try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
    }

    public static ServiceState Check(DavinciConfig cfg, bool deep)
    {
      string cli = FindCli(cfg.ClaudeCmd);
      if (cli == null)
        return new ServiceState(false, "Claude Code не найден (" + cfg.ClaudeCmd + ")",
          "Установите Claude Code (npm i -g @anthropic-ai/claude-code) или укажите claudeCmd в davinci.json", true);

      string baseUrl = Env(cfg, "ANTHROPIC_BASE_URL");
      bool custom = !string.IsNullOrEmpty(baseUrl);
      if (!custom) baseUrl = DefaultApi;
      string where = Where(baseUrl);
      string model = EffectiveModel(cfg);
      string label = model ?? "модель по умолчанию";
      string detail = "claude: " + cli + "\nAPI: " + baseUrl + "\nмодель: " + label;

      string reach = Reach(baseUrl);
      if (reach != null)
        return new ServiceState(false, "Claude: нет связи с " + where + (DavinciConfig.IsOllamaUrl(baseUrl) ? " — запустите Ollama" : ""),
          detail + "\n" + reach, true);

      if (deep && custom)
      {
        string err = TinyRequest(baseUrl, Env(cfg, "ANTHROPIC_AUTH_TOKEN") ?? Env(cfg, "ANTHROPIC_API_KEY"), model);
        if (err != null) return new ServiceState(false, "Claude: " + Friendly(err), detail + "\n" + err);
      }
      return new ServiceState(true, "Claude готов · " + label + (custom ? " (" + where + ")" : ""), detail);
    }

    // Модель для --model. Со сторонним API алиасы Claude Code не годятся: он дописывает к ним
    // суффиксы вроде [1m], и сервер отвечает «invalid model name» — берём имя модели явно.
    public static string EffectiveModel(DavinciConfig cfg)
    {
      if (!string.IsNullOrEmpty(cfg.Model)) return cfg.Model;
      if (string.IsNullOrEmpty(Env(cfg, "ANTHROPIC_BASE_URL"))) return null;
      foreach (string name in new[] { "ANTHROPIC_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL",
                                      "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL" })
      {
        string v = Env(cfg, name);
        if (!string.IsNullOrEmpty(v)) return v;
      }
      return null;
    }

    // Проверка «по-настоящему» для кнопки «Проверить» в настройках: при стороннем API — запрос
    // на 1 токен; при входе Claude Code — короткий `claude -p` (вход, модель, связь).
    public static ServiceState TestNow(DavinciConfig cfg)
    {
      ServiceState s = Check(cfg, true);
      if (!s.Ok || !string.IsNullOrEmpty(Env(cfg, "ANTHROPIC_BASE_URL"))) return s;
      string err = RunCli(cfg);
      return err == null ? new ServiceState(true, "Claude отвечает · " + (EffectiveModel(cfg) ?? "модель по умолчанию"), s.Detail)
                         : new ServiceState(false, "Claude: " + Friendly(err), s.Detail + "\n" + err);
    }

    static string RunCli(DavinciConfig cfg)
    {
      try
      {
        string model = EffectiveModel(cfg);
        var psi = new ProcessStartInfo
        {
          FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
          Arguments = "/d /s /c \"\"" + cfg.ClaudeCmd + "\" -p \"Ответь одним словом: ок\" --output-format text"
            + (string.IsNullOrEmpty(model) ? "" : " --model \"" + model + "\"") + "\"",
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          RedirectStandardInput = true,
          StandardOutputEncoding = Encoding.UTF8,
          StandardErrorEncoding = Encoding.UTF8,
          WorkingDirectory = Path.GetTempPath(),
        };
        ClaudeRunner.MergeUserEnvironment(psi);
        foreach (var kv in cfg.EffectiveEnv()) psi.EnvironmentVariables[kv.Key] = kv.Value;
        using (var p = Process.Start(psi))
        {
          p.StandardInput.Close();
          var stderr = p.StandardError.ReadToEndAsync();
          var stdout = p.StandardOutput.ReadToEndAsync();
          if (!p.WaitForExit(90000))
          {
            try { Process.Start("taskkill", "/T /F /PID " + p.Id).WaitForExit(5000); } catch { }
            return "нет ответа за 90 с";
          }
          string output = (stdout.Result ?? "").Trim();
          if (p.ExitCode == 0 && output.Length > 0 && !output.StartsWith("API Error")) return null;
          string err = (stderr.Result ?? "").Trim();
          return output.Length > 0 ? output : err.Length > 0 ? err : "код выхода " + p.ExitCode;
        }
      }
      catch (Exception e) { return e.Message; }
    }

    // Понятная причина по тексту ошибки API или claude.
    public static string Friendly(string error)
    {
      string e = (error ?? "").ToLowerInvariant();
      if (e.Contains("rate_limit") || e.Contains("usage") || e.Contains("429")) return "лимит использования модели исчерпан";
      if (e.Contains("invalid model") || e.Contains("model not found") || e.Contains("not_found")) return "неизвестная модель";
      if (e.Contains("authentication") || e.Contains("401") || e.Contains("api key") || e.Contains("not logged in")) return "нет входа или неверный ключ";
      if (e.Contains("overloaded") || e.Contains("529")) return "сервер перегружен";
      if (e.Contains("connection") || e.Contains("econnrefused") || e.Contains("timed out") || e.Contains("timeout")) return "нет связи с API";
      string first = (error ?? "").Trim();
      return first.Length > 80 ? first.Substring(0, 80) + "…" : first;
    }

    // ---- окружение ----

    static string Env(DavinciConfig cfg, string name)
    {
      string v;
      if (cfg.EffectiveEnv().TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
      v = RegEnv(Registry.CurrentUser, "Environment", name);
      if (!string.IsNullOrEmpty(v)) return v;
      v = RegEnv(Registry.LocalMachine, MachineEnv, name);
      if (!string.IsNullOrEmpty(v)) return v;
      v = Environment.GetEnvironmentVariable(name);
      return string.IsNullOrEmpty(v) ? null : v;
    }

    const string MachineEnv = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    static string RegEnv(RegistryKey hive, string path, string name)
    {
      try
      {
        using (RegistryKey k = hive.OpenSubKey(path))
        {
          object v = k == null ? null : k.GetValue(name);
          return v == null ? null : Environment.ExpandEnvironmentVariables(Convert.ToString(v));
        }
      }
      catch { return null; }
    }

    // claude.cmd в PATH процесса, пользователя и машины (как увидит cmd /c у ClaudeRunner).
    static string FindCli(string cmd)
    {
      if (string.IsNullOrEmpty(cmd)) return null;
      if (Path.IsPathRooted(cmd)) return File.Exists(cmd) ? cmd : null;
      string path = Environment.GetEnvironmentVariable("PATH") + ";"
        + RegEnv(Registry.CurrentUser, "Environment", "PATH") + ";"
        + RegEnv(Registry.LocalMachine, MachineEnv, "PATH");
      string[] names = Path.HasExtension(cmd) ? new[] { cmd } : new[] { cmd + ".cmd", cmd + ".exe", cmd };
      foreach (string dir in path.Split(';'))
      {
        if (dir.Trim().Length == 0) continue;
        foreach (string n in names)
        {
          try
          {
            string p = Path.Combine(dir.Trim(), n);
            if (File.Exists(p)) return p;
          }
          catch { }
        }
      }
      return null;
    }

    // ---- сеть ----

    static string Where(string url)
    {
      try { Uri u = new Uri(url); return u.IsDefaultPort ? u.Host : u.Host + ":" + u.Port; }
      catch { return url; }
    }

    // null — сервер ответил (любым HTTP-статусом); иначе причина.
    static string Reach(string url)
    {
      try
      {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "GET";
        req.Timeout = 3000;
        req.ReadWriteTimeout = 3000;
        req.Proxy = WebRequest.GetSystemWebProxy();
        using (req.GetResponse()) { }
        return null;
      }
      catch (WebException e)
      {
        if (e.Response != null) { e.Response.Close(); return null; }
        return e.Message;
      }
      catch (Exception e) { return e.Message; }
    }

    // Запрос на 1 токен к Anthropic-совместимому API. null — модель ответила, иначе текст ошибки.
    static string TinyRequest(string baseUrl, string token, string model)
    {
      if (model == null) return null; // модель неизвестна — проверять нечего
      try
      {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + "/v1/messages");
        req.Method = "POST";
        req.Timeout = 30000;
        req.ReadWriteTimeout = 30000;
        req.ContentType = "application/json";
        req.Headers["anthropic-version"] = "2023-06-01";
        if (!string.IsNullOrEmpty(token)) { req.Headers["x-api-key"] = token; req.Headers["Authorization"] = "Bearer " + token; }
        byte[] body = Encoding.UTF8.GetBytes("{\"model\":" + Json.Write(model) +
          ",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}]}");
        req.ContentLength = body.Length;
        using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
        using (req.GetResponse()) { }
        return null;
      }
      catch (WebException e)
      {
        if (e.Response == null) return e.Message;
        using (StreamReader r = new StreamReader(e.Response.GetResponseStream(), Encoding.UTF8))
        {
          string text = r.ReadToEnd();
          try
          {
            var d = Json.Parse(text) as Dictionary<string, object>;
            object err;
            var ed = d != null && d.TryGetValue("error", out err) ? err as Dictionary<string, object> : null;
            if (ed != null) return Field(ed, "type") + ": " + Field(ed, "message");
          }
          catch { }
          return ((int)((HttpWebResponse)e.Response).StatusCode) + " " + text;
        }
      }
      catch (Exception e) { return e.Message; }
    }

    static string Field(Dictionary<string, object> d, string key)
    {
      return d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
    }
  }
}
