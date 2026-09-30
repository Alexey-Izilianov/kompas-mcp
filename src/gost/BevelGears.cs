using System;

namespace KompasMcp.Gost
{
  // Ортогональная прямозубая коническая передача (Σ=90°), ГОСТ 19624-74.
  // Упрощение: смещение xe=0, высота зуба h* = 2m + 0.2m (внешняя высота).
  public class BevelWheel
  {
    public double Me;      // внешний окружной модуль
    public int Z;          // число зубьев
    public double Delta;   // угол делительного конуса (град)
    public double De;      // внешний делительный диаметр
    public double Dae;     // внешний диаметр вершин
    public double Dfe;     // внешний диаметр впадин
    public double Ri;      // внешнее конусное расстояние
    public double Ha;      // внешняя высота ножки зуба
    public double Hf;      // внешняя высота головки зуба
    public double ThetaF;  // угол конуса впадин (град)
    public double DeltaA;  // угол конуса вершин (град)
    public double B;       // ширина венца (дано или 0.3R)
    public double Di;      // делительный диаметр внутреннего торца
  }

  public static class BevelGears
  {
    // wheel=true для колеса (угол = 90° - delta1); вход delta — угол своего конуса
    public static BevelWheel Wheel(double me, int z, double deltaDeg, double b)
    {
      if (me <= 0 || me > 25) throw new Exception("Модуль me должен быть 0..25");
      if (z < 8 || z > 200) throw new Exception("Число зубьев конического колеса 8..200");
      if (deltaDeg <= 5 || deltaDeg >= 85) throw new Exception("Угол делительного конуса 5..85 (орто-передача)");
      if (b < 0 || b > 0.5 * 1000) throw new Exception("Ширина венца должна быть 0..500");

      BevelWheel w = new BevelWheel();
      w.Me = me; w.Z = z;
      w.De = me * z;
      double dRad = deltaDeg * Math.PI / 180.0;
      w.Ri = w.De / (2.0 * Math.Sin(dRad));
      // внешняя высота головки/ножки (h_a*=1, c*=0.2, xe=0)
      double hf = 1.2 * me;   // ножка
      double ha = 1.0 * me;   // головка
      w.Ha = ha; w.Hf = hf;
      // ГОСТ 2.405: dae по внешнему делительному + головка на внешнем торце
      w.Dae = w.De + 2.0 * ha * Math.Cos(dRad);
      w.Dfe = w.De - 2.0 * hf * Math.Cos(dRad);
      // угол ножки/вершин (тангенс от конусного расстояния)
      double thetaF = Math.Atan(hf / w.Ri);
      w.ThetaF = thetaF * 180.0 / Math.PI;
      w.DeltaA = deltaDeg + thetaF * 180.0 / Math.PI;
      // ширина венца: дано или 0.3R (ГОСТ 19624: b <= 0.3R, округлять — не делаем)
      if (b <= 0) b = 0.3 * w.Ri;
      if (b > 0.35 * w.Ri) b = 0.35 * w.Ri; // жёсткое ограничение, чтобы не выйти за конус
      w.B = b;
      // делительный диаметр внутреннего торца: de - 2b*sin(delta)
      w.Di = w.De - 2.0 * b * Math.Sin(dRad);
      return w;
    }

    // Парный угол конуса (Σ=90°): delta2 = 90 - delta1
    public static double MateDelta(double deltaDeg) { return 90.0 - deltaDeg; }
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
      if (m <= 0 || m > 20) throw new Exception("Модуль m должен быть 0..20");
      if (z1 < 1 || z1 > 4) throw new Exception("Число заходов z1 = 1..4");
      if (q < 6 || q > 25) throw new Exception("Коэффициент диаметра q = 6..25");
      if (z2 < 20 || z2 > 400) throw new Exception("Число зубьев колеса z2 = 20..400");

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
  }
}