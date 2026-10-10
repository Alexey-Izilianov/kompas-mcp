using System;
using System.Collections.Generic;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Проверочные расчёты по ГОСТ (без КОМПАСа). Каждый тул возвращает вердикт, промежуточные
  // величины и строки tech — их можно передать в параметр tech чертежа детали.
  public static class ToolsCheck
  {
    public static void Register()
    {
      ToolRegistry.Add("gear_check",
        "Проверочный расчёт цилиндрической прямозубой передачи (x=0) по ГОСТ 21354-87: контактная прочность и изгиб зубьев, отклонение передаточного числа по ГОСТ 2185. Стали улучшенные, 150..350 НВ. Возвращает tech — строки для технических требований чертежа.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Модуль""},
""z1"":{""type"":""integer"",""description"":""Число зубьев шестерни""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса""},
""width"":{""type"":""number"",""description"":""Рабочая ширина венца, мм""},
""torque"":{""type"":""number"",""description"":""Момент на шестерне T1, Н·м""},
""rpm"":{""type"":""number"",""description"":""Частота вращения шестерни n1, об/мин""},
""hb1"":{""type"":""number"",""description"":""Твёрдость шестерни, НВ (деф 270)""},
""hb2"":{""type"":""number"",""description"":""Твёрдость колеса, НВ (деф 250)""},
""accuracy"":{""type"":""integer"",""description"":""Степень точности по ГОСТ 1643 (деф 8)""},
""ratio"":{""type"":""number"",""description"":""Требуемое передаточное число (для проверки отклонения)""}},
""required"":[""m"",""z1"",""z2"",""width"",""torque"",""rpm""]}",
        a => GearCheck(a));

      ToolRegistry.Add("worm_check",
        "Проверочный расчёт червячной передачи (архимедов червяк, бронзовый венец): скорость скольжения, КПД, самоторможение, контактная прочность и изгиб зубьев колеса, отклонение передаточного числа. Возвращает tech — строки для технических требований.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number""},""z1"":{""type"":""integer""},""q"":{""type"":""number""},""z2"":{""type"":""integer""},
""torque"":{""type"":""number"",""description"":""Момент на колесе T2, Н·м""},
""rpm"":{""type"":""number"",""description"":""Частота вращения червяка n1, об/мин""},
""sigmaB"":{""type"":""number"",""description"":""Предел прочности бронзы венца, МПа (деф 275 — БрО10Ф1, кокиль)""},
""sigmaT"":{""type"":""number"",""description"":""Предел текучести бронзы, МПа (деф 200)""},
""hardWorm"":{""type"":""boolean"",""description"":""Червяк закалён ≥ 45 HRC и шлифован (деф true)""},
""ratio"":{""type"":""number"",""description"":""Требуемое передаточное число""}},
""required"":[""m"",""z1"",""q"",""z2"",""torque"",""rpm""]}",
        a => WormCheck(a));

      ToolRegistry.Add("spring_check",
        "Проверочный расчёт пружины сжатия по ГОСТ 13765: жёсткость, прогибы и высоты при F1/F2, сила F3 при смыкании витков, касательные напряжения (коэффициент Валя), устойчивость (H0/D). Возвращает tech — строки для технических требований (их же рисует spring_drawing при заданных F1/F2).",
        @"{""type"":""object"",""properties"":{
""wireDiameter"":{""type"":""number"",""description"":""Диаметр проволоки d, мм""},
""coilDiameter"":{""type"":""number"",""description"":""Средний диаметр D, мм""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (деф 0.3·D)""},
""F1"":{""type"":""number"",""description"":""Сила предварительной деформации, Н""},
""F2"":{""type"":""number"",""description"":""Сила рабочей деформации, Н""},
""G"":{""type"":""number"",""description"":""Модуль сдвига, МПа (деф 78500)""},
""tauAllow"":{""type"":""number"",""description"":""Допускаемое напряжение при F3, МПа (деф 785 = 0.5·Rm стали 60С2А)""}},
""required"":[""wireDiameter"",""coilDiameter"",""n"",""F1"",""F2""]}",
        a => SpringCheck(a));
    }

    static object GearCheck(Dictionary<string, object> a)
    {
      Strength.SpurResult r = Strength.Spur(ToolRegistry.GetDbl(a, "m"), ToolRegistry.GetInt(a, "z1"),
        ToolRegistry.GetInt(a, "z2"), ToolRegistry.GetDbl(a, "width"), ToolRegistry.GetDbl(a, "torque"),
        ToolRegistry.GetDbl(a, "rpm"), ToolRegistry.GetDbl(a, "hb1", 270), ToolRegistry.GetDbl(a, "hb2", 250),
        ToolRegistry.GetInt(a, "accuracy", 8), ToolRegistry.GetDbl(a, "ratio", 0));
      List<string> tech = new List<string>
      {
        "Передача проверена по ГОСТ 21354-87 при T1 = " + F(ToolRegistry.GetDbl(a, "torque")) + " Н·м, n1 = " +
          F(ToolRegistry.GetDbl(a, "rpm")) + " об/мин: контактное напряжение " + F0(r.SigmaH) + " МПа (допускаемое " +
          F0(r.SigmaHP) + "), напряжение изгиба " + F0(r.SigmaF1) + "/" + F0(r.SigmaF2) + " МПа (допускаемые " +
          F0(r.SigmaFP1) + "/" + F0(r.SigmaFP2) + ").",
        "Твёрдость зубьев: шестерня " + F0(ToolRegistry.GetDbl(a, "hb1", 270)) + " НВ, колесо " +
          F0(ToolRegistry.GetDbl(a, "hb2", 250)) + " НВ."
      };
      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "ok", r.Ok }, { "u", Math.Round(r.U, 4) }, { "v_mps", Math.Round(r.V, 3) }, { "Ft_N", Math.Round(r.Ft, 1) },
        { "eps_alpha", Math.Round(r.EpsAlpha, 3) }, { "KH", Math.Round(r.KH, 3) }, { "KF", Math.Round(r.KF, 3) },
        { "sigmaH_MPa", Math.Round(r.SigmaH, 1) }, { "sigmaHP_MPa", Math.Round(r.SigmaHP, 1) },
        { "safetyH", Math.Round(r.SigmaHP / r.SigmaH, 3) },
        { "sigmaF1_MPa", Math.Round(r.SigmaF1, 1) }, { "sigmaF2_MPa", Math.Round(r.SigmaF2, 1) },
        { "sigmaFP1_MPa", Math.Round(r.SigmaFP1, 1) }, { "sigmaFP2_MPa", Math.Round(r.SigmaFP2, 1) },
        { "tech", tech }
      };
      if (!double.IsNaN(r.RatioDeviation))
      {
        res["ratio_deviation_pct"] = Math.Round(r.RatioDeviation, 2);
        res["ratio_allowed_pct"] = r.RatioAllowed;
      }
      res["verdict"] = r.Ok ? "условия прочности выполнены" : Verdict(
        r.SigmaH > 1.05 * r.SigmaHP ? "контактная прочность (увеличьте ширину, модуль или твёрдость)" : null,
        r.SigmaF1 > r.SigmaFP1 || r.SigmaF2 > r.SigmaFP2 ? "изгиб зубьев (увеличьте модуль)" : null,
        !double.IsNaN(r.RatioDeviation) && r.RatioDeviation > r.RatioAllowed ? "отклонение передаточного числа" : null);
      return res;
    }

    static object WormCheck(Dictionary<string, object> a)
    {
      WormPair g = WormGears.Worm(ToolRegistry.GetDbl(a, "m"), ToolRegistry.GetInt(a, "z1"),
        ToolRegistry.GetDbl(a, "q"), ToolRegistry.GetInt(a, "z2"));
      Strength.WormResult r = Strength.Worm(g, ToolRegistry.GetDbl(a, "torque"), ToolRegistry.GetDbl(a, "rpm"),
        ToolRegistry.GetDbl(a, "sigmaB", 275), ToolRegistry.GetDbl(a, "sigmaT", 200),
        ToolRegistry.GetBool(a, "hardWorm", true), ToolRegistry.GetDbl(a, "ratio", 0));
      List<string> tech = new List<string>
      {
        "Передача проверена при T2 = " + F(ToolRegistry.GetDbl(a, "torque")) + " Н·м, n1 = " +
          F(ToolRegistry.GetDbl(a, "rpm")) + " об/мин: контактное напряжение " + F0(r.SigmaH) + " МПа (допускаемое " +
          F0(r.SigmaHP) + "), напряжение изгиба " + F0(r.SigmaF) + " МПа (допускаемое " + F0(r.SigmaFP) + ").",
        "Скорость скольжения " + F(Math.Round(r.Vs, 2)) + " м/с, КПД передачи " + F(Math.Round(r.Efficiency, 2)) +
          (r.SelfLocking ? ", передача самотормозящая." : ".")
      };
      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "ok", r.Ok }, { "u", r.U }, { "gamma_deg", Math.Round(r.Gamma, 4) }, { "vs_mps", Math.Round(r.Vs, 3) },
        { "friction", Math.Round(r.Friction, 4) }, { "efficiency", Math.Round(r.Efficiency, 3) },
        { "self_locking", r.SelfLocking }, { "K", Math.Round(r.K, 3) }, { "Ft2_N", Math.Round(r.Ft2, 1) },
        { "sigmaH_MPa", Math.Round(r.SigmaH, 1) }, { "sigmaHP_MPa", Math.Round(r.SigmaHP, 1) },
        { "sigmaF_MPa", Math.Round(r.SigmaF, 1) }, { "sigmaFP_MPa", Math.Round(r.SigmaFP, 1) },
        { "tech", tech }
      };
      if (!double.IsNaN(r.RatioDeviation))
      {
        res["ratio_deviation_pct"] = Math.Round(r.RatioDeviation, 2);
        res["ratio_allowed_pct"] = r.RatioAllowed;
      }
      res["verdict"] = r.Ok ? "условия прочности выполнены" : Verdict(
        r.SigmaH > 1.05 * r.SigmaHP ? "контактная прочность (увеличьте модуль или межосевое расстояние)" : null,
        r.SigmaF > r.SigmaFP ? "изгиб зубьев колеса" : null,
        !double.IsNaN(r.RatioDeviation) && r.RatioDeviation > r.RatioAllowed ? "отклонение передаточного числа" : null);
      return res;
    }

    internal static Strength.SpringResult SpringLoads(Dictionary<string, object> a, out Gost13765.Spring s)
    {
      double? t = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      double? gMod = a.ContainsKey("G") ? (double?)ToolRegistry.GetDbl(a, "G") : null;
      s = Gost13765.Calc(ToolRegistry.GetDbl(a, "wireDiameter"), ToolRegistry.GetDbl(a, "coilDiameter"),
        ToolRegistry.GetInt(a, "n"), t, gMod);
      return Strength.Spring(s, ToolRegistry.GetDbl(a, "F1"), ToolRegistry.GetDbl(a, "F2"),
        ToolRegistry.GetDbl(a, "tauAllow", 785));
    }

    internal static List<string> SpringTech(Strength.SpringResult r)
    {
      List<string> tech = new List<string>
      {
        "Сила пружины при предварительной деформации F1 = " + F(Math.Round(r.F1, 1)) + " Н, при рабочей F2 = " +
          F(Math.Round(r.F2, 1)) + " Н, максимальная F3 = " + F(Math.Round(r.F3, 1)) + " Н.",
        "Касательное напряжение при F3 " + F0(r.Tau3) + " МПа (допускаемое " + F0(r.TauAllow) + ")."
      };
      if (r.NeedsGuide) tech.Add("Пружину устанавливать на оправку или в стакан (H0/D = " + F(Math.Round(r.Slenderness, 2)) + ").");
      return tech;
    }

    static object SpringCheck(Dictionary<string, object> a)
    {
      Gost13765.Spring s;
      Strength.SpringResult r = SpringLoads(a, out s);
      return new Dictionary<string, object>
      {
        { "ok", r.Ok }, { "stiffness_N_per_mm", Math.Round(s.Stiffness, 3) }, { "h0_mm", Math.Round(s.H0, 2) },
        { "s1_mm", Math.Round(r.S1, 2) }, { "s2_mm", Math.Round(r.S2, 2) }, { "s3_mm", Math.Round(r.S3, 2) },
        { "h1_mm", Math.Round(r.H1, 2) }, { "h2_mm", Math.Round(r.H2, 2) }, { "h3_mm", Math.Round(r.H3, 2) },
        { "F3_N", Math.Round(r.F3, 1) },
        { "tau1_MPa", Math.Round(r.Tau1, 1) }, { "tau2_MPa", Math.Round(r.Tau2, 1) }, { "tau3_MPa", Math.Round(r.Tau3, 1) },
        { "slenderness", Math.Round(r.Slenderness, 3) }, { "needs_guide", r.NeedsGuide },
        { "verdict", r.Ok ? "условия выполнены" : Verdict(
            r.F2 > 0.9 * r.F3 ? "рабочая сила близка к силе смыкания витков (нужно F2 ≤ 0.9·F3: увеличьте шаг)" : null,
            r.Tau3 > r.TauAllow ? "напряжение при F3 выше допускаемого (увеличьте d или уменьшите D)" : null) },
        { "tech", SpringTech(r) }
      };
    }

    static string Verdict(params string[] issues)
    {
      List<string> list = new List<string>();
      foreach (string s in issues) if (s != null) list.Add(s);
      return "не выполнено: " + string.Join("; ", list.ToArray());
    }

    static string F(double v) { return Tools2D.FmtNum(v); }

    static string F0(double v) { return Tools2D.FmtNum(Math.Round(v)); }
  }
}
