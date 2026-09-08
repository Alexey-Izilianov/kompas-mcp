# kompas-mcp — MCP-сервер автоматизации КОМПАС-3D

MCP-сервер (stdio, построчный JSON-RPC) с семантическими инструментами поверх
проверенных идиом API-5/API-7 КОМПАС-3D v22. Без внешних зависимостей: чистый C#
(сборка через csc.exe из .NET Framework 4). Позволяет любому MCP-клиенту
(Claude Code, Claude Desktop и др.) строить параметрические детали и чертежи
по ЕСКД одной фразой.

## Возможности (48 инструментов)

- **Инфраструктура**: `ping`, `kompas_status`, `kompas_start`, `kompas_show`,
  `kompas_stop`, `list_open_documents`, `kill_kompas`
- **2D-чертежи**: форматы A0–A4, виды, линии/дуги/окружности, размеры
  (линейные/диаметральные/радиальные/угловые), штриховка, шероховатость,
  основная надпись (штамп), техтребования, экспорт в PNG
- **3D-детали**: эскизы на плоскостях (в т.ч. смещённых), выдавливание/вырезы,
  вращения, отверстия, фаски/скругления, массивы, массовые характеристики,
  рендер в PNG (работает даже в невидимом режиме)
- **Расчёты**: библиотека материалов, допуски и посадки ЕСДП (ГОСТ 25346/25347),
  моменты инерции сечений
- **Стандартные изделия** (генераторы по таблицам ГОСТ): болты ГОСТ 7805,
  гайки ГОСТ 5915, шайбы ГОСТ 11371, шариковые подшипники ГОСТ 8338
- **Зубчатые передачи**: эвольвентная геометрия ГОСТ 16532 (`gear_calc`,
  `gear_wheel` — 3D-колесо с эвольвентным профилем)
- **Пружины**: расчёт по ГОСТ 13765 (`spring_calc`), 3D-модель через спираль +
  кинематическое выдавливание (`spring_create`)

## Сборка

Требуется установленный КОМПАС-3D v22 (SDK не обязателен — нужны только
interop-DLL) и .NET Framework 4 (csc.exe входит в Windows).

1. Скопируйте interop-DLL из поставки КОМПАС (`Kompas6API5.dll`, `KompasAPI7.dll`,
   `Kompas6Constants.dll`, `Kompas6Constants3D.dll`, `KAPITypes.dll`) в папку
   `lib\` рядом с `build.ps1`.
2. Сборка:
   ```
   powershell -ExecutionPolicy Bypass -File build.ps1    # → KompasMcp.exe
   ```

## Подключение к MCP-клиенту

Claude Code:
```
claude mcp add kompas -- <путь>\KompasMcp.exe
```

Claude Desktop (`claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "kompas": {
      "command": "C:\\путь\\KompasMcp.exe"
    }
  }
}
```

## Тест протокола (без клиента)

```
powershell -ExecutionPolicy Bypass -File tests\protocol_test.ps1
```

В `tests\` лежат smoke-сценарии (построчный JSON-RPC через stdin):
`smoke_flange.jsonl` — чертёж фланца, `smoke_3d.jsonl` — 3D-детали,
`smoke_std.jsonl` — крепёж и подшипник, `smoke_gear.jsonl` — зубчатые колёса,
`smoke_spring.jsonl` — пружина. Запуск: `KompasMcp.exe < tests\smoke_3d.jsonl`.

## Жизненный цикл КОМПАСа

- MCP держит СВОЙ экземпляр (`KOMPAS.Application.5`); каждый
  `Activator.CreateInstance` поднимает новый процесс KOMPAS.Exe, поэтому PID
  фиксируется дифференциальным снапшотом процессов.
- `kompas_start` (visible=false по умолчанию) → инструменты → `kompas_stop`.
- Чужие запущенные экземпляры КОМПАСа не затрагиваются.

## Журнал

`kompas-mcp.log` рядом с exe — все вызовы и ошибки (stdout занят протоколом).
Артефакты по умолчанию пишутся в `mcp-out\` рядом с exe.