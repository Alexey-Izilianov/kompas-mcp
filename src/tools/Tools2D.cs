using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using KAPITypes;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // 2D-чертёжный домен. Идиомы — из проверенного flange_draw.cs:
  //  - lt_DocSheetStandart + ksSheetPar(shtType=1, layoutName="") + ksStandartSheet(direct=true = горизонтально);
  //    макет «Первый лист. Форма 1» с рамкой+штампом создаётся автоматически;
  //  - ksCreateSheetView(x=0,y=0) → координаты геометрии = мм листа;
  //  - размеры: ksLDimParam/ksRDimParam, tPar.Init(false)+_AUTONOMINAL, sign=1 = ⌀,
  //    префикс через _PREFIX + строка в GetTextArr() (в одну строку со значением);
  //  - диаметральный размер = ksDiamDimension(ksRDimParam!), НЕ ksADimParam;
  //  - штриховка ksHatch: контур чертится дважды (до блока style 1 и внутри блока style 2), затем ksEndObj;
  //    контур обязан быть замкнут;
  //  - штамп через API-7 IStamp.Text[ksStampEnum], затем stamp.Update().
  public static class Tools2D
  {
    static ksDocument2D doc;        // текущий чертёж
    static string docPath;          // куда сохранён
    static int viewNum;             // номер листового вида

    // Сброс при Stop/Detach: поля держат COM-ссылки на закрытый документ.
    public static void Reset()
    {
      doc = null;
      docPath = null;
      viewNum = 0;
    }

    // ---- реестр ----

    public static void Register()
    {
      ToolRegistry.Add("create_drawing",
        "Создать чертёж (лист стандартного формата; рамка и штамп формы 1 создаются автоматически макетом). Становится активным документом для остальных 2D-tools.",
        @"{""type"":""object"",""properties"":{
""format"":{""type"":""string"",""enum"":[""A4"",""A3"",""A2"",""A1"",""A0""],""description"":""Формат листа (по умолчанию A3)""},
""landscape"":{""type"":""boolean"",""description"":""true = горизонтальный лист (по умолчанию), false = вертикальный""},
""path"":{""type"":""string"",""description"":""Полный путь сохранения .cdw (по умолчанию kompas-test\\mcp-out\\drawing.cdw)""},
""name"":{""type"":""string"",""description"":""Комментарий/имя документа""}}}",
        a => CreateDrawing(a));

      ToolRegistry.Add("create_view",
        "Создать листовой вид (система координат). Объекты рисуются в мм листа относительно вида; масштаб вида применяется к координатам.",
        @"{""type"":""object"",""properties"":{
""x"":{""type"":""number""},""y"":{""type"":""number""},
""scale"":{""type"":""number"",""description"":""По умолчанию 1""},
""angle"":{""type"":""number"",""description"":""По умолчанию 0""},
""name"":{""type"":""string""}}}",
        a =>
        {
          ksDocument2D d = GetDoc();
          KompasObject kompas = KompasHost.Kompas;
          ksViewParam par = (ksViewParam)kompas.GetParamStruct((short)StructType2DEnum.ko_ViewParam);
          par.Init();
          par.x = ToolRegistry.GetDbl(a, "x", 0);
          par.y = ToolRegistry.GetDbl(a, "y", 0);
          par.scale_ = ToolRegistry.GetDbl(a, "scale", 1);
          par.angle = ToolRegistry.GetDbl(a, "angle", 0);
          par.state = (short)ldefin2d.stACTIVE;
          par.name = ToolRegistry.GetStr(a, "name", "Вид");
          int vnum = 0;
          d.ksCreateSheetView(par, ref vnum);
          viewNum = vnum;
          return Id("view", vnum);
        });

      ToolRegistry.Add("line",
        "Отрезок. style: 1=основная, 2=тонкая, 3=осевая.",
        @"{""type"":""object"",""properties"":{
""x1"":{""type"":""number""},""y1"":{""type"":""number""},""x2"":{""type"":""number""},""y2"":{""type"":""number""},
""style"":{""type"":""integer"",""description"":""По умолчанию 1""}},
""required"":[""x1"",""y1"",""x2"",""y2""]}",
        a =>
        {
          int obj = GetDoc().ksLineSeg(ToolRegistry.GetDbl(a, "x1"), ToolRegistry.GetDbl(a, "y1"),
            ToolRegistry.GetDbl(a, "x2"), ToolRegistry.GetDbl(a, "y2"), ToolRegistry.GetInt(a, "style", 1));
          return Id("line", obj);
        });

      ToolRegistry.Add("circle",
        "Окружность.",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},""r"":{""type"":""number""},
""style"":{""type"":""integer"",""description"":""По умолчанию 1""}},
""required"":[""xc"",""yc"",""r""]}",
        a =>
        {
          int obj = GetDoc().ksCircle(ToolRegistry.GetDbl(a, "xc"), ToolRegistry.GetDbl(a, "yc"),
            ToolRegistry.GetDbl(a, "r"), ToolRegistry.GetInt(a, "style", 1));
          return Id("circle", obj);
        });

      ToolRegistry.Add("circle_array",
        "Отверстия по окружности: n отверстий радиуса r на радиусе arrayRadius вокруг (xc,yc), каждое с осевыми перекрестиями (style 3). startAngle — угол первого отверстия в градусах (по умолчанию 90, вверх).",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},
""arrayRadius"":{""type"":""number"",""description"":""Радиус расположения""},
""n"":{""type"":""integer"",""description"":""Число отверстий""},
""r"":{""type"":""number"",""description"":""Радиус отверстия""},
""startAngle"":{""type"":""number"",""description"":""По умолчанию 90""},
""crossArm"":{""type"":""number"",""description"":""Длина оси-креста от центра отверстия (по умолчанию r+1)""}}}",
        a => CircleArray(a));

      ToolRegistry.Add("arc",
        "Дуга: центр, радиус и две точки на концах дуги (x1,y1)→(x2,y2). direction=1 — против часовой, -1 — по часовой.",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},""r"":{""type"":""number""},
