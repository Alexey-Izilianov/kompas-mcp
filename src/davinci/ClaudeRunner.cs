// Спавн claude -p (stream-json) и разбор потока. Сообщение — через stdin
// (не argv: кириллица/кавычки). Стоп — taskkill /T /F (Process.Kill(true)
// в .NET 4 нет; /T обязательно — иначе останется сирота kompas-mcp).
// Форма флагов верифицирована Этапом 0 (memory: davinci-kompas-copilot).
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace KompasMcp.Davinci
{
  public class ClaudeRunnerOptions
  {
    public string ClaudeCmd = "claude.cmd";  // резолвится через PATH (cmd /c)
    public string McpConfigPath;             // обязателен
    public string Workspace;                 // рабочий каталог сессии
    public string SystemPrompt;              // --append-system-prompt (Этап 5)
    public string AllowedTools = "mcp__kompas-mcp";
    public bool Restricted = true;
    public int TimeoutSec;                   // 0 = без таймаута (стоп только вручную)
  }

  public class ClaudeRunner
  {
    readonly object gate = new object();
    Process proc;
    Timer watchdog;
    bool exitFired;

    public ClaudeRunnerOptions Options;
    public string SessionId { get; private set; }
    public bool Running { get { lock (gate) { return proc != null && !proc.HasExited; } } }
    public int Pid { get { lock (gate) { return proc == null ? 0 : proc.Id; } } }

    // Обработчики вызываются из фоновых потоков чтения — UI-поток
    // синхронизирует сам (Control.BeginInvoke в панели, Этап 4).
    public event Action<StreamEvent> OnEvent;
    public event Action<string> OnStderr;
    public event Action<Exception> OnError;
    public event Action OnExit;

    public void Run(string message, string resumeSessionId)
    {
      lock (gate)
      {
        if (proc != null && !proc.HasExited)
          throw new InvalidOperationException("Runner уже работает");
      }
      exitFired = false;
      if (!string.IsNullOrEmpty(resumeSessionId)) SessionId = resumeSessionId;
      ClaudeRunnerOptions o = Options ?? new ClaudeRunnerOptions();
      if (string.IsNullOrEmpty(o.McpConfigPath))
        throw new InvalidOperationException("ClaudeRunnerOptions.McpConfigPath не задан");

      var psi = new ProcessStartInfo();
      psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
      // cmd /s /c "..." : крайние кавычки снимаются целиком, внутренние сохраняются.
      psi.Arguments = "/d /s /c \"\"" + o.ClaudeCmd + "\" " + BuildArgs(o, resumeSessionId) + "\"";
      psi.UseShellExecute = false;
      psi.RedirectStandardInput = true;
      psi.RedirectStandardOutput = true;
      psi.RedirectStandardError = true;
      psi.CreateNoWindow = true;
      psi.StandardOutputEncoding = new System.Text.UTF8Encoding(false);
      psi.StandardErrorEncoding = new UTF8Encoding(false);
      if (!string.IsNullOrEmpty(o.Workspace)) psi.WorkingDirectory = o.Workspace;

      Log.Write("runner: spawn " + psi.Arguments);
      lock (gate) proc = Process.Start(psi);
      new Thread(WriteStdin) { IsBackground = true }.Start(message);
      new Thread(ReadStdout) { IsBackground = true }.Start();
      new Thread(ReadStderr) { IsBackground = true }.Start();
      if (o.TimeoutSec > 0)
        watchdog = new Timer(delegate { Stop(); }, null, o.TimeoutSec * 1000, Timeout.Infinite);
    }

    string BuildArgs(ClaudeRunnerOptions o, string resumeSessionId)
    {
      var b = new System.Text.StringBuilder();
      b.Append("--verbose --output-format stream-json --strict-mcp-config");
      b.Append(" --mcp-config \"").Append(o.McpConfigPath).Append("\"");
      if (!string.IsNullOrEmpty(o.AllowedTools))
        b.Append(" --allowedTools \"").Append(o.AllowedTools).Append("\"");
      b.Append(" --permission-prompts none");
      if (o.Restricted) b.Append(" --restricted");
      if (!string.IsNullOrEmpty(o.SystemPrompt))
        b.Append(" --append-system-prompt \"").Append(o.SystemPrompt.Replace("\"", "\\\"")).Append("\"");
      if (!string.IsNullOrEmpty(resumeSessionId))
        b.Append(" --resume ").Append(resumeSessionId);
      return b.ToString();
    }

    void WriteStdin(object arg)
    {
      try
      {
        string message = (string)arg;
        proc.StandardInput.Write(message);
        proc.StandardInput.Flush();
      }
      catch (Exception e) { Log.Error("runner stdin", e); FireError(e); }
      finally { try { proc.StandardInput.Close(); } catch { } }
    }

    void ReadStdout()
    {
      try
      {
        string line;
        while ((line = proc.StandardOutput.ReadLine()) != null)
        {
          Log.Write("runner: event " + (line.Length > 300 ? line.Substring(0, 300) : line));
          StreamEvent ev = StreamEvent.Parse(line);
          if (ev == null) continue;
          if (ev.SessionId != null) SessionId = ev.SessionId;
          if (OnEvent != null) OnEvent(ev);
        }
      }
      catch (Exception e) { if (!exitFired) { Log.Error("runner stdout", e); FireError(e); } }
      FireExit();
    }

    void ReadStderr()
    {
      try
      {
        string line;
        while ((line = proc.StandardError.ReadLine()) != null)
        {
          if (line.Length > 0)
          {
            Log.Write("runner: stderr " + line);
            if (OnStderr != null) OnStderr(line);
          }
        }
      }
      catch { }
    }

    // taskkill /T /F: убивает дерево claude + kompas-mcp; КОМПАС юзера не в этом дереве.
    public void Stop()
    {
      int p = Pid;
      if (p == 0) return;
      Log.Write("runner: stop, pid=" + p);
      try
      {
        var psi = new ProcessStartInfo("taskkill", "/PID " + p + " /T /F");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        Process.Start(psi).WaitForExit(5000);
      }
      catch (Exception e) { Log.Error("taskkill", e); FireError(e); }
    }

    public bool WaitForExit(int ms)
    {
      lock (gate) return proc == null || proc.WaitForExit(ms);
    }

    void FireError(Exception e)
    {
      if (exitFired) return;
      if (OnError != null) { try { OnError(e); } catch { } }
    }

    void FireExit()
    {
      lock (gate)
      {
        if (exitFired) return;
        exitFired = true;
      }
      if (watchdog != null) { try { watchdog.Dispose(); } catch { } watchdog = null; }
      Log.Write("runner: exit, session=" + SessionId);
      if (OnExit != null) { try { OnExit(); } catch (Exception e) { Log.Error("OnExit handler", e); } }
    }
  }
}