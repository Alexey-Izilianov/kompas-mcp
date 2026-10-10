using System;
using System.Collections.Generic;

namespace KompasMcp.Gost
{
  // Ортогональная прямозубая коническая передача (Σ=90°), ГОСТ 19624-74.
  // Упрощение: смещение xe = 0, внешняя высота зуба 2.2·me (головка me, ножка 1.2·me).
  public class BevelWheel
  {
    public double Me;      // внешний окружной модуль
    public int Z;          // число зубьев
    public double Delta;   // угол делительного конуса (град)
    public double De;      // внешний делительный диаметр
    public double Dae;     // внешний диаметр вершин
    public double Dfe;     // внешний диаметр впадин
    public double Ri;      // внешнее конусное расстояние
    public double Ha;      // внешняя высота головки зуба
    public double Hf;      // внешняя высота ножки зуба
    public double ThetaF;  // угол конуса впадин (град)
    public double DeltaA;  // угол конуса вершин (град)
    public double B;       // ширина венца (дано или 0.3R)
    public double Di;      // делительный диаметр внутреннего торца
  }

  public static class BevelGears
  {
    // deltaDeg — угол делительного конуса этого колеса (у парного — MateDelta)
    public static BevelWheel Wheel(double me, int z, double deltaDeg, double b)
    {
      if (me <= 0 || me > 25) throw new ToolException("Модуль me должен быть 0..25");
      if (z < 8 || z > 200) throw new ToolException("Число зубьев конического колеса 8..200");
      if (deltaDeg <= 5 || deltaDeg >= 85) throw new ToolException("Угол делительного конуса 5..85 (орто-передача)");
      if (b < 0 || b > 500) throw new ToolException("Ширина венца должна быть 0..500");

      BevelWheel w = new BevelWheel();
      w.Me = me; w.Z = z; w.Delta = deltaDeg;
      w.De = me * z;
      double dRad = deltaDeg * Math.PI / 180.0;
      w.Ri = w.De / (2.0 * Math.Sin(dRad));
      double ha = me, hf = 1.2 * me;   // h_a* = 1, c* = 0.2
      w.Ha = ha; w.Hf = hf;
      // ГОСТ 2.405: dae по внешнему делительному + головка на внешнем торце
      w.Dae = w.De + 2.0 * ha * Math.Cos(dRad);
      w.Dfe = w.De - 2.0 * hf * Math.Cos(dRad);
      // угол ножки/вершин (тангенс от конусного расстояния)
      double thetaF = Math.Atan(hf / w.Ri);
      w.ThetaF = thetaF * 180.0 / Math.PI;
      w.DeltaA = deltaDeg + thetaF * 180.0 / Math.PI;
      // ширина венца: задана или 0.3·Re (ГОСТ 19624), не больше 0.35·Re — иначе зуб уходит к вершине
      if (b <= 0) b = 0.3 * w.Ri;
      if (b > 0.35 * w.Ri) b = 0.35 * w.Ri;
      w.B = b;
      // делительный диаметр внутреннего торца: de - 2b*sin(delta)
      w.Di = w.De - 2.0 * b * Math.Sin(dRad);
      return w;
    }

    // Парный угол конуса (Σ=90°): delta2 = 90 - delta1
    public static double MateDelta(double deltaDeg) { return 90.0 - deltaDeg; }

    // Осевой профиль заготовки (общий для чертежа и 3D-модели)
    public static BevelBlank Blank(double me, int z, double deltaDeg, double width, double bore, double hubL)
    {
      if (bore <= 0) throw new ToolException("Нужен bore > 0");
      BevelWheel w = Wheel(me, z, deltaDeg, width);
      double d = deltaDeg * Math.PI / 180.0;
      double[] ug = { Math.Sin(d), Math.Cos(d) }, ub = { Math.Cos(d), -Math.Sin(d) };
      double re = w.Ri, k = (w.Ri - w.B) / w.Ri, rim = 1.5 * me;
      Func<double, double, double[]> pt = (along, across) =>
        new[] { along * ug[0] + across * ub[0], along * ug[1] + across * ub[1] };
      BevelBlank b = new BevelBlank { W = w, Rb = bore / 2.0, HubL = hubL };
      b.Pe = pt(re, 0);
      b.Te = pt(re, w.Ha);
      b.Fe = pt(re, -w.Hf);
      b.Ge = pt(re, -w.Hf - rim);
      b.Ti = pt(k * re, k * w.Ha);
      b.Fi = pt(k * re, -k * w.Hf);
      b.Gi = pt(k * re, -k * w.Hf - rim);
      if (b.Ti[1] < 1.0)
        throw new ToolException("Угол делительного конуса слишком велик для z=" + z + ": зубья заходят за вершину конуса");
      double rMin = Math.Min(b.Ge[0], b.Gi[0]);
      if (b.Rb > rMin - 1.0)
        throw new ToolException("Отверстие не помещается: наибольший bore " + (2 * (rMin - 1.0)).ToString("0.##"));
      b.Rh = Math.Min(0.8 * bore, b.Ge[0] - me);
      if (b.Rh < b.Rb + 1.0) b.HubL = 0;
      return b;
    }
  }

