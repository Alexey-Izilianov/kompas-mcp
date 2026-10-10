using System;
using System.Collections.Generic;

namespace KompasMcp.Gost
{
  // Допуски и посадки ЕСДП (ГОСТ 25346-2013 / ГОСТ 25347-2013 = ISO 286), размеры до 500 мм, мкм.
  // Значения IT и основных отклонений — табличные: формулы стандарта дают их только до округления
  // по особым правилам. Отверстия: A..H — EI = −es вала той же буквы; K, M, N (до IT8) и P..U
  // (до IT7) — ES = −ei вала + Δ, Δ = IT(n) − IT(n−1); грубее — без Δ (K, N — ES = 0).
  public static class GostEsdp
  {
    // Основные интервалы (для IT) и промежуточные (для отклонений): верхние границы, мм.
    static readonly double[] Steps = { 3, 6, 10, 18, 30, 50, 80, 120, 180, 250, 315, 400, 500 };
    static readonly double[] Fine = { 3, 6, 10, 14, 18, 24, 30, 40, 50, 65, 80, 100, 120, 140, 160, 180,
                                      200, 225, 250, 280, 315, 355, 400, 450, 500 };

    // IT1..IT18 по основным интервалам
    static readonly double[][] It =
    {
      new double[] { 0.8, 1, 1, 1.2, 1.5, 1.5, 2, 2.5, 3.5, 4.5, 6, 7, 8 },
      new double[] { 1.2, 1.5, 1.5, 2, 2.5, 2.5, 3, 4, 5, 7, 8, 9, 10 },
      new double[] { 2, 2.5, 2.5, 3, 4, 4, 5, 6, 8, 10, 12, 13, 15 },
      new double[] { 3, 4, 4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 20 },
      new double[] { 4, 5, 6, 8, 9, 11, 13, 15, 18, 20, 23, 25, 27 },
      new double[] { 6, 8, 9, 11, 13, 16, 19, 22, 25, 29, 32, 36, 40 },
      new double[] { 10, 12, 15, 18, 21, 25, 30, 35, 40, 46, 52, 57, 63 },
      new double[] { 14, 18, 22, 27, 33, 39, 46, 54, 63, 72, 81, 89, 97 },
      new double[] { 25, 30, 36, 43, 52, 62, 74, 87, 100, 115, 130, 140, 155 },
      new double[] { 40, 48, 58, 70, 84, 100, 120, 140, 160, 185, 210, 230, 250 },
      new double[] { 60, 75, 90, 110, 130, 160, 190, 220, 250, 290, 320, 360, 400 },
      new double[] { 100, 120, 150, 180, 210, 250, 300, 350, 400, 460, 520, 570, 630 },
      new double[] { 140, 180, 220, 270, 330, 390, 460, 540, 630, 720, 810, 890, 970 },
      new double[] { 250, 300, 360, 430, 520, 620, 740, 870, 1000, 1150, 1300, 1400, 1550 },
      new double[] { 400, 480, 580, 700, 840, 1000, 1200, 1400, 1600, 1850, 2100, 2300, 2500 },
      new double[] { 600, 750, 900, 1100, 1300, 1600, 1900, 2200, 2500, 2900, 3200, 3600, 4000 },
      new double[] { 1000, 1200, 1500, 1800, 2100, 2500, 3000, 3500, 4000, 4600, 5200, 5700, 6300 },
      new double[] { 1400, 1800, 2200, 2700, 3300, 3900, 4600, 5400, 6300, 7200, 8100, 8900, 9700 }
    };

    // Основные отклонения валов по промежуточным интервалам: a..g — |es|, k..u — ei; 0 у t — нет поля.
    static readonly Dictionary<string, int[]> Shaft = new Dictionary<string, int[]>
    {
      { "a", new[] { 270, 270, 280, 290, 290, 300, 300, 310, 320, 340, 360, 380, 410, 460, 520, 580, 660, 740, 820, 920, 1050, 1200, 1350, 1500, 1650 } },
      { "b", new[] { 140, 140, 150, 150, 150, 160, 160, 170, 180, 190, 200, 220, 240, 260, 280, 310, 340, 380, 420, 480, 540, 600, 680, 760, 840 } },
      { "c", new[] { 60, 70, 80, 95, 95, 110, 110, 120, 130, 140, 150, 170, 180, 200, 210, 230, 240, 260, 280, 300, 330, 360, 400, 440, 480 } },
      { "d", new[] { 20, 30, 40, 50, 50, 65, 65, 80, 80, 100, 100, 120, 120, 145, 145, 145, 170, 170, 170, 190, 190, 210, 210, 230, 230 } },
      { "e", new[] { 14, 20, 25, 32, 32, 40, 40, 50, 50, 60, 60, 72, 72, 85, 85, 85, 100, 100, 100, 110, 110, 125, 125, 135, 135 } },
      { "f", new[] { 6, 10, 13, 16, 16, 20, 20, 25, 25, 30, 30, 36, 36, 43, 43, 43, 50, 50, 50, 56, 56, 62, 62, 68, 68 } },
      { "g", new[] { 2, 4, 5, 6, 6, 7, 7, 9, 9, 10, 10, 12, 12, 14, 14, 14, 15, 15, 15, 17, 17, 18, 18, 20, 20 } },
      { "k", new[] { 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 5, 5 } },
      { "m", new[] { 2, 4, 6, 7, 7, 8, 8, 9, 9, 11, 11, 13, 13, 15, 15, 15, 17, 17, 17, 20, 20, 21, 21, 23, 23 } },
      { "n", new[] { 4, 8, 10, 12, 12, 15, 15, 17, 17, 20, 20, 23, 23, 27, 27, 27, 31, 31, 31, 34, 34, 37, 37, 40, 40 } },
      { "p", new[] { 6, 12, 15, 18, 18, 22, 22, 26, 26, 32, 32, 37, 37, 43, 43, 43, 50, 50, 50, 56, 56, 62, 62, 68, 68 } },
      { "r", new[] { 10, 15, 19, 23, 23, 28, 28, 34, 34, 41, 43, 51, 54, 63, 65, 68, 77, 80, 84, 94, 98, 108, 114, 126, 132 } },
      { "s", new[] { 14, 19, 23, 28, 28, 35, 35, 43, 43, 53, 59, 71, 79, 92, 100, 108, 122, 130, 140, 158, 170, 190, 208, 232, 252 } },
      { "t", new[] { 0, 0, 0, 0, 0, 0, 41, 48, 54, 66, 75, 91, 104, 122, 134, 146, 166, 180, 196, 218, 240, 268, 294, 330, 360 } },
      { "u", new[] { 18, 23, 28, 33, 33, 41, 48, 60, 70, 87, 102, 124, 144, 170, 190, 210, 236, 258, 284, 315, 350, 390, 435, 490, 540 } }
    };

