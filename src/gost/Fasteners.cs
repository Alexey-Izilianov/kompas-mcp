using System;

namespace KompasMcp.Gost
{
  // Таблицы стандартных изделий.
  // ГОСТ 7805 - болты с шестигранной головкой (класс точности А, исполнение 1)
  // ГОСТ 5915 - гайки шестигранные
  // ГОСТ 11371 - шайбы круглые
  // ГОСТ 8338 - подшипники шариковые радиальные однорядные
  // Шпильки/штифты - формульные (без таблиц: geometry-only),
  // размеры из аргументов: stud ГОСТ 22032 (b1=1d), pin ГОСТ 3128, cpin ГОСТ 24896 (конусность 1:50).
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
      switch ((int)Math.Round(d))
      {
        case 6: return new HexBolt { D = 6, S = 10, E = 11.05, K = 4.0, B = 18 };
        case 8: return new HexBolt { D = 8, S = 13, E = 14.38, K = 5.3, B = 22 };
        case 10: return new HexBolt { D = 10, S = 16, E = 17.77, K = 6.4, B = 26 };
        case 12: return new HexBolt { D = 12, S = 18, E = 20.03, K = 7.5, B = 30 };
        case 16: return new HexBolt { D = 16, S = 24, E = 26.75, K = 10.0, B = 38 };
        case 20: return new HexBolt { D = 20, S = 30, E = 33.03, K = 12.5, B = 46 };
        case 24: return new HexBolt { D = 24, S = 36, E = 39.55, K = 15.0, B = 54 };
        // ГОСТ 7805-70, таблицы 1-2: М27 (применять не рекомендуется), М30, М36, М42, М48
        case 27: return new HexBolt { D = 27, S = 41, E = 45.6, K = 17.0, B = 60 };
        case 30: return new HexBolt { D = 30, S = 46, E = 51.3, K = 18.7, B = 66 };
        case 36: return new HexBolt { D = 36, S = 55, E = 61.3, K = 22.5, B = 78 };
        case 42: return new HexBolt { D = 42, S = 65, E = 72.6, K = 26.0, B = 90 };
        case 48: return new HexBolt { D = 48, S = 75, E = 83.9, K = 30.0, B = 102 };
        default: return null;
      }
    }

    public static HexNut Nut(double d)
    {
      switch ((int)Math.Round(d))
      {
        case 6: return new HexNut { D = 6, S = 10, E = 11.05, M = 5.2 };
        case 8: return new HexNut { D = 8, S = 13, E = 14.38, M = 6.8 };
        case 10: return new HexNut { D = 10, S = 16, E = 17.77, M = 8.4 };
        case 12: return new HexNut { D = 12, S = 18, E = 20.03, M = 10.8 };
        case 16: return new HexNut { D = 16, S = 24, E = 26.75, M = 14.8 };
        case 20: return new HexNut { D = 20, S = 30, E = 33.03, M = 18.0 };
        case 24: return new HexNut { D = 24, S = 36, E = 39.55, M = 21.5 };
        // ГОСТ 5915-70: М27-M48
        case 27: return new HexNut { D = 27, S = 41, E = 45.2, M = 23.6 };
        case 30: return new HexNut { D = 30, S = 46, E = 50.9, M = 25.6 };
        case 36: return new HexNut { D = 36, S = 55, E = 60.8, M = 31.0 };
        case 42: return new HexNut { D = 42, S = 65, E = 71.3, M = 34.0 };
        case 48: return new HexNut { D = 48, S = 75, E = 82.6, M = 38.0 };
        default: return null;
      }
    }

    public static Washer WasherFor(double d)
    {
      switch ((int)Math.Round(d))
      {
        case 6: return new Washer { D = 6, D1 = 6.4, D2 = 12.0, S = 1.6 };
        case 8: return new Washer { D = 8, D1 = 8.4, D2 = 16.0, S = 2.0 };
        case 10: return new Washer { D = 10, D1 = 10.5, D2 = 20.0, S = 2.0 };
        case 12: return new Washer { D = 12, D1 = 13.0, D2 = 24.0, S = 2.5 };
        case 16: return new Washer { D = 16, D1 = 17.0, D2 = 30.0, S = 3.0 };
        case 20: return new Washer { D = 20, D1 = 21.0, D2 = 37.0, S = 3.0 };
        case 24: return new Washer { D = 24, D1 = 25.0, D2 = 44.0, S = 4.0 };
        // ГОСТ 11371-78 (норм. ряд, исполнение 1, кл. С): М27-M48
        case 27: return new Washer { D = 27, D1 = 30.0, D2 = 50.0, S = 4.0 };
        case 30: return new Washer { D = 30, D1 = 33.0, D2 = 56.0, S = 4.0 };
        case 36: return new Washer { D = 36, D1 = 39.0, D2 = 66.0, S = 5.0 };
        case 42: return new Washer { D = 42, D1 = 45.0, D2 = 78.0, S = 7.0 };
        case 48: return new Washer { D = 48, D1 = 52.0, D2 = 92.0, S = 8.0 };
        default: return null;
      }
    }

    // ГОСТ 8338-75, лёгкая (200) и средняя (300) серии. Посадочный диаметр по двум последним
    // цифрам кода: 00..03 — 10, 12, 15, 17 мм, далее ×5 (208 => d = 40). Масса и шарик — справочные.
    public static BallBearing Bearing(int code)
    {
      int n = code % 100;
      double bore = n < 4 ? new[] { 10.0, 12, 15, 17 }[n] : n * 5.0;
      double dOuter, width, mass, ball;
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
        case 211: dOuter = 100; width = 21; mass = 0.60; ball = 14.29; break;
        case 212: dOuter = 110; width = 22; mass = 0.78; ball = 15.88; break;
        case 213: dOuter = 120; width = 23; mass = 0.99; ball = 16.67; break;
        case 214: dOuter = 125; width = 24; mass = 1.08; ball = 17.46; break;
        case 215: dOuter = 130; width = 25; mass = 1.18; ball = 17.46; break;
        case 305: dOuter = 62; width = 17; mass = 0.229; ball = 11.5; break;
        case 306: dOuter = 72; width = 19; mass = 0.345; ball = 12.7; break;
        case 307: dOuter = 80; width = 21; mass = 0.414; ball = 13.49; break;
        case 308: dOuter = 90; width = 23; mass = 0.63; ball = 15.08; break;
        case 309: dOuter = 100; width = 25; mass = 0.83; ball = 17.46; break;
        case 310: dOuter = 110; width = 27; mass = 1.08; ball = 19.05; break;
        case 311: dOuter = 120; width = 29; mass = 1.37; ball = 20.64; break;
        case 312: dOuter = 130; width = 31; mass = 1.71; ball = 22.23; break;
        case 313: dOuter = 140; width = 33; mass = 2.11; ball = 23.81; break;
        case 314: dOuter = 150; width = 35; mass = 2.60; ball = 25.4; break;
        default: return null;
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