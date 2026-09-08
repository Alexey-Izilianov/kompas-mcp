using System;
using System.Collections.Generic;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Расчётные tools: не требуют КОМПАСа, чистые ГОСТ-модули.
  public static class ToolsCalc
  {
    public static void Register()
    {
      ToolRegistry.Add("material_library",
        "Список материалов справочника (имя, плотность).",
        "{}",
        a => new Dictionary<string, object> { { "materials", Materials.List() } });

      ToolRegistry.Add("material_lookup",
        "Найти материал по имени (префикс).",
        @"{""type"":""object"",""properties"":{""name"":{""type"":""string""}},""required"":[""name""]}",
        a =>
        {
          Materials.Mat m = Materials.Find(ToolRegistry.GetStr(a, "name", ""));
          if (m == null) throw new ToolException("Материал не найден: " + ToolRegistry.GetStr(a, "name"));
          var d = new Dictionary<string, object>();
          d["name"] = m.Name;
          d["density_kg_m3"] = m.Density;
          return d;
        });

      ToolRegistry.Add("fit_calc",
        "Посадка ЕСДП по ГОСТ 25346/25347: поля допусков отверстия и вала (напр. 'H7'/'p6'), диаметр до 500 мм. Возвращает отклонения (мкм) и зазоры/натяги.",
        @"{""type"":""object"",""properties"":{
""hole"":{""type"":""string"",""description"":""напр. H7""},
""shaft"":{""type"":""string"",""description"":""напр. p6""},
""diameter"":{""type"":""number"",""description"":""мм, до 500""}},
""required"":[""hole"",""shaft"",""diameter""]}",
        a => GostEsdp.Fit(
          ToolRegistry.GetStr(a, "hole", "H7"),
          ToolRegistry.GetStr(a, "shaft", "p6"),
          ToolRegistry.GetDbl(a, "diameter")));

      ToolRegistry.Add("tolerance_it",
        "Значение допуска IT (мкм) для квалитета и диаметра.",
        @"{""type"":""object"",""properties"":{
""grade"":{""type"":""integer"",""description"":""1-18""},
""diameter"":{""type"":""number""}},
""required"":[""grade"",""diameter""]}",
        a =>
        {
          int grade = ToolRegistry.GetInt(a, "grade");
          double d = ToolRegistry.GetDbl(a, "diameter");
          return new Dictionary<string, object>
          {
            { "grade", grade },
            { "diameter", d },
            { "IT_um", GostEsdp.ItValue(grade, d) }
          };
        });

      ToolRegistry.Add("section_properties",
        "Геометрические характеристики сечения: круг, кольцо, прямоугольник — площадь, моменты инерции/сопротивления.",
        @"{""type"":""object"",""properties"":{
""shape"":{""type"":""string"",""enum"":[""circle"",""ring"",""rect""]},
""r"":{""type"":""number"",""description"":""радиус (circle/ring)""},
""ri"":{""type"":""number"",""description"":""внутренний радиус (ring)""},
""b"":{""type"":""number"",""description"":""ширина (rect)""},
""h"":{""type"":""number"",""description"":""высота (rect)""}},
""required"":[""shape""]}",
        a => SectionProps(a));
    }

    static object SectionProps(Dictionary<string, object> a)
    {
      string shape = ToolRegistry.GetStr(a, "shape", "circle");
      var res = new Dictionary<string, object>();
      if (shape == "circle")
      {
        double r = ToolRegistry.GetDbl(a, "r");
        double A = Math.PI * r * r;
        double I = Math.PI * Math.Pow(r, 4) / 4.0;
        res["area_mm2"] = Round3(A);
        res["I_mm4"] = Round1(I);
        res["W_mm3"] = Round1(I / r);
        res["i_mm"] = Round3(Math.Sqrt(I / A));
      }
      else if (shape == "ring")
      {
        double r = ToolRegistry.GetDbl(a, "r");
        double ri = ToolRegistry.GetDbl(a, "ri");
        if (ri >= r) throw new ToolException("ri должен быть меньше r");
        double A = Math.PI * (r * r - ri * ri);
        double I = Math.PI * (Math.Pow(r, 4) - Math.Pow(ri, 4)) / 4.0;
        res["area_mm2"] = Round3(A);
        res["I_mm4"] = Round1(I);
        res["W_mm3"] = Round1(I / r);
        res["i_mm"] = Round3(Math.Sqrt(I / A));
      }
      else if (shape == "rect")
      {
        double b = ToolRegistry.GetDbl(a, "b");
        double h = ToolRegistry.GetDbl(a, "h");
        res["area_mm2"] = Round3(b * h);
        res["I_mm4"] = Round1(b * h * h * h / 12.0);
        res["W_mm3"] = Round1(b * h * h / 6.0);
      }
      else throw new ToolException("Неизвестное сечение: " + shape);
      return res;
    }

    static double Round3(double v) { return Math.Round(v, 3); }
    static double Round1(double v) { return Math.Round(v, 1); }
  }
}