using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using KAPITypes;

namespace KompasMcp.Tools
{
  // 2D-чертёжный домен. Идиомы — из проверенного flange_draw.cs (memory: kompas3d-automation):
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
      docPath = ToolRegistry.GetStr(a, "path", Paths.Out("drawing.cdw"));
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
      object ptsObj;
      if (!a.TryGetValue("points", out ptsObj)) throw new ToolException("Нет points");
      List<object> pts = ptsObj as List<object>;
      if (pts == null || pts.Count < 2) throw new ToolException("points: минимум 2 точки");

      int n = closed ? pts.Count : pts.Count - 1;
      int made = 0;
      for (int i = 0; i < n; i++)
      {
        List<object> p1 = pts[i] as List<object>;
        List<object> p2 = pts[(i + 1) % pts.Count] as List<object>;
        d.ksLineSeg(Num(p1, 0), Num(p1, 1), Num(p2, 0), Num(p2, 1), style);
        made++;
      }
      return new Dictionary<string, object> { { "segments", made } };
    }

    static double Num(List<object> p, int idx)
    {
      if (p == null || p.Count <= idx) throw new ToolException("Точка должна быть парой [x,y]");
      return ToDbl(p[idx]);
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

      // 1) контур заранее обычными объектами (style 1) — линии внутри блока штриховки не рендерятся
      DrawContour(d, contour, 1);

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