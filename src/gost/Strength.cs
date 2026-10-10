using System;

namespace KompasMcp.Gost
{
  // Проверочные расчёты передач и пружин — инженерная оценка по упрощённым методикам:
  //  - цилиндрическая прямозубая передача: ГОСТ 21354-87 (контакт и изгиб), коэффициенты
  //    нагрузки — по скорости и степени точности, материалы — улучшенные стали HB ≤ 350;
  //  - червячная передача (архимедов червяк, бронзовый венец): методика Чернавского;
  //  - пружина сжатия: ГОСТ 13765-86 (силы, высоты, напряжения с коэффициентом Валя).
  // Срок службы и шероховатость не учитываются (ZN = YN = 1).
  public static class Strength
  {
    public class SpurResult
    {
      public double U, V, Ft, EpsAlpha, KH, KF;
      public double SigmaH, SigmaHP, SigmaF1, SigmaF2, SigmaFP1, SigmaFP2;
      public double RatioDeviation = double.NaN, RatioAllowed;
      public bool Ok;
    }

    public static SpurResult Spur(double m, int z1, int z2, double b, double t1Nm, double n1,
      double hb1, double hb2, int accuracy, double uTarget)
    {
      if (m <= 0 || z1 < 6 || z2 < 6 || b <= 0) throw new Exception("Нужны m > 0, z ≥ 6, b > 0");
      if (t1Nm <= 0 || n1 <= 0) throw new Exception("Нужны момент T1 > 0 и частота n1 > 0");
      if (hb1 < 150 || hb1 > 350 || hb2 < 150 || hb2 > 350)
        throw new Exception("Методика — для улучшенных сталей: твёрдость 150..350 НВ");
      SpurResult r = new SpurResult();
      double d1 = m * z1;
      r.U = (double)z2 / z1;
      r.V = Math.PI * d1 * n1 / 60000.0;
      r.Ft = 2000.0 * t1Nm / d1;
      r.EpsAlpha = 1.88 - 3.2 * (1.0 / z1 + 1.0 / z2);
      double zEps = Math.Sqrt((4.0 - r.EpsAlpha) / 3.0);
      const double zE = 190.0, zH = 2.49;   // сталь по стали; α = 20°, x = 0, β = 0
      double grade = Math.Max(0, accuracy - 8);
      double kHv = 1.0 + (0.035 + 0.005 * grade) * r.V, kFv = 1.0 + (0.07 + 0.01 * grade) * r.V;
      r.KH = 1.05 * kHv;      // KHβ = 1.05 — симметричное расположение колёс
      r.KF = 1.08 * kFv;
      r.SigmaH = zE * zH * zEps * Math.Sqrt(r.Ft * r.KH * (r.U + 1) / (b * d1 * r.U));
      // σHlim = 2HB + 70, SH = 1.1, ZR·Zv ≈ 0.9; для прямозубой — меньшее из двух колёс
      r.SigmaHP = Math.Min(2 * hb1 + 70, 2 * hb2 + 70) * 0.9 / 1.1;
      double yF1 = 3.47 + 13.2 / z1, yF2 = 3.47 + 13.2 / z2;
      r.SigmaF1 = yF1 * r.Ft * r.KF / (b * m);
      r.SigmaF2 = r.SigmaF1 * yF2 / yF1;
      r.SigmaFP1 = 1.75 * hb1 / 1.7;   // σFlim = 1.75HB, SF = 1.7
      r.SigmaFP2 = 1.75 * hb2 / 1.7;
      r.Ok = r.SigmaH <= 1.05 * r.SigmaHP && r.SigmaF1 <= r.SigmaFP1 && r.SigmaF2 <= r.SigmaFP2;
      if (uTarget > 0)
      {
        // ГОСТ 2185: отклонение передаточного числа ≤ 2.5 % при u ≤ 4.5, иначе ≤ 4 %
        r.RatioDeviation = Math.Abs(r.U - uTarget) / uTarget * 100.0;
        r.RatioAllowed = uTarget <= 4.5 ? 2.5 : 4.0;
        r.Ok &= r.RatioDeviation <= r.RatioAllowed;
      }
      return r;
    }

    public class WormResult
    {
      public double U, Gamma, Vs, Friction, Efficiency, Ft2, K;
      public double SigmaH, SigmaHP, SigmaF, SigmaFP;
      public bool SelfLocking, Ok;
      public double RatioDeviation = double.NaN, RatioAllowed;
    }

    // vs, м/с → приведённый коэффициент трения f' (оловянная бронза, закалённый шлифованный червяк)
    static readonly double[,] FrictionTable =
    {
      { 0.1, 0.08 }, { 0.5, 0.055 }, { 1, 0.045 }, { 1.5, 0.04 }, { 2, 0.035 }, { 2.5, 0.03 },
      { 3, 0.028 }, { 4, 0.023 }, { 7, 0.018 }, { 10, 0.016 }, { 15, 0.014 }
    };

    // zv → коэффициент формы зуба червячного колеса YF
    static readonly double[,] FormTable =
    {
      { 20, 1.98 }, { 24, 1.88 }, { 26, 1.85 }, { 28, 1.80 }, { 30, 1.76 }, { 32, 1.71 }, { 35, 1.64 },
      { 37, 1.61 }, { 40, 1.55 }, { 45, 1.48 }, { 50, 1.45 }, { 60, 1.40 }, { 80, 1.34 }, { 100, 1.30 },
      { 150, 1.27 }
    };

