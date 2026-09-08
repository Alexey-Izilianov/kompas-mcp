using System;

namespace KompasMcp.Gost
{
  // Цилиндрические винтовые пружины сжатия/растяжения, ГОСТ 13765-86 (параметры) / ГОСТ 2.401 (изображение).
  // Расчёт: индекс пружины c = D/d, жёсткость k = G*d^4 / (8*D^3*n), коэффициент формы витка (Валь) Kc,
  // касательное напряжение tau = Kc * 8*F*D / (pi*d^3).
  // Материал по умолчанию: пружинная сталь, G = 78500 МПа (60С2А / 65Г).
  public static class Gost13765
  {
    public const double DefaultShearModulusMPa = 78500.0; // G, МПа

    public class Spring
    {
      public double D;          // средний диаметр витка (мм)
      public double WireD;      // диаметр проволоки d (мм)
      public int N;             // число рабочих витков
      public double T;          // шаг (мм)
      public double G;          // модуль сдвига (МПа)
      public double IndexC;     // индекс пружины c = D/d
      public double Wahl;       // коэффициент формы витка
      public double Stiffness;  // жёсткость, Н/мм
      public double H0;         // свободная высота (сжатие, 2 опорных витка): n*t + 2*d
      public int TotalCoils;    // полные витки: n + 2
      public double WireLen;   // длина проволоки (мм): pi*D*N/sin(угол подъёма) ~ pi*D*sqrt(1+(t/(pi*D))^2)*N
    }

    public static Spring Calc(double d, double D, int n, double? t, double? gMPa)
    {
      if (d <= 0 || D <= 0 || n <= 0) throw new ArgumentException("d, D, n должны быть > 0");
      if (D <= d) throw new ArgumentException("Средний диаметр D должен быть больше диаметра проволоки d");
      double tt = t ?? 0.3 * D; // типовой шаг ~ 0.3*D
      if (tt < d) throw new ArgumentException("Шаг t должен быть больше диаметра проволоки (иначе витки сомкнуты)");
      double c = D / d;
      // Валь: (4c-1)/(4c-4) + 0.615/c
      double wahl = (4 * c - 1) / (4 * c - 4) + 0.615 / c;
      double G = gMPa ?? DefaultShearModulusMPa;
      double k = G * Math.Pow(d, 4) / (8.0 * Math.Pow(D, 3) * n); // Н/мм (МПа=Н/мм^2)
      double helix = Math.PI * D;
      double wireLen = Math.Sqrt(helix * helix + tt * tt) * n + Math.PI * D * 1.5; // + 1.5 витка опорных
      return new Spring
      {
        D = D,
        WireD = d,
        N = n,
        T = tt,
        G = G,
        IndexC = c,
        Wahl = wahl,
        Stiffness = k,
        H0 = n * tt + 2 * d,
        TotalCoils = n + 2,
        WireLen = wireLen
      };
    }

    // Касательное напряжение при осевой силе F (Н), МПа
    public static double ShearStress(Spring s, double forceN)
    {
      return s.Wahl * 8.0 * forceN * s.D / (Math.PI * Math.Pow(s.WireD, 3));
    }

    // Прогиб при силе F (мм)
    public static double Deflection(Spring s, double forceN)
    {
      return forceN / s.Stiffness;
    }
  }
}