    public static double ItValue(int grade, double d)
    {
      if (grade < 1 || grade > 18) throw new ToolException("Квалитет " + grade + " не поддерживается (1–18)");
      return It[grade - 1][Index(Steps, d)];
    }

    static int Index(double[] bounds, double d)
    {
      if (d <= 0 || d > 500) throw new ToolException("Диаметр " + d + " вне диапазона (0–500 мм)");
      int i = 0;
      while (d > bounds[i]) i++;
      return i;
    }

    static int ShaftValue(string letter, double d)
    {
      int[] row;
      if (!Shaft.TryGetValue(letter, out row)) throw new ToolException("Поле допуска '" + letter + "' не поддерживается (a–h, js, k–u)");
      int v = row[Index(Fine, d)];
      if (letter == "t" && v == 0) throw new ToolException("Поле t определено для диаметров свыше 24 мм");
      return v;
    }

    // Вал: верхнее es и нижнее ei отклонения
    static void ShaftDevs(string letter, int grade, double d, double it, out double es, out double ei)
    {
      if (letter == "h") { es = 0; ei = -it; return; }
      if (letter == "js") { es = it / 2; ei = -it / 2; return; }
      if ("abcdefg".Contains(letter)) { es = -ShaftValue(letter, d); ei = es - it; return; }
      // у k нижнее отклонение ненулевое только для IT4..IT7 (до 3 мм — всегда 0)
      ei = letter == "k" && (grade < 4 || grade > 7) ? 0 : ShaftValue(letter, d);
      es = ei + it;
    }

    // Отверстие: верхнее ES и нижнее EI отклонения
    static void HoleDevs(string letter, int grade, double d, double it, out double es, out double ei)
    {
      string s = letter.ToLowerInvariant();
      if (letter == "H") { ei = 0; es = it; return; }
      if (letter == "JS") { es = it / 2; ei = -it / 2; return; }
      if ("ABCDEFG".Contains(letter)) { ei = ShaftValue(s, d); es = ei + it; return; }
      double delta = grade >= 3 ? it - ItValue(grade - 1, d) : 0;
      if ("KMN".Contains(letter))
      {
        if (grade <= 8) es = -ShaftValue(s, d) + delta;
        else es = letter == "M" ? -ShaftValue(s, d) : 0;
      }
      else es = -ShaftValue(s, d) + (grade <= 7 ? delta : 0);
      ei = es - it;
    }

    public static Dictionary<string, object> Fit(string holeField, string shaftField, double d)
    {
      string hl = Letter(holeField).ToUpperInvariant(), sl = Letter(shaftField).ToLowerInvariant();
      int hg = Grade(holeField), sg = Grade(shaftField);
      double itH = ItValue(hg, d), itS = ItValue(sg, d);
      double hes, hei, ses, sei;
      HoleDevs(hl, hg, d, itH, out hes, out hei);
      ShaftDevs(sl, sg, d, itS, out ses, out sei);

      double clearanceMin = hei - ses;   // < 0 — натяг
      double clearanceMax = hes - sei;
      string kind = clearanceMin >= 0 ? "с зазором" : clearanceMax <= 0 ? "с натягом" : "переходная";

      return new Dictionary<string, object>
      {
        { "diameter", d },
        { "hole", new Dictionary<string, object> { { "field", holeField }, { "ES_um", hes }, { "EI_um", hei }, { "IT_um", itH } } },
        { "shaft", new Dictionary<string, object> { { "field", shaftField }, { "es_um", ses }, { "ei_um", sei }, { "IT_um", itS } } },
        { "clearance_min_um", clearanceMin },
        { "clearance_max_um", clearanceMax },
        { "fit_type", kind }
      };
    }

    // «H7» → «H», «js6» → «js»
    static string Letter(string field)
    {
      int n = 0;
      while (n < field.Length && char.IsLetter(field[n])) n++;
      if (n == 0) throw new ToolException("Нет буквы основного отклонения в поле: " + field);
      return field.Substring(0, n);
    }

    static int Grade(string field)
    {
      int g;
      if (!int.TryParse(field.Substring(Letter(field).Length), out g))
        throw new ToolException("Нет квалитета в поле: " + field);
      return g;
    }
  }
}
