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
        "2D-чертёж винтовой цилиндрической пружины сжатия по ГОСТ 2.401: сечения витков со штриховкой, видимые передние полувитки, опорные плоскости, размеры (H0, d, D, наружный, t), технические требования (навивка, число витков, длина развёрнутой проволоки), штамп. При заданных F1/F2 — диаграмма испытаний (H1..H3, F1..F3) и силы в технических требованиях (расчёт как у spring_check). Параметры — как у spring_calc.",
        @"{""type"":""object"",""properties"":{
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d (мм)""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр витка D (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*D если не задан)""},
""F1"":{""type"":""number"",""description"":""Сила предварительной деформации, Н — вместе с F2 включает диаграмму испытаний""},
""F2"":{""type"":""number"",""description"":""Сила рабочей деформации, Н""},
""tauAllow"":{""type"":""number"",""description"":""Допускаемое напряжение при F3, МПа (деф 785)""},
""scale"":{""type"":""number"",""description"":""Масштаб вида (по умолчанию — наибольший стандартный, при котором лист вмещает вид)""},
""x"":{""type"":""number"",""description"":""X оси пружины на листе (по умолчанию — по центру поля)""},
""y"":{""type"":""number"",""description"":""Y нижней опорной плоскости на листе""},
""material"":{""type"":""string""},""designation"":{""type"":""string"",""description"":""Обозначение в штамп""},
""tech"":{""type"":""array"",""items"":{""type"":""string""},""description"":""Технические требования (по умолчанию — навивка, витки, длина проволоки, твёрдость)""},
""path"":{""type"":""string"",""description"":""Путь сохранения .cdw""},
""png"":{""type"":""string"",""description"":""Путь PNG-рендера листа""}},
""required"":[""wireDiameter"",""coilDiameter"",""n""]}",
        a => SpringDrawing(a));

      ToolRegistry.Add("spring_create",
        "3D-модель винтовой цилиндрической пружины (навивка правая). compression — сжатия: опорные витки с шагом d, рабочие с шагом t, торцы сошлифованы, высота H0 как на чертеже. extension — растяжения: витки вплотную, зацепы (прямой участок + полупетля диаметром D) на обоих концах. Масса — с переоткрытием файла (у кинематических тел КОМПАС иначе отдаёт 0).",
        @"{""type"":""object"",""properties"":{
""type"":{""type"":""string"",""enum"":[""compression"",""extension""],""description"":""По умолчанию compression""},
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d (мм)""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр витка D (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг рабочих витков сжатия (0.3*coilDiameter если не задан)""},
""name"":{""type"":""string""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d""}},
""required"":[""wireDiameter"",""coilDiameter"",""n""]}",
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
      // при заданных F1/F2 — диаграмма испытаний справа и силы в технических требованиях
      bool loads = a.ContainsKey("F1") && a.ContainsKey("F2");
      Gost13765.Spring s;
      Strength.SpringResult load = null;
      if (loads) load = ToolsCheck.SpringLoads(a, out s);
      else
      {
        double? tArg = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
        try { s = Gost13765.Calc(d, D, n, tArg, null); }
        catch (ArgumentException e) { throw new ToolException(e.Message); }
      }
      double t = s.T;
      double rOut = (D + d) / 2.0;
      double[] fit = DrawKit.Fit(-rOut, rOut, 0, s.H0, 30, loads ? 125 : 34, 42, 12, ToolRegistry.GetDbl(a, "scale", 0));
      double scale = fit[2];
      double x0 = ToolRegistry.GetDbl(a, "x", fit[0]);
      double y0 = ToolRegistry.GetDbl(a, "y", fit[1]);

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Пружина d" + Tools2D.FmtNum(d) + " D" + Tools2D.FmtNum(D);
      Tools2D.CreateDrawing(createArgs);
      Tools2D.MakeView(x0, y0, scale, "Разрез пружины");

      ksDocument2D d2d = Tools2D.GetDoc();
      double H0 = s.H0;
      double rHalf = (D + d) / 2.0;      // наружный радиус на виде

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

      // Сечения вдоль витка чередуются справа/слева через t/2: опорные (обе стороны у плоскости),
      // правые рабочие z = d+(j−0.5)t (j = 1..n), левые z = d+j·t (j = 1..n−1) — так концы
      // симметричны и последнее левое не налезает на верхнее опорное. Видимы передние полувитки
      // (справа вверх-влево); последний правый уходит в верхнее опорное слева.
      double sin45 = Math.Sin(Math.PI / 4.0) * (d / 2.0);
      double cos45 = Math.Cos(Math.PI / 4.0) * (d / 2.0);
      double zLastR = 0;
      for (int j = 1; j <= n; j++)
      {
        double zr = d + (j - 0.5) * t;
        double zl = j < n ? d + j * t : H0 - d / 2.0;
        SpringCoil(d2d, D / 2.0, zr, d, hatchArgs);
        if (j < n) SpringCoil(d2d, -D / 2.0, zl, d, hatchArgs);
        d2d.ksLineSeg(D / 2.0 - cos45, zr + sin45, -D / 2.0 + cos45, zl + sin45, 1);
        zLastR = zr;
      }

      // опорные плоскости (сплошная основная)
      d2d.ksLineSeg(-rHalf, 0, rHalf, 0, 1);
      d2d.ksLineSeg(-rHalf, H0, rHalf, H0, 1);
      // ось пружины
      d2d.ksLineSeg(0, -6 / scale, 0, H0 + 6 / scale, 3);

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

      if (load != null) TestDiagram(d2d, s, load, rHalf, scale);

      // по ГОСТ 2.401 характеристики витков — в технических требованиях
      Dictionary<string, object> res = new Dictionary<string, object>();
      res["h0_mm"] = Math.Round(H0, 2);
      res["t_mm"] = Math.Round(t, 2);
      List<string> tech = DrawKit.TechItems(a,
        "Направление навивки правое.",
        "Число рабочих витков n = " + n + ".",
        "Число витков полное n1 = " + s.TotalCoils.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
        "Длина развёрнутой пружины L = " + Tools2D.FmtNum(Math.Round(s.WireLen, 1)) + " мм.",
        "44...50 HRC.");
      if (load != null)
      {
        if (!a.ContainsKey("tech")) tech.AddRange(ToolsCheck.SpringTech(load));
        res["F3_N"] = Math.Round(load.F3, 1);
        res["check_ok"] = load.Ok;
      }
      DrawKit.Finish(a, res, "Пружина", "Проволока 60С2А ГОСТ 14963-78", scale, tech, 292.0);
      return res;
    }

    // Диаграмма испытаний (ГОСТ 2.401): ось сил — на уровне опорной плоскости, характеристика —
    // от (0; H0) до (F3; H3); для F1..F3 — линии высот от пружины, выноски к оси сил и размеры H1..H3.
    static void TestDiagram(ksDocument2D d, Gost13765.Spring s, Strength.SpringResult r, double rOut, double scale)
    {
      double x0 = rOut + 30 / scale, w = 45 / scale, from = rOut + 2 / scale;
      double[] f = { r.F1, r.F2, r.F3 };
      double[] h = { r.H1, r.H2, r.H3 };
      d.ksLineSeg(x0, 0, x0 + w + 6 / scale, 0, 2);
      d.ksLineSeg(from, s.H0, x0, s.H0, 2);
      d.ksLineSeg(x0, s.H0, x0 + w, r.H3, 1);
      for (int i = 0; i < 3; i++)
      {
        double x = x0 + w * f[i] / r.F3;
        d.ksLineSeg(from, h[i], x, h[i], 2);
        d.ksLineSeg(x, h[i], x, 0, 2);
        d.ksText(x - 2.5 / scale, -6 / scale, 0, 3.5, 1, 0, "F" + (i + 1));
        DrawKit.Dim(x0 + w + 6 / scale, 0, x, h[i], true, 10 + 9 * i);
      }
    }

    static object SpringCreate(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "wireDiameter");
      double D = ToolRegistry.GetDbl(a, "coilDiameter");
      int n = ToolRegistry.GetInt(a, "n");
      string type = ToolRegistry.GetStr(a, "type", "compression");
      if (type != "compression" && type != "extension") throw new ToolException("type: compression или extension");
      bool extension = type == "extension";
      double? t = extension ? (double?)d : a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      Gost13765.Spring s;
      try { s = Gost13765.Calc(d, D, n, t, null); }
      catch (ArgumentException e) { throw new ToolException(e.Message); }

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      createArgs["name"] = ToolRegistry.GetStr(a, "name",
        (extension ? "Пружина растяжения d" : "Пружина d") + Tools2D.FmtNum(d) + " D" + Tools2D.FmtNum(D));
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      Tools3D.CreatePart(createArgs);

      Dictionary<string, object> res = new Dictionary<string, object>();
      if (extension) BuildExtension(d, D, n, res);
      else BuildCompression(d, D, n, s.T, s.H0, res);

      Tools3D.SetMaterialOp(new Dictionary<string, object>
        { { "name", "Сталь пружинная 60С2А ГОСТ 14963-78" }, { "density", 7850.0 } });
      Dictionary<string, object> saveArgs = new Dictionary<string, object>();
      if (path != null) saveArgs["path"] = path;
      Tools3D.SavePart(saveArgs);
      res["saved"] = Tools3D.PartPath;
      res["type"] = type;
      res["stiffness_N_per_mm"] = Math.Round(s.Stiffness, 3);
      res["mass_kg"] = Tools3D.ReliableMass();
      return res;
    }

    // Сжатия: опорные витки с шагом d по концам, n рабочих с шагом t, торцы сошлифованы
    // плоскостями z = 0 и z = H0 (как на чертеже ГОСТ 2.401). Три спирали — три кинематических
    // тела: смена шага на одной траектории даёт излом, который выдавливание не проходит.
    static void BuildCompression(double d, double D, int n, double t, double H0, Dictionary<string, object> res)
    {
      ksPart p = Tools3D.GetPart();
      double[] z0 = { 0, d, d + n * t };
      double[] step = { d, t, d };
      double[] turns = { 1, n, 1 };
      for (int i = 0; i < 3; i++)
      {
        ksEntity plane = i == 0 ? Tools3D.PlaneByName(p, "XOY") : Tools3D.OffsetPlane("XOY", z0[i]);
        ksEntity spiral = Tools3D.Spiral(plane, D, step[i], turns[i], rightHand: true);
        ksEntity wire = WireSection(D / 2.0, z0[i], d);
        Tools3D.Evolution(wire, spiral, cut: false);
      }
      // шлифовка торцов: срезаем половину проволоки ниже 0 и выше H0
      Grind(D, d, 0, -1);
      Grind(D, d, H0, 1);
      res["h0_mm"] = H0;
      res["t_mm"] = t;
      res["total_coils"] = n + 2;
    }

    // Растяжения: витки вплотную (шаг d), зацепы — прямой участок d и полупетля диаметром D
    // в осевой плоскости XOZ (вращение сечения проволоки вокруг оси, параллельной Y).
    static void BuildExtension(double d, double D, int n, Dictionary<string, object> res)
    {
      ksPart p = Tools3D.GetPart();
      double r = D / 2.0, top = n * d, leg = d;
      Leg(r, -leg, leg, d, baseOp: true);
      Tools3D.RevolveCircle(Tools3D.OffsetPlane("XOY", -leg), r, 0, d / 2.0,
        new[] { 0.0, -D }, new[] { 0.0, D }, 180, reverse: true);
      ksEntity spiral = Tools3D.Spiral(Tools3D.PlaneByName(p, "XOY"), D, d, n, rightHand: true);
      Tools3D.Evolution(WireSection(r, 0, d), spiral, cut: false);
      Leg(r, top, leg, d, baseOp: false);
      Tools3D.RevolveCircle(Tools3D.OffsetPlane("XOY", top + leg), r, 0, d / 2.0,
        new[] { 0.0, -D }, new[] { 0.0, D }, 180);
      double hook = leg + r + d / 2.0;
      res["body_length_mm"] = (n + 1) * d;
      res["length_mm"] = top + 2 * hook;
      res["hook_inner_mm"] = top + 2 * (leg + r) - d;
      res["coils"] = n;
    }

    // круг проволоки в осевой плоскости XOZ в точке (x, z) — начало спирали (y эскиза = −z)
    static ksEntity WireSection(double x, double z, double d)
    {
      Tools3D.Sketch(new Dictionary<string, object>
      {
        { "plane", "XOZ" },
        { "elements", new List<object> { new Dictionary<string, object>
          { { "type", "circle" }, { "xc", x }, { "yc", -z }, { "r", d / 2.0 } } } }
      });
      return Tools3D.LastSketch;
    }

    static void Leg(double x, double z0, double len, double d, bool baseOp)
    {
      Dictionary<string, object> sk = new Dictionary<string, object>
      {
        { "plane", "XOY" },
        { "elements", new List<object> { new Dictionary<string, object>
          { { "type", "circle" }, { "xc", x }, { "yc", 0.0 }, { "r", d / 2.0 } } } }
      };
      if (z0 != 0) sk["offset"] = z0;
      Tools3D.Sketch(sk);
      Tools3D.Extrude(new Dictionary<string, object> { { "depth", len }, { "base", baseOp } }, cut: false);
    }

    static void Grind(double D, double d, double z, int dir)
    {
      Dictionary<string, object> sk = new Dictionary<string, object>
      {
        { "plane", "XOY" },
        { "elements", new List<object> { new Dictionary<string, object>
          { { "type", "circle" }, { "xc", 0.0 }, { "yc", 0.0 }, { "r", D } } } }
      };
      if (z != 0) sk["offset"] = z;
      Tools3D.Sketch(sk);
      Tools3D.Extrude(new Dictionary<string, object> { { "mode", "blind" }, { "depth", d }, { "direction", dir } }, cut: true);
    }
  }
}