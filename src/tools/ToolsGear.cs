using System;
using System.Collections.Generic;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Зубчатые передачи (Фаза 5). gear_calc - чистый расчёт ГОСТ 16532 без КОМПАСа;
  // gear_wheel - 3D-колесо: эвольвентный профиль (аппроксимация отрезками) -> extrude -> отверстие.
  public static class ToolsGear
  {
    public static void Register()
    {
      ToolRegistry.Add("gear_calc",
        "Расчёт цилиндрической эвольвентной передачи без смещения (ГОСТ 16532): диаметры, межосевое, проверка подрезания.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Модуль""},
""z1"":{""type"":""integer"",""description"":""Число зубьев шестерни""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса""}}}",
        a => GearCalc(a));

      ToolRegistry.Add("gear_wheel",
        "Создать 3D-зубчатое колесо: эвольвентный профиль (ГОСТ 16532, x=0) в эскизе, выдавливание, центральное отверстие.",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Модуль""},
""z"":{""type"":""integer"",""description"":""Число зубьев""},
""width"":{""type"":""number"",""description"":""Ширина венца (мм)""},
""bore"":{""type"":""number"",""description"":""Диаметр центрального отверстия (0 = без отверстия)""},
""samples"":{""type"":""integer"",""description"":""Точек на сторону профиля (по умолчанию 6)""},
""name"":{""type"":""string""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d""}}}",
        a => GearWheel(a));
    }

    static object GearCalc(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z1 = ToolRegistry.GetInt(a, "z1");
      int z2 = ToolRegistry.GetInt(a, "z2");
      if (m <= 0 || z1 < 6 || z2 < 6) throw new ToolException("Нужны m > 0 и целые z >= 6");
      Gost16532.Mesh mesh = Gost16532.MeshGeom(m, z1, z2);
      return new Dictionary<string, object>
      {
        { "a", mesh.A },
        { "ratio", mesh.Ratio },
        { "w1", WheelDict(mesh.W1) },
        { "w2", WheelDict(mesh.W2) }
      };
    }

    static Dictionary<string, object> WheelDict(Gost16532.Wheel w)
    {
      return new Dictionary<string, object>
      {
        { "m", w.M }, { "z", w.Z },
        { "d", w.D }, { "da", w.Da }, { "df", w.Df }, { "db", w.Db },
        { "undercut", w.Undercut }
      };
    }

    static object GearWheel(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z = ToolRegistry.GetInt(a, "z");
      double width = ToolRegistry.GetDbl(a, "width");
      double bore = ToolRegistry.GetDbl(a, "bore", 0);
      int samples = ToolRegistry.GetInt(a, "samples", 6);
      if (m <= 0 || z < 6) throw new ToolException("Нужны m > 0 и целые z >= 6");
      if (width <= 0 || width > 500) throw new ToolException("Ширина венца должна быть 0..500");
      Gost16532.Wheel w = Gost16532.WheelGeom(m, z);
      if (w.Undercut) throw new ToolException("z=" + z + " < 17 без смещения - подрезание профиля; увеличьте z");

      string name = ToolRegistry.GetStr(a, "name", "Колесо m" + m + " z" + z);
      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      createArgs["name"] = name;
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      Tools3D.CreatePart(createArgs);

      // профиль: замкнутая полилиния точек
      List<double[]> pts = Gost16532.ProfilePoints(m, z, samples);
      Dictionary<string, object> sk = new Dictionary<string, object>();
      sk["plane"] = "XOY";
      List<object> segs = new List<object>();
      for (int i = 0; i < pts.Count; i++)
      {
        double[] p1 = pts[i];
        double[] p2 = pts[(i + 1) % pts.Count];
        Dictionary<string, object> seg = new Dictionary<string, object>();
        seg["type"] = "line";
        seg["x1"] = Math.Round(p1[0], 4);
        seg["y1"] = Math.Round(p1[1], 4);
        seg["x2"] = Math.Round(p2[0], 4);
        seg["y2"] = Math.Round(p2[1], 4);
        segs.Add(seg);
      }
      sk["elements"] = segs;
      Tools3D.Sketch(sk);

      Dictionary<string, object> ex = new Dictionary<string, object>();
      ex["depth"] = width;
      ex["base"] = true;
      ex["direction"] = 1;
      Tools3D.Extrude(ex, cut: false);

      if (bore > 0)
      {
        if (bore >= w.Df) throw new ToolException("Отверстие bore=" + bore + " больше df=" + w.Df);
        Dictionary<string, object> boreSk = new Dictionary<string, object>();
        boreSk["plane"] = "XOY";
        Dictionary<string, object> circ = new Dictionary<string, object>();
        circ["type"] = "circle";
        circ["xc"] = 0.0; circ["yc"] = 0.0; circ["r"] = bore / 2.0;
        List<object> els = new List<object>();
        els.Add(circ);
        boreSk["elements"] = els;
        Tools3D.Sketch(boreSk);
        Dictionary<string, object> cut = new Dictionary<string, object>();
        cut["mode"] = "through";
        Tools3D.Extrude(cut, cut: true);
      }

      Dictionary<string, object> mat = new Dictionary<string, object>();
      mat["name"] = "Сталь 45 ГОСТ 1050-88";
      mat["density"] = 7850.0;
      Tools3D.SetMaterialOp(mat);

      Dictionary<string, object> saveArgs = new Dictionary<string, object>();
      if (path != null) saveArgs["path"] = path;
      Tools3D.SavePart(saveArgs);

      return new Dictionary<string, object>
      {
        { "saved", saveArgs.ContainsKey("path") ? saveArgs["path"] : Tools3D.PartPath },
        { "da", w.Da }, { "df", w.Df }, { "d", w.D },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }
  }
}