""x1"":{""type"":""number""},""y1"":{""type"":""number""},""x2"":{""type"":""number""},""y2"":{""type"":""number""},
""direction"":{""type"":""integer"",""description"":""1=CCW (по умолчанию), -1=CW""},
""style"":{""type"":""integer"",""description"":""По умолчанию 1""}},
""required"":[""xc"",""yc"",""r"",""x1"",""y1"",""x2"",""y2""]}",
        a =>
        {
          int obj = GetDoc().ksArcByPoint(ToolRegistry.GetDbl(a, "xc"), ToolRegistry.GetDbl(a, "yc"),
            ToolRegistry.GetDbl(a, "r"), ToolRegistry.GetDbl(a, "x1"), ToolRegistry.GetDbl(a, "y1"),
            ToolRegistry.GetDbl(a, "x2"), ToolRegistry.GetDbl(a, "y2"),
            (short)ToolRegistry.GetInt(a, "direction", 1), ToolRegistry.GetInt(a, "style", 1));
          return Id("arc", obj);
        });

      ToolRegistry.Add("polyline",
        "Ломаная из точек [[x,y],...] — цепочка отрезков.",
        @"{""type"":""object"",""properties"":{
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}}},
""style"":{""type"":""integer"",""description"":""По умолчанию 1""},
""closed"":{""type"":""boolean"",""description"":""Замкнуть контур (последнюю точку соединить с первой)""}},
""required"":[""points""]}",
        a => Polyline(a));

      ToolRegistry.Add("text",
        "Текст на чертеже.",
        @"{""type"":""object"",""properties"":{
""x"":{""type"":""number""},""y"":{""type"":""number""},
""h"":{""type"":""number"",""description"":""Высота, мм (по умолчанию 5)""},
""angle"":{""type"":""number"",""description"":""По умолчанию 0""},
""s"":{""type"":""string""}},
""required"":[""x"",""y"",""s""]}",
        a =>
        {
          int obj = GetDoc().ksText(ToolRegistry.GetDbl(a, "x"), ToolRegistry.GetDbl(a, "y"),
            ToolRegistry.GetDbl(a, "angle", 0), ToolRegistry.GetDbl(a, "h", 5), 1, 0,
            ToolRegistry.GetStr(a, "s", ""));
          return Id("text", obj);
        });

      ToolRegistry.Add("lin_dim",
        "Линейный размер между двумя точками. ang: 0=горизонтальный, 90=вертикальный, иначе наклонный. dx,dy — смещение размерной линии. sign=1 → ⌀. prefix — текст перед значением (напр. '6 отв. ').",
        @"{""type"":""object"",""properties"":{
""x1"":{""type"":""number""},""y1"":{""type"":""number""},""x2"":{""type"":""number""},""y2"":{""type"":""number""},
""dx"":{""type"":""number""},""dy"":{""type"":""number""},
""ang"":{""type"":""number"",""description"":""По умолчанию 0""},
""sign"":{""type"":""integer"",""description"":""1 = знак диаметра ⌀""},
""prefix"":{""type"":""string""},
""textPos"":{""type"":""number"",""description"":""Позиция текста, % (по умолчанию 50)""}},
""required"":[""x1"",""y1"",""x2"",""y2""]}",
        a => LinDim(a));

      ToolRegistry.Add("diam_dim",
        "Диаметральный размер на окружности (xc,yc,r). ang — направление размерной линии в градусах.",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},""r"":{""type"":""number""},
""ang"":{""type"":""number""},
""prefix"":{""type"":""string""},
""textPos"":{""type"":""number"",""description"":""По умолчанию 75""}},
""required"":[""xc"",""yc"",""r"",""ang""]}",
        a => DiamDim(a));

      ToolRegistry.Add("rad_dim",
        "Радиальный размер на дуге/окружности.",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},""r"":{""type"":""number""},
""ang"":{""type"":""number"",""description"":""Направление, градусы""},
""prefix"":{""type"":""string""}},
""required"":[""xc"",""yc"",""r"",""ang""]}",
        a => RadDim(a));

      ToolRegistry.Add("ang_dim",
        "Угловой размер: вершина (xc,yc) и по точке на каждой стороне угла.",
        @"{""type"":""object"",""properties"":{
""xc"":{""type"":""number""},""yc"":{""type"":""number""},
""x1"":{""type"":""number""},""y1"":{""type"":""number""},""x2"":{""type"":""number""},""y2"":{""type"":""number""},
""textPos"":{""type"":""number"",""description"":""По умолчанию 50""}},
""required"":[""xc"",""yc"",""x1"",""y1"",""x2"",""y2""]}",
        a => AngDim(a));

      ToolRegistry.Add("hatch",
        "Штриховка замкнутым контуром. Внутри tool'а зашита идиома двойного контура (обводка style 1 до блока штриховки + повтор контура внутри блока style 2, затем ksEndObj) — иначе контур не виден или штриховка ложится огрызком. Контур обязан быть замкнут. contour: [{type:'line',x1,y1,x2,y2} | {type:'arc',xc,yc,r,x1,y1,x2,y2,direction}].",
        @"{""type"":""object"",""properties"":{
""contour"":{""type"":""array"",""items"":{""type"":""object""}},
""angle"":{""type"":""number"",""description"":""Угол штриховки, градусы (по умолчанию 45)""},
""step"":{""type"":""number"",""description"":""Шаг, мм (по умолчанию 2)""},
""outline"":{""type"":""boolean"",""description"":""true = дополнительно обвести контур основной линией (по умолчанию true)""},
""x0"":{""type"":""number"",""description"":""Точка внутри контура""},
""y0"":{""type"":""number""}},
""required"":[""contour"",""x0"",""y0""]}",
        a => Hatch(a));

      ToolRegistry.Add("rough",
        "Знак шероховатости с текстом (напр. 'Ra 6,3').",
        @"{""type"":""object"",""properties"":{
""x"":{""type"":""number""},""y"":{""type"":""number""},
""ang"":{""type"":""number"",""description"":""По умолчанию 0""},
""text"":{""type"":""string""}},
""required"":[""x"",""y"",""text""]}",
        a => Rough(a));

      ToolRegistry.Add("fill_stamp",
        "Заполнить основную надпись (штамп) активного чертежа. Семантические поля: partNumber(обозначение), description(наименование), material, mass, scale, sheetNumber(№ листа), numberOfSheets(листов).",
        @"{""type"":""object"",""properties"":{
""partNumber"":{""type"":""string""},""description"":{""type"":""string""},""material"":{""type"":""string""},
""mass"":{""type"":""string""},""scale"":{""type"":""string""},""sheetNumber"":{""type"":""string""},""numberOfSheets"":{""type"":""string""}}}",
        a => FillStamp(a));

      ToolRegistry.Add("tech_demands",
        "Технические требования: пункты (строки) от точки x,y вниз с шагом по высоте текста.",
        @"{""type"":""object"",""properties"":{
""x"":{""type"":""number""},""y"":{""type"":""number"",""description"":""Координаты первой строки""},
""items"":{""type"":""array"",""items"":{""type"":""string""}},
""h"":{""type"":""number"",""description"":""Высота текста, по умолчанию 7""}},
""required"":[""items""]}",
        a => TechDemands(a));

      ToolRegistry.Add("save_document",
        "Сохранить активный чертёж. path можно опустить — сохранит по пути создания.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
        a =>
        {
          string p = ToolRegistry.GetStr(a, "path", null);
          if (p == null) p = docPath;
          p = AbsPath(p);
          bool ok = GetDoc().ksSaveDocument(p);
          if (!ok) throw new ToolException("ksSaveDocument вернул false: " + p);
          docPath = p;
          return new Dictionary<string, object> { { "saved", p } };
        });

      ToolRegistry.Add("render_png",
        "Отрендерить текущий чертёж в PNG (визуальная проверка).",
        @"{""type"":""object"",""properties"":{
""path"":{""type"":""string"",""description"":""По умолчанию <чертёж>.png""},
""resolution"":{""type"":""integer"",""description"":""DPI, по умолчанию 96""}}}",
        a => RenderPng(a));

      ToolRegistry.Add("cut_line",
        "Линия разреза/сечения (ЕСКД 2.305): штрихи + стрелки + надпись у обоих концов. points — ломаная линии (начало, изломы, конец, >=2 точки). label — буква (у обоих штрихов). right=1 — стрелки справа по ходу линии (надписи снаружи, автоматически). Точные позиции надписей можно задать labelX1..labelY2.",
        @"{""type"":""object"",""properties"":{
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}}},
""label"":{""type"":""string"",""description"":""Буква разреза, по умолчанию А""},
""right"":{""type"":""integer"",""description"":""0=стрелки слева, 1=справа (по умолчанию 1)""},
""labelOffset"":{""type"":""number"",""description"":""Авто-смещение надписей от штрихов, мм (по умолчанию 8)""},
""labelX1"":{""type"":""number""},""labelY1"":{""type"":""number""},
""labelX2"":{""type"":""number""},""labelY2"":{""type"":""number""}},
""required"":[""points""]}",
        a => CutLine(a));

      ToolRegistry.Add("dim_group",
        "Группа линейных размеров по одной оси (все точки на горизонтальной или вертикальной прямой). mode='chain' — размеры между последовательными точками на одном уровне; mode='base' — размеры от первой точки, при cascade=true ступенчато (step, мм). offset — смещение размерной линии (chain горизонтальный: dy, вертикальный: dx).",
        @"{""type"":""object"",""properties"":{
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}}},
""mode"":{""type"":""string"",""enum"":[""chain"",""base""],
""description"":""chain (по умолчанию) | base""},
""offset"":{""type"":""number"",""description"":""По умолчанию -15""},
""cascade"":{""type"":""boolean"",""description"":""Для base: ступенчатые размеры (по умолчанию false)""},
""step"":{""type"":""number"",""description"":""Шаг каскада, по умолчанию 8""},
""sign"":{""type"":""integer"",""description"":""1 = знак диаметра ⌀""},
""prefix"":{""type"":""string""},
""textPos"":{""type"":""number"",""description"":""Позиция текста, % (по умолчанию 50)""}},
""required"":[""points""]}",
        a => DimGroup(a));

      ToolRegistry.Add("export_dxf",
        "Экспортировать активный чертёж в DXF. path можно опустить — по пути чертежа с заменой расширения. Экспорт может быть запрещён Компас-Защита. Применимо к чертежу; для DXF-совместимости выноски/шероховатость упрощаются самим КОМПАСом.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
        a =>
        {
          string p = ToolRegistry.GetStr(a, "path", null);
          if (p == null)
          {
            string baseName = docPath ?? Paths.Out("drawing.cdw");
            p = Path.ChangeExtension(baseName, ".dxf");
          }
          bool ok = GetDoc().ksSaveToDXF(AbsPath(p));
          if (!ok) throw new ToolException("ksSaveToDXF вернул false (возможно, экспорт запрещён Компас-Защита): " + p);
          return new Dictionary<string, object> { { "dxf", p } };
        });

      ToolRegistry.Add("gear_drawing",
        "2D-чертёж цилиндрического прямозубого колеса по ГОСТ 2.402: осевой разрез (штриховка половин), торцевой вид (окружности da/d/df/ступицы/отверстия), таблица параметров. Геометрия ГОСТ 16532, x=0. web>0 — колесо с диском и прорезями, web=0 — сплошное (тогда hubD/hubL игнорируются).",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Модуль""},
