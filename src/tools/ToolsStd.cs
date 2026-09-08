using System;
using System.Collections.Generic;
using KompasMcp.Gost;

namespace KompasMcp.Tools
{
  // Стандартные изделия: параметрические ГОСТ-генераторы (3D). Идиомы сборки — те же,
  // что в Tools3D (sketch → extrude/revolve); головка болта/гайка — шестигранник по E.
  public static class ToolsStd
  {
    public static void Register()
    {
      ToolRegistry.Add("std_part",
        "Создать стандартное изделие в 3D по таблицам ГОСТ. kind: bolt (d, length), nut (d), washer (d), bearing (code). Модели упрощённые: без резьбы и фасок; подшипник - кольца (+ дорожка-тор при balls=true).",
        @"{""type"":""object"",""properties"":{
""kind"":{""type"":""string"",""enum"":[""bolt"",""nut"",""washer"",""bearing""]},
""d"":{""type"":""number"",""description"":""Номинал резьбы/крепежа М (для bolt/nut/washer)""},
""length"":{""type"":""number"",""description"":""Длина болта L (для bolt)""},
""code"":{""type"":""integer"",""description"":""Код подшипника (для bearing), напр. 208""},
""name"":{""type"":""string"",""description"":""Имя детали (по умолчанию из ГОСТ)""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d""},
""balls"":{""type"":""boolean"",""description"":""Подшипник: добавить дорожку-тор (масса завышается)""}}}",
        a => StdPart(a));
    }

    static object StdPart(Dictionary<string, object> a)
    {
      string kind = ToolRegistry.GetStr(a, "kind");
      switch (kind)
      {
        case "bolt":
        {
          double d = ToolRegistry.GetDbl(a, "d");
          double L = ToolRegistry.GetDbl(a, "length");
          Fasteners.HexBolt b = Fasteners.Bolt(d);
          if (b == null) throw new ToolException("Нет номинала М" + d + " в таблице болтов (6..24)");
          if (L <= 0 || L > 300) throw new ToolException("Длина болта должна быть 0..300");
          if (L < b.K + 2) throw new ToolException("Длина болта должна быть больше высоты головки");

          CreatePart(ToolRegistry.GetStr(a, "name", "Болт М" + d + "x" + L), a);

          // головка: шестигранник z 0..K
          double[] xs, ys;
          Fasteners.HexVertices(b.E / 2.0, out xs, out ys);
          Dictionary<string, object> hex = new Dictionary<string, object>();
          hex["plane"] = "XOY";
          List<object> segs = new List<object>();
          for (int i = 0; i < 6; i++)
          {
            Dictionary<string, object> seg = new Dictionary<string, object>();
            seg["type"] = "line";
            seg["x1"] = Math.Round(xs[i], 4);
            seg["y1"] = Math.Round(ys[i], 4);
            seg["x2"] = Math.Round(xs[(i + 1) % 6], 4);
            seg["y2"] = Math.Round(ys[(i + 1) % 6], 4);
            segs.Add(seg);
          }
          hex["elements"] = segs;
          Tools3D.Sketch(hex);
          Tools3D.Extrude(Depth(b.K, baseOp: true), cut: false);

          // стержень: эскиз на плоскости z=-L, выдавливание вверх
          Dictionary<string, object> shaft = CircleDict(d / 2.0);
          shaft["offset"] = -L;
          Tools3D.Sketch(shaft);
          Tools3D.Extrude(Depth(L, baseOp: false), cut: false);

          SetSteel();
          return Save(ToolRegistry.GetStr(a, "path", null), b.D, b.S, b.K, L);
        }
        case "nut":
        {
          double d = ToolRegistry.GetDbl(a, "d");
          Fasteners.HexNut n = Fasteners.Nut(d);
          if (n == null) throw new ToolException("Нет номинала М" + d + " в таблице гаек (6..24)");

          CreatePart(ToolRegistry.GetStr(a, "name", "Гайка М" + d), a);
          double[] xs, ys;
          Fasteners.HexVertices(n.E / 2.0, out xs, out ys);
          Dictionary<string, object> hex = new Dictionary<string, object>();
          hex["plane"] = "XOY";
          List<object> segs = new List<object>();
          for (int i = 0; i < 6; i++)
          {
            Dictionary<string, object> seg = new Dictionary<string, object>();
            seg["type"] = "line";
            seg["x1"] = Math.Round(xs[i], 4);
            seg["y1"] = Math.Round(ys[i], 4);
            seg["x2"] = Math.Round(xs[(i + 1) % 6], 4);
            seg["y2"] = Math.Round(ys[(i + 1) % 6], 4);
            segs.Add(seg);
          }
          hex["elements"] = segs;
          Tools3D.Sketch(hex);
          Tools3D.Extrude(Depth(n.M, baseOp: true), cut: false);
          // резьбовое отверстие: упрощение - номинальный диаметр
          Tools3D.Sketch(CircleDict(n.D / 2.0));
          Tools3D.Extrude(ThroughDict(), cut: true);
          SetSteel();
          return Save(ToolRegistry.GetStr(a, "path", null), n.D, n.S, n.M, 0);
        }
        case "washer":
        {
          double d = ToolRegistry.GetDbl(a, "d");
          Fasteners.Washer w = Fasteners.WasherFor(d);
          if (w == null) throw new ToolException("Нет номинала М" + d + " в таблице шайб (6..24)");

          CreatePart(ToolRegistry.GetStr(a, "name", "Шайба " + d), a);
          Dictionary<string, object> ring = new Dictionary<string, object>();
          ring["plane"] = "XOY";
          ring["elements"] = new List<object> { Circle(w.D2 / 2.0), Circle(w.D1 / 2.0) };
          Tools3D.Sketch(ring);
          Tools3D.Extrude(Depth(w.S, baseOp: true), cut: false);
          SetSteel();
          return Save(ToolRegistry.GetStr(a, "path", null), w.D1, w.D2, w.S, 0);
        }
        case "bearing":
        {
          int code = ToolRegistry.GetInt(a, "code");
          Fasteners.BallBearing br = Fasteners.Bearing(code);
          if (br == null) throw new ToolException("Нет кода " + code + " в таблице подшипников (201-210, 305-307)");
          bool balls = ToolRegistry.GetBool(a, "balls", false);

          CreatePart(ToolRegistry.GetStr(a, "name", "Подшипник " + code), a);
          double rOut = br.DOuter / 2.0;
          double rOutIn = (br.DOuter - br.BallDia) / 2.0;
          double rIn = br.D / 2.0;
          double rInOut = (br.D + br.BallDia) / 2.0;

          Dictionary<string, object> outer = new Dictionary<string, object>();
          outer["plane"] = "XOY";
          outer["elements"] = new List<object> { Circle(rOut), Circle(rOutIn) };
          Tools3D.Sketch(outer);
          Tools3D.Extrude(Depth(br.B, baseOp: true), cut: false);

          Dictionary<string, object> inner = new Dictionary<string, object>();
          inner["plane"] = "XOY";
          inner["elements"] = new List<object> { Circle(rInOut), Circle(rIn) };
          Tools3D.Sketch(inner);
          Tools3D.Extrude(Depth(br.B, baseOp: false), cut: false);

          if (balls)
          {
            // дорожка-тор: круг в XOZ на радиусе середины зазора, вращение вокруг оси Z
            double rm = (rOutIn + rInOut) / 2.0;
            Dictionary<string, object> tor = new Dictionary<string, object>();
            tor["plane"] = "XOZ";
            List<object> els = new List<object>();
            Dictionary<string, object> circ = new Dictionary<string, object>();
            circ["type"] = "circle";
            circ["xc"] = Math.Round(rm, 4);
            circ["yc"] = Math.Round(br.B / 2.0, 4);
            circ["r"] = br.BallDia / 2.0;
            els.Add(circ);
            Dictionary<string, object> axis = new Dictionary<string, object>();
            axis["type"] = "line";
            axis["x1"] = 0; axis["y1"] = 0; axis["x2"] = 0; axis["y2"] = br.B;
            axis["style"] = 3;
            els.Add(axis);
            tor["elements"] = els;
            Tools3D.Sketch(tor);
            Tools3D.Revolve(AngleDict(360), cut: false);
          }
          SetSteel();
          return Save(ToolRegistry.GetStr(a, "path", null), br.D, br.DOuter, br.B, 0);
        }
        default:
          throw new ToolException("Неизвестный kind: " + kind);
      }
    }

    // ---- хелперы построения (через internal-методы Tools3D) ----

    static Dictionary<string, object> Circle(double r)
    {
      Dictionary<string, object> c = new Dictionary<string, object>();
      c["type"] = "circle";
      c["xc"] = 0.0;
      c["yc"] = 0.0;
      c["r"] = r;
      return c;
    }

    static Dictionary<string, object> CircleDict(double r)
    {
      Dictionary<string, object> d = new Dictionary<string, object>();
      d["plane"] = "XOY";
      d["elements"] = new List<object> { Circle(r) };
      return d;
    }

    static Dictionary<string, object> Depth(double depth, bool baseOp, int dir = 1)
    {
      Dictionary<string, object> d = new Dictionary<string, object>();
      d["depth"] = depth;
      d["base"] = baseOp;
      d["direction"] = dir;
      return d;
    }

    static Dictionary<string, object> ThroughDict()
    {
      Dictionary<string, object> d = new Dictionary<string, object>();
      d["mode"] = "through";
      return d;
    }

    static Dictionary<string, object> AngleDict(double angle)
    {
      Dictionary<string, object> d = new Dictionary<string, object>();
      d["angle"] = angle;
      return d;
    }

    static void CreatePart(string name, Dictionary<string, object> a)
    {
      Dictionary<string, object> args = new Dictionary<string, object>();
      args["name"] = name;
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null) args["path"] = path;
      Tools3D.CreatePart(args);
    }

    static void SetSteel()
    {
      Dictionary<string, object> m = new Dictionary<string, object>();
      m["name"] = "Сталь 45 ГОСТ 1050-88";
      m["density"] = 7850.0;
      Tools3D.SetMaterialOp(m);
    }

    static object Save(string path, double d1, double d2, double height, double length)
    {
      Dictionary<string, object> s = new Dictionary<string, object>();
      if (path != null) s["path"] = path;
      Tools3D.SavePart(s);
      Dictionary<string, object> res = new Dictionary<string, object>();
      res["saved"] = s.ContainsKey("path") ? s["path"] : Tools3D.PartPath;
      res["mass_kg"] = Tools3D.CurrentMass();
      return res;
    }
  }
}