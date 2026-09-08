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

    // Половина угловой толщины зуба на радиусе r (рад). Для r < db эвольвенты нет - NaN.
    public static double HalfAngleAtRad(double m, int z, double r)
    {
      double alpha = Math.PI * AlphaDeg / 180.0;
      double d = m * z;
      double db = d * Math.Cos(alpha);
      double s = Math.PI * m / 2.0;
      if (r <= db) return double.NaN;
      double phiR = Math.Acos(db / r);
      double sr = r * (s / d + Inv(alpha) - Inv(phiR));
      return sr / (2.0 * r);
    }

    // Замкнутый контур колеса точками [x,y] для эскиза (аппроксимация эвольвенты отрезками).
    // От db вверх - эвольвента, от db вниз до rf - радиальные отрезки (упрощение),
    // вершины и впадины - хорды. Контур замкнут: последняя точка стыкуется с первой хордой по rf.
    public static List<double[]> ProfilePoints(double m, int z, int samplesPerFlank)
    {
      var pts = new List<double[]>();
      double ra = m * z / 2.0 + m;
      double rf = m * z / 2.0 - 1.25 * m;
      double db = m * z * Math.Cos(Math.PI * AlphaDeg / 180.0) / 2.0;
      double step = 2.0 * Math.PI / z;
      double rStart = Math.Max(db, rf);

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
        double baseAng = step * tooth;
        // правая эвольвентная сторона зуба: от ножки к вершине
        for (int i = 0; i <= samplesPerFlank; i++)
        {
          double r = rStart + (ra - rStart) * i / samplesPerFlank;
          double psi = HalfAngleAtRad(m, z, r);
          if (double.IsNaN(psi)) psi = 0;
          add(r * Math.Cos(baseAng + psi), r * Math.Sin(baseAng + psi));
        }
        // вершина зуба: вторая точка на ra
        double angTop = baseAng + step - HalfAngleAtRad(m, z, ra);
        add(ra * Math.Cos(angTop), ra * Math.Sin(angTop));
        // левая эвольвентная сторона: от вершины вниз
        for (int i = samplesPerFlank; i >= 0; i--)
        {
          double r = rStart + (ra - rStart) * i / samplesPerFlank;
          double psi = HalfAngleAtRad(m, z, r);
          if (double.IsNaN(psi)) psi = 0;
          add(r * Math.Cos(baseAng + step - psi), r * Math.Sin(baseAng + step - psi));
        }
        // ножка радиально вниз до rf; хорда по впадине до первого пункта следующего зуба - отрезком
        if (rStart > rf)
        {
          double psiS = HalfAngleAtRad(m, z, rStart);
          if (double.IsNaN(psiS)) psiS = 0;
          double angDown = baseAng + step - psiS;
          add(rf * Math.Cos(angDown), rf * Math.Sin(angDown));
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