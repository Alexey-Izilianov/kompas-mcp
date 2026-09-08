using System;
using System.Collections.Generic;

namespace KompasMcp.Gost
{
  // Справочник материалов для SetMaterial/массы. Плотности — справочные, кг/м³.
  // Пополнять по мере надобности; значения сверены с машиностроительными справочниками.
  public static class Materials
  {
    public class Mat { public string Name; public double Density; public string Note; }

    static readonly List<Mat> mats = new List<Mat>();

    static Materials()
    {
      Add("Сталь 45 ГОСТ 1050-88", 7850, "конструкционная углеродистая качественная");
      Add("Сталь 40Х ГОСТ 4543-2016", 7850, "легированная хромом");
      Add("Сталь 40ХН ГОСТ 4543-2016", 7850, "хромоникелевая");
      Add("Сталь 12ХН3А ГОСТ 4543-2016", 7850, "цементуемая");
      Add("Сталь 65Г ГОСТ 14959-2016", 7850, "пружинная");
      Add("Сталь 60С2А ГОСТ 14959-2016", 7850, "пружинная");
      Add("Сталь 30ХГСА ГОСТ 4543-2016", 7850, "хромомарганецкремниевая");
      Add("Сталь У8А ГОСТ 1435-2003", 7830, "инструментальная");
      Add("Сталь 20 ГОСТ 1050-2013", 7850, "цементуемая");
      Add("Ст3сп ГОСТ 380-2005", 7850, "углеродистая общего назначения");
      Add("СЧ20 ГОСТ 1412-85", 7200, "серый чугун");
      Add("СЧ25 ГОСТ 1412-85", 7250, "серый чугун");
      Add("ВЧ60 ГОСТ 7293-85", 7300, "высокопрочный чугун");
      Add("БрОЦС5-5-5 ГОСТ 613-79", 8800, "бронза литейная");
      Add("БрАЖ9-4 ГОСТ 18175-78", 7600, "безоловянная бронза");
      Add("Л63 ГОСТ 15527-2004", 8500, "латунь");
      Add("АК6 ГОСТ 4784-2019", 2700, "алюминиевый сплав ковочный");
      Add("Д16 ГОСТ 4784-2019", 2780, "дюраль");
      Add("АМг6 ГОСТ 4784-2019", 2640, "магналий");
      Add("ВТ4 ГОСТ 19807-91", 4510, "титановый сплав");
      Add("ВТ22 ГОСТ 19807-91", 4600, "титановый сплав");
      Add("Медь М1 ГОСТ 859-2014", 8940, "");
      Add("ПВХ", 1400, "полимер");
      Add("Капролон", 1160, "полиамид 6 блочный");
      Add("Фторопласт-4", 2200, "PTFE");
    }

    static void Add(string name, double density, string note)
    {
      mats.Add(new Mat { Name = name, Density = density, Note = note });
    }

    public static List<object> List()
    {
      var list = new List<object>();
      foreach (Mat m in mats)
      {
        var d = new Dictionary<string, object>();
        d["name"] = m.Name;
        d["density_kg_m3"] = m.Density;
        if (!string.IsNullOrEmpty(m.Note)) d["note"] = m.Note;
        list.Add(d);
      }
      return list;
    }

    public static Mat Find(string name)
    {
      string q = name.Trim().ToLowerInvariant();
      foreach (Mat m in mats)
        if (m.Name.ToLowerInvariant().StartsWith(q) || q.StartsWith(m.Name.Split(' ')[0].ToLowerInvariant()) && m.Name.ToLowerInvariant().Contains(q))
          return m;
      return null;
    }
  }
}