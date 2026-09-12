using System;
using System.Collections.Generic;

namespace KompasMcp.Gost
{
  // Цилиндрические эвольвентные передачи, ГОСТ 16532-70 (геометрия), ГОСТ 13755-2003 (исходный контур, альфа=20).
  // Без смещения (x=0). Толщина зуба на радиусе r: s_r = r * [ s/d + inv(alpha) - inv(acos(db/r)) ].
  public static class Gost16532
  {
    public const double AlphaDeg = 20.0;

    public class Wheel
    {
      public double M;          // модуль
      public int Z;             // число зубьев
      public double D;          // делительный
      public double Da;         // вершин
      public double Df;         // впадин
      public double Db;         // основной
      public double ToothAddendum;   // ha = m
      public double ToothDedendum;   // hf = 1.25*m
      public double ToothThickness;  // s = pi*m/2 на делительном
      public bool Undercut;     // z < 17 без смещения - подрезание
    }

    public static Wheel WheelGeom(double m, int z)
    {
      double alpha = Math.PI * AlphaDeg / 180.0;
      double d = m * z;
      return new Wheel
      {
        M = m,
        Z = z,
        D = d,
        Da = d + 2 * m,
        Df = d - 2.5 * m,
        Db = d * Math.Cos(alpha),
        ToothAddendum = m,
        ToothDedendum = 1.25 * m,
        ToothThickness = Math.PI * m / 2.0,
        Undercut = z < 17
      };
    }

    public class Mesh
    {
      public double A;          // межосевое
      public Wheel W1;
      public Wheel W2;
      public double Ratio;      // передаточное z2/z1
    }

    public static Mesh MeshGeom(double m, int z1, int z2)
    {
      return new Mesh
      {
        A = m * (z1 + z2) / 2.0,
        W1 = WheelGeom(m, z1),
        W2 = WheelGeom(m, z2),
        Ratio = (double)z2 / z1
      };
    }

    // inv(t) = tan(t) - t
    static double Inv(double t) { return Math.Tan(t) - t; }

    // Половина угловой толщины зуба на радиусе r (рад):
    // psi(r) = s_r/(2r), s_r = 2r*(s/d + inv(a) - inv(a_r)), т.е. psi = s/d + inv(a) - inv(acos(rb/r)).
    // Для r < rb эвольвенты нет - NaN (ниже rb профиль продолжается радиально с psi(rb)).
    public static double HalfAngleAtRad(double m, int z, double r)
    {
      double alpha = Math.PI * AlphaDeg / 180.0;
      double rb = m * z * Math.Cos(alpha) / 2.0; // радиус основной окружности, r задан радиусом
      double d = m * z;
      double s = Math.PI * m / 2.0;
      if (r <= rb) return double.NaN;
      return s / d + Inv(alpha) - Inv(Math.Acos(rb / r));
    }

    // Замкнутый контур колеса точками [x,y] для эскиза (аппроксимация эвольвенты отрезками).
    // Зуб центрирован на угле tooth*step: грани tooth*step ± psi(r).
    // От rb вверх - эвольвента, от rb вниз до rf - радиальные отрезки,
    // вершины и впадины - хорды. Контур замкнут: впадина - хорда от ножки зуба k к ножке зуба k+1.
    public static List<double[]> ProfilePoints(double m, int z, int samplesPerFlank)
    {
      var pts = new List<double[]>();
      double ra = m * z / 2.0 + m;
      double rf = m * z / 2.0 - 1.25 * m;
      double rb = m * z * Math.Cos(Math.PI * AlphaDeg / 180.0) / 2.0;
      double step = 2.0 * Math.PI / z;
      double rStart = Math.Max(rb, rf);
      double alpha = Math.PI * AlphaDeg / 180.0;
      // psi на основной окружности - радиальное продолжение профиля ниже rb
      double psiBase = Math.PI * m / 2.0 / (m * z) + Inv(alpha);

      // Добавление точки с фильтром дублей (нулевой сегмент делает контур невалидным для КОМПАСа)
      Action<double, double> add = delegate(double x, double y)
      {
        if (pts.Count > 0)
        {
          double[] p = pts[pts.Count - 1];
          if (Math.Abs(p[0] - x) < 1e-9 && Math.Abs(p[1] - y) < 1e-9) return;
        }
        pts.Add(new double[] { x, y });
      };

      for (int tooth = 0; tooth < z; tooth++)
      {
        double c = step * tooth; // центр зуба
        // левая грань: от ножки вверх к вершине (угол растёт, т.к. psi убывает с r)
        for (int i = 0; i <= samplesPerFlank; i++)
        {
          double r = rStart + (ra - rStart) * i / samplesPerFlank;
          double psi = HalfAngleAtRad(m, z, r);
          if (double.IsNaN(psi)) psi = psiBase;
          add(r * Math.Cos(c - psi), r * Math.Sin(c - psi));
        }
        // вершина: правая точка на ra (хорда вершины)
        double psiTop = HalfAngleAtRad(m, z, ra);
        if (double.IsNaN(psiTop)) psiTop = psiBase;
        add(ra * Math.Cos(c + psiTop), ra * Math.Sin(c + psiTop));
        // правая грань: от вершины вниз
        for (int i = samplesPerFlank - 1; i >= 0; i--)
        {
          double r = rStart + (ra - rStart) * i / samplesPerFlank;
          double psi = HalfAngleAtRad(m, z, r);
          if (double.IsNaN(psi)) psi = psiBase;
          add(r * Math.Cos(c + psi), r * Math.Sin(c + psi));
        }
        // ножка радиально вниз до rf; далее хорда по впадине к ножке следующего зуба
        if (rStart > rf)
        {
          add(rf * Math.Cos(c + psiBase), rf * Math.Sin(c + psiBase));
        }
      }
      // замыкание: если последняя точка совпала с первой - убрать её
      if (pts.Count > 1)
      {
        double[] f = pts[0];
        double[] l = pts[pts.Count - 1];
        if (Math.Abs(f[0] - l[0]) < 1e-9 && Math.Abs(f[1] - l[1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
      }
      return pts;
    }
  }
}