using System;
using System.Collections.Generic;
using Kompas6API5;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // 3D-модели конических и червячных передач и их сборки (параметры — gost/BevelGears.cs).
  // Оси всех колёс — глобальная Z. Профили зубьев — прямобочные 20°: для архимедова
  // червяка это точный осевой профиль, для колёс — упрощение (без эвольвенты).
  //  - червяк: цилиндр с шейками, впадина — кинематический вырез трапеции по спирали,
  //    заходы — круговым массивом выреза;
  //  - червячное колесо: тело вращением до дуги впадин горловины, зубья — лофт венца
  //    с закруткой на угол подъёма, вершины подрезаются вращением по дуге горловины и daM2;
  //  - коническое колесо: заготовка вращением (конус вершин, внешний и внутренний
  //    дополнительные конусы), впадина — вырез по сечениям, сходящимся в вершину
  //    делительного конуса (вершина — в начале координат), и круговой массив.
  public static class ToolsGear3D
  {
    static readonly double TanAlpha = Math.Tan(20.0 * Math.PI / 180.0);

    public static void Register()
    {
      ToolRegistry.Add("worm_shaft",
        "3D-модель архимедова червяка (ГОСТ 19036/19672): нарезанная часть с витками z1 заходов и шейки. Ось — Z, нарезка от journalL до journalL+length.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Осевой модуль""},
""z1"":{""type"":""integer"",""description"":""Число заходов (1..4)""},
""q"":{""type"":""number"",""description"":""Коэффициент диаметра (6..25)""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса — для длины нарезки по ГОСТ 19672 (деф 40)""},
""length"":{""type"":""number"",""description"":""Длина нарезанной части (деф (4+0.2·z2)·m)""},
""journalR"":{""type"":""number"",""description"":""Радиус шеек (деф 0.75·df1/2)""},
""journalL"":{""type"":""number"",""description"":""Длина шеек (деф 2.8·m+6)""},
""hand"":{""type"":""string"",""enum"":[""right"",""left""],""description"":""Направление витка (деф right)""},
""name"":{""type"":""string""},""path"":{""type"":""string"",""description"":""Путь .m3d""}},
""required"":[""m"",""z1"",""q""]}",
        a => WormShaft(a));

      ToolRegistry.Add("worm_wheel",
        "3D-модель червячного колеса (ГОСТ 19036/2.402): венец с горловиной (дуги вершин и впадин с центром на оси червяка), зубья с наклоном на угол подъёма витка, сквозное отверстие. Ось — Z, средняя плоскость z=0.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number""},""z1"":{""type"":""integer""},""q"":{""type"":""number""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса (20..400)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.75·da1, z1=4: 0.67·da1)""},
""bore"":{""type"":""number"",""description"":""Диаметр отверстия""},
""hand"":{""type"":""string"",""enum"":[""right"",""left""]},
""name"":{""type"":""string""},""path"":{""type"":""string""}},
""required"":[""m"",""z1"",""q"",""z2"",""bore""]}",
        a => WormWheel(a));

      ToolRegistry.Add("bevel_gear_wheel",
        "3D-модель прямозубого конического колеса (ГОСТ 19624, Σ=90°): зубья сходятся в вершину делительного конуса (начало координат), внешний и внутренний дополнительные конусы, ступица с тыльной стороны. Ось — Z.",
        @"{""type"":""object"",""properties"":{
""me"":{""type"":""number"",""description"":""Внешний окружной модуль""},
""z"":{""type"":""integer"",""description"":""Число зубьев (8..200)""},
""delta"":{""type"":""number"",""description"":""Угол делительного конуса, град (5..85)""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.3·Re)""},
""bore"":{""type"":""number"",""description"":""Диаметр отверстия""},
""hubL"":{""type"":""number"",""description"":""Длина ступицы за тыльным торцом (деф 0.5·bore, 0 — без ступицы)""},
""name"":{""type"":""string""},""path"":{""type"":""string""}},
""required"":[""me"",""z"",""delta"",""bore""]}",
        a => BevelWheel3D(a));

      ToolRegistry.Add("worm_pair",
        "3D-сборка червячной передачи: создаёт червяк и колесо (.m3d) и сборку (.a3d); колесо — ось Z, червяк — вдоль Y на межосевом a, виток червяка в зацеплении с зубом колеса.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number""},""z1"":{""type"":""integer""},""q"":{""type"":""number""},""z2"":{""type"":""integer""},
""bore"":{""type"":""number"",""description"":""Отверстие колеса""},
""width"":{""type"":""number"",""description"":""Ширина венца колеса""},
""hand"":{""type"":""string"",""enum"":[""right"",""left""]},
""name"":{""type"":""string""},""path"":{""type"":""string"",""description"":""Путь .a3d (компоненты рядом)""}},
""required"":[""m"",""z1"",""q"",""z2"",""bore""]}",
        a => WormPairAsm(a));

      ToolRegistry.Add("bevel_pair",
        "3D-сборка ортогональной конической передачи: шестерня z1 и колесо z2 (.m3d), сборка (.a3d); вершины конусов совпадают, колесо — ось Z, шестерня — ось X, зуб шестерни во впадине колеса.",
        @"{""type"":""object"",""properties"":{
""me"":{""type"":""number""},""z1"":{""type"":""integer""},""z2"":{""type"":""integer""},
""width"":{""type"":""number"",""description"":""Ширина венца (деф 0.3·Re)""},
""bore1"":{""type"":""number""},""bore2"":{""type"":""number""},
""name"":{""type"":""string""},""path"":{""type"":""string"",""description"":""Путь .a3d (компоненты рядом)""}},
""required"":[""me"",""z1"",""z2"",""bore1"",""bore2""]}",
        a => BevelPairAsm(a));
    }

    // ================== червяк ==================

    static WormPair Pair(Dictionary<string, object> a, int z2Default)
    {
      try
      {
        return WormGears.Worm(ToolRegistry.GetDbl(a, "m"), ToolRegistry.GetInt(a, "z1"),
          ToolRegistry.GetDbl(a, "q"), ToolRegistry.GetInt(a, "z2", z2Default));
      }
      catch (ToolException) { throw; }
      catch (Exception e) { throw new ToolException(e.Message); }
    }

    static bool RightHand(Dictionary<string, object> a)
    {
      string hand = ToolRegistry.GetStr(a, "hand", "right");
      if (hand != "right" && hand != "left") throw new ToolException("hand: right или left");
      return hand == "right";
    }

    static object WormShaft(Dictionary<string, object> a)
    {
      WormPair g = Pair(a, 40);
      double L = ToolRegistry.GetDbl(a, "length", WormGears.ThreadLength(g.M, g.Z2));
      double jL = ToolRegistry.GetDbl(a, "journalL", WormGears.JournalLength(g.M));
      double rf = g.Df1 / 2.0, ra = g.Da1 / 2.0, r0 = g.D1 / 2.0;
      double rj = ToolRegistry.GetDbl(a, "journalR", 0.75 * rf);
      if (rj <= 0 || rj >= rf) throw new ToolException("journalR должен быть в (0; df1/2=" + Tools2D.FmtNum(rf) + ")");
      if (L < g.P || L > 2000 || jL <= 0) throw new ToolException("Нужны length ≥ шага и journalL > 0");
      bool right = RightHand(a);

      CreatePart(a, "Червяк m" + Tools2D.FmtNum(g.M) + " z1=" + g.Z1 + " q" + Tools2D.FmtNum(g.Q));
      Cylinder(0, rj, jL, true);
      Cylinder(jL, ra, L, false);
      Cylinder(jL + L, rj, jL, false);

      // впадина витка в осевом сечении: ширина P/2 на делительном диаметре, стороны 20°,
      // от линии впадин до +1 мм над вершинами. Спираль идёт от z=0 через всю деталь:
      // на шейках профиль (r ≥ rf > rj) тела не касается.
      double rTop = ra + 1.0;
      double hwRoot = g.P / 4.0 - (r0 - rf) * TanAlpha, hwTop = g.P / 4.0 + (rTop - r0) * TanAlpha;
      ksEntity profile = Tools3D.PolySketch(Tools3D.PlaneByName(Tools3D.GetPart(), "XOZ"), new List<double[]> {
        new[] { rf, -hwRoot }, new[] { rTop, -hwTop }, new[] { rTop, hwTop }, new[] { rf, hwRoot } });
      ksEntity spiral = Tools3D.Spiral(Tools3D.PlaneByName(Tools3D.GetPart(), "XOY"),
        g.D1, g.Pz, (2 * jL + L) / g.Pz, right);
      ksEntity groove = Tools3D.Evolution(profile, spiral, cut: true);
      Tools3D.CircularCopyZ(groove, g.Z1);

      SetMaterial("Сталь 40Х ГОСТ 4543-2016", 7820);
      object saved = Save(a);
      return new Dictionary<string, object>
      {
        { "saved", saved },
        { "d1", g.D1 }, { "da1", g.Da1 }, { "df1", g.Df1 },
        { "gamma_deg", Math.Round(g.Gamma, 4) }, { "p", Math.Round(g.P, 4) }, { "pz", Math.Round(g.Pz, 4) },
        { "length", L }, { "total_length", L + 2 * jL }, { "thread_from_z", jL },
        { "hand", right ? "right" : "left" },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }

    // ================== червячное колесо ==================

    static object WormWheel(Dictionary<string, object> a)
    {
      WormPair g = Pair(a, 40);
      double b2 = ToolRegistry.GetDbl(a, "width", WormGears.WheelWidth(g));
      double rb = ToolRegistry.GetDbl(a, "bore") / 2.0;
      double r2 = g.D2 / 2.0, rf2 = g.Df2 / 2.0, rM = g.DaM2 / 2.0;
      double rTh = WormGears.ThroatTipRadius(g), rThRoot = WormGears.ThroatRootRadius(g);
      double h = b2 / 2.0;
      if (b2 <= 0 || h >= rThRoot)
        throw new ToolException("Ширина венца должна быть в (0; " + Tools2D.FmtNum(2 * rThRoot) + ") — шире дуги впадин горловины");
      if (rb <= 0 || rb > rf2 - 2.0 * g.M)
        throw new ToolException("Нужен bore в (0; " + Tools2D.FmtNum(2 * (rf2 - 2.0 * g.M)) + ")");
      bool right = RightHand(a);

      CreatePart(a, "Колесо червячное m" + Tools2D.FmtNum(g.M) + " z2=" + g.Z2);

      // тело: от отверстия до дуги впадин горловины; профили — (радиус, z)
      List<double[]> core = new List<double[]>();
      core.Add(new[] { rb, -h });
      core.AddRange(ThroatArc(g.A, rThRoot, -h, h, 24));
      core.Add(new[] { rb, h });
      Tools3D.RevolveZ(core, cut: false);

      // зубья: венец в поперечных сечениях с закруткой θ(z) = z·tgγ/r2; низ зубьев утоплен
      // в тело, вершины — с запасом до подрезки (ширина вершины не меньше 0.3m)
      double tanG = Math.Tan(g.Gamma * Math.PI / 180.0) * (right ? 1 : -1);
      double rLo = rf2 - 0.1 * g.M;   // глубже нельзя: при z2 = 20 зубья смыкаются у основания
      double rHi = Math.Min(rM + 0.5 * g.M, r2 + (Math.PI * g.M / 2.0 - 0.3 * g.M) / (2.0 * TanAlpha));
      double twist = Math.Abs(b2 * tanG / r2);
      int nSec = 2 + (int)(twist / (10.0 * Math.PI / 180.0));
      List<ksEntity> sections = new List<ksEntity>();
      for (int i = 0; i < nSec; i++)
      {
        double z = -h + b2 * i / (nSec - 1);
        List<double[]> ring = ToothRing(g.Z2, r2, rLo, rHi, Math.PI * g.M / 4.0, z * tanG / r2);
        sections.Add(Tools3D.PolySketch(SectionPlane(z), ring));
      }
      Tools3D.Loft(sections, cut: false, baseOp: false);

      // подрезка вершин: всё снаружи min(daM2/2, дуга вершин горловины)
      double zT = h + 1.0, rBig = rHi + 2.0 * g.M;
      double zc = Math.Sqrt(rTh * rTh - (g.A - rM) * (g.A - rM));   // дуга вершин выходит на daM2
      double zE = Math.Min(zc, zT);
      List<double[]> trim = new List<double[]>();
      trim.Add(new[] { rBig, -zT });
      trim.Add(new[] { rBig, zT });
      if (zc < zT) trim.Add(new[] { rM, zT });
      trim.AddRange(ThroatArc(g.A, rTh, zE, -zE, 24));
      if (zc < zT) trim.Add(new[] { rM, -zT });
      Tools3D.RevolveZ(trim, cut: true);
      // сечения лофта — сплошные диски с зубьями, поэтому отверстие режется последним
      Tools3D.Sketch(CircleSketch(rb));
      Tools3D.Extrude(new Dictionary<string, object> { { "mode", "through" } }, cut: true);

      SetMaterial("БрО10Ф1 ГОСТ 613-79", 8760);
      object saved = Save(a);
      return new Dictionary<string, object>
      {
        { "saved", saved },
        { "d2", g.D2 }, { "da2", g.Da2 }, { "df2", g.Df2 }, { "daM2", g.DaM2 },
        { "a", g.A }, { "width", b2 }, { "throat_radius", rTh },
        { "hand", right ? "right" : "left" },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }

    // Дуга горловины: центр на оси червяка (радиус a от оси колеса, z = 0),
    // r(z) = a − √(R² − z²); точки (r, z) от z0 до z1 включительно.
    static IEnumerable<double[]> ThroatArc(double a, double R, double z0, double z1, int n)
    {
      for (int i = 0; i <= n; i++)
      {
        double z = z0 + (z1 - z0) * i / n;
        yield return new[] { a - Math.Sqrt(R * R - z * z), z };
      }
    }

    // Поперечное сечение венца: z зубьев с прямыми боковыми сторонами 20°, толщина зуба
    // 2·halfPitch на радиусе r0; зуб k — по центру угла 2πk/z + rot. Между зубьями — дуга rLo.
    static List<double[]> ToothRing(int z, double r0, double rLo, double rHi, double halfPitch, double rot)
    {
      List<double[]> pts = new List<double[]>(z * 6);
      double step = 2.0 * Math.PI / z;
      double aLo = (halfPitch + (r0 - rLo) * TanAlpha) / rLo, aHi = (halfPitch - (rHi - r0) * TanAlpha) / rHi;
      for (int k = 0; k < z; k++)
      {
        double c = k * step + rot;
        pts.Add(Polar(rLo, c - aLo));
        pts.Add(Polar(rHi, c - aHi));
        pts.Add(Polar(rHi, c + aHi));
        pts.Add(Polar(rLo, c + aLo));
        double gap = step - 2 * aLo;
        pts.Add(Polar(rLo, c + aLo + gap / 3));
        pts.Add(Polar(rLo, c + aLo + 2 * gap / 3));
      }
      return pts;
    }

    // ================== коническое колесо ==================

    internal static BevelBlank Bevel(double me, int z, double deltaDeg, double width, double bore, double hubL)
    {
      try { return BevelGears.Blank(me, z, deltaDeg, width, bore, hubL); }
      catch (Exception e) { throw new ToolException(e.Message); }
    }

    static object BevelWheel3D(Dictionary<string, object> a)
    {
      double me = ToolRegistry.GetDbl(a, "me");
      int z = ToolRegistry.GetInt(a, "z");
      double bore = ToolRegistry.GetDbl(a, "bore");
      BevelBlank b = Bevel(me, z, ToolRegistry.GetDbl(a, "delta"), ToolRegistry.GetDbl(a, "width", 0), bore,
        ToolRegistry.GetDbl(a, "hubL", 0.5 * bore));
      BuildBevel(a, b, "Колесо коническое me" + Tools2D.FmtNum(me) + " z" + z);
      object saved = Save(a);
      BevelWheel w = b.W;
      return new Dictionary<string, object>
      {
        { "saved", saved },
        { "de", w.De }, { "dae", Math.Round(w.Dae, 4) }, { "dfe", Math.Round(w.Dfe, 4) },
        { "Re", Math.Round(w.Ri, 4) }, { "width", Math.Round(w.B, 4) }, { "delta_deg", w.Delta },
        { "back_face_z", Math.Round(b.BackZ, 4) }, { "apex", "начало координат" },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }

    static void BuildBevel(Dictionary<string, object> a, BevelBlank b, string defName)
    {
      BevelWheel w = b.W;
      CreatePart(a, defName);
      Tools3D.RevolveZ(b.Outline(), cut: false);

      // впадина в опорной плоскости z = Re·cosδ (через делительную окружность внешнего торца):
      // высоты в этой плоскости растянуты в 1/cosδ, угол профиля tgα' = tgα·cosδ.
      // Сечения — центральная проекция из вершины конуса, вырез проходит деталь насквозь.
      double d = w.Delta * Math.PI / 180.0;
      double zRef = w.Ri * Math.Cos(d), re = w.De / 2.0;
      double rLo = b.Fe[0] * zRef / b.Fe[1];
      double rHi = b.Te[0] * zRef / b.Te[1] + w.Me;
      double tanA = TanAlpha * Math.Cos(d), half = Math.PI * w.Me / 4.0;
      double aLo = (half - (re - rLo) * tanA) / rLo, aHi = (half + (rHi - re) * tanA) / rHi;
      double[][] space = { Polar(rLo, -aLo), Polar(rHi, -aHi), Polar(rHi, aHi), Polar(rLo, aLo) };
      List<ksEntity> sections = new List<ksEntity>();
      foreach (double zs in new[] { b.Ti[1] - 0.5, b.EndZ + 0.5 })
      {
        double s = zs / zRef;
        List<double[]> sec = new List<double[]>();
        foreach (double[] q in space) sec.Add(new[] { q[0] * s, q[1] * s });
        sections.Add(Tools3D.PolySketch(SectionPlane(zs), sec));
      }
      ksEntity cut = Tools3D.Loft(sections, cut: true, baseOp: false);
      Tools3D.CircularCopyZ(cut, w.Z);
      SetMaterial("Сталь 40Х ГОСТ 4543-2016", 7820);
    }

    // ================== сборки ==================

    static object WormPairAsm(Dictionary<string, object> a)
    {
      WormPair g = Pair(a, 40);
      bool right = RightHand(a);
      string asmPath = AsmPath(a, "worm_pair.a3d");
      string dir = System.IO.Path.GetDirectoryName(asmPath);
      string tag = "m" + Tools2D.FmtNum(g.M) + "_z" + g.Z1 + "x" + g.Z2;
      string wormPath = System.IO.Path.Combine(dir, "worm_" + tag + ".m3d");
      string wheelPath = System.IO.Path.Combine(dir, "worm_wheel_" + tag + ".m3d");

      Dictionary<string, object> wa = Sub(a, "m", "z1", "q", "z2", "hand");
      wa["path"] = wormPath;
      Dictionary<string, object> worm = (Dictionary<string, object>)WormShaft(wa);
      Tools3D.CloseCurrentDoc();
      Dictionary<string, object> ka = Sub(a, "m", "z1", "q", "z2", "hand", "bore", "width");
      ka["path"] = wheelPath;
      WormWheel(ka);
      Tools3D.CloseCurrentDoc();

      CreateAsm(asmPath, ToolRegistry.GetStr(a, "name", "Передача червячная " + tag.Replace('_', ' ')));
      ksPart cWheel = Tools3D.AddComponentImpl(wheelPath, true);
      ksPart cWorm = Tools3D.AddComponentImpl(wormPath, true);
      // колесо: зуб 0 — на +X в средней плоскости. Червяк — вдоль +Y на x = a, его локальная +X
      // смотрит на колесо (−X): там впадины на z = j·P (для любого числа заходов)
      double zc = (double)worm["thread_from_z"] + (double)worm["length"] / 2.0;
      double zg = Math.Round(zc / g.P) * g.P;
      Tools3D.PlacePart(cWheel, 0, 0, 0, 0);
      Tools3D.PlacePartAxes(cWorm, new[] { g.A, -zg, 0 }, new[] { -1.0, 0, 0 }, new[] { 0, 0, 1.0 });
      return FinishAsm(new Dictionary<string, object>
      {
        { "worm", wormPath }, { "wheel", wheelPath }, { "a", g.A }, { "ratio", g.U }
      });
    }

    static object BevelPairAsm(Dictionary<string, object> a)
    {
      double me = ToolRegistry.GetDbl(a, "me");
      int z1 = ToolRegistry.GetInt(a, "z1"), z2 = ToolRegistry.GetInt(a, "z2");
      if (z1 < 8 || z2 < z1) throw new ToolException("Нужны 8 ≤ z1 ≤ z2");
      double delta1 = Math.Atan2(z1, z2) * 180.0 / Math.PI;
      double width = ToolRegistry.GetDbl(a, "width", 0);
      string asmPath = AsmPath(a, "bevel_pair.a3d");
      string dir = System.IO.Path.GetDirectoryName(asmPath);
      string tag = "me" + Tools2D.FmtNum(me) + "_z" + z1 + "x" + z2;
      string pinionPath = System.IO.Path.Combine(dir, "bevel_pinion_" + tag + ".m3d");
      string wheelPath = System.IO.Path.Combine(dir, "bevel_wheel_" + tag + ".m3d");

      double bore1 = ToolRegistry.GetDbl(a, "bore1"), bore2 = ToolRegistry.GetDbl(a, "bore2");
      BevelBlank L1 = Bevel(me, z1, delta1, width, bore1, 0.5 * bore1);
      BevelBlank L2 = Bevel(me, z2, 90.0 - delta1, width, bore2, 0.5 * bore2);
      BuildBevel(new Dictionary<string, object> { { "path", pinionPath } }, L1, "Шестерня коническая z" + z1);
      Save(new Dictionary<string, object> { { "path", pinionPath } });
      Tools3D.CloseCurrentDoc();
      BuildBevel(new Dictionary<string, object> { { "path", wheelPath } }, L2, "Колесо коническое z" + z2);
      Save(new Dictionary<string, object> { { "path", wheelPath } });
      Tools3D.CloseCurrentDoc();

      CreateAsm(asmPath, ToolRegistry.GetStr(a, "name", "Передача коническая " + tag.Replace('_', ' ')));
      ksPart cWheel = Tools3D.AddComponentImpl(wheelPath, true);
      ksPart cPinion = Tools3D.AddComponentImpl(pinionPath, true);
      // впадины обоих колёс — на локальной +X. Колесо: впадина на +X, туда же смотрит линия
      // контакта. Шестерня: ось → X, локальная +X → +Z, поворот на полшага ставит зуб на +Z
      double psi = Math.PI / z1;
      Tools3D.PlacePart(cWheel, 0, 0, 0, 0);
      Tools3D.PlacePartAxes(cPinion, new[] { 0.0, 0, 0 },
        new[] { 0, -Math.Sin(psi), Math.Cos(psi) }, new[] { 0, -Math.Cos(psi), -Math.Sin(psi) });
      return FinishAsm(new Dictionary<string, object>
      {
        { "pinion", pinionPath }, { "wheel", wheelPath },
        { "delta1_deg", Math.Round(delta1, 4) }, { "delta2_deg", Math.Round(90 - delta1, 4) },
        { "ratio", Math.Round((double)z2 / z1, 4) }
      });
    }

    static string AsmPath(Dictionary<string, object> a, string defName)
    {
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path == null) return Paths.Out(defName);
      return Paths.Abs(path);
    }

    static void CreateAsm(string path, string name)
    {
      Tools3D.CreateAssembly(new Dictionary<string, object> { { "name", name }, { "path", path } });
    }

    static Dictionary<string, object> FinishAsm(Dictionary<string, object> res)
    {
      try { Tools3D.GetPart().RebuildModel(); } catch (Exception e) { Log.Error("assembly rebuild", e); }
      Tools3D.SavePart(new Dictionary<string, object>());
      res["saved"] = Tools3D.PartPath;
      return res;
    }

    static Dictionary<string, object> Sub(Dictionary<string, object> a, params string[] keys)
    {
      Dictionary<string, object> r = new Dictionary<string, object>();
      foreach (string k in keys)
      {
        object v;
        if (a.TryGetValue(k, out v)) r[k] = v;
      }
      return r;
    }

    // ================== общие помощники ==================

    static double[] Polar(double r, double th) { return new[] { r * Math.Cos(th), r * Math.Sin(th) }; }

    static ksEntity SectionPlane(double z)
    {
      return Math.Abs(z) < 1e-9 ? Tools3D.PlaneByName(Tools3D.GetPart(), "XOY") : Tools3D.OffsetPlane("XOY", z);
    }

    static Dictionary<string, object> CircleSketch(double r)
    {
      Dictionary<string, object> c = new Dictionary<string, object>
      {
        { "type", "circle" }, { "xc", 0.0 }, { "yc", 0.0 }, { "r", r }
      };
      return new Dictionary<string, object> { { "plane", "XOY" }, { "elements", new List<object> { c } } };
    }

    static void Cylinder(double z0, double r, double depth, bool baseOp)
    {
      Dictionary<string, object> sk = CircleSketch(r);
      if (z0 != 0) sk["offset"] = z0;
      Tools3D.Sketch(sk);
      Tools3D.Extrude(new Dictionary<string, object> { { "depth", depth }, { "base", baseOp } }, cut: false);
    }

    static void CreatePart(Dictionary<string, object> a, string defName)
    {
      Dictionary<string, object> args = new Dictionary<string, object>();
      args["name"] = ToolRegistry.GetStr(a, "name", defName);
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) args["path"] = path;
      Tools3D.CreatePart(args);
    }

    static void SetMaterial(string name, double density)
    {
      Tools3D.SetMaterialOp(new Dictionary<string, object> { { "name", name }, { "density", density } });
    }

    static object Save(Dictionary<string, object> a)
    {
      Dictionary<string, object> s = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) s["path"] = path;
      return ((Dictionary<string, object>)Tools3D.SavePart(s))["saved"];
    }
  }
}
