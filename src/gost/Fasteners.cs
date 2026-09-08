using System;

namespace KompasMcp.Gost
{
  // Таблицы стандартных изделий (первый набор).
  // ГОСТ 7805 - болты с шестигранной головкой (нормальная точность, исполнение 1)
  // ГОСТ 5915 - гайки шестигранные (нормальная точность)
  // ГОСТ 11371 - шайбы круглые
  // ГОСТ 8338 - подшипники шариковые радиальные однорядные
  public static class Fasteners
  {
    public class HexBolt
    {
      public double D;        // номинал резьбы, мм
      public double S;        // размер под ключ
      public double E;        // диаметр описанной окружности головки
      public double K;        // высота головки
      public double B;        // длина резьбы (l <= 125)
    }

    public class HexNut
    {
      public double D;
      public double S;
      public double E;
      public double M;        // высота гайки
    }

    public class Washer
    {
      public double D;        // номинал крепежа
      public double D1;       // внутренний диаметр
      public double D2;       // наружный диаметр
      public double S;        // толщина
    }

    public class BallBearing
    {
      public int Code;
      public double D;        // внутр. диаметр (посадочный)
      public double DOuter;   // наружный диаметр
      public double B;        // ширина
      public double R;        // радиус фаски
      public double BallDia;  // диаметр шарика (упрощённая модель)
      public double MassKg;   // масса по справочнику
    }

    // Болт М(12) => d=12. Возвращает null, если номинала нет в таблице.
    public static HexBolt Bolt(double d)
    {
      double dd = Math.Round(d);
      switch ((int)dd)
      {
        case 6: return new HexBolt { D = 6, S = 10, E = 11.05, K = 4.0, B = 18 };
        case 8: return new HexBolt { D = 8, S = 13, E = 14.38, K = 5.3, B = 22 };
        case 10: return new HexBolt { D = 10, S = 16, E = 17.77, K = 6.4, B = 26 };
        case 12: return new HexBolt { D = 12, S = 18, E = 20.03, K = 7.5, B = 30 };
        case 16: return new HexBolt { D = 16, S = 24, E = 26.75, K = 10.0, B = 38 };
        case 20: return new HexBolt { D = 20, S = 30, E = 33.03, K = 12.5, B = 46 };
        case 24: return new HexBolt { D = 24, S = 36, E = 39.55, K = 15.0, B = 54 };
        default: return null;
      }
    }

    public static HexNut Nut(double d)
    {
      double dd = Math.Round(d);
      switch ((int)dd)
      {
        case 6: return new HexNut { D = 6, S = 10, E = 11.05, M = 5.2 };
        case 8: return new HexNut { D = 8, S = 13, E = 14.38, M = 6.8 };
        case 10: return new HexNut { D = 10, S = 16, E = 17.77, M = 8.4 };
        case 12: return new HexNut { D = 12, S = 18, E = 20.03, M = 10.8 };
        case 16: return new HexNut { D = 16, S = 24, E = 26.75, M = 14.8 };
        case 20: return new HexNut { D = 20, S = 30, E = 33.03, M = 18.0 };
        case 24: return new HexNut { D = 24, S = 36, E = 39.55, M = 21.5 };
        default: return null;
      }
    }

    public static Washer WasherFor(double d)
    {
      double dd = Math.Round(d);
      switch ((int)dd)
      {
        case 6: return new Washer { D = 6, D1 = 6.4, D2 = 12.0, S = 1.6 };
        case 8: return new Washer { D = 8, D1 = 8.4, D2 = 16.0, S = 2.0 };
        case 10: return new Washer { D = 10, D1 = 10.5, D2 = 20.0, S = 2.0 };
        case 12: return new Washer { D = 12, D1 = 13.0, D2 = 24.0, S = 2.5 };
        case 16: return new Washer { D = 16, D1 = 17.0, D2 = 30.0, S = 3.0 };
        case 20: return new Washer { D = 20, D1 = 21.0, D2 = 37.0, S = 3.0 };
        case 24: return new Washer { D = 24, D1 = 25.0, D2 = 44.0, S = 4.0 };
        default: return null;
      }
    }

    // Код: посадочный диаметр = (code % 100)*5, напр. 208 => d=40, D=80, B=18
    public static BallBearing Bearing(int code)
    {
      double bore = (code % 100) * 5.0;
      double dOuter, width, mass, ball;
      if (code >= 300 && code < 400)
      {
        // серия 300 (средняя): D = bore*2.2 примерно
        switch (code)
        {
          case 305: dOuter = 62; width = 17; mass = 0.229; ball = 11.5; break;
          case 306: dOuter = 72; width = 19; mass = 0.345; ball = 12.7; break;
          case 307: dOuter = 80; width = 21; mass = 0.414; ball = 13.49; break;
          default: return null;
        }
      }
      else
      {
        switch (code)
        {
          case 201: dOuter = 32; width = 10; mass = 0.040; ball = 7.14; break;
          case 202: dOuter = 35; width = 11; mass = 0.056; ball = 7.94; break;
          case 203: dOuter = 40; width = 12; mass = 0.072; ball = 8.73; break;
          case 204: dOuter = 47; width = 14; mass = 0.110; ball = 9.53; break;
          case 205: dOuter = 52; width = 15; mass = 0.130; ball = 10.32; break;
          case 206: dOuter = 62; width = 16; mass = 0.206; ball = 11.11; break;
          case 207: dOuter = 72; width = 17; mass = 0.288; ball = 11.51; break;
          case 208: dOuter = 80; width = 18; mass = 0.368; ball = 12.7; break;
          case 209: dOuter = 85; width = 19; mass = 0.414; ball = 12.7; break;
          case 210: dOuter = 90; width = 20; mass = 0.455; ball = 14.29; break;
          default: return null;
        }
      }
      return new BallBearing
      {
        Code = code,
        D = bore,
        DOuter = dOuter,
        B = width,
        R = Math.Max(0.5, bore / 40.0),
        BallDia = ball,
        MassKg = mass
      };
    }

    // Площадь правильного шестиугольника по радиусу описанной окружности
    public static double HexArea(double rOuter)
    {
      return 2.598076211 * rOuter * rOuter;
    }

    // Вершины шестиугольника (плоская грань сверху: вершины под углами 30+60i)
    public static void HexVertices(double rOuter, out double[] xs, out double[] ys)
    {
      xs = new double[6];
      ys = new double[6];
      for (int i = 0; i < 6; i++)
      {
        double a = Math.PI * (30 + 60 * i) / 180.0;
        xs[i] = rOuter * Math.Cos(a);
        ys[i] = rOuter * Math.Sin(a);
      }
    }
  }
}