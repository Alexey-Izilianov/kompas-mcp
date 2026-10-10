using System;
using System.Collections.Generic;
using System.Globalization;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Чертежи конических и червячных передач (ГОСТ 2.402, 2.405, 2.406): главное изображение
  // (осевой разрез колёс; вид червяка с местным разрезом профиля витков), размеры, таблица
  // параметров, технические требования; оформление — DrawKit. Ось детали на листе горизонтальна:
  // x — вдоль оси, y — радиус. Дуги горловины — ломаными: они же граница штриховки сечения.
  public static class ToolsBevelWorm
  {
    const string Common = @",
""scale"":{""type"":""number"",""description"":""Масштаб вида (по умолчанию — наибольший стандартный, при котором лист вмещает вид)""},
""accuracy"":{""type"":""string"",""description"":""Степень точности по ГОСТ 1643/1758/3675""},
""material"":{""type"":""string"",""description"":""Материал в штамп""},
""designation"":{""type"":""string"",""description"":""Обозначение в штамп""},
""tech"":{""type"":""array"",""items"":{""type"":""string""},""description"":""Технические требования (по умолчанию типовые)""},
""path"":{""type"":""string"",""description"":""Путь .cdw""},
""png"":{""type"":""string"",""description"":""Путь PNG-рендера листа""}}";

    static readonly double TanAlpha = Math.Tan(20.0 * Math.PI / 180.0);

    public static void Register()
    {
      ToolRegistry.Add("bevel_gear_drawing",
        "Чертёж прямозубого конического колеса (ГОСТ 2.405): осевой разрез с внешним и внутренним дополнительными конусами и ступицей, делительный конус до вершины, размеры (Ø вершин, отверстия, ступицы, ширина венца, базовое расстояние), таблица параметров, технические требования. Геометрия — как у bevel_gear_wheel.",
        @"{""type"":""object"",""properties"":{
""me"":{""type"":""number"",""description"":""Внешний окружной модуль""},
""z"":{""type"":""integer"",""description"":""Число зубьев (8..200)""},
""delta"":{""type"":""number"",""description"":""Угол делительного конуса, град (5..85)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.3·Re)""},
""bore"":{""type"":""number"",""description"":""Диаметр отверстия""},
""hubL"":{""type"":""number"",""description"":""Длина ступицы за тыльным торцом (деф 0.5·bore, 0 — без ступицы)""}" + Common + @",
""required"":[""me"",""z"",""delta"",""bore""]}",
        a => BevelDrawing(a));

      ToolRegistry.Add("worm_drawing",
        "Чертёж архимедова червяка (ГОСТ 2.406, 19036): вид с линиями вершин, впадин и делительной, местный разрез с осевым профилем витков, размеры, таблица параметров, технические требования.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Осевой модуль""},
""z1"":{""type"":""integer"",""description"":""Число заходов (1..4)""},
""q"":{""type"":""number"",""description"":""Коэффициент диаметра (6..25)""},
""z2"":{""type"":""integer"",""description"":""Число зубьев сопряжённого колеса (деф 40, для длины нарезки)""},
""length"":{""type"":""number"",""description"":""Длина нарезанной части (деф (4+0.2·z2)·m)""},
""journalR"":{""type"":""number"",""description"":""Радиус шеек (деф 0.75·df1/2)""},
""journalL"":{""type"":""number"",""description"":""Длина шеек (деф 2.8·m+6)""}" + Common + @",
""required"":[""m"",""z1"",""q""]}",
        a => WormDrawing(a));

      ToolRegistry.Add("worm_wheel_drawing",
        "Чертёж червячного колеса (ГОСТ 2.406): осевой разрез с горловиной (дуги вершин и впадин с центром на оси червяка), размеры (наибольший диаметр, отверстие, ширина венца, радиус горловины), таблица параметров, технические требования. Геометрия — как у worm_wheel.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number""},""z1"":{""type"":""integer""},""q"":{""type"":""number""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса (20..400)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.75·da1, z1=4: 0.67·da1)""},
""bore"":{""type"":""number"",""description"":""Диаметр отверстия""}" + Common + @",
""required"":[""m"",""z1"",""q"",""z2"",""bore""]}",
        a => WormWheelDrawing(a));
    }

    // ================== коническое колесо ==================

    static object BevelDrawing(Dictionary<string, object> a)
    {
      double me = ToolRegistry.GetDbl(a, "me");
      int z = ToolRegistry.GetInt(a, "z");
      double delta = ToolRegistry.GetDbl(a, "delta");
      double bore = ToolRegistry.GetDbl(a, "bore");
      BevelBlank b = BevelGears.Blank(me, z, delta, ToolRegistry.GetDbl(a, "width", 0), bore,
        ToolRegistry.GetDbl(a, "hubL", 0.5 * bore));
      BevelWheel w = b.W;
      double xEnd = b.EndZ, rc = b.Te[0];
      double[] f = DrawKit.Fit(0, xEnd, -rc, rc, 12, 52, 30, 20, ToolRegistry.GetDbl(a, "scale", 0));
      double s = f[2];

      CreateDrawing(a, "Колесо коническое me" + F(me) + " z" + z);
      Tools2D.MakeView(f[0], f[1], s, "Осевой разрез");

      // сечение: x = z модели, y = r (вершина делительного конуса — слева в начале координат)
      List<double[]> half = Swap(b.Outline());
      SectionWithBore(half, b.Rb);
      foreach (bool m in new[] { false, true })
      {
        DrawKit.Poly(new[] { Swap(b.Fi), Swap(b.Fe) }, 1, false, m);       // впадины — основной в разрезе
        DrawKit.Poly(new[] { Swap(b.Pe), new[] { 0.0, 0.0 } }, 3, false, m); // делительный конус до вершины
      }
      Axis(-4 / s, xEnd + 5 / s);

      // размеры: диаметры — справа, длины — снизу, ширина венца — вдоль образующей вершин
      DrawKit.Dim(xEnd, b.Rb, xEnd, -b.Rb, true, 11, true);
      if (b.HubL > 0) DrawKit.Dim(xEnd, b.Rh, xEnd, -b.Rh, true, 21, true);
      DrawKit.Dim(b.Te[1], rc, b.Te[1], -rc, true, (xEnd - b.Te[1]) * s + 30, true);
      DrawKit.Dim(b.Te[1], -rc, b.BackZ, -b.Ge[0], false, -8);
      DrawKit.Dim(b.Ti[1], -b.Ti[0], xEnd, -b.Rb, false, -((rc - b.Ti[0]) * s + 18));
      // ширина венца — вдоль образующей конуса вершин, сдвиг по нормали наружу
      double ang = Math.Atan2(b.Te[0] - b.Ti[0], b.Te[1] - b.Ti[1]);
      Tools2D.LinDim(new Dictionary<string, object>
      {
        { "x1", b.Ti[1] }, { "y1", b.Ti[0] }, { "x2", b.Te[1] }, { "y2", b.Te[0] },
        { "ang", ang * 180.0 / Math.PI }, { "dx", -Math.Sin(ang) * 8 }, { "dy", Math.Cos(ang) * 8 }
      });

      double mate = BevelGears.MateDelta(delta);
      double tableBottom = DrawKit.ParamTable(new List<string[]> {
        new[] { "Модуль внешний окружной", "me", F(me) },
        new[] { "Число зубьев", "z", I(z) },
        new[] { "Тип зуба", "", "прямой" },
        new[] { "Исходный контур", "", "ГОСТ 13754-81" },
        new[] { "Коэффициент смещения", "xe", "0" },
        new[] { "Угол делительного конуса", "", F(delta) + "°" },
        new[] { "Межосевой угол передачи", "", "90°" },
        new[] { "Степень точности", "", ToolRegistry.GetStr(a, "accuracy", "8-B ГОСТ 1758-81") },
        new[] { "Внешнее конусное расстояние", "Re", F(w.Ri) },
        new[] { "Средний делительный диаметр", "dm", F(w.De - w.B * Math.Sin(delta * Math.PI / 180.0)) },
        new[] { "Внешняя высота зуба", "he", F(w.Ha + w.Hf) },
        new[] { "Угол делит. конуса сопряж. колеса", "", F(mate) + "°" }
      }.ConvertAll(Row));

      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "dae", Math.Round(w.Dae, 3) }, { "de", w.De }, { "dfe", Math.Round(w.Dfe, 3) },
        { "Re", Math.Round(w.Ri, 3) }, { "width", Math.Round(w.B, 3) }
      };
      DrawKit.Finish(a, res, "Колесо коническое", "Сталь 40Х ГОСТ 4543-2016", s,
        DrawKit.TechItems(a, "235...262 НВ.", "Неуказанные предельные отклонения размеров: H14, h14, ±IT14/2."),
        tableBottom);
      return res;
    }

    // ================== червяк ==================

    static object WormDrawing(Dictionary<string, object> a)
    {
      WormPair g = Pair(a);
      double L = ToolRegistry.GetDbl(a, "length", WormGears.ThreadLength(g.M, g.Z2));
      double jL = ToolRegistry.GetDbl(a, "journalL", WormGears.JournalLength(g.M));
      double rf = g.Df1 / 2.0, ra = g.Da1 / 2.0, r1 = g.D1 / 2.0, P = g.P;
      double rj = ToolRegistry.GetDbl(a, "journalR", 0.75 * rf);
      if (rj <= 0 || rj >= rf) throw new ToolException("journalR должен быть в (0; df1/2=" + F(rf) + ")");
      if (L < 2 * P) throw new ToolException("Длина нарезки должна быть не меньше двух шагов (" + F(2 * P) + ")");
      double L2 = L / 2.0, xE = L2 + jL;
      double[] f = DrawKit.Fit(-xE, xE, -ra, ra, 28, 40, 34, 20, ToolRegistry.GetDbl(a, "scale", 0));
      double s = f[2];

      CreateDrawing(a, "Червяк m" + F(g.M) + " z1=" + g.Z1 + " q" + F(g.Q));
      Tools2D.MakeView(f[0], f[1], s, "Червяк");

      // местный разрез сверху посередине: витки по центрам 0 и ±P, границы разреза — по серединам
      // крайних витков, чтобы линия вершин переходила в профиль без разрыва
      double xs = P, yb = rf - 1.2 * g.M;
      double hwRoot = P / 4.0 + (r1 - rf) * TanAlpha, hwTip = P / 4.0 - (ra - r1) * TanAlpha;
      List<double[]> profile = new List<double[]> {
        new[] { -xs, ra }, new[] { -xs + hwTip, ra }, new[] { -xs + hwRoot, rf },
        new[] { -hwRoot, rf }, new[] { -hwTip, ra }, new[] { hwTip, ra }, new[] { hwRoot, rf },
        new[] { xs - hwRoot, rf }, new[] { xs - hwTip, ra }, new[] { xs, ra } };
      double amp = 0.8 / s;
      List<double[]> right = DrawKit.Wavy(new[] { xs, ra }, new[] { xs, yb }, amp);
      List<double[]> bottom = DrawKit.Wavy(new[] { xs, yb }, new[] { -xs, yb }, amp);
      List<double[]> left = DrawKit.Wavy(new[] { -xs, yb }, new[] { -xs, ra }, amp);
      List<double[]> region = new List<double[]>(profile);
      region.AddRange(right.GetRange(1, right.Count - 1));
      region.AddRange(bottom.GetRange(1, bottom.Count - 1));
      region.AddRange(left.GetRange(1, left.Count - 2));
      DrawKit.Hatch(region);
      DrawKit.Poly(profile, 1, false);
      DrawKit.Poly(right, 2, false);
      DrawKit.Poly(bottom, 2, false);
      DrawKit.Poly(left, 2, false);

      // контур вида: шейки, торцы нарезки, вершины (основная), впадины (тонкая), делительная (штрихпунктир)
      foreach (bool m in new[] { false, true })
      {
        DrawKit.Poly(new[] { new[] { -xE, rj }, new[] { -L2, rj }, new[] { -L2, ra } }, 1, false, m);
        DrawKit.Poly(new[] { new[] { L2, ra }, new[] { L2, rj }, new[] { xE, rj } }, 1, false, m);
        if (m)
        {
          DrawKit.Poly(new[] { new[] { -L2, ra }, new[] { L2, ra } }, 1, false, true);
          DrawKit.Poly(new[] { new[] { -L2, rf }, new[] { L2, rf } }, 2, false, true);
        }
        else
        {
          DrawKit.Poly(new[] { new[] { -L2, ra }, new[] { -xs, ra } }, 1, false);
          DrawKit.Poly(new[] { new[] { xs, ra }, new[] { L2, ra } }, 1, false);
          DrawKit.Poly(new[] { new[] { -L2, rf }, new[] { -xs, rf } }, 2, false);
          DrawKit.Poly(new[] { new[] { xs, rf }, new[] { L2, rf } }, 2, false);
        }
        DrawKit.Poly(new[] { new[] { -L2 - 2 / s, r1 }, new[] { L2 + 2 / s, r1 } }, 3, false, m);
      }
      Tools2D.GetDoc().ksLineSeg(-xE, rj, -xE, -rj, 1);
      Tools2D.GetDoc().ksLineSeg(xE, rj, xE, -rj, 1);
      Axis(-xE - 5 / s, xE + 5 / s);

      // размеры: шаг по профилю, Ø вершин и шейки, длины нарезки и общая — снизу
      DrawKit.Dim(0, ra, P, ra, false, 8);
      DrawKit.Dim(L2, ra, L2, -ra, true, (xE - L2) * s + 12, true);
      DrawKit.Dim(-xE, rj, -xE, -rj, true, -10, true);
      DrawKit.Dim(-L2, -ra, L2, -ra, false, -10);
      DrawKit.Dim(-xE, -rj, xE, -rj, false, -((ra - rj) * s + 20));

      double tableBottom = DrawKit.ParamTable(new List<string[]> {
        new[] { "Модуль", "m", F(g.M) },
        new[] { "Число витков", "z1", I(g.Z1) },
        new[] { "Вид червяка", "", "ZA" },
        new[] { "Делительный угол подъёма", "", Deg(g.Gamma) },
        new[] { "Направление линии витка", "", "правое" },
        new[] { "Исходный производящий червяк", "", "ГОСТ 19036-94" },
        new[] { "Степень точности", "", ToolRegistry.GetStr(a, "accuracy", "8-С ГОСТ 3675-81") },
        new[] { "Делительный диаметр", "d1", F(g.D1) },
        new[] { "Ход витка", "pz1", F(g.Pz) },
        new[] { "Межосевое расстояние", "aw", F(g.A) },
        new[] { "Число зубьев сопряж. колеса", "z2", I(g.Z2) }
      }.ConvertAll(Row));

      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "d1", g.D1 }, { "da1", g.Da1 }, { "df1", g.Df1 }, { "gamma", Math.Round(g.Gamma, 4) }, { "length", L }
      };
      DrawKit.Finish(a, res, "Червяк", "Сталь 40Х ГОСТ 4543-2016", s,
        DrawKit.TechItems(a, "Витки: 45...50 HRC.", "Неуказанные предельные отклонения размеров: H14, h14, ±IT14/2."),
        tableBottom);
      return res;
    }

    // ================== червячное колесо ==================

    static object WormWheelDrawing(Dictionary<string, object> a)
    {
      WormPair g = Pair(a);
      double b2 = ToolRegistry.GetDbl(a, "width", WormGears.WheelWidth(g));
      double rb = ToolRegistry.GetDbl(a, "bore") / 2.0;
      double h = b2 / 2.0, rM = g.DaM2 / 2.0, rf2 = g.Df2 / 2.0;
      double rTh = WormGears.ThroatTipRadius(g), rRoot = WormGears.ThroatRootRadius(g);
      if (b2 <= 0 || h >= rRoot)
        throw new ToolException("Ширина венца должна быть в (0; " + F(2 * rRoot) + ") — шире дуги впадин горловины");
      if (rb <= 0 || rb > rf2 - 2.0 * g.M) throw new ToolException("Нужен bore в (0; " + F(2 * (rf2 - 2.0 * g.M)) + ")");
      // горловина: дуга вершин (центр — ось червяка на высоте a) выходит на daM2 при |x| = zc
      double zc = Math.Sqrt(rTh * rTh - (g.A - rM) * (g.A - rM));
      Func<double, double> top = x => Math.Abs(x) >= zc ? rM : g.A - Math.Sqrt(rTh * rTh - x * x);
      double[] f = DrawKit.Fit(-h, h, -rM, g.A + 3, 34, 42, 30, 8, ToolRegistry.GetDbl(a, "scale", 0));
      double s = f[2];

      CreateDrawing(a, "Колесо червячное m" + F(g.M) + " z2=" + g.Z2);
      Tools2D.MakeView(f[0], f[1], s, "Осевой разрез");

      List<double[]> half = new List<double[]> { new[] { -h, rb }, new[] { -h, top(-h) } };
      double zE = Math.Min(zc, h);
      if (zc < h) half.Add(new[] { -zc, rM });
      half.AddRange(Arc(g.A, rTh, -zE, zE, 24));
      if (zc < h) half.Add(new[] { zc, rM });
      half.Add(new[] { h, top(h) });
      half.Add(new[] { h, rb });
      SectionWithBore(half, rb);
      double rPitch = g.D1 / 2.0, xp = Math.Min(h + 2 / s, rPitch * 0.98);
      foreach (bool m in new[] { false, true })
      {
        DrawKit.Poly(Arc(g.A, rRoot, -h, h, 24), 1, false, m);     // впадины — основной в разрезе
        DrawKit.Poly(Arc(g.A, rPitch, -xp, xp, 24), 3, false, m);  // делительная окружность горловины
      }
      Axis(-h - 5 / s, h + 5 / s);
      // центр дуги горловины — ось червяка
      double arm = 3 / s;
      Tools2D.GetDoc().ksLineSeg(-arm, g.A, arm, g.A, 3);
      Tools2D.GetDoc().ksLineSeg(0, g.A - arm, 0, g.A + arm, 3);

      DrawKit.Dim(h, rb, h, -rb, true, 12, true);
      if (zc < h) DrawKit.Dim(h, rM, h, -rM, true, 22, true);
      else DrawKit.Dim(h, top(h), h, -top(h), true, 22, true);
      DrawKit.Dim(-h, -top(-h), h, -top(h), false, -10);
      // полка влево: текст выходит из сечения наружу
      Tools2D.RadDim(new Dictionary<string, object>
        { { "xc", 0.0 }, { "yc", g.A }, { "r", rTh }, { "ang", 235.0 }, { "shelfDir", -1 } });

      double tableBottom = DrawKit.ParamTable(new List<string[]> {
        new[] { "Модуль", "m", F(g.M) },
        new[] { "Число зубьев", "z2", I(g.Z2) },
        new[] { "Направление линии зуба", "", "правое" },
        new[] { "Коэффициент смещения червяка", "x", "0" },
        new[] { "Исходный производящий червяк", "", "ГОСТ 19036-94" },
        new[] { "Степень точности", "", ToolRegistry.GetStr(a, "accuracy", "8-С ГОСТ 3675-81") },
        new[] { "Межосевое расстояние", "aw", F(g.A) },
        new[] { "Делительный диаметр", "d2", F(g.D2) },
        new[] { "Диаметр вершин зубьев", "da2", F(g.Da2) },
        new[] { "Вид сопряжённого червяка", "", "ZA" },
        new[] { "Число витков сопряж. червяка", "z1", I(g.Z1) }
      }.ConvertAll(Row));

      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "d2", g.D2 }, { "da2", g.Da2 }, { "daM2", g.DaM2 }, { "df2", g.Df2 }, { "a", g.A }, { "width", b2 },
        { "throat_radius", rTh }
      };
      DrawKit.Finish(a, res, "Колесо червячное", "БрО10Ф1 ГОСТ 613-79", s,
        DrawKit.TechItems(a, "Неуказанные предельные отклонения размеров: H14, h14, ±IT14/2."), tableBottom);
      return res;
    }

    // ================== общее ==================

    static WormPair Pair(Dictionary<string, object> a)
    {
      return WormGears.Worm(ToolRegistry.GetDbl(a, "m"), ToolRegistry.GetInt(a, "z1"),
        ToolRegistry.GetDbl(a, "q"), ToolRegistry.GetInt(a, "z2", 40));
    }

    static void CreateDrawing(Dictionary<string, object> a, string name)
    {
      Dictionary<string, object> args = new Dictionary<string, object> { { "name", name } };
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) args["path"] = path;
      Tools2D.CreateDrawing(args);
    }

    // половина осевого сечения (y ≥ 0): штриховка и обводка обеих половин
    static void Section(List<double[]> half)
    {
      foreach (bool m in new[] { false, true })
      {
        DrawKit.Hatch(half, m);
        DrawKit.Poly(half, 1, true, m);
      }
    }

    // Сечение с фасками отверстия: half начинается и кончается точками на отверстии (x0 < x1),
    // соседние с ними — торцы.
    static void SectionWithBore(List<double[]> half, double rb)
    {
      double x0 = half[0][0], x1 = half[half.Count - 1][0];
      double c = Math.Min(Tools2D.BoreChamfer(2 * rb),
        Math.Min(half[1][1] - rb, half[half.Count - 2][1] - rb) / 3.0);
      var p = new List<double[]> { new[] { x0 + c, rb }, new[] { x0, rb + c } };
      p.AddRange(half.GetRange(1, half.Count - 2));
      p.Add(new[] { x1, rb + c });
      p.Add(new[] { x1 - c, rb });
      Section(p);
      Tools2D.BoreEdges(x0, x1, rb, c);
    }

    static void Axis(double x0, double x1) { Tools2D.GetDoc().ksLineSeg(x0, 0, x1, 0, 3); }

    // дуга горловины r(x) = a − √(R² − x²), точки (x, r) от x0 до x1
    static List<double[]> Arc(double a, double R, double x0, double x1, int n)
    {
      List<double[]> pts = new List<double[]>(n + 1);
      for (int i = 0; i <= n; i++)
      {
        double x = x0 + (x1 - x0) * i / n;
        pts.Add(new[] { x, a - Math.Sqrt(R * R - x * x) });
      }
      return pts;
    }

    static double[] Swap(double[] rz) { return new[] { rz[1], rz[0] }; }

    static List<double[]> Swap(List<double[]> rz) { return rz.ConvertAll(Swap); }

    // строка таблицы: «наименование» | «значение»; обозначение параметра дописывается к наименованию
    static string[] Row(string[] r)
    {
      return new[] { r[1].Length > 0 ? r[0] + " " + r[1] : r[0], r[2] };
    }

    static string F(double v) { return Tools2D.FmtNum(v); }

    static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

    static string Deg(double deg)
    {
      int d = (int)Math.Floor(deg);
      double minF = (deg - d) * 60.0;
      int mi = (int)Math.Floor(minF);
      int sec = (int)Math.Round((minF - mi) * 60.0);
      if (sec == 60) { sec = 0; mi++; }
      if (mi == 60) { mi = 0; d++; }
      return d + "°" + mi.ToString("00") + "'" + sec.ToString("00") + "\"";
    }
  }
}
