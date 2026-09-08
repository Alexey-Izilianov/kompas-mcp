using System;
using System.Collections.Generic;

namespace KompasMcp.Tools
{
  // Инфраструктурные tools: жизненный цикл КОМПАСа и диагностика.
  public static class InfraTools
  {
    public static void Register()
    {
      ToolRegistry.Add("ping",
        "Проверка связи с сервером. Не обращается к КОМПАСу.",
        "{}",
        a => new Dictionary<string, object> { { "ok", true }, { "server", "kompas-mcp" } });

      ToolRegistry.Add("kompas_status",
        "Статус экземпляра КОМПАС, удерживаемого MCP-сервером: запущен ли, PID, видимость, открытые документы. Не запускает КОМПАС.",
        "{}",
        a => KompasHost.Status());

      ToolRegistry.Add("kompas_start",
        "Запустить экземпляр КОМПАС-3D (MCP держит свой процесс). visible=false по умолчанию — окно скрыто, автоматизация работает. Повторный вызов безопасен.",
        @"{""type"":""object"",""properties"":{""visible"":{""type"":""boolean"",""description"":""Показать окно КОМПАСа (по умолчанию false)""}}}",
        a =>
        {
          KompasHost.Start(ToolRegistry.GetBool(a, "visible", false));
          return KompasHost.Status();
        });

      ToolRegistry.Add("kompas_show",
        "Показать/скрыть окно КОМПАСа, запущенного MCP-сервером.",
        @"{""type"":""object"",""properties"":{""visible"":{""type"":""boolean"",""description"":""true = показать, false = скрыть""}},""required"":[""visible""]}",
        a =>
        {
          if (!KompasHost.IsRunning) throw new ToolException("КОМПАС не запущен (kompas_start)");
          KompasHost.Show(ToolRegistry.GetBool(a, "visible", true));
          return KompasHost.Status();
        });

      ToolRegistry.Add("kompas_stop",
        "Полностью закрыть экземпляр КОМПАС, запущенный MCP-сервером (несохранённые изменения теряются). Чужие экземпляры КОМПАСа не трогаются.",
        "{}",
        a =>
        {
          KompasHost.Stop();
          return new Dictionary<string, object> { { "stopped", true } };
        });

      ToolRegistry.Add("list_open_documents",
        "Список открытых документов в экземпляре КОМПАС, удерживаемом MCP (имя и путь).",
        "{}",
        a =>
        {
          var st = KompasHost.Status();
          return new Dictionary<string, object> { { "documents", st["documents"] } };
        });
    }
  }
}