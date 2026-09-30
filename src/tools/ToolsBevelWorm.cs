using System;
using System.Collections.Generic;
using System.Globalization;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using KAPITypes;
using KompasMcp;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Чертежи конических и червячных передач (ГОСТ 2.405-75 / 19036-81 / 2.402),
  // параметрика — gost/BevelGears.cs. Упрощённые учебные изображения:
  // главный вид — осевой разрез (ось горизонтальная), торцевой вид, таблица
  // параметров. Геометрия в мм, масштаб через scale листового вида.
  public static class ToolsBevelWorm
  {
    public static void Register()
    {
      ToolRegistry.Add("bevel_gear_drawing",
        "Чертёж прямозубого конического колеса (ГОСТ 2.405-75): осевой разрез, торцевой вид, таблица параметров. Сплошное колесо без выступа ступицы.",
        @"{""type"":""object"",""properties"":{
""me"":{""type"":""number"",""description"":""Внешний окружной модуль""},
""z"":{""type"":""integer"",""description"":""Число зубьев (8..200)""},
""delta"":{""type"":""number"",""description"":""Угол делительного конуса, град (5..85)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.3 внешнего конусного расстояния)""},
""bore"":{""type"":""number"",""description"":""Диаметр отверстия""},
""hubL"":{""type"":""number"",""description"":""Длина ступицы за венцом (деф 12)""},
""accuracy"":{""type"":""string""},
""scale"":{""type"":""number""},
""cx"":{""type"":""number""},""cy"":{""type"":""number""},
""name"":{""type"":""string""},""path"":{""type"":""string""},""png"":{""type"":""string""}}}",
        a => BevelDrawing(a));

      ToolRegistry.Add("worm_drawing",
        "Чертёж червяка (ГОСТ 19036-81, архимедов): осевой разрез с профилем витков, торцевой вид, таблица параметров.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Осевой модуль""},
""z1"":{""type"":""integer"",""description"":""Число заходов (1..4)""},
""q"":{""type"":""number"",""description"":""Коэффициент диаметра (6..25)""},
""z2"":{""type"":""integer"",""description"":""Число зубьев сопряжённого колеса (для длины нарезки)""},
""length"":{""type"":""number"",""description"":""Длина нарезанной части (деф ГОСТ 19672)""},
""journalR"":{""type"":""number""},""journalL"":{""type"":""number""},
""accuracy"":{""type"":""string""},
""scale"":{""type"":""number""},
""cx"":{""type"":""number""},""cy"":{""type"":""number""},
""name"":{""type"":""string""},""path"":{""type"":""string""},""png"":{""type"":""string""}}}",
        a => WormDrawing(a));

      ToolRegistry.Add("worm_wheel_drawing",
        "Чертёж червячного колеса (ГОСТ 2.402-75): осевой разрез с горловиной (дуга через 3 точки), торцевой вид, таблица параметров.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number""},""z1"":{""type"":""integer""},""q"":{""type"":""number""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса (20..400)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.75·da1, z1=4: 0.67·da1)""},
""bore"":{""type"":""number""},
""accuracy"":{""type"":""string""},
""scale"":{""type"":""number""},
""cx"":{""type"":""number""},""cy"":{""type"":""number""},
""name"":{""type"":""string""},""path"":{""type"":""string""},""png"":{""type"":""string""}}}",
        a => WormWheelDrawing(a));
    }

    // ================== коническое колесо ==================

    static object BevelDrawing(Dictionary<string, object> a)
    {
      double me = ToolRegistry.GetDbl(a, "me");
      int z = ToolRegistry.GetInt(a, "z");
      double delta = ToolRegistry.GetDbl(a, "delta");
      double bore = ToolRegistry.GetDbl(a, "bore");
      if (bore <= 0) throw new ToolException("Нужен bore > 0");
      BevelWheel w = BevelGears.Wheel(me, z, delta, ToolRegistry.GetDbl(a, "width", 0));
      double hL = ToolRegistry.GetDbl(a, "hubL", 12);
      double rad = delta * Math.PI / 180.0;
      double tanA = Math.Tan(w.DeltaA * Math.PI / 180.0);           // конус вершин
      double dF = delta - w.ThetaF;                                 // конус впадин
      double tanF = Math.Tan(dF * Math.PI / 180.0);
      double rc = w.Dae / 2.0, rf = w.Dfe / 2.0, rb = bore / 2.0;
      // радиус линии вершин/впадин на расстоянии x назад от переднего (большого) торца
      double rTip = rc - (w.B + hL) * tanA;     // на заднем торце всего колеса
      if (rTip < rb + 2)
        throw new ToolException("Отверстие " + bore + " не проходит: радиус конуса у заднего торца " +
          F(rTip) + " — уменьшите bore/hubL");
      double scale = ToolRegistry.GetDbl(a, "scale", 1);
      // деф 90: торцевой вид с деф 110 упирался в колонку таблицы (x 258)
      double cx = ToolRegistry.GetDbl(a, "cx", 90);
      double cy = ToolRegistry.GetDbl(a, "cy", 120);
      string accuracy = ToolRegistry.GetStr(a, "accuracy", "8-В");

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Колесо коническое me" + F(w.Me) + " z" + z;
      Tools2D.CreateDrawing(createArgs);

      // ---------- главный вид: осевой разрез ----------
      Tools2D.MakeView(cx, cy, scale, "Осевой разрез");
      ksDocument2D d = Tools2D.GetDoc();

      // контур верхней половины: линия отверстия → передний торец → конус вершин → задний торец
      // (x=−hL задний торец ступицы, x=0 внутренний торец венца, x=+b внешний/передний торец)
      double yRootI = rf - w.B * tanF;  // впадины на внутреннем торце
      // штриховка материалом (зубья в разрезе условно штрихуем, как в gear_drawing)
      List<object> hatch = new List<object>();
      double[] hx = new double[] { -hL, w.B, w.B, -hL };
      double[] hy = new double[] { rb, rb, rc, rTip };
      for (int i = 0; i < 4; i++)
      {
        Dictionary<string, object> s = new Dictionary<string, object>();
        s["type"] = "line";
        s["x1"] = hx[i]; s["y1"] = hy[i];
        s["x2"] = hx[(i + 1) % 4]; s["y2"] = hy[(i + 1) % 4];
        hatch.Add(s);
      }
      Dictionary<string, object> h1 = new Dictionary<string, object>();
      h1["contour"] = hatch;
      h1["angle"] = 45.0; h1["step"] = 2.5;
      h1["x0"] = (w.B - hL) / 2.0; h1["y0"] = rb + 2; h1["outline"] = false;
      Tools2D.Hatch(h1);
      // нижняя половина — зеркально
      List<object> hatch2 = Mirror(hatch);
      Dictionary<string, object> h2 = new Dictionary<string, object>();
      h2["contour"] = hatch2;
      h2["angle"] = 45.0; h2["step"] = 2.5;
      h2["x0"] = (w.B - hL) / 2.0; h2["y0"] = -rb - 2; h2["outline"] = false;
      Tools2D.Hatch(h2);

      // обводка основными линиями (верх + низ, замкнутый контур включая задний торец)
      Outline(hx, hy, d, false);
      Outline(hx, hy, d, true);
      d.ksLineSeg(-hL, rTip, -hL, rb, 1);
      d.ksLineSeg(-hL, -rTip, -hL, -rb, 1);
      // линии впадин (только венец, x 0..B)
      d.ksLineSeg(0, yRootI, w.B, rf, 1);
      d.ksLineSeg(0, -yRootI, w.B, -rf, 1);
      // делительный конус — штрихпунктир до вершины (точки схода на оси)
      double rDelB = w.De / 2.0 - w.B * Math.Tan(rad);   // (для отладки)
      if (rDelB < 0) throw new ToolException("Внутренняя ошибка геометрии");
      double xApex = w.B - w.Ri * Math.Cos(rad);
      d.ksLineSeg(w.B, w.De / 2.0, xApex, 0, 3);
      d.ksLineSeg(w.B, -w.De / 2.0, xApex, 0, 3);
      // ось
      double ax = Math.Max(hL, w.B) + 6;
      d.ksLineSeg(-hL - 6, 0, w.B + 6, 0, 3);

      // размеры: ширина венца, длина ступицы
      Dim(d, 0, rc, w.B, rc, 0, 12);                 // ширина B над контуром
      Dim(d, -hL, -rb, 0, -rb, 0, -10);              // ступица под контуром

      // ---------- торцевой вид ----------
      double cx2 = cx + scale * (hL + rc) + 30.0;
      Tools2D.MakeView(cx2, cy, scale, "Торцевой вид");
      d.ksCircle(0, 0, rc, 1);
      d.ksCircle(0, 0, w.De / 2.0, 3);
      d.ksCircle(0, 0, rf, 2);
      d.ksCircle(0, 0, rb, 1);
      double arm = rc + 4;
      d.ksLineSeg(-arm, 0, arm, 0, 3);
      d.ksLineSeg(0, -arm, 0, arm, 3);
            double[] angs = new double[] { 45.0, 270.0, 100.0, 60.0 };
      double[] radii = new double[] { rc, w.De / 2.0, rf, rb };
      for (int i = 0; i < 4; i++) Diam(radii[i], angs[i]);

      // ---------- таблица параметров ----------
      MakeTable(258.0, "Таблица параметров", new string[][] {
        new string[] { "Внешний окружной модуль", F(w.Me) },
        new string[] { "Число зубьев", z.ToString(CultureInfo.InvariantCulture) },
        new string[] { "Тип зуба", "прямые" },
        new string[] { "Угол делительного конуса", F(delta) + "°" },
        new string[] { "Сопряжённая шестерня", "δ=" + F(BevelGears.MateDelta(delta)) + "°" },
        new string[] { "Внешнее конусное расстояние", F(w.Ri) },
        new string[] { "Внешний диаметр вершин", F(w.Dae) },
        new string[] { "Степень точности", accuracy }
      });

      Dictionary<string, object> res = new Dictionary<string, object>();
      res["path"] = Tools2D.DocPath;
      res["dae"] = w.Dae; res["dfe"] = w.Dfe; res["de"] = w.De; res["ri"] = w.Ri;
      res["width"] = w.B; res["thetaF"] = w.ThetaF; res["deltaA"] = w.DeltaA;
      if (cx2 + scale * rc > 260.0) res["warning"] = "торцевой вид налезает на таблицу — уменьшите scale/cx";
      RenderPng(d, a, res);
      StampSet(createArgs["name"] as string, ToolRegistry.GetStr(a, "designation", null));
      Tools2D.GetDoc().ksSaveDocument(Tools2D.DocPath);
      return res;
    }
    // ================== червяк ==================

    static object WormDrawing(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z1 = ToolRegistry.GetInt(a, "z1");
      double q = ToolRegistry.GetDbl(a, "q");
      int z2 = ToolRegistry.GetInt(a, "z2", 40);
      WormPair g = WormGears.Worm(m, z1, q, z2);
      // длина нарезанной части ГОСТ 19672: b1 = (4+0.2·z2)·m
      double L = ToolRegistry.GetDbl(a, "length", Math.Ceiling((4.0 + 0.2 * z2) * m));
      double jL = ToolRegistry.GetDbl(a, "journalL", Math.Ceiling(2.8 * m + 6));
      double rj = ToolRegistry.GetDbl(a, "journalR", 0.75 * g.Df1 / 2.0);
      double L2 = L / 2.0, rf = g.Df1 / 2.0, rc = g.Da1 / 2.0;
      if (rj <= 0 || rj >= rf) throw new ToolException("journalR должен быть в (0; Df1/2=" + F(rf) + ")");
      double scale = ToolRegistry.GetDbl(a, "scale", 1);
      // деф 90: торцевой вид с деф 110 упирался в колонку таблицы (x 258)
      double cx = ToolRegistry.GetDbl(a, "cx", 90);
      double cy = ToolRegistry.GetDbl(a, "cy", 120);
      string accuracy = ToolRegistry.GetStr(a, "accuracy", "8-С");

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Червяк m" + F(m) + " z1" + z1 + " q" + F(q);
      Tools2D.CreateDrawing(createArgs);

      // ---------- главный вид: осевой разрез ----------
      Tools2D.MakeView(cx, cy, scale, "Осевой разрез");
      ksDocument2D d = Tools2D.GetDoc();

      // штриховка тела: сердечник (до линий впадин) и шейки
      Tools2D.HatchRect(-L2, L2, -rf, rf, false);
      Tools2D.HatchRect(-L2 - jL, -L2, -rj, rj, false);
      Tools2D.HatchRect(L2, L2 + jL, -rj, rj, false);

      // обводка (верхний + нижний контур): шейка → бурт → сердечник
      double[] px = new double[] { -L2 - jL, -L2, -L2, L2, L2, L2 + jL };
      double[] py = new double[] { rj, rj, rf, rf, rj, rj };
      Outline(px, py, d, false);
      Outline(px, py, d, true);
      // торцевые линии шеек
      d.ksLineSeg(-L2 - jL, rj, -L2 - jL, -rj, 1);
      d.ksLineSeg(L2 + jL, rj, L2 + jL, -rj, 1);
      d.ksLineSeg(L2, rf, L2, -rf, 1);   // бурт: сердечник обрывается на L
      d.ksLineSeg(-L2, rf, -L2, -rf, 1);

      // профиль витков: трапеции над и под сердечником
      double p = g.P;
      double ht = 2.2 * m;                                    // высота профиля над впадиной
      double wt = Math.Max(0.2 * p, p - 2.0 * ht * Math.Tan(20.0 * Math.PI / 180.0));
      double wb = 0.7 * p;
      int n = (int)Math.Floor(L / p);
      for (int k = 0; k < n; k++)
      {
        double xm = -L2 + (k + 0.5) * p;
        if (xm + wb / 2.0 > L2) break;
        Tooth(d, xm, wt, wb, rf, ht, false);
        Tooth(d, xm, wt, wb, rf, ht, true);
      }
      // ось
      d.ksLineSeg(-L2 - jL - 6, 0, L2 + jL + 6, 0, 3);

      // размеры: длина нарезанной части
      Dim(d, -L2, rc, L2, rc, 0, 12);

      // ---------- торцевой вид ----------
      double cx2 = cx + scale * (L2 + jL + rc) + 35.0;
      Tools2D.MakeView(cx2, cy, scale, "Торцевой вид");
      d.ksCircle(0, 0, rc, 1);
      d.ksCircle(0, 0, g.D1 / 2.0, 3);
      d.ksCircle(0, 0, rf, 2);
      d.ksCircle(0, 0, rj, 1);
      double arm = rc + 4;
      d.ksLineSeg(-arm, 0, arm, 0, 3);
      d.ksLineSeg(0, -arm, 0, arm, 3);
      double[] angs = new double[] { 45.0, 270.0, 100.0, 60.0 };
      double[] radii = new double[] { rc, g.D1 / 2.0, rf, rj };
      for (int i = 0; i < 4; i++) Diam(radii[i], angs[i]);

      // ---------- таблица ----------
      MakeTable(258.0, "Таблица параметров", new string[][] {
        new string[] { "Осевой модуль", F(m) },
        new string[] { "Число заходов", z1.ToString(CultureInfo.InvariantCulture) },
        new string[] { "Коэффициент диаметра червяка", F(q) },
        new string[] { "Делительный диаметр", F(g.D1) },
        new string[] { "Угол подъёма линии витка", F(g.Gamma) + "°" },
        new string[] { "Осевой шаг", F(g.P) },
        new string[] { "Ход витка", F(g.Pz) },
        new string[] { "Направление линии витка", "правое" },
        new string[] { "Сопряжённое колесо", "z2=" + z2 },
        new string[] { "Степень точности", accuracy }
      });

      Dictionary<string, object> res = new Dictionary<string, object>();
      res["path"] = Tools2D.DocPath;
      res["d1"] = g.D1; res["da1"] = g.Da1; res["df1"] = g.Df1;
      res["gamma"] = g.Gamma; res["length"] = L;
      RenderPng(d, a, res);
      StampSet(createArgs["name"] as string, ToolRegistry.GetStr(a, "designation", null));
      d.ksSaveDocument(Tools2D.DocPath);
      return res;
    }

    // трапеция витка червяка: стороны + вершина (низ лежит на линии впадин)
    static void Tooth(ksDocument2D d, double xm, double wt, double wb, double rf, double ht, bool mirror)
    {
      double s = mirror ? -1 : 1;
      double y0 = s * rf, y1 = s * (rf + ht);
      d.ksLineSeg(xm - wb / 2.0, y0, xm - wt / 2.0, y1, 1);
      d.ksLineSeg(xm + wt / 2.0, y1, xm + wb / 2.0, y0, 1);
      d.ksLineSeg(xm - wt / 2.0, y1, xm + wt / 2.0, y1, 1);
    }

    // ================== червячное колесо ==================

    static object WormWheelDrawing(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z1 = ToolRegistry.GetInt(a, "z1", 1);
      double q = ToolRegistry.GetDbl(a, "q");
      int z2 = ToolRegistry.GetInt(a, "z2");
      double bore = ToolRegistry.GetDbl(a, "bore");
      if (bore <= 0) throw new ToolException("Нужен bore > 0");
      WormPair g = WormGears.Worm(m, z1, q, z2);
      double b2 = ToolRegistry.GetDbl(a, "width", (z1 <= 3 ? 0.75 : 0.67) * g.Da1);
      double rb = bore / 2.0;
      if (b2 >= g.DaM2) throw new ToolException("Ширина венца " + b2 + " >= daM2=" + F(g.DaM2));
      if (b2 <= 0 || rb * 2 >= g.Df2)
        throw new ToolException("Нужны bore < df2=" + F(g.Df2) + " и width > 0");
      // подбор масштаба/центра: торцевой вид не должен лезть в колонку
      // таблицы (x от 258) — грабля первых прогонов
      double b22 = b2 / 2.0;
      double sFit = Math.Min(1.0, Math.Min(200.0 / g.DaM2, 198.0 / (2 * b22 + g.DaM2)));
      double scale = ToolRegistry.GetDbl(a, "scale", sFit);
      double cx = ToolRegistry.GetDbl(a, "cx", Math.Max(40, Math.Min(110, 220 - sFit * (b22 + g.DaM2))));
      double cy = ToolRegistry.GetDbl(a, "cy", 120);
      string accuracy = ToolRegistry.GetStr(a, "accuracy", "8-С");

      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      createArgs["name"] = "Колесо червячное m" + F(m) + " z2" + z2;
      Tools2D.CreateDrawing(createArgs);

      double rTh = g.A + g.Da2 / 2.0;      // дуга горловины (центр — точка оси червяка (0,−a))
      double ySide = -g.A + Math.Sqrt(rTh * rTh - b22 * b22);  // вершины у торцов венца
      double ySideRoot = -g.A + Math.Sqrt((g.A + g.Df2 / 2.0) * (g.A + g.Df2 / 2.0) - b22 * b22);
      if (ySide < g.Df2 / 2.0 || ySide > g.DaM2 / 2.0)
        throw new ToolException("Ширина венца " + F(b2) + " не согласуется с горловиной (торец венца y=" +
          F(ySide) + ", df2/2=" + F(g.Df2 / 2.0) + ", daM2/2=" + F(g.DaM2 / 2.0) + ")");

      // ---------- главный вид: осевой разрез ----------
      Tools2D.MakeView(cx, cy, scale, "Осевой разрез");
      ksDocument2D d = Tools2D.GetDoc();

      // материал между отверстием и впадинами; полосу зуба (между дугами впадин
      // и вершин) не штрихуем. Дугу в контуре штриховки НЕ применять (грабля
      // NOTES: ksArcByPoint может пойти длинным путём → блоб) — аппроксимация
      // отрезками по 8°
      List<object> hatch = HatchPoly(b22, rb, ySideRoot, g);
      Dictionary<string, object> h1 = new Dictionary<string, object>();
      h1["contour"] = hatch;
      h1["angle"] = 45.0; h1["step"] = 2.5;
      h1["x0"] = 0; h1["y0"] = (rb + ySideRoot) / 2.0; h1["outline"] = false;
      Tools2D.Hatch(h1);
      List<object> hatch2 = Mirror(hatch);
      Dictionary<string, object> h2 = new Dictionary<string, object>();
      h2["contour"] = hatch2;
      h2["angle"] = 45.0; h2["step"] = 2.5;
      h2["x0"] = 0; h2["y0"] = -(rb + ySideRoot) / 2.0; h2["outline"] = false;
      Tools2D.Hatch(h2);

      // обводка: торцы венца (отверстие → наибольший диаметр), горловина дугой
      d.ksLineSeg(-b22, rb, -b22, g.DaM2 / 2.0, 1);
      d.ksLineSeg(b22, rb, b22, g.DaM2 / 2.0, 1);
      d.ksLineSeg(b22, rb, -b22, rb, 1);   // линия отверстия (половина)
      d.ksLineSeg(b22, -rb, -b22, -rb, 1);
      // дуги вершин и впадин ломаными по 12° (грабля NOTES: дуговые методы
      // ksArcBy* рисуют ПОЛНУЮ окружность вместо дуги — подтверждено пробой)
      double cyA = -g.A;
      double tTip = Math.Atan2(ySide - cyA, b22);          // угол вершины дуги Da2
      double tRoot = Math.Atan2(ySideRoot - cyA, b22);      // угол дуги Df2
      ArcLines(d, 0, cyA, g.A + g.Da2 / 2.0, tTip, Math.PI - tTip, 24, 1);
      ArcLines(d, 0, -cyA, g.A + g.Da2 / 2.0, -(Math.PI - tTip), -tTip, 24, 1);
      ArcLines(d, 0, cyA, g.A + g.Df2 / 2.0, tRoot, Math.PI - tRoot, 24, 2);
      ArcLines(d, 0, -cyA, g.A + g.Df2 / 2.0, -(Math.PI - tRoot), -tRoot, 24, 2);
      d.ksLineSeg(-b22, g.D2 / 2.0, b22, g.D2 / 2.0, 3);
      d.ksLineSeg(-b22, -g.D2 / 2.0, b22, -g.D2 / 2.0, 3);
      // ось колеса и ось червяка (штрихпунктир)
      d.ksLineSeg(-b22 - 6, 0, b22 + 6, 0, 3);
      d.ksLineSeg(-b22 - 6, -g.A, b22 + 6, -g.A, 3);

      // размеры
      Dim(d, -b22, g.DaM2 / 2.0, b22, g.DaM2 / 2.0, 0, 12);   // ширина венца

      // ---------- торцевой вид ----------
      double cx2 = cx + scale * (b22 + g.DaM2 / 2.0) + 30.0;
      Tools2D.MakeView(cx2, cy, scale, "Торцевой вид");
      d.ksCircle(0, 0, g.DaM2 / 2.0, 1);
      d.ksCircle(0, 0, g.Da2 / 2.0, 1);
      d.ksCircle(0, 0, g.Df2 / 2.0, 2);
      d.ksCircle(0, 0, g.D2 / 2.0, 3);
      d.ksCircle(0, 0, rb, 1);
      double arm = g.DaM2 / 2.0 + 4;
      d.ksLineSeg(-arm, 0, arm, 0, 3);
      d.ksLineSeg(0, -arm, 0, arm, 3);
      double[] angs = new double[] { 45.0, 270.0, 100.0, 60.0 };
      double[] radii = new double[] { g.Da2 / 2.0, g.D2 / 2.0, g.Df2 / 2.0, rb };
      for (int i = 0; i < 4; i++) Diam(radii[i], angs[i]);

      // ---------- таблица ----------
      MakeTable(258.0, "Таблица параметров", new string[][] {
        new string[] { "Модуль осевой", F(m) },
        new string[] { "Число зубьев", z2.ToString(CultureInfo.InvariantCulture) },
        new string[] { "Исходный червяк", "ГОСТ 19036-81" },
        new string[] { "Сопряжённый червяк", "m=" + F(m) + "; z1=" + z1 + "; q=" + F(q) },
        new string[] { "Направление линии витка", "правое" },
        new string[] { "Коэффициент смещения", "0" },
        new string[] { "Межосевое расстояние", F(g.A) },
        new string[] { "Степень точности", accuracy }
      });

      Dictionary<string, object> res = new Dictionary<string, object>();
      res["path"] = Tools2D.DocPath;
      res["d2"] = g.D2; res["da2"] = g.Da2; res["daM2"] = g.DaM2; res["df2"] = g.Df2;
      res["a"] = g.A; res["width"] = b2;
      RenderPng(d, a, res);
      StampSet(createArgs["name"] as string, ToolRegistry.GetStr(a, "designation", null));
      d.ksSaveDocument(Tools2D.DocPath);
      return res;
    }

    // ================== общие хелперы ==================

    // дуга отрезками от угла t1 до t2 (рад, n сегментов) — точки в контур
    static void Arc(List<object> c, double xc, double yc, double r,
                    double t1, double t2, int n)
    {
      for (int i = 0; i < n; i++)
      {
        double p1 = t1 + (t2 - t1) * i / n;
        double p2 = t1 + (t2 - t1) * (i + 1) / n;
        Seg(c, xc + r * Math.Cos(p1), yc + r * Math.Sin(p1),
               xc + r * Math.Cos(p2), yc + r * Math.Sin(p2));
      }
    }

    // то же, но сразу линиями со стилем s (для обводки вне штриховки)
    static void ArcLines(ksDocument2D d, double xc, double yc, double r,
                         double t1, double t2, int n, int s)
    {
      for (int i = 0; i < n; i++)
      {
        double p1 = t1 + (t2 - t1) * i / n;
        double p2 = t1 + (t2 - t1) * (i + 1) / n;
        d.ksLineSeg(xc + r * Math.Cos(p1), yc + r * Math.Sin(p1),
                    xc + r * Math.Cos(p2), yc + r * Math.Sin(p2), s);
      }
    }

    // замкнутый контур штриховки тела червячного колеса от отверстия до впадин;
    // дуга впадин — ломаной (ksArcBy* в контуре ненадёжен, см. NOTES)
    static List<object> HatchPoly(double b22, double rb, double ySideRoot, WormPair g)
    {
      List<object> c = new List<object>();
      Seg(c, -b22, rb, b22, rb);
      Seg(c, b22, rb, b22, ySideRoot);
      double rr = g.A + g.Df2 / 2.0;
      double cy = -g.A;
      int n = 12;
      double a1 = Math.Atan2(ySideRoot - cy, b22);
      Arc(c, 0, cy, rr, a1, Math.PI - a1, n);
      Seg(c, -b22, ySideRoot, -b22, rb);
      return c;
    }

    static void Seg(List<object> c, double x1, double y1, double x2, double y2)
    {
      Dictionary<string, object> s = new Dictionary<string, object>();
      s["type"] = "line"; s["x1"] = x1; s["y1"] = y1; s["x2"] = x2; s["y2"] = y2;
      c.Add(s);
    }

    // ================== обводка/размеры ==================

    // полилиния контура style 1 (верх/низ — по mirror)
    static void Outline(double[] xs, double[] ys, ksDocument2D d, bool mirror)
    {
      double s = mirror ? -1 : 1;
      for (int i = 0; i < xs.Length - 1; i++)
        d.ksLineSeg(xs[i], s * ys[i], xs[i + 1], s * ys[i + 1], 1);
    }

    // зеркальная копия контура штриховки
    static List<object> Mirror(List<object> src)
    {
      List<object> dst = new List<object>();
      for (int i = src.Count - 1; i >= 0; i--)
      {
        Dictionary<string, object> s = src[i] as Dictionary<string, object>;
        Dictionary<string, object> n = new Dictionary<string, object>();
        foreach (string key in s.Keys) n[key] = s[key];
        string type = ToolRegistry.GetStr(s, "type", "line");
        foreach (string key in s.Keys)
          if (key.Length == 2 && key[0] == 'y' && n[key] is double)
            n[key] = -ToolRegistry.GetDbl(s, key, 0);
        dst.Add(n);
      }
      return dst;
    }

    static void Dim(ksDocument2D d, double x1, double y1, double x2, double y2, double dx, double dy)
    {
      Dictionary<string, object> dim = new Dictionary<string, object>();
      dim["x1"] = x1; dim["y1"] = y1; dim["x2"] = x2; dim["y2"] = y2;
      dim["ang"] = 0.0; dim["dx"] = dx; dim["dy"] = dy;
      Tools2D.LinDim(dim);
    }

    static void Diam(double r, double ang)
    {
      Dictionary<string, object> dim = new Dictionary<string, object>();
      dim["xc"] = 0.0; dim["yc"] = 0.0; dim["r"] = r;
      dim["ang"] = ang; dim["textPos"] = 60.0;
      Tools2D.DiamDim(dim);
    }

    // таблица параметров (идиома GearDrawing): col1 115 мм, строки 10 мм, растёт от якоря вверх
    static void MakeTable(double x, string name, string[][] rows)
    {
      Tools2D.MakeView(x, 145.0, 1.0, name);
      ksDocument2D d = Tools2D.GetDoc();
      double col1 = 115, rowH = 10;
      double tw = col1 + 40, th = rows.Length * rowH;
      for (int i = 0; i <= rows.Length; i++)
        d.ksLineSeg(0, i * rowH, tw, i * rowH, 1);
      d.ksLineSeg(0, 0, 0, th, 1);
      d.ksLineSeg(col1, 0, col1, th, 1);
      d.ksLineSeg(tw, 0, tw, th, 1);
      for (int i = 0; i < rows.Length; i++)
      {
        // длинные названия — сжимаем кегль (ширина символа ≈ 0.8h)
        string k = rows[i][0], v = rows[i][1];
        double h = 5.0;
        if (k.Length * 0.8 * h > col1 - 4) h = Math.Max(2.5, (col1 - 4) / (0.8 * k.Length));
        double top = th - i * rowH;
        d.ksText(2, top - rowH / 2.0 - 1.8, 0, h, 1, 0, k);
        double hv = 5.0;
        if (v.Length * 0.8 * hv > 38) hv = Math.Max(2.5, 38 / (0.8 * v.Length));
        d.ksText(col1 + 2, top - rowH / 2.0 - 1.8, 0, hv, 1, 0, v);
      }
    }

    static void RenderPng(ksDocument2D d, Dictionary<string, object> a, Dictionary<string, object> res)
    {
      string png = ToolRegistry.GetStr(a, "png", null);
      if (png == null || png.Length == 0) return;
      object rObj = d.RasterFormatParam();
      ksRasterFormatParam rf = (ksRasterFormatParam)rObj;
      if (rf == null) throw new ToolException("RasterFormatParam вернул null");
      rf.format = 3;
      if (!rf.Init()) throw new ToolException("raster.Init вернул false");
      rf.extResolution = 300;
      png = System.IO.Path.GetFullPath(png);
      if (!d.SaveAsToRasterFormat(png, rObj)) throw new ToolException("SaveAsToRasterFormat вернул false");
      res["png"] = png;
    }

    // штамп через API-7 (идиома Tools2D/ToolsSpc)
    static void StampSet(string description, string partNumber)
    {
      try
      {
        IApplication app7 = KompasHost.App7;
        IKompasDocument2D doc7 = (IKompasDocument2D)app7.ActiveDocument;
        if (doc7 == null) return;
        ILayoutSheet sheet = (ILayoutSheet)doc7.LayoutSheets.ItemByNumber[1];
        IStamp stamp = (IStamp)sheet.Stamp;
        if (partNumber != null && partNumber.Length > 0)
          stamp.Text[(int)ksStampEnum.ksStPartNumber].Str = partNumber;
        if (description != null && description.Length > 0)
          stamp.Text[(int)ksStampEnum.ksStDescription].Str = description;
        stamp.Text[(int)ksStampEnum.ksStSheetNumber].Str = "1";
        stamp.Text[(int)ksStampEnum.ksStNumberOfSheets].Str = "1";
        stamp.Update();
      }
      catch (Exception e) { Log.Write("штамп: " + e.Message); }
    }

    static string F(double v) { return Tools2D.FmtNum(v); }
  }
}