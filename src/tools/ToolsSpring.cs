using System;
using System.Collections.Generic;
using Kompas6API5;
using Kompas6Constants3D;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Пружины (Фаза 6). spring_calc - расчёт ГОСТ 13765 без КОМПАСа;
  // spring_create - 3D: o3d_cylindricSpiral (траектория) + o3d_bossEvolution (проволока - круг по спирали).
  public static class ToolsSpring
  {
    public static void Register()
    {
      ToolRegistry.Add("spring_calc",
        "Расчёт винтовой цилиндрической пружины (ГОСТ 13765): индекс, жёсткость, коэффициент Валя, высота, длина проволоки.",
        @"{""type"":""object"",""properties"":{
""d"":{""type"":""number"",""description"":""Диаметр проволоки (мм)""},
""D"":{""type"":""number"",""description"":""Средний диаметр витка (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*D если не задан)""},
""G"":{""type"":""number"",""description"":""Модуль сдвига, МПа (78500 по умолчанию)""}}}",
        a => SpringCalc(a));

      ToolRegistry.Add("spring_create",
        "Создать 3D-пружину сжатия: спираль o3d_cylindricSpiral + выдавливание круга проволоки по траектории.",
        @"{""type"":""object"",""properties"":{
""d"":{""type"":""number"",""description"":""Диаметр проволоки (мм)""},
""D"":{""type"":""number"",""description"":""Средний диаметр витка (мм)""},
""n"":{""type"":""integer"",""description"":""Число рабочих витков""},
""t"":{""type"":""number"",""description"":""Шаг (0.3*D если не задан)""},
""name"":{""type"":""string""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d""}}}",
        a => SpringCreate(a));
    }

    static object SpringCalc(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "d");
      double D = ToolRegistry.GetDbl(a, "D");
      int n = ToolRegistry.GetInt(a, "n");
      double? t = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      double? g = a.ContainsKey("G") ? (double?)ToolRegistry.GetDbl(a, "G") : null;
      Gost13765.Spring s = Gost13765.Calc(d, D, n, t, g);
      return new Dictionary<string, object>
      {
        { "index_c", Math.Round(s.IndexC, 3) },
        { "wahl_kc", Math.Round(s.Wahl, 4) },
        { "stiffness_N_per_mm", Math.Round(s.Stiffness, 3) },
        { "h0_mm", Math.Round(s.H0, 2) },
        { "t_mm", s.T },
        { "total_coils", s.TotalCoils },
        { "wire_len_mm", Math.Round(s.WireLen, 1) }
      };
    }

    static object SpringCreate(Dictionary<string, object> a)
    {
      double d = ToolRegistry.GetDbl(a, "d");
      double D = ToolRegistry.GetDbl(a, "D");
      int n = ToolRegistry.GetInt(a, "n");
      double? t = a.ContainsKey("t") ? (double?)ToolRegistry.GetDbl(a, "t") : null;
      Gost13765.Spring s = Gost13765.Calc(d, D, n, t, null);

      string name = ToolRegistry.GetStr(a, "name", "Пружина d" + d + " D" + D);
      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      createArgs["name"] = name;
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) createArgs["path"] = path;
      Tools3D.CreatePart(createArgs);

      ksPart p = Tools3D.GetPart();

      // траектория: цилиндрическая спираль на XOY
      ksEntity spiral = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cylindricSpiral);
      ksCylindricSpiralDefinition spDef = (ksCylindricSpiralDefinition)spiral.GetDefinition();
      spDef.SetPlane(Tools3D.PlaneByName(p, "XOY"));
      spDef.SetLocation(0, 0);
      spDef.turn = s.TotalCoils;
      spDef.step = s.T;
      spDef.diam = D;
      if (!spiral.Create()) throw new ToolException("cylindricSpiral.Create вернул 0");

      // проволока: круг d/2 в XOZ в точке старта спирали (D/2, 0)
      Dictionary<string, object> sk = new Dictionary<string, object>();
      sk["plane"] = "XOZ";
      Dictionary<string, object> circ = new Dictionary<string, object>();
      circ["type"] = "circle";
      circ["xc"] = Math.Round(D / 2.0, 4);
      circ["yc"] = 0.0;
      circ["r"] = d / 2.0;
      List<object> els = new List<object>();
      els.Add(circ);
      sk["elements"] = els;
      Tools3D.Sketch(sk);
      ksEntity skEntity = Tools3D.LastSketch;

      // эволюция: круг по спирали
      ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossEvolution);
      ksBossEvolutionDefinition def = (ksBossEvolutionDefinition)op.GetDefinition();
      def.SetSketch(skEntity);
      ksEntityCollection pathArr = (ksEntityCollection)def.PathPartArray();
      pathArr.Add(spiral);
      if (!op.Create()) throw new ToolException("bossEvolution.Create вернул 0");
      p.RebuildModel();

      Dictionary<string, object> mat = new Dictionary<string, object>();
      mat["name"] = "Сталь пружинная 60С2А ГОСТ 14963-78";
      mat["density"] = 7850.0;
      Tools3D.SetMaterialOp(mat);

      Dictionary<string, object> saveArgs = new Dictionary<string, object>();
      if (path != null) saveArgs["path"] = path;
      Tools3D.SavePart(saveArgs);

      return new Dictionary<string, object>
      {
        { "saved", saveArgs.ContainsKey("path") ? saveArgs["path"] : Tools3D.PartPath },
        { "h0_mm", s.H0 }, { "t_mm", s.T }, { "total_coils", s.TotalCoils },
        { "stiffness_N_per_mm", Math.Round(s.Stiffness, 3) },
        { "mass_kg", Tools3D.CurrentMass() }
      };
    }
  }
}