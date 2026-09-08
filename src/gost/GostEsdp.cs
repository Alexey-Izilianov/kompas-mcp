using System;
using System.Collections.Generic;

namespace KompasMcp.Gost
{
  // Допуски и посадки ЕСДП (ГОСТ 25346-2013 / 25347-82), размеры до 500 мм.
  // IT — через единицу допуска i = 0.45·∛D + 0.001·D (D — среднее геометрическое ступени).
  // Основные отклонения валов: a..h — по формулам ГОСТ 25346 (es < 0); k..u — по таблице (ei > 0); js — ±IT/2.
  // Отверстия: A..H — зеркально (EI = −es вала); K..ZC — ES = −ei вала + Δ, где Δ = ITn − IT(n−1) (квалитеты 3..7).
  // ПРИМЕЧАНИЕ: таблица k..u — сверить значения с ГОСТ 25347 при первом прогоне (smoke-тест на ступени 30-50).
  // Отклонения в мкм.
  public static class GostEsdp
  {
    static readonly double[] stepUpper = { 3, 6, 10, 18, 30, 50, 80, 120, 180, 250, 315, 400, 500 };

    // Основные отклонения валов k, m, n, p, r, s, t, u — нижние ei (мкм) по ступеням. t — с 24 мм.
    static readonly int[][] shaftEi =
    {
      new int[] { 0,  2,  4,  6, 10, 14,  0, 18 },   // до 3
      new int[] { 1,  4,  8, 12, 15, 19,  0, 23 },   // 3-6
      new int[] { 1,  6, 10, 15, 19, 23,  0, 28 },   // 6-10
      new int[] { 1,  7, 12, 18, 23, 28,  0, 33 },   // 10-18
      new int[] { 2,  8, 15, 22, 28, 35, 41, 41 },   // 18-30 (t только с 24 мм)
      new int[] { 2,  9, 17, 26, 34, 43, 48, 54 },   // 30-50
      new int[] { 2, 11, 20, 32, 41, 53, 59, 70 },   // 50-80
      new int[] { 3, 13, 23, 37, 48, 63, 71, 87 },   // 80-120
      new int[] { 3, 15, 27, 43, 59, 71, 83, 100 },  // 120-180
      new int[] { 4, 17, 31, 50, 63, 79, 92, 108 },  // 180-250
      new int[] { 4, 21, 34, 56, 65, 88, 100, 114 }, // 250-315
      new int[] { 4, 21, 37, 62, 68, 98, 108, 126 }, // 315-400
      new int[] { 5, 23, 40, 68, 75, 108, 114, 132 } // 400-500
    };
    static readonly string[] shaftEiLetters = { "k", "m", "n", "p", "r", "s", "t", "u" };

    // ---- ступени и IT ----

    static int StepIndex(double d)
    {
      double lo = 0;
      for (int i = 0; i < stepUpper.Length; i++)
      {
        if (d <= stepUpper[i] && d > lo) return i;
        lo = stepUpper[i];
      }
      throw new ArgumentException("Диаметр " + d + " вне диапазона (до 500 мм)");
    }

    static double GeomMean(int idx)
    {
      if (idx == 0) return Math.Sqrt(1.0 * stepUpper[0]); // первый шаг принимают √(1·3)
      return Math.Sqrt(stepUpper[idx - 1] * stepUpper[idx]);
    }

    public static int ItValue(int grade, double d)
    {
      int it;
      switch (grade)
      {
        case 14: it = 250; break;   // IT14..IT18 — постоянные до 500 мм
        case 15: it = 400; break;
        case 16: it = 600; break;
        case 17: it = 1000; break;
        case 18: it = 1600; break;
        default:
          {
            if (grade < 1 || grade > 13) throw new ArgumentException("Квалитет " + grade + " не поддерживается (1-18)");
            double i = 0.45 * Math.Pow(GeomMean(StepIndex(d)), 1.0 / 3.0) + 0.001 * GeomMean(StepIndex(d));
            double k;
            switch (grade)
            {
              case 1: k = 2; break;
              case 2: k = 2.7; break;
              case 3: k = 4; break;
              case 4: k = 5; break;
              case 5: k = 7; break;
              case 6: k = 10; break;
              case 7: k = 16; break;
              case 8: k = 25; break;
              case 9: k = 40; break;
              case 10: k = 64; break;
              case 11: k = 100; break;
              case 12: k = 160; break;
              default: k = 250; break;
            }
            it = (int)Math.Round(k * i, MidpointRounding.AwayFromZero);
            break;
          }
      }
      return it;
    }

    // ---- отклонения вала: es, ei (мкм) ----