    public static WormResult Worm(WormPair g, double t2Nm, double n1, double sigmaB, double sigmaT,
      bool hardWorm, double uTarget)
    {
      if (t2Nm <= 0 || n1 <= 0) throw new Exception("Нужны момент на колесе T2 > 0 и частота червяка n1 > 0");
      if (sigmaB <= 0 || sigmaT <= 0) throw new Exception("Нужны σв > 0 и σт > 0 материала венца");
      WormResult r = new WormResult();
      double gam = g.Gamma * Math.PI / 180.0;
      r.U = g.U;
      r.Gamma = g.Gamma;
      r.Vs = Math.PI * g.D1 * n1 / (60000.0 * Math.Cos(gam));
      r.Friction = Interp(FrictionTable, r.Vs);
      double rho = Math.Atan(r.Friction);
      // 0.96 — потери в подшипниках и на перемешивание масла
      r.Efficiency = 0.96 * Math.Tan(gam) / Math.Tan(gam + rho);
      r.SelfLocking = gam <= rho;
      double kv = r.Vs <= 3 ? 1.0 : r.Vs <= 7.5 ? 1.1 : 1.25;
      r.K = 1.05 * kv;
      double zq = g.Z2 / g.Q;
      double t2Nmm = t2Nm * 1000.0;
      r.SigmaH = 170.0 / zq * Math.Sqrt(Math.Pow((zq + 1) / g.A, 3) * t2Nmm * r.K);
      double cv = Math.Max(0.7, Math.Min(1.0, 1.66 * Math.Pow(Math.Max(r.Vs, 0.1), -0.352)));
      r.SigmaHP = cv * (hardWorm ? 0.9 : 0.75) * sigmaB;
      r.Ft2 = 2000.0 * t2Nm / g.D2;
      double b2 = WormGears.WheelWidth(g);
      double zv = g.Z2 / Math.Pow(Math.Cos(gam), 3);
      r.SigmaF = 0.7 * Interp(FormTable, zv) * r.Ft2 * r.K / (b2 * g.M);
      r.SigmaFP = 0.25 * sigmaT + 0.08 * sigmaB;   // нереверсивная передача
      r.Ok = r.SigmaH <= 1.05 * r.SigmaHP && r.SigmaF <= r.SigmaFP;
      if (uTarget > 0)
      {
        r.RatioDeviation = Math.Abs(r.U - uTarget) / uTarget * 100.0;
        r.RatioAllowed = 4.0;   // ГОСТ 2144: червячные передачи — до 4 %
        r.Ok &= r.RatioDeviation <= r.RatioAllowed;
      }
      return r;
    }

    public class SpringResult
    {
      public double F1, F2, F3, S1, S2, S3, H1, H2, H3, Tau1, Tau2, Tau3, TauAllow, Slenderness;
      public bool Ok, NeedsGuide;
    }

    // Пружина сжатия с поджатыми и шлифованными концами (по 1 опорному витку, 1.5 обработанных):
    // H3 = (n1 − 0.5)·d, n1 = n + 2; F3 — сила при соприкосновении витков.
    public static SpringResult Spring(Gost13765.Spring s, double f1, double f2, double tauAllow)
    {
      if (f1 < 0 || f2 <= f1) throw new Exception("Нужны 0 ≤ F1 < F2");
      SpringResult r = new SpringResult { F1 = f1, F2 = f2, TauAllow = tauAllow };
      r.H3 = (s.TotalCoils - 0.5) * s.WireD;
      r.S3 = s.H0 - r.H3;
      if (r.S3 <= 0) throw new Exception("Шаг слишком мал: витки соприкасаются в свободном состоянии");
      r.F3 = s.Stiffness * r.S3;
      r.S1 = f1 / s.Stiffness;
      r.S2 = f2 / s.Stiffness;
      r.H1 = s.H0 - r.S1;
      r.H2 = s.H0 - r.S2;
      r.Tau1 = Gost13765.ShearStress(s, f1);
      r.Tau2 = Gost13765.ShearStress(s, f2);
      r.Tau3 = Gost13765.ShearStress(s, r.F3);
      r.Slenderness = s.H0 / s.D;
      r.NeedsGuide = r.Slenderness > 2.6;   // иначе пружина теряет устойчивость без оправки
      // рабочий ход не доходит до смыкания витков: F2 ≤ 0.9·F3 (ГОСТ 13765, δ = 0.1)
      r.Ok = f2 <= 0.9 * r.F3 && r.Tau3 <= tauAllow;
      return r;
    }

    static double Interp(double[,] table, double x)
    {
      int n = table.GetLength(0);
      if (x <= table[0, 0]) return table[0, 1];
      for (int i = 1; i < n; i++)
        if (x <= table[i, 0])
        {
          double t = (x - table[i - 1, 0]) / (table[i, 0] - table[i - 1, 0]);
          return table[i - 1, 1] + t * (table[i, 1] - table[i - 1, 1]);
        }
      return table[n - 1, 1];
    }
  }
}
