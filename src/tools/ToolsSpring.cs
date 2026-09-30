using System;
using System.Collections.Generic;
using Kompas6API5;
using Kompas6Constants3D;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Пружины (Фаза 6). spring_calc - расчёт ГОСТ 13765 без КОМПАСа;
  // spring_create - 3D: o3d_cylindricSpiral (траектория) + o3d_bossEvolution (проволока - круг по спирали).
  public static class ToolsSpring
  {
    public static void Register()
    {
      ToolRegistry.Add("spring_calc",
        "Расчёт винтовой цилиндрической пружины (ГОСТ 13765): индекс, жёсткость, коэффициент Валя, высота, длина проволоки.",
        @"{""type"":""object"",""properties"":{
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d (мм)""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр витка D (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*coilDiameter если не задан)""},
""G"":{""type"":""number"",""description"":""Модуль сдвига, МПа (78500 по умолчанию)""}}}",
        a => SpringCalc(a));

      ToolRegistry.Add("spring_drawing",
        "2D-чертёж винтовой цилиндрической пружины сжатия по ГОСТ 2.401: сечения витков (окружности с штриховкой), видимые передние полувитки наклонными линиями, опорные плоскости, размеры (H0, d, D, наружный, t), таблица параметров. Параметры — как у spring_calc.",
        @"{""type"":""object"",""properties"":{
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d (мм)""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр витка D (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*D если не задан)""},
""scale"":{""type"":""number"",""description"":""Масштаб вида (по умолчанию 1)""},
""path"":{""type"":""string"",""description"":""Путь сохранения .cdw""},
""x"":{""type"":""number"",""description"":""X оси пружины на листе (по умолчанию 150)""},
""y"":{""type"":""number"",""description"":""Y нижней опорной плоскости на листе (по умолчанию 90)""}}}",
        a => SpringDrawing(a));

      ToolRegistry.Add("spring_create",
        "Создать 3D-пружину сжатия: спираль o3d_cylindricSpiral + выдавливание круга проволоки по траектории.",
        @"{""type"":""object"",""properties"":{
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d (мм)""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр витка D (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*coilDiameter если не задан)""},
""name"":{""type"":""string""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d""}}}",
        a => SpringCreate(a));
    }

    static object SpringCalc(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "wireDiameter");
      double D = ToolRegistry.GetDbl(a, "coilDiameter");
      int n = ToolRegistry.GetInt(a, "n");
      double? t = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      double? g = a.ContainsKey("G") ? (double?)ToolRegistry.GetDbl(a, "G") : null;
      Gost13765.Spring s = Gost13765.Calc(d, D, n, t, g);
      return new Dictionary<string, object>
      {
        { "index_c", Math.Round(s.IndexC, 3) },
        { "wahl_kc", Math.Round(s.Wahl, 4) },
        { "stiffness_N_per_mm", Math.Round(s.Stiffness, 3) },
        { "h0_mm", Math.Round(s.H0, 2) },
        { "t_mm", s.T },
        { "total_coils", s.TotalCoils },
        { "wire_len_mm", Math.Round(s.WireLen, 1) }
      };
    }

    // ---- чертёж пружины (ГОСТ 2.401) ----

    // Контур окружности-сечения для штриховки: 24-угольник из ОТРЕЗКОВ.
    // Грабля КОМПАСа: контур hatch из дуг (ksArcByPoint) ненадёжен — заливка
    // перетекает наружу блобом при двух и более hatches в одном виде;
    // контур из отрезков работает стабильно. Отклонение 24-угольника от
    // окружности при r<10 мм < 0.1 мм — незаметно.
    static List<object> CircleContour(double xc, double yc, double r)
    {
      List<object> contour = new List<object>();
      int nSeg = 24;
      double prevX = xc + r, prevY = yc;
      for (int i = 1; i <= nSeg; i++)
      {
        double ang = 2.0 * Math.PI * i / nSeg;
        double x = xc + r * Math.Cos(ang), y = yc + r * Math.Sin(ang);
        Dictionary<string, object> seg = new Dictionary<string, object>();
        seg["type"] = "line";
        seg["x1"] = prevX; seg["y1"] = prevY; seg["x2"] = x; seg["y2"] = y;
        contour.Add(seg);
        prevX = x; prevY = y;
      }
      return contour;
    }

    // Сечение витка: окружность радиуса d/2 с центром (xc, zc).
    // Грабля: дуги style 1, нарисованные до/после блока штриховки с контуром
    // из дуг, ломают заливку (перетекает блобом) — поэтому окружность рисуется
    // ksCircle (style 1), а hatch идёт с outline=false (контур — только внутри блока).
    static void SpringCoil(ksDocument2D d, double xc, double zc, double wireD, Dictionary<string, object> hatchArgs)
    {
      d.ksCircle(xc, zc, wireD / 2.0, 1);
      hatchArgs["contour"] = CircleContour(xc, zc, wireD / 2.0);
      hatchArgs["outline"] = false;
      hatchArgs["x0"] = xc;
      hatchArgs["y0"] = zc;
      Tools2D.Hatch(hatchArgs);
    }

    static object SpringDrawing(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "wireDiameter");
      double D = ToolRegistry.GetDbl(a, "coilDiameter");
      int n = ToolRegistry.GetInt(a, "n");
      double? tArg = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      Gost13765.Spring s = Gost13765.Calc(d, D, n, tArg, null);
      double t = s.T;
      double scale = ToolRegistry.GetDbl(a, "scale", 1);
      if (scale <= 0) throw new ToolException("scale должен быть > 0");
      double x0 = ToolRegistry.GetDbl(a, "x", 150);
      double y0 = ToolRegistry.GetDbl(a, "y", 90);

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Пружина d" + Tools2D.FmtNum(d) + " D" + Tools2D.FmtNum(D);
      Tools2D.CreateDrawing(createArgs);
      Tools2D.MakeView(x0, y0, scale, "Разрез пружины");

      ksDocument2D d2d = Tools2D.GetDoc();
      double H0 = s.H0;
      double rHalf = (D + d) / 2.0;      // наружный радиус на виде
      double zTop = H0 - d / 2.0;        // центр сечений верхнего опорного витка

      // штриховка: один и тот же набор параметров для всех сечений
      Dictionary<string, object> hatchArgs = new Dictionary<string, object>();
      hatchArgs["angle"] = 45.0;
      hatchArgs["step"] = Math.Min(2.0, d / 2.0);

      // опорные витки: сечения прижаты к опорным плоскостям, z=0.5d и H0-0.5d
      double[] supportZ = new double[] { d / 2.0, H0 - d / 2.0 };
      double[] supportX = new double[] { -D / 2.0, D / 2.0 };
      for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
          SpringCoil(d2d, supportX[j], supportZ[i], d, hatchArgs);

      // рабочие витки: правые z=d+(j-0.5)t, левые z=d+j*t (левое выше правого на t/2)
      double sin45 = Math.Sin(Math.PI / 4.0)* (d/2.0);
      double cos45 = Math.Cos(Math.PI / 4.0)* (d/2.0);
      double zLastL = 0, zLastR = 0;
      for (int j = 1; j <= n; j++)
      {
        double zr = d + (j - 0.5) * t;
        double zl = d + j * t;
        SpringCoil(d2d, D / 2.0, zr, d, hatchArgs);
        SpringCoil(d2d, -D / 2.0, zl, d, hatchArgs);
        // видимый передний полувиток: от правого сечения вверх-влево к левому
        d2d.ksLineSeg(D / 2.0 - cos45, zr + sin45, -D / 2.0 + cos45, zl + sin45, 1);
        zLastR = zr; zLastL = zl;
      }
      // переход от опорного витка к первому рабочему: крутая линия от правого
      // опорного сечения вверх-влево к первому левому рабочему.
      // Наверху соединительная линия к верхнему опорному НЕ рисуется:
      // это задняя (невидимая) сторона полувитка.
      d2d.ksLineSeg(D / 2.0 - cos45, d / 2.0 + sin45, -D / 2.0 + cos45, d + t + sin45, 1);

      // опорные плоскости (сплошная основная)
      d2d.ksLineSeg(-rHalf, 0, rHalf, 0, 1);
      d2d.ksLineSeg(-rHalf, H0, rHalf, H0, 1);
      // ось пружины
      d2d.ksLineSeg(0, -10, 0, H0 + 10, 3);

      // размеры: H0 слева снаружи, ⌀D и ⌀(D+d) снизу, t справа снаружи, ⌀d хордой сечения
      Dictionary<string, object> dimH0 = new Dictionary<string, object>();
      dimH0["x1"] = -rHalf; dimH0["y1"] = 0; dimH0["x2"] = -rHalf; dimH0["y2"] = H0;
      dimH0["ang"] = 90.0; dimH0["dx"] = -12.0; dimH0["dy"] = 0.0;
      Tools2D.LinDim(dimH0);

      Dictionary<string, object> dimD = new Dictionary<string, object>();
      dimD["x1"] = -D / 2.0; dimD["y1"] = 0; dimD["x2"] = D / 2.0; dimD["y2"] = 0;
      dimD["ang"] = 0.0; dimD["dx"] = 0.0; dimD["dy"] = -14.0; dimD["sign"] = 1;
      Tools2D.LinDim(dimD);

      Dictionary<string, object> dimDa = new Dictionary<string, object>();
      dimDa["x1"] = -rHalf; dimDa["y1"] = 0; dimDa["x2"] = rHalf; dimDa["y2"] = 0;
      dimDa["ang"] = 0.0; dimDa["dx"] = 0.0; dimDa["dy"] = -30.0; dimDa["sign"] = 1;
      Tools2D.LinDim(dimDa);

      Dictionary<string, object> dimT = new Dictionary<string, object>();
      dimT["x1"] = D / 2.0 + d / 2.0; dimT["y1"] = zLastR - t; dimT["x2"] = D / 2.0 + d / 2.0; dimT["y2"] = zLastR;
      dimT["ang"] = 90.0; dimT["dx"] = 14.0; dimT["dy"] = 0.0; dimT["prefix"] = "t ";
      Tools2D.LinDim(dimT);

      Dictionary<string, object> dimW = new Dictionary<string, object>();
      dimW["x1"] = -D / 2.0 - d / 2.0; dimW["y1"] = d / 2.0; dimW["x2"] = -D / 2.0 + d / 2.0; dimW["y2"] = d / 2.0;
      dimW["ang"] = 0.0; dimW["dx"] = 0.0; dimW["dy"] = -8.0; dimW["sign"] = 1;
      Tools2D.LinDim(dimW);

      // таблица параметров (ГОСТ 2.401, масштаб 1 — мм листа)
      Tools2D.MakeView(258.0, 130.0, 1.0, "Таблица параметров");
      List<string[]> rows = new List<string[]>();
      rows.Add(new string[] { "Диаметр проволоки", Tools2D.FmtNum(d) });
      rows.Add(new string[] { "Средний диаметр витка", Tools2D.FmtNum(D) });
      rows.Add(new string[] { "Шаг витка", Tools2D.FmtNum(t) });
      rows.Add(new string[] { "Рабочих витков", n.ToString(System.Globalization.CultureInfo.InvariantCulture) });
      rows.Add(new string[] { "Полных витков", s.TotalCoils.ToString(System.Globalization.CultureInfo.InvariantCulture) });
      rows.Add(new string[] { "Направление навивки", "правое" });
      rows.Add(new string[] { "Длина развёрнутой проволоки", Tools2D.FmtNum(Math.Round(s.WireLen, 1)) });

      double col1 = 105, rowH = 10;
      double tw = col1 + 60, th = rows.Count * rowH;
      for (int i = 0; i <= rows.Count; i++)
        d2d.ksLineSeg(0, i * rowH, tw, i * rowH, 1);
      d2d.ksLineSeg(0, 0, 0, th, 1);
      d2d.ksLineSeg(col1, 0, col1, th, 1);
      d2d.ksLineSeg(tw, 0, tw, th, 1);
      for (int i = 0; i < rows.Count; i++)
      {
        string[] row = rows[i];
        double top = th - i * rowH;
        d2d.ksText(2, top - rowH / 2.0 - 1.8, 0, 5, 1, 0, row[0]);
        d2d.ksText(col1 + 2, top - rowH / 2.0 - 1.8, 0, 5, 1, 0, row[1]);
      }

      Dictionary<string, object> res = new Dictionary<string, object>();
      res["path"] = Tools2D.DocPath;
      res["h0_mm"] = Math.Round(H0, 2);
      res["t_mm"] = Math.Round(t, 2);
      double topY = y0 + scale * (H0 + 10);
      double leftX = x0 + scale * (-rHalf - 12.0 - 15.0);
      double rightX = x0 + scale * (rHalf + 14.0 + 20.0);
      if (topY > 285 || leftX < 25 || rightX > 250)
      {
        res["warning"] = "вид пружины/размеры близки к границам листа (" +
          Math.Round(leftX, 0) + ".." + Math.Round(rightX, 0) + ", сверху до " + Math.Round(topY, 0) +
          ") — подберите scale/x/y";
      }
      return res;
    }

    static object SpringCreate(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "wireDiameter");
      double D = ToolRegistry.GetDbl(a, "coilDiameter");
      int n = ToolRegistry.GetInt(a, "n");
      double? t = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      Gost13765.Spring s = Gost13765.Calc(d, D, n, t, null);

      string name = ToolRegistry.GetStr(a, "name", "Пружина d" + d + " D" + D);
      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      createArgs["name"] = name;
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      Tools3D.CreatePart(createArgs);

      ksPart p = Tools3D.GetPart();

      // траектория: цилиндрическая спираль на XOY
      ksEntity spiral = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cylindricSpiral);
      ksCylindricSpiralDefinition spDef = (ksCylindricSpiralDefinition)spiral.GetDefinition();
      spDef.SetPlane(Tools3D.PlaneByName(p, "XOY"));
      spDef.SetLocation(0, 0);
      spDef.turn = s.TotalCoils;
      spDef.step = s.T;
      spDef.diam = D;
      if (!spiral.Create()) throw new ToolException("cylindricSpiral.Create вернул 0");

      // проволока: круг d/2 в XOZ в точке старта спирали (D/2, 0)
      Dictionary<string, object> sk = new Dictionary<string, object>();
      sk["plane"] = "XOZ";
      Dictionary<string, object> circ = new Dictionary<string, object>();
      circ["type"] = "circle";
      circ["xc"] = Math.Round(D / 2.0, 4);
      circ["yc"] = 0.0;
      circ["r"] = d / 2.0;
      List<object> els = new List<object>();
      els.Add(circ);
      sk["elements"] = els;
      Tools3D.Sketch(sk);
      ksEntity skEntity = Tools3D.LastSketch;

      // эволюция: круг по спирали
      ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossEvolution);
      ksBossEvolutionDefinition def = (ksBossEvolutionDefinition)op.GetDefinition();
      def.SetSketch(skEntity);
      ksEntityCollection pathArr = (ksEntityCollection)def.PathPartArray();
      pathArr.Add(spiral);
      if (!op.Create()) throw new ToolException("bossEvolution.Create вернул 0");
      p.RebuildModel();

      Dictionary<string, object> mat = new Dictionary<string, object>();
      mat["name"] = "Сталь пружинная 60С2А ГОСТ 14963-78";
      mat["density"] = 7850.0;
      Tools3D.SetMaterialOp(mat);

      Dictionary<string, object> saveArgs = new Dictionary<string, object>();
      if (path != null) saveArgs["path"] = path;
      Tools3D.SavePart(saveArgs);

      return new Dictionary<string, object>
      {
        { "saved", saveArgs.ContainsKey("path") ? saveArgs["path"] : Tools3D.PartPath },
        { "h0_mm", s.H0 }, { "t_mm", s.T }, { "total_coils", s.TotalCoils },
        { "stiffness_N_per_mm", Math.Round(s.Stiffness, 3) },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }
  }
}