    static void ShaftDevs(string letter, double d, int it, out int es, out int ei)
    {
      int idx = StepIndex(d);
      double D = GeomMean(idx);
      switch (letter)
      {
        case "a": es = d <= 120 ? -(int)Math.Round(265 + 1.3 * D) : -(int)Math.Round(3.5 * D); break;
        case "b": es = d <= 160 ? -(int)Math.Round(140 + 0.85 * D) : -(int)Math.Round(1.8 * D); break;
        case "c": es = d <= 40 ? -52 : -(int)Math.Round(95 + 0.8 * D); break;
        case "d": es = -(int)Math.Round(16 * Math.Pow(D, 0.44)); break;
        case "e": es = -(int)Math.Round(11 * Math.Pow(D, 0.41)); break;
        case "f": es = -(int)Math.Round(5.5 * Math.Pow(D, 0.41)); break;
        case "g": es = -(int)Math.Round(2.5 * Math.Pow(D, 0.34)); break;
        case "h": es = 0; break;
        case "js": es = it - it / 2; break;
        default:
          {
            int li = IndexOf(shaftEiLetters, letter);
            if (li < 0) throw new ArgumentException("Поле допуска вала '" + letter + "' не поддерживается");
            ei = shaftEi[idx][li];
            es = ei + it;
            return;
          }
      }
      ei = es - it;
    }

    // ---- отклонения отверстия: ES, EI (мкм) ----

    static void HoleDevs(string letter, double d, int it, out int es, out int ei)
    {
      if (letter == "H")
      {
        ei = 0;
        es = it;
        return;
      }
      if (letter == "JS")
      {
        es = it - it / 2;
        ei = -es;
        return;
      }
      if ("ABCDEFGH".IndexOf(letter) >= 0)
      {
        // зеркально валу той же буквы: EI = −es(вала), ES = EI + IT
        int ses, sei;
        ShaftDevs(letter.ToLowerInvariant(), d, it, out ses, out sei);
        ei = -ses;
        es = ei + it;
        return;
      }
      if ("KMNPRSTU".IndexOf(letter) >= 0)
      {
        // зеркально с Δ = ITn − IT(n−1) для квалитетов 3..7
        int ses, sei;
        ShaftDevs(letter.ToLowerInvariant(), d, it, out ses, out sei);
        int delta = 0;
        int grade = lastGrade;
        if (grade >= 3 && grade <= 7) delta = it - ItValue(grade - 1, d);
        if (letter == "N" && grade >= 9) { es = 0; ei = -it; return; }
        es = -sei + delta;
        ei = es - it;
        return;
      }
      throw new ArgumentException("Поле допуска отверстия '" + letter + "' не поддерживается");
    }

    static int lastGrade;

    // ---- публичный расчёт посадки ----

    public static Dictionary<string, object> Fit(string holeField, string shaftField, double d)
    {
      int itH = ItValue(GradeOf(holeField), d);
      int itS = ItValue(GradeOf(shaftField), d);

      lastGrade = GradeOf(holeField);
      int hes, hei;
      HoleDevs(LetterOf(holeField), d, itH, out hes, out hei);

      lastGrade = GradeOf(shaftField);
      int ses, sei;
      ShaftDevs(LetterOf(shaftField).ToLowerInvariant(), d, itS, out ses, out sei);

      int clearanceMin = hei - ses;   // минимальный зазор = EI(отв) − es(вала); <0 → натяг
      int clearanceMax = hes - sei;   // максимальный зазор = ES(отв) − ei(вала)

      string kind;
      if (clearanceMin >= 0) kind = "с зазором";
      else if (clearanceMax <= 0) kind = "с натягом";
      else kind = "переходная";

      var hole = new Dictionary<string, object>();
      hole["field"] = holeField;
      hole["ES_um"] = hes;
      hole["EI_um"] = hei;
      hole["IT_um"] = itH;

      var shaft = new Dictionary<string, object>();
      shaft["field"] = shaftField;
      shaft["es_um"] = ses;
      shaft["ei_um"] = sei;
      shaft["IT_um"] = itS;

      var res = new Dictionary<string, object>();
      res["diameter"] = d;
      res["hole"] = hole;
      res["shaft"] = shaft;
      res["clearance_min_um"] = clearanceMin;
      res["clearance_max_um"] = clearanceMax;
      res["fit_type"] = kind;
      return res;
    }

    static string LetterOf(string field)
    {
      if (field.Length >= 2 && (field[1] == 'S' || field[1] == 's') &&
          (field[0] == 'J' || field[0] == 'j')) return "JS";
      return field.Substring(0, 1).ToUpperInvariant();
    }

    static int GradeOf(string field)
    {
      int start = (field.Length >= 2 && (field[1] == 'S' || field[1] == 's')) ? 2 : 1;
      int g;
      if (!int.TryParse(field.Substring(start), out g))
        throw new ArgumentException("Нет квалитета в поле: " + field);
      return g;
    }

    static int IndexOf(string[] arr, string s)
    {
      for (int i = 0; i < arr.Length; i++) if (arr[i] == s) return i;
      return -1;
    }
  }
}