  // Заготовка конического колеса в осевой полуплоскости (r, z): вершина делительного конуса —
  // начало координат, ось Z; внешний торец — при большем z. Точки на внешнем (e) и внутреннем (i)
  // торцах: P — делительная, T — вершина зуба, F — впадина, G — низ обода под впадинами.
  public class BevelBlank
  {
    public BevelWheel W;
    public double[] Pe, Te, Fe, Ge, Ti, Fi, Gi;
    public double Rb, Rh, HubL;

    public double BackZ { get { return Ge[1]; } }
    public double EndZ { get { return HubL > 0 ? Ge[1] + HubL : Ge[1]; } }

    // Замкнутый контур: торец со стороны вершины → внутренний доп. конус → конус вершин →
    // внешний доп. конус → тыльный торец → ступица → отверстие
    public List<double[]> Outline()
    {
      List<double[]> p = new List<double[]> { new[] { Rb, Gi[1] }, Gi, Ti, Te, Ge };
      if (HubL > 0)
      {
        p.Add(new[] { Rh, BackZ });
        p.Add(new[] { Rh, EndZ });
      }
      p.Add(new[] { Rb, EndZ });
      return p;
    }
  }

  // Червячная передача (витки червяка архимедовы, ГОСТ 19036-81 / 19672-74)
  public class WormPair
  {
    public double M;       // осевой модуль
    public int Z1;         // число заходов червяка
    public double Q;       // коэффициент диаметра червяка
    public int Z2;         // число зубьев колеса
    public double D1;      // делительный диаметр червяка
    public double Da1; public double Df1;
    public double P;       // осевой шаг
    public double Pz;      // ход витка
    public double Gamma;   // угол подъёма винтовой линии (град)
    public double D2;      // делительный диаметр колеса
    public double Da2;     // диаметр вершин колеса
    public double DaM2;    // наибольший диаметр колеса
    public double Df2;     // диаметр впадин колеса
    public double A;       // межосевое расстояние
    public double U;       // передаточное число
  }

  public static class WormGears
  {
    public static WormPair Worm(double m, int z1, double q, int z2)
    {
      if (m <= 0 || m > 20) throw new ToolException("Модуль m должен быть 0..20");
      if (z1 < 1 || z1 > 4) throw new ToolException("Число заходов z1 = 1..4");
      if (q < 6 || q > 25) throw new ToolException("Коэффициент диаметра q = 6..25");
      if (z2 < 20 || z2 > 400) throw new ToolException("Число зубьев колеса z2 = 20..400");

      WormPair w = new WormPair();
      w.M = m; w.Z1 = z1; w.Q = q; w.Z2 = z2;
      w.D1 = m * q;
      w.P = Math.PI * m;
      w.Pz = w.P * z1;
      w.Gamma = Math.Atan(z1 / q) * 180.0 / Math.PI;
      w.Da1 = w.D1 + 2.0 * m;
      w.Df1 = w.D1 - 2.4 * m;   // h_a*=1, c*=0.2
      w.D2 = m * z2;
      w.Da2 = w.D2 + 2.0 * m;
      w.Df2 = w.D2 - 2.4 * m;
      // наибольший диаметр наружный (ГОСТ 2.402): z1=1: +2m, z1=2..3: +1.5m, z1=4: +m
      if (z1 == 1) w.DaM2 = w.Da2 + 2.0 * m;
      else if (z1 == 4) w.DaM2 = w.Da2 + 1.0 * m;
      else w.DaM2 = w.Da2 + 1.5 * m;
      w.A = (w.D1 + w.D2) / 2.0;
      w.U = (double)z2 / (double)z1;
      return w;
    }

    // Длина нарезанной части червяка (ГОСТ 19672): b1 = (4 + 0.2·z2)·m
    public static double ThreadLength(double m, int z2) { return Math.Ceiling((4.0 + 0.2 * z2) * m); }

    public static double JournalLength(double m) { return Math.Ceiling(2.8 * m + 6); }

    // Ширина венца колеса: b2 ≤ 0.75·da1 при z1 ≤ 3, b2 ≤ 0.67·da1 при z1 = 4
    public static double WheelWidth(WormPair g) { return (g.Z1 <= 3 ? 0.75 : 0.67) * g.Da1; }

    // Радиусы дуг горловины колеса (центр — на оси червяка): по вершинам и по впадинам
    public static double ThroatTipRadius(WormPair g) { return g.A - g.Da2 / 2.0; }
    public static double ThroatRootRadius(WormPair g) { return g.A - g.Df2 / 2.0; }
  }
}