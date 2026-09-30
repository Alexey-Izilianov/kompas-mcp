using System;
using System.Collections.Generic;
using Kompas6API5;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Зубчатые передачи (Фаза 5). gear_calc - чистый расчёт ГОСТ 16532 без КОМПАСа;
  // gear_wheel - 3D-колесо: эвольвентный профиль (аппроксимация отрезками) -> extrude -> отверстие;
  // gear_pair - 3D-сборка пары: межосевое + фаза зацепления через placement компонентов.
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

      ToolRegistry.Add("gear_pair",
        "3D-сборка пары зубчатых колёс (ГОСТ 16532, x=0): создаёт шестерню и колесо (.m3d), сборку (.a3d), расставляет колёса на межосевом расстоянии a с фазой зацепления (зуб шестерни - напротив впадины колеса).",
        @"{""type"":""object"",""properties"":{
""m"":{""type"":""number"",""description"":""Модуль""},
""z1"":{""type"":""integer"",""description"":""Число зубьев шестерни""},
""z2"":{""type"":""integer"",""description"":""Число зубьев колеса""},
""width1"":{""type"":""number"",""description"":""Ширина венца шестерни (по умолчанию 10*m)""},
""width2"":{""type"":""number"",""description"":""Ширина венца колеса (по умолчанию width1)""},
""bore1"":{""type"":""number"",""description"":""Диаметр отверстия шестерни (0 = без отверстия)""},
""bore2"":{""type"":""number"",""description"":""Диаметр отверстия колеса (0 = без отверстия)""},
""samples"":{""type"":""integer"",""description"":""Точек на сторону профиля (по умолчанию 6)""},
""name"":{""type"":""string"",""description"":""Имя сборки""},
""path"":{""type"":""string"",""description"":""Путь сохранения .a3d (компоненты пишутся рядом: шестерня*.m3d / колесо*.m3d)""}}}",
        a => GearPair(a));
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

    // Сборка пары колёс. Оси обоих колёс вдоль глобального Z (профили в XOY,
    // выдавливание вдоль Z). Шестерня в начале координат, колесо смещено на
    // межосевое a по X. Фаза зацепления: зуб шестерни центрирован на +X (угол 0
    // и т.п. в ProfilePoints), значит у колеса в направлении шестерни (-X, угол
    // 180) должна быть впадина: центры впадин - (k+0.5)*step, поворот колеса
    // phi = 180 - (k+0.5)*step, k - ближайшая к 180 впадина (phi в [0, шаг)).
    static object GearPair(Dictionary<string, object> a)
    {
      double m = ToolRegistry.GetDbl(a, "m");
      int z1 = ToolRegistry.GetInt(a, "z1");
      int z2 = ToolRegistry.GetInt(a, "z2");
      int samples = ToolRegistry.GetInt(a, "samples", 6);
      if (m <= 0 || z1 < 6 || z2 < 6) throw new ToolException("Нужны m > 0 и целые z >= 6");
      Gost16532.Mesh mesh = Gost16532.MeshGeom(m, z1, z2);
      if (mesh.W1.Undercut || mesh.W2.Undercut)
        throw new ToolException("z < 17 без смещения - подрезание профиля; увеличьте z");

      double defWidth = 10.0 * m;
      double width1 = ToolRegistry.GetDbl(a, "width1", defWidth);
      double width2 = ToolRegistry.GetDbl(a, "width2", width1);
      if (width1 <= 0 || width1 > 500 || width2 <= 0 || width2 > 500)
        throw new ToolException("Ширина венца должна быть 0..500");

      string asmPath = ToolRegistry.GetStr(a, "path", null);
      if (asmPath != null && !System.IO.Path.IsPathRooted(asmPath))
        asmPath = System.IO.Path.GetFullPath(asmPath);
      if (asmPath == null) asmPath = KompasMcp.Paths.Out("gear_pair.a3d");
      string dir = System.IO.Path.GetDirectoryName(asmPath);
      string nameOfGears = "m" + Tools2D.FmtNum(m);
      string path1 = System.IO.Path.Combine(dir, "pinion" + nameOfGears + "_z" + z1 + ".m3d");
      string path2 = System.IO.Path.Combine(dir, "wheel" + nameOfGears + "_z" + z2 + ".m3d");

      // шестерня: создать, сохранить, закрыть (CreatePart замещает активный документ)
      Dictionary<string, object> a1 = new Dictionary<string, object>();
      a1["m"] = m; a1["z"] = z1; a1["width"] = width1;
      a1["bore"] = ToolRegistry.GetDbl(a, "bore1", 0);
      a1["samples"] = samples;
      a1["name"] = "Шестерня z" + z1;
      a1["path"] = path1;
      GearWheel(a1);
      Tools3D.CloseCurrentDoc();

      Dictionary<string, object> a2 = new Dictionary<string, object>();
      a2["m"] = m; a2["z"] = z2; a2["width"] = width2;
      a2["bore"] = ToolRegistry.GetDbl(a, "bore2", 0);
      a2["samples"] = samples;
      a2["name"] = "Колесо z" + z2;
      a2["path"] = path2;
      GearWheel(a2);
      Tools3D.CloseCurrentDoc();

      // сборка
      Dictionary<string, object> asmArgs = new Dictionary<string, object>();
      string asmName = ToolRegistry.GetStr(a, "name",
        "Передача m" + Tools2D.FmtNum(m) + " z" + z1 + "x" + z2);
      asmArgs["name"] = asmName;
      asmArgs["path"] = asmPath;
      Tools3D.CreateAssembly(asmArgs);

      ksPart c1 = Tools3D.AddComponentImpl(path1, false);
      ksPart c2 = Tools3D.AddComponentImpl(path2, false);
      try { Tools3D.GetPart().RebuildModel(); } catch { }

      // фаза зацепления: поворот колеса так, чтобы впадина встала против зуба шестерни
      double step2 = 2.0 * Math.PI / z2;
      double k = Math.Floor(Math.PI / step2 - 0.5);
      double phi2Rad = Math.PI - (k + 0.5) * step2; // в [0, step2)
      double phi2Deg = phi2Rad * 180.0 / Math.PI;

      Tools3D.PlacePart(c1, 0, 0, 0, 0);
      Tools3D.PlacePart(c2, mesh.A, 0, 0, phi2Deg);
      try { Tools3D.GetPart().RebuildModel(); }
      catch (Exception e) { Log.Error("gear_pair rebuild", e); }

      Dictionary<string, object> saveArgs = new Dictionary<string, object>();
      Tools3D.SavePart(saveArgs);

      return new Dictionary<string, object>
      {
        { "assembly", asmName },
        { "saved", Tools3D.PartPath },
        { "pinion", path1 },
        { "wheel", path2 },
        { "a", mesh.A },
        { "ratio", Math.Round(mesh.Ratio, 4) },
        { "mesh_phase_deg", Math.Round(phi2Deg, 3) },
        { "da1", mesh.W1.Da }, { "da2", mesh.W2.Da }
      };
    }
  }
}