""z"":{""type"":""integer"",""description"":""Число зубьев""},
""width"":{""type"":""number"",""description"":""Ширина венца b, мм""},
""bore"":{""type"":""number"",""description"":""Диаметр центрального отверстия d2, мм""},
""hubD"":{""type"":""number"",""description"":""Наружный диаметр ступицы (по умолчанию 1.6*bore)""},
""hubL"":{""type"":""number"",""description"":""Длина ступицы (по умолчанию width+10)""},
""web"":{""type"":""number"",""description"":""Толщина диска c (по умолчанию 0.3*width; 0 = сплошное)""},
""accuracy"":{""type"":""string"",""description"":""Степень точности (по умолчанию 8-В)""},
""scale"":{""type"":""number"",""description"":""Масштаб видов (по умолчанию 1)""},
""path"":{""type"":""string"",""description"":""Путь сохранения .cdw""},
""cx"":{""type"":""number"",""description"":""X главного вида на листе (по умолчанию 130)""},
""cy"":{""type"":""number"",""description"":""Y видов на листе (по умолчанию 160)""}}}",
        a => GearDrawing(a));

      ToolRegistry.Add("close_drawing",
        "Закрыть текущий чертёж (сохраняйте save_document заранее).",
        "{}",
        a =>
        {
          bool ok = GetDoc().ksCloseDocument();
          doc = null;
          docPath = null;
          viewNum = 0;
          return new Dictionary<string, object> { { "closed", ok } };
        });
    }

    // ---- состояние ----

    public static ksDocument2D GetDoc()
    {
      if (doc == null) throw new ToolException("Нет активного чертежа — вызовите create_drawing");
      return doc;
    }

    public static string DocPath { get { return docPath; } }

    // КОМПАС резолвит относительные пути от своего cwd, сервер — от своего; единая точка правды — абсолютные пути.
    static string AbsPath(string p)
    {
      return Path.GetFullPath(p);
    }

    static Dictionary<string, object> Id(string type, int obj)
    {
      return new Dictionary<string, object> { { "type", type }, { "obj", obj } };
    }

    // ---- создание чертежа ----

    static object CreateDrawing(Dictionary<string, object> a)
    {
      string fmt = ToolRegistry.GetStr(a, "format", "A3");
      int format = FormatCode(fmt);

      KompasObject kompas = KompasHost.Kompas;
      doc = (ksDocument2D)kompas.Document2D();

      ksDocumentParam docPar = (ksDocumentParam)kompas.GetParamStruct((short)StructType2DEnum.ko_DocumentParam);
      if (docPar == null) throw new ToolException("ko_DocumentParam == null");
      // КОМПАС резолвит относительный путь от своего cwd (у COM-сервера он свой) — пути всегда абсолютные
      docPath = AbsPath(ToolRegistry.GetStr(a, "path", Paths.Out("drawing.cdw")));
      string dir = Path.GetDirectoryName(docPath);
      if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
      docPar.fileName = docPath;
      docPar.comment = ToolRegistry.GetStr(a, "name", "");
      docPar.regime = 0;
      docPar.type = (short)DocType.lt_DocSheetStandart;

      ksSheetPar shPar = (ksSheetPar)docPar.GetLayoutParam();
      shPar.shtType = 1;
      shPar.layoutName = string.Empty;
      ksStandartSheet stPar = (ksStandartSheet)shPar.GetSheetParam();
      stPar.format = (short)format;
      stPar.multiply = 1;
      stPar.direct = ToolRegistry.GetBool(a, "landscape", true);

      if (!doc.ksCreateDocument(docPar)) throw new ToolException("ksCreateDocument вернул false");

      // Листовой вид в начале координат листа: координаты геометрии = мм листа
      ksViewParam par = (ksViewParam)kompas.GetParamStruct((short)StructType2DEnum.ko_ViewParam);
      par.Init();
      par.x = 0; par.y = 0;
      par.scale_ = 1;
      par.angle = 0;
      par.state = (short)ldefin2d.stACTIVE;
      par.name = "Виды";
      int vnum = 1;
      doc.ksCreateSheetView(par, ref vnum);
      viewNum = vnum;

      return new Dictionary<string, object> { { "path", docPath }, { "format", fmt }, { "viewNumber", vnum } };
    }

    // А3=3 проверено на flange_draw.cs; остальные коды (A0..A4) уточнить при первом прогоне render_png.
    static int FormatCode(string fmt)
    {
      switch (fmt)
      {
        case "A0": return 0;
        case "A1": return 1;
        case "A2": return 2;
        case "A3": return 3;
        case "A4": return 4;
        default: throw new ToolException("Неизвестный формат: " + fmt);
      }
    }

    // ---- примитивы ----

    static object Polyline(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      int style = ToolRegistry.GetInt(a, "style", 1);
      bool closed = ToolRegistry.GetBool(a, "closed", false);
      List<double[]> pts = GetPoints(a, "points");

      int n = closed ? pts.Count : pts.Count - 1;
      int made = 0;
      for (int i = 0; i < n; i++)
      {
        double[] p1 = pts[i];
        double[] p2 = pts[(i + 1) % pts.Count];
        d.ksLineSeg(p1[0], p1[1], p2[0], p2[1], style);
        made++;
      }
      return new Dictionary<string, object> { { "segments", made } };
    }

    static double ToDbl(object o)
    {
      if (o is double) return (double)o;
      if (o is int) return (int)o;
      if (o is long) return (long)o;
      double d;
      if (double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
      throw new ToolException("Не число: " + o);
    }

    // ---- размеры ----

    static object CircleArray(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      double xc = ToolRegistry.GetDbl(a, "xc");
      double yc = ToolRegistry.GetDbl(a, "yc");
      double R = ToolRegistry.GetDbl(a, "arrayRadius");
      int n = ToolRegistry.GetInt(a, "n");
      double r = ToolRegistry.GetDbl(a, "r");
      double start = ToolRegistry.GetDbl(a, "startAngle", 90);
      double arm = ToolRegistry.GetDbl(a, "crossArm", r + 1);

      for (int i = 0; i < n; i++)
      {
        double ang = Math.PI * (start + 360.0 * i / n) / 180.0;
        double hx = xc + R * Math.Cos(ang);
        double hy = yc + R * Math.Sin(ang);
        d.ksCircle(hx, hy, r, 1);
        d.ksLineSeg(hx - arm, hy, hx + arm, hy, 3);
        d.ksLineSeg(hx, hy - arm, hx, hy + arm, 3);
      }
      return new Dictionary<string, object> { { "holes", n } };
    }

    static object LinDim(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksLDimParam p = (ksLDimParam)kompas.GetParamStruct((short)StructType2DEnum.ko_LDimParam);
      ksDimDrawingParam dPar = (ksDimDrawingParam)p.GetDPar();
      ksLDimSourceParam sPar = (ksLDimSourceParam)p.GetSPar();
      ksDimTextParam tPar = (ksDimTextParam)p.GetTPar();

      dPar.Init();
      dPar.textPos = ToolRegistry.GetInt(a, "textPos", 50);
      dPar.textBase = 2;
      dPar.pt1 = 2;
      dPar.pt2 = 2;
      dPar.ang = ToolRegistry.GetDbl(a, "ang", 0);

      sPar.Init();
      sPar.x1 = ToolRegistry.GetDbl(a, "x1");
      sPar.y1 = ToolRegistry.GetDbl(a, "y1");
      sPar.x2 = ToolRegistry.GetDbl(a, "x2");
      sPar.y2 = ToolRegistry.GetDbl(a, "y2");
      sPar.dx = ToolRegistry.GetDbl(a, "dx", 0);
      sPar.dy = ToolRegistry.GetDbl(a, "dy", 0);
      sPar.basePoint = 1;

      InitText(kompas, tPar, ToolRegistry.GetInt(a, "sign", 0), ToolRegistry.GetStr(a, "prefix", null));

      int obj = d.ksLinDimension(p);
      return Id("lin_dim", obj);
    }

    static object DiamDim(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksRDimParam p = (ksRDimParam)kompas.GetParamStruct((short)StructType2DEnum.ko_RDimParam);
      ksRDimDrawingParam dPar = (ksRDimDrawingParam)p.GetDPar();
      ksRDimSourceParam sPar = (ksRDimSourceParam)p.GetSPar();
      ksDimTextParam tPar = (ksDimTextParam)p.GetTPar();

      sPar.Init();
      sPar.xc = ToolRegistry.GetDbl(a, "xc");
      sPar.yc = ToolRegistry.GetDbl(a, "yc");
      sPar.rad = ToolRegistry.GetDbl(a, "r");

      dPar.Init();
      dPar.textPos = ToolRegistry.GetInt(a, "textPos", 75);
      dPar.pt1 = 2;
      dPar.pt2 = 2;
      dPar.shelfDir = 1;
      dPar.ang = ToolRegistry.GetDbl(a, "ang");

      InitText(kompas, tPar, ToolRegistry.GetInt(a, "sign", 1), ToolRegistry.GetStr(a, "prefix", null));

      int obj = d.ksDiamDimension(p);
      return Id("diam_dim", obj);
    }

    static object RadDim(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksRDimParam p = (ksRDimParam)kompas.GetParamStruct((short)StructType2DEnum.ko_RDimParam);
      ksRDimDrawingParam dPar = (ksRDimDrawingParam)p.GetDPar();
      ksRDimSourceParam sPar = (ksRDimSourceParam)p.GetSPar();
      ksDimTextParam tPar = (ksDimTextParam)p.GetTPar();

      sPar.Init();
      sPar.xc = ToolRegistry.GetDbl(a, "xc");
      sPar.yc = ToolRegistry.GetDbl(a, "yc");
      sPar.rad = ToolRegistry.GetDbl(a, "r");

      dPar.Init();
      dPar.textPos = ToolRegistry.GetInt(a, "textPos", 75);
      dPar.pt1 = 2;
      dPar.pt2 = 2;
      dPar.shelfDir = 1;
      dPar.ang = ToolRegistry.GetDbl(a, "ang");

      InitText(kompas, tPar, 0, ToolRegistry.GetStr(a, "prefix", null));

      int obj = d.ksRadDimension(p);
      return Id("rad_dim", obj);
    }

    static object AngDim(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksADimParam p = (ksADimParam)kompas.GetParamStruct((short)StructType2DEnum.ko_ADimParam);
      ksDimDrawingParam dPar = (ksDimDrawingParam)p.GetDPar();
      ksADimSourceParam sPar = (ksADimSourceParam)p.GetSPar();
      ksDimTextParam tPar = (ksDimTextParam)p.GetTPar();

      dPar.Init();
      dPar.textPos = ToolRegistry.GetInt(a, "textPos", 50);
      dPar.textBase = 2;

      sPar.Init();
      sPar.xc = ToolRegistry.GetDbl(a, "xc");
      sPar.yc = ToolRegistry.GetDbl(a, "yc");
      sPar.x1 = ToolRegistry.GetDbl(a, "x1");
      sPar.y1 = ToolRegistry.GetDbl(a, "y1");
      sPar.x2 = ToolRegistry.GetDbl(a, "x2");
      sPar.y2 = ToolRegistry.GetDbl(a, "y2");

      InitText(kompas, tPar, 0, null);

      int obj = d.ksAngDimension(p);
      return Id("ang_dim", obj);
    }

    // tPar.Init(false) + _AUTONOMINAL + sign; префикс через _PREFIX + строка в GetTextArr (в одну строку).
    static void InitText(KompasObject kompas, ksDimTextParam tPar, int sign, string prefix)
    {
      tPar.Init(false);
      tPar.SetBitFlagValue(ldefin2d._AUTONOMINAL, true);
      tPar.sign = sign;
      if (prefix != null && prefix.Length > 0)
      {
        tPar.SetBitFlagValue(ldefin2d._PREFIX, true);
        ksChar255 str = (ksChar255)kompas.GetParamStruct((short)StructType2DEnum.ko_Char255);
        ksDynamicArray arr = (ksDynamicArray)tPar.GetTextArr();
        str.str = prefix;
        arr.ksAddArrayItem(-1, str);
      }
    }

    // ---- штриховка ----

    static object Hatch(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      object cObj;
      List<object> contour = a.TryGetValue("contour", out cObj) ? cObj as List<object> : null;
      if (contour == null || contour.Count == 0) throw new ToolException("Пустой contour");
      double ang = ToolRegistry.GetDbl(a, "angle", 45);
      double step = ToolRegistry.GetDbl(a, "step", 2);
      double x0 = ToolRegistry.GetDbl(a, "x0");
      double y0 = ToolRegistry.GetDbl(a, "y0");

      // 1) контур заранее обычными объектами (style 1) — линии внутри блока штриховки не рендерятся;
      //    outline=false — заливка без обводки (обводку рисует вызывающий)
      bool outline = ToolRegistry.GetBool(a, "outline", true);
      if (outline) DrawContour(d, contour, 1);

      // 2) блок штриховки: повтор контура внутри (style 2), затем ksEndObj
      d.ksHatch(0, ang, step, x0, y0, 0);
      DrawContour(d, contour, 2);
      d.ksEndObj();

      return new Dictionary<string, object> { { "hatch", true }, { "segments", contour.Count } };
    }

    static void DrawContour(ksDocument2D d, List<object> contour, int style)
    {
      foreach (object segObj in contour)
      {
        Dictionary<string, object> seg = segObj as Dictionary<string, object>;
        if (seg == null) throw new ToolException("Сегмент контура должен быть объектом");
        string type = ToolRegistry.GetStr(seg, "type", "line");
        if (type == "line")
        {
          d.ksLineSeg(ToolRegistry.GetDbl(seg, "x1"), ToolRegistry.GetDbl(seg, "y1"),
            ToolRegistry.GetDbl(seg, "x2"), ToolRegistry.GetDbl(seg, "y2"), style);
        }
        else if (type == "arc")
        {
          d.ksArcByPoint(ToolRegistry.GetDbl(seg, "xc"), ToolRegistry.GetDbl(seg, "yc"),
            ToolRegistry.GetDbl(seg, "r"), ToolRegistry.GetDbl(seg, "x1"), ToolRegistry.GetDbl(seg, "y1"),
            ToolRegistry.GetDbl(seg, "x2"), ToolRegistry.GetDbl(seg, "y2"),
            (short)ToolRegistry.GetInt(seg, "direction", 1), style);
        }
        else throw new ToolException("Неизвестный тип сегмента: " + type);
      }
    }

    // ---- линия разреза/сечения ----

    // POINT_ARR (automation) = 2, ko_CutLineParam = 65, ko_MathPointParam = 14.
    static object CutLine(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;

      List<double[]> pts = GetPoints(a, "points");
      if (pts.Count < 2) throw new ToolException("points: минимум 2 точки");
      string label = ToolRegistry.GetStr(a, "label", "А");
      int right = ToolRegistry.GetInt(a, "right", 1);
      double off = ToolRegistry.GetDbl(a, "labelOffset", 8);

      // авто-смещение надписей наружу от стрелок: right=1 -> по ходу линии стрелки справа,
      // надписи слева (нормаль CCW координатной плоскости от направления участка)
      double ux = pts[1][0] - pts[0][0], uy = pts[1][1] - pts[0][1];
      double l1 = Math.Sqrt(ux * ux + uy * uy);
      if (l1 < 1e-9) throw new ToolException("Нулевой первый участок линии разреза");
      ux /= l1; uy /= l1;
      double sign = right == 1 ? 1 : -1;
      double px = -uy, py = ux;      // CCW-перп
      if (sign < 0) { px = -px; py = -py; }

      double[] last = pts[pts.Count - 1];
      double[] prev = pts[pts.Count - 2];
      double vx = last[0] - prev[0], vy = last[1] - prev[1];
      double l2 = Math.Sqrt(vx * vx + vy * vy);
      if (l2 < 1e-9) throw new ToolException("Нулевой последний участок линии разреза");
      vx /= l2; vy /= l2;
      double qx = -vy, qy = vx;
      if (sign < 0) { qx = -qx; qy = -qy; }

      ksCutLineParam cp = (ksCutLineParam)kompas.GetParamStruct((short)StructType2DEnum.ko_CutLineParam);
      if (cp == null) throw new ToolException("ko_CutLineParam == null");
      cp.Init();
      cp.type = 0;                 // надпись строкой (str)
      cp.right = (short)right;
      cp.str = label;
      cp.x1 = ToolRegistry.GetDbl(a, "labelX1", pts[0][0] + off * px);
      cp.y1 = ToolRegistry.GetDbl(a, "labelY1", pts[0][1] + off * py);
      cp.x2 = ToolRegistry.GetDbl(a, "labelX2", last[0] + off * qx);
      cp.y2 = ToolRegistry.GetDbl(a, "labelY2", last[1] + off * qy);

      ksDynamicArray arr = (ksDynamicArray)kompas.GetDynamicArray(2);
      if (arr == null) throw new ToolException("GetDynamicArray(POINT_ARR) == null");
      foreach (double[] p in pts)
      {
        ksMathPointParam mp = (ksMathPointParam)kompas.GetParamStruct((short)StructType2DEnum.ko_MathPointParam);
        mp.Init();
        mp.x = p[0];
        mp.y = p[1];
        arr.ksAddArrayItem(-1, mp);
      }
      if (!cp.SetpMathPoint(arr)) throw new ToolException("SetpMathPoint вернул false");

      int obj = d.ksCutLine(cp);
      if (obj == 0) throw new ToolException("ksCutLine вернул 0");
      return Id("cut_line", obj);
    }

    // ---- группа размеров ----

    static object DimGroup(Dictionary<string, object> a)
    {
      List<double[]> pts = GetPoints(a, "points");
      if (pts.Count < 2) throw new ToolException("points: минимум 2 точки");
      string mode = ToolRegistry.GetStr(a, "mode", "chain");
      if (mode != "chain" && mode != "base") throw new ToolException("mode: chain | base");
      double off = ToolRegistry.GetDbl(a, "offset", -15);
      bool casc = ToolRegistry.GetBool(a, "cascade", false);
      double step = ToolRegistry.GetDbl(a, "step", 8);

      // одна ось: либо все y равны (горизонтальные размеры, ang=0, смещение по dy),
      // либо все x равны (вертикальные, ang=90, смещение по dx)
      bool vertical = ToolRegistry.GetBool(a, "vertical", false);
      bool horizontal = ToolRegistry.GetBool(a, "horizontal", false);
      if (!vertical && !horizontal)
      {
        double y0 = pts[0][1];
        vertical = true;
        foreach (double[] p in pts)
          if (Math.Abs(p[0] - pts[0][0]) > 1e-6) vertical = false;
        if (!vertical)
        {
          horizontal = true;
          foreach (double[] p in pts)
            if (Math.Abs(p[1] - y0) > 1e-6) { horizontal = false; break; }
        }
        if (!vertical && !horizontal)
          throw new ToolException("Точки не лежат на одной горизонтальной или вертикальной оси; укажите vertical=true/false явно или выровняйте точки");
      }
      else if (vertical && horizontal)
        throw new ToolException("vertical и horizontal взаимоисключающи");
      double ang = ToolRegistry.GetDbl(a, "ang", vertical ? 90 : 0);

      int m = pts.Count - 1;
      for (int i = 0; i < m; i++)
      {
        double[] p1 = mode == "chain" ? pts[i] : pts[0];
        double[] p2 = pts[i + 1];
        double o = off;
        if (mode == "base" && casc) o = off - i * step;
        Dictionary<string, object> args = new Dictionary<string, object>();
        args["x1"] = p1[0]; args["y1"] = p1[1];
        args["x2"] = p2[0]; args["y2"] = p2[1];
        if (horizontal) { args["dx"] = o; args["dy"] = 0.0; }
        else { args["dx"] = 0.0; args["dy"] = o; }
        if (a.ContainsKey("ang")) args["ang"] = ToolRegistry.GetDbl(a, "ang");
        if (a.ContainsKey("prefix")) args["prefix"] = ToolRegistry.GetStr(a, "prefix");
        if (ToolRegistry.GetBool(a, "sign", false)) args["sign"] = 1;
        if (a.ContainsKey("textPos")) args["textPos"] = ToolRegistry.GetDbl(a, "textPos");
        LinDim(args);
      }
      return new Dictionary<string, object> { { "dims", m }, { "mode", mode } };
    }

    static List<double[]> GetPoints(Dictionary<string, object> a, string key)
    {
      object ptsObj;
      if (!a.TryGetValue(key, out ptsObj)) throw new ToolException("Нет " + key);
      List<object> raw = ptsObj as List<object>;
      if (raw == null || raw.Count < 2) throw new ToolException(key + ": минимум 2 точки [x,y]");
      List<double[]> res = new List<double[]>();
      foreach (object o in raw)
      {
        List<object> pair = o as List<object>;
        if (pair == null || pair.Count != 2) throw new ToolException("Точка должна быть парой [x,y]");
        res.Add(new double[] { ToDbl(pair[0]), ToDbl(pair[1]) });
      }
      return res;
    }

    // ---- чертёж зубчатого колеса (ГОСТ 2.402) ----

    static string FmtNum(double v)
    {
      return v.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');
    }

    // Штриховка прямоугольной области отдельным блоком ksHatch (надёжно: тривиально замкнутый контур).
    // Без обводки (outline=false): обводку сечения рисует вызывающий.
    static void HatchRect(double x0, double x1, double y0, double y1, bool mirror)
    {
      double ya = mirror ? -y1 : y0, yb = mirror ? -y0 : y1;
      List<object> segs = new List<object>();
      double[] xs = new double[] { x0, x1, x1, x0 };
      double[] ys = new double[] { ya, ya, yb, yb };
      for (int i = 0; i < 4; i++)
      {
        Dictionary<string, object> s = new Dictionary<string, object>();
        s["type"] = "line";
        s["x1"] = xs[i]; s["y1"] = ys[i];
        s["x2"] = xs[(i + 1) % 4]; s["y2"] = ys[(i + 1) % 4];
        segs.Add(s);
      }
      Dictionary<string, object> a = new Dictionary<string, object>();
      a["contour"] = segs;
      a["angle"] = 45.0; a["step"] = 2.5;
      a["x0"] = (x0 + x1) / 2.0; a["y0"] = (ya + yb) / 2.0;
      a["outline"] = false;
      Hatch(a);
    }

    // Внешний контур верхней половины сечения (y — радиус, x — вдоль оси; замкнутый цикл точек).
    // Полости между диском и венцом открыты вбок — контур обходит их стены. mirror — нижняя половина.
    static List<object> SectionContour(double xh0, double xh1, double xr0, double xr1,
      double xw0, double xw1, double ybore, double yhub, double ydf, double yra, bool disc, bool mirror)
    {
      double[][] p = disc
        ? new double[][]
          {
            new double[] { xh0, ybore }, new double[] { xh1, ybore },
            new double[] { xh1, yhub },  new double[] { xw1, yhub },
            new double[] { xw1, ydf },   new double[] { xr1, ydf },
            new double[] { xr1, yra },   new double[] { xr0, yra },
            new double[] { xr0, ydf },   new double[] { xw0, ydf },
            new double[] { xw0, yhub },  new double[] { xh0, yhub }
          }
        : new double[][]
          {
            new double[] { xh0, ybore }, new double[] { xh1, ybore },
            new double[] { xh1, yra },   new double[] { xh0, yra }
          };
      List<object> segs = new List<object>();
      for (int i = 0; i < p.Length; i++)
      {
        double[] p1 = p[i];
        double[] p2 = p[(i + 1) % p.Length];
        Dictionary<string, object> s = new Dictionary<string, object>();
        s["type"] = "line";
        s["x1"] = p1[0]; s["y1"] = mirror ? -p1[1] : p1[1];
        s["x2"] = p2[0]; s["y2"] = mirror ? -p2[1] : p2[1];
        segs.Add(s);
      }
      return segs;
    }

    static void AddLoop(List<object> segs, double[][] p)
    {
      for (int i = 0; i < p.Length; i++)
      {
        double[] p1 = p[i];
        double[] p2 = p[(i + 1) % p.Length];
        Dictionary<string, object> s = new Dictionary<string, object>();
        s["type"] = "line";
        s["x1"] = p1[0]; s["y1"] = p1[1]; s["x2"] = p2[0]; s["y2"] = p2[1];
        segs.Add(s);
      }
    }

    // Создать листовой вид (становится активным): координаты геометрии — мм вида.
    static int MakeView(double x, double y, double scale, string name)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksViewParam par = (ksViewParam)kompas.GetParamStruct((short)StructType2DEnum.ko_ViewParam);
      par.Init();
      par.x = x; par.y = y;
      par.scale_ = scale;
      par.angle = 0;
      par.state = (short)ldefin2d.stACTIVE;
      par.name = name;
      int vnum = 0;
      d.ksCreateSheetView(par, ref vnum);
      viewNum = vnum;
      return vnum;
    }

    static Dictionary<string, object> GearDrawing(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z = ToolRegistry.GetInt(a, "z");
      double b = ToolRegistry.GetDbl(a, "width");
      double d2 = ToolRegistry.GetDbl(a, "bore");
      if (m <= 0 || z < 6) throw new ToolException("Нужны m > 0 и целые z >= 6");
      if (b <= 0 || d2 <= 0) throw new ToolException("Нужны width > 0 и bore > 0");
      double scale = ToolRegistry.GetDbl(a, "scale", 1);
      if (scale <= 0) throw new ToolException("scale должен быть > 0");
      double cx = ToolRegistry.GetDbl(a, "cx", 130);
      double cy = ToolRegistry.GetDbl(a, "cy", 160);

      Gost16532.Wheel w = Gost16532.WheelGeom(m, z);
      if (d2 >= w.Df) throw new ToolException("bore=" + d2 + " >= диаметра впадин df=" + w.Df);

      // диск: web=0 → сплошное колесо, иначе диск с прорезями
      double webDef = 0.3 * b;
      object webObj;
      bool disc = !(a.TryGetValue("web", out webObj) && ToDbl(webObj) <= 0);
      double web = ToolRegistry.GetDbl(a, "web", disc ? webDef : 0);
      if (!disc) web = 0;

      double hubD = 0, hubL = 0;
      if (!disc) hubL = b; // сплошное: длину задаёт венец
      if (disc)
      {
        hubD = ToolRegistry.GetDbl(a, "hubD", 1.6 * d2);
        hubL = ToolRegistry.GetDbl(a, "hubL", b + 10);
        if (hubD <= d2 || hubD >= w.Df)
          throw new ToolException("Требуется d2 < hubD < df: bore=" + d2 + ", hubD=" + hubD + ", df=" + w.Df);
        if (web >= Math.Min(b, hubL))
          throw new ToolException("Толщина диска web=" + web + " должна быть меньше min(width, hubL)=" + Math.Min(b, hubL));
      }

      string accuracy = ToolRegistry.GetStr(a, "accuracy", "8-В");

      // ---- лист ----
      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Колесо зубчатое m" + FmtNum(m) + " z" + z;
      CreateDrawing(createArgs);

      double ra = w.Da / 2.0;
      double ydf = w.Df / 2.0;
      double yhub = disc ? hubD / 2.0 : d2 / 2.0;
      double ybore = d2 / 2.0;
      double xh0 = -hubL / 2.0, xh1 = hubL / 2.0;
      double xr0 = -b / 2.0, xr1 = b / 2.0;
      double xw0 = -web / 2.0, xw1 = web / 2.0;

      // ---- главный вид: осевой разрез ----
      MakeView(cx, cy, scale, "Осевой разрез");

      // обводка внешнего контура сечения (обе половины) основной линией
      List<object> outline = SectionContour(xh0, xh1, xr0, xr1, xw0, xw1, ybore, yhub, ydf, ra, disc, false);
      List<object> outlineDown = SectionContour(xh0, xh1, xr0, xr1, xw0, xw1, ybore, yhub, ydf, ra, disc, true);
      DrawContour(GetDoc(), outline, 1);
      DrawContour(GetDoc(), outlineDown, 1);

      // заливка полосами с зазором 0.5мм от линий контура (иначе заливка перетекает
      // через примыкающие углы): outline=false — без двойной обводки
      if (disc)
      {
        HatchRect(xr0 + 0.5, xr1 - 0.5, ydf + 0.5, ra - 0.5, false);
        HatchRect(xw0 + 0.5, xw1 - 0.5, yhub + 0.5, ydf - 0.5, false);
        HatchRect(xh0 + 0.5, xh1 - 0.5, ybore, yhub - 0.5, false);
        HatchRect(xr0 + 0.5, xr1 - 0.5, ydf + 0.5, ra - 0.5, true);
        HatchRect(xw0 + 0.5, xw1 - 0.5, yhub + 0.5, ydf - 0.5, true);
        HatchRect(xh0 + 0.5, xh1 - 0.5, ybore, yhub - 0.5, true);
      }
      else
      {
        HatchRect(xh0 + 0.5, xh1 - 0.5, ybore, ra - 0.5, false);
        HatchRect(xh0 + 0.5, xh1 - 0.5, ybore, ra - 0.5, true);
      }

      // ось вращения (штрихпунктирная)
      double ax = Math.Max(hubL, b) / 2.0 + 5;
      GetDoc().ksLineSeg(-ax, 0, ax, 0, 3);

      // размеры: ширина венца — над контуром
      Dictionary<string, object> dimB = new Dictionary<string, object>();
      dimB["x1"] = xr0; dimB["y1"] = ra; dimB["x2"] = xr1; dimB["y2"] = ra;
      dimB["ang"] = 0.0; dimB["dx"] = 0.0; dimB["dy"] = 10.0;
      LinDim(dimB);

      if (disc)
      {
        // длина ступицы — под контуром
        Dictionary<string, object> dimH = new Dictionary<string, object>();
        dimH["x1"] = xh0; dimH["y1"] = -yhub; dimH["x2"] = xh1; dimH["y2"] = -yhub;
        dimH["ang"] = 0.0; dimH["dx"] = 0.0; dimH["dy"] = -10.0;
        LinDim(dimH);
      }

      // ---- торцевой вид ----
      double cx2 = cx + scale * (hubL / 2.0 + ra) + 25.0;
      MakeView(cx2, cy, scale, "Вид слева");

      ksDocument2D d = GetDoc();
      if (disc) d.ksCircle(0, 0, hubD / 2.0, 1);
      d.ksCircle(0, 0, ra, 1);
      d.ksCircle(0, 0, w.Df / 2.0, 2);   // окружность впадин — сплошная тонкая
      d.ksCircle(0, 0, w.D / 2.0, 3);    // делительный — штрихпунктирная
      d.ksCircle(0, 0, ybore, 1);
      double arm2 = ra + 4;
      d.ksLineSeg(-arm2, 0, arm2, 0, 3);
      d.ksLineSeg(0, -arm2, 0, arm2, 3);

      // выноски диаметров: da — вправо-вверх, d — вниз, df — вверх, bore — вправо-вверх
      // (вбок влево нельзя — выноска пересечёт осевой разрез)
      double[] angs = new double[] { 45.0, 270.0, 100.0, 60.0 };
      double[] radii = new double[] { ra, w.D / 2.0, w.Df / 2.0, ybore };
      for (int i = 0; i < 4; i++)
      {
        Dictionary<string, object> dim = new Dictionary<string, object>();
        dim["xc"] = 0; dim["yc"] = 0; dim["r"] = radii[i];
        dim["ang"] = angs[i];
        dim["textPos"] = 60.0;
        DiamDim(dim);
      }

      // ---- таблица параметров (ГОСТ 2.402, масштаб 1 — мм листа) ----
      MakeView(258.0, 145.0, 1.0, "Таблица параметров");

      List<string[]> rows = new List<string[]>();
      rows.Add(new string[] { "Модуль", FmtNum(m) });
      rows.Add(new string[] { "Число зубьев", z.ToString(CultureInfo.InvariantCulture) });
      rows.Add(new string[] { "Угол наклона зубьев", "0°" });
      rows.Add(new string[] { "Исходный контур", "ГОСТ 13755-2015" });
      rows.Add(new string[] { "Коэффициент смещения", "0" });
      rows.Add(new string[] { "Степень точности", accuracy });
      if (w.Undercut) rows.Add(new string[] { "Примечание", "z<17: подрезание профиля" });

      double col1 = 95, rowH = 10;
      double tw = col1 + 60, th = rows.Count * rowH;
      for (int i = 0; i <= rows.Count; i++)
        d.ksLineSeg(0, i * rowH, tw, i * rowH, 1);
      d.ksLineSeg(0, 0, 0, th, 1);
      d.ksLineSeg(col1, 0, col1, th, 1);
      d.ksLineSeg(tw, 0, tw, th, 1);
      for (int i = 0; i < rows.Count; i++)
      {
        string[] row = rows[i];
        double top = th - i * rowH;
        d.ksText(2, top - rowH / 2.0 - 1.8, 0, 5, 1, 0, row[0]);
        d.ksText(col1 + 2, top - rowH / 2.0 - 1.8, 0, 5, 1, 0, row[1]);
      }

      // предупреждение о наложении видов (не ошибка)
      Dictionary<string, object> res = new Dictionary<string, object>();
      res["path"] = docPath;
      res["da"] = w.Da; res["df"] = w.Df; res["d"] = w.D;
      if (w.Undercut) res["warning"] = "z<" + 17 + " без смещения: подрезание профиля (ГОСТ 16532)";
      double rightEdge = cx2 + scale * ra;
      if (rightEdge > 250.0) res["warning2"] = "торцевой вид близок к таблице параметров (right edge " + FmtNum(rightEdge) + " мм) — уменьшите scale";
      if (cy + scale * ra > 292.0 || cy - scale * ra < 60.0) res["warning3"] = "виды выходят на рамку/штамп — поднимите cy или уменьшите scale";
      return res;
    }

    // ---- шероховатость ----

    static object Rough(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      KompasObject kompas = KompasHost.Kompas;
      ksRoughParam rp = (ksRoughParam)kompas.GetParamStruct((short)StructType2DEnum.ko_RoughParam);
      ksRoughPar r = (ksRoughPar)rp.GetrPar();
      r.Init();
      r.style = 1;
      r.type = 0;
      r.x = ToolRegistry.GetDbl(a, "x");
      r.y = ToolRegistry.GetDbl(a, "y");
      r.ang = ToolRegistry.GetDbl(a, "ang", 0);
      r.cText1 = 1;
      ksDynamicArray arr = (ksDynamicArray)r.GetpText();
      ksChar255 str = (ksChar255)kompas.GetParamStruct((short)StructType2DEnum.ko_Char255);
      str.str = ToolRegistry.GetStr(a, "text", "");
      arr.ksAddArrayItem(-1, str);
      int obj = d.ksRough(rp);
      return Id("rough", obj);
    }

    // ---- штамп ----

    static object FillStamp(Dictionary<string, object> a)
    {
      IApplication app7 = KompasHost.App7;
      IKompasDocument2D doc7 = (IKompasDocument2D)app7.ActiveDocument;
      if (doc7 == null) throw new ToolException("Нет активного документа в API-7");

      ILayoutSheet sheet = (ILayoutSheet)doc7.LayoutSheets.ItemByNumber[1];
      IStamp stamp = (IStamp)sheet.Stamp;

      SetStamp(stamp, (int)ksStampEnum.ksStPartNumber, ToolRegistry.GetStr(a, "partNumber", null));
      SetStamp(stamp, (int)ksStampEnum.ksStDescription, ToolRegistry.GetStr(a, "description", null));
      SetStamp(stamp, (int)ksStampEnum.ksStMaterial, ToolRegistry.GetStr(a, "material", null));
      SetStamp(stamp, (int)ksStampEnum.ksStMass, ToolRegistry.GetStr(a, "mass", null));
      SetStamp(stamp, (int)ksStampEnum.ksStScale, ToolRegistry.GetStr(a, "scale", null));
      SetStamp(stamp, (int)ksStampEnum.ksStSheetNumber, ToolRegistry.GetStr(a, "sheetNumber", null));
      SetStamp(stamp, (int)ksStampEnum.ksStNumberOfSheets, ToolRegistry.GetStr(a, "numberOfSheets", null));
      stamp.Update();

      return new Dictionary<string, object> { { "stamp", true } };
    }

    static void SetStamp(IStamp stamp, int column, string value)
    {
      if (value == null) return;
      stamp.Text[column].Str = value;
    }

    // ---- техтребования ----

    static object TechDemands(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      double x = ToolRegistry.GetDbl(a, "x", 240);
      double y = ToolRegistry.GetDbl(a, "y", 100);
      double h = ToolRegistry.GetDbl(a, "h", 7);
      object itemsObj;
      if (!a.TryGetValue("items", out itemsObj)) throw new ToolException("Нет items");
      List<object> items = itemsObj as List<object>;
      if (items == null || items.Count == 0) throw new ToolException("Пустой items");

      double dy = h * 10.0 / 7.0; // межстрочный интервал
      for (int i = 0; i < items.Count; i++)
        d.ksText(x, y - i * dy, 0, h, 1, 0, items[i].ToString());
      return new Dictionary<string, object> { { "lines", items.Count } };
    }

    // ---- рендер ----

    static object RenderPng(Dictionary<string, object> a)
    {
      ksDocument2D d = GetDoc();
      string p = ToolRegistry.GetStr(a, "path", null);
      if (p == null)
      {
        string baseName = docPath ?? Paths.Out("drawing.cdw");
        p = Path.ChangeExtension(baseName, ".png");
      }
      p = AbsPath(p);
      object rObj = d.RasterFormatParam();
      ksRasterFormatParam rPar = (ksRasterFormatParam)rObj;
      rPar.Init();
      rPar.format = ldefin2d.FORMAT_PNG;
      rPar.colorBPP = 24;
      rPar.extResolution = ToolRegistry.GetInt(a, "resolution", 96);
      rPar.colorType = 0;
      bool ok = d.SaveAsToRasterFormat(p, rObj);
      if (!ok) throw new ToolException("SaveAsToRasterFormat вернул false");
      return new Dictionary<string, object> { { "png", p } };
    }
  }
}