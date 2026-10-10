using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using KAPITypes;

namespace KompasMcp.Tools
{
  // Сборочный чертёж по 3D-сборке (ГОСТ 2.109): ассоциативные виды в проекционной связи
  // (главный — «спереди», вид сверху — под ним, вид слева — справа), упрощение — без невидимых
  // линий, габаритные размеры, позиции линиями-выносками. Одинаковые компоненты обозначаются
  // одной позицией (деталь-представитель), количество — в спецификации, которая строится тем же
  // списком через spec_drawing: сначала детали, затем стандартные изделия (порядок ГОСТ 2.106).
  public static class ToolsAsmDrawing
  {
    static readonly string[] StandardNames =
      { "Болт", "Винт", "Гайка", "Шайба", "Шпилька", "Штифт", "Шплинт", "Подшипник", "Кольцо" };

    // поле видов на А3: от левой рамки до правой, ниже графы 26; левее штампа (x < 225) виды
    // доходят до нижней рамки, правее — до технических требований над штампом
    const double FieldX0 = 25, FieldX1 = 410, FieldY1 = 274, StampX = 225, BottomLeft = 12, BottomRight = 90;
    const double LeaderMargin = 32, Gap = 30, DimMargin = 16;

    class Position
    {
      public int Number, Qty;
      public string Name, Designation;
      public bool Standard;
      public List<Tools3D.ComponentInfo> Instances = new List<Tools3D.ComponentInfo>();
    }

    public static void Register()
    {
      ToolRegistry.Add("assembly_drawing",
        "Сборочный чертёж по файлу сборки .a3d (ГОСТ 2.109): ассоциативные виды в проекционной связи (front — главный, top — под ним, left — справа), без невидимых линий, габаритные размеры, позиции линиями-выносками (одинаковые компоненты — одна позиция), технические требования, штамп; спецификация ГОСТ 2.106 тем же списком позиций (spec=false — без неё). Масштаб подбирается по листу А3.",
        @"{""type"":""object"",""properties"":{
""assembly"":{""type"":""string"",""description"":""Путь к сборке .a3d""},
""views"":{""type"":""array"",""items"":{""type"":""string"",""enum"":[""front"",""top"",""left""]},""description"":""Виды, первый всегда front (по умолчанию [front, top])""},
""scale"":{""type"":""number"",""description"":""Масштаб (по умолчанию — наибольший стандартный, при котором виды помещаются)""},
""name"":{""type"":""string"",""description"":""Наименование изделия (по умолчанию — имя сборки)""},
""designation"":{""type"":""string"",""description"":""Обозначение изделия; к чертежу добавляется «СБ»""},
""tech"":{""type"":""array"",""items"":{""type"":""string""},""description"":""Технические требования""},
""spec"":{""type"":""boolean"",""description"":""Сформировать спецификацию (по умолчанию true)""},
""path"":{""type"":""string"",""description"":""Путь .cdw (по умолчанию <сборка>_sb.cdw)""},
""png"":{""type"":""string"",""description"":""PNG-рендер чертежа (спецификация — рядом с суффиксом _spec)""}},
""required"":[""assembly""]}",
        a => AssemblyDrawing(a));
    }

    static object AssemblyDrawing(Dictionary<string, object> a)
    {
      string src = Paths.Abs(ToolRegistry.GetStr(a, "assembly"));
      if (!File.Exists(src)) throw new ToolException("Нет файла сборки: " + src);
      List<string> views = ViewList(a);

      Tools3D.OpenPart(src);
      double[] gmin, gmax;
      List<Tools3D.ComponentInfo> comps = Tools3D.ComponentInfos(out gmin, out gmax);
      string asmName = ToolRegistry.GetStr(a, "name", null) ?? Tools3D.GetPart().name;
      Tools3D.CloseCurrentDoc();
      if (comps.Count == 0) throw new ToolException("В сборке нет компонентов");
      List<Position> positions = Group(comps);

      string path = ToolRegistry.GetStr(a, "path", null) ??
        Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_sb.cdw");
      Tools2D.CreateDrawing(new Dictionary<string, object> { { "path", path }, { "name", asmName } });
      IKompasDocument2D doc7 = (IKompasDocument2D)KompasHost.App7.ActiveDocument;
      IViews coll = doc7.ViewsAndLayersManager.Views;

      // виды в масштабе 1 — по ним узнаём габариты проекций, затем раскладываем
      Dictionary<string, IAssociationView> av = new Dictionary<string, IAssociationView>();
      Dictionary<string, double[]> box = new Dictionary<string, double[]>();
      foreach (string v in views)
      {
        IAssociationView view = (IAssociationView)coll.Add(LtViewType.vt_Arbitrary);
        view.SourceFileName = src;
        view.ProjectionName = Projection(v);
        view.HiddenLinesVisible = false;
        IView iv = (IView)view;
        iv.X = 0; iv.Y = 0; iv.Scale = 1; iv.Name = ViewName(v);
        if (!((IDrawingObject)view).Update()) throw new ToolException("Не удалось построить вид " + v);
        try
        {
          IViewDesignation des = (IViewDesignation)view;
          des.ShowName = false;
          des.ShowScale = false;
        }
        catch (Exception e) { Log.Write("обозначение вида: " + e.Message); }
        av[v] = view;
        box[v] = ProjectedBox(view, gmin, gmax);
      }

      double[] f = box["front"], t = box.ContainsKey("top") ? box["top"] : null, l = box.ContainsKey("left") ? box["left"] : null;
      double wF = f[1] - f[0], hF = f[3] - f[2];
      double wL = l != null ? l[1] - l[0] : 0, hT = t != null ? t[3] - t[2] : 0;
      double fixedW = LeaderMargin + DimMargin + (l != null ? Gap : 0);
      double fixedH = DimMargin + 4 + (t != null ? Gap : 0);
      double scale = ToolRegistry.GetDbl(a, "scale", 0);
      if (scale <= 0)
      {
        // стопка «главный + сверху» стоит слева и может спускаться до нижней рамки, но должна
        // пройти левее штампа; вид слева справа от главного — выше технических требований
        double fit = Math.Min((FieldX1 - FieldX0 - fixedW) / (wF + wL), (FieldY1 - BottomLeft - fixedH) / (hF + hT));
        if (t != null) fit = Math.Min(fit, (StampX - FieldX0 - LeaderMargin) / wF);
        fit = Math.Min(fit, (FieldY1 - BottomRight - DimMargin - 4) / hF);
        scale = DrawKit.StdScale(fit);
      }

      // проекционная связь: у главного и вида сверху общая ось x, у главного и вида слева — ось y
      double xF = FieldX0 + LeaderMargin - scale * f[0];
      double yF = FieldY1 - 4 - scale * f[3];
      Place(av["front"], xF, yF, scale);
      if (t != null) Place(av["top"], xF, yF + scale * f[2] - DimMargin - Gap - scale * t[3], scale);
      if (l != null) Place(av["left"], xF + scale * f[1] + DimMargin + Gap - scale * l[0], yF, scale);

      // на главном виде: габаритные размеры и позиции
      IView main = (IView)av["front"];
      main.Current = true;
      ksDocument2D d = Tools2D.GetDoc();
      d.ksOpenView(main.Number);
      DrawKit.Dim(f[0], f[2], f[1], f[2], false, -10);
      DrawKit.Dim(f[1], f[2], f[1], f[3], true, 10);
      Leaders(d, av["front"], positions, comps, f, scale);

      Dictionary<string, object> res = new Dictionary<string, object>();
      string designation = ToolRegistry.GetStr(a, "designation", null);
      Dictionary<string, object> finishArgs = new Dictionary<string, object>(a);
      finishArgs["designation"] = designation != null ? designation + " СБ" : null;
      finishArgs.Remove("material");
      List<string> tech = DrawKit.TechItems(a, "Размеры для справок.");
      double techTop = 60 + 10 + tech.Count * 12;
      DrawKit.Finish(finishArgs, res, asmName + " Сборочный чертёж", null, scale, tech, techTop);

      List<object> rows = new List<object>();
      foreach (Position p in positions)
        rows.Add(new Dictionary<string, object>
        {
          { "pos", p.Number }, { "name", p.Name }, { "qty", p.Qty }, { "standard", p.Standard },
          { "designation", p.Designation }
        });
      res["positions"] = rows;

      if (ToolRegistry.GetBool(a, "spec", true))
      {
        Tools2D.CloseDrawing();
        res["spec"] = Spec(a, positions, asmName, designation, path);
      }
      return res;
    }

    static List<string> ViewList(Dictionary<string, object> a)
    {
      object o;
      List<object> raw = a.TryGetValue("views", out o) ? o as List<object> : null;
      List<string> res = new List<string> { "front" };
      if (raw == null) { res.Add("top"); return res; }
      foreach (object v in raw)
      {
        string s = v.ToString();
        if (s != "front" && s != "top" && s != "left") throw new ToolException("Вид " + s + ": допустимы front, top, left");
        if (!res.Contains(s)) res.Add(s);
      }
      return res;
    }

    internal static string Projection(string v)
    {
      switch (v)
      {
        case "top": return "#Сверху";
        case "left": return "#Слева";
        default: return "#Спереди";
      }
    }

    internal static string ViewName(string v)
    {
      switch (v)
      {
        case "top": return "Вид сверху";
        case "left": return "Вид слева";
        default: return "Главный вид";
      }
    }

    internal static void Place(IAssociationView view, double x, double y, double scale)
    {
      IView iv = (IView)view;
      iv.X = x; iv.Y = y; iv.Scale = scale;
      ((IDrawingObject)view).Update();
    }

    // Координаты вида для точки модели. GetProjectionPoint есть в КОМПАС v22, но не в interop-сборке
    // SDK (она старше) — вызов поздним связыванием через IDispatch.
    internal static double[] Project(IAssociationView view, double x, double y, double z)
    {
      object[] args = { x, y, z, 0.0, 0.0 };
      ParameterModifier mod = new ParameterModifier(5);
      mod[3] = true; mod[4] = true;
      object ok = view.GetType().InvokeMember("GetProjectionPoint", BindingFlags.InvokeMethod, null, view, args,
        new[] { mod }, null, null);
      if (!(ok is bool) || !(bool)ok) throw new ToolException("GetProjectionPoint вернул false");
      return new[] { Convert.ToDouble(args[3]), Convert.ToDouble(args[4]) };
    }

    // габарит проекции (minX, maxX, minY, maxY) по 8 углам габарита сборки
    internal static double[] ProjectedBox(IAssociationView view, double[] mn, double[] mx)
    {
      double[] b = { double.MaxValue, double.MinValue, double.MaxValue, double.MinValue };
      for (int i = 0; i < 8; i++)
      {
        double[] p = Project(view, (i & 1) == 0 ? mn[0] : mx[0], (i & 2) == 0 ? mn[1] : mx[1], (i & 4) == 0 ? mn[2] : mx[2]);
        b[0] = Math.Min(b[0], p[0]); b[1] = Math.Max(b[1], p[0]);
        b[2] = Math.Min(b[2], p[1]); b[3] = Math.Max(b[3], p[1]);
      }
      return b;
    }

    // Позиции: полки — столбцом слева от вида, по высоте в порядке точек-указателей (без пересечений
    // выносок между собой), шаг не меньше 10 мм листа. Точка-указатель — узел сетки 7×7 внутри
    // проекции габарита экземпляра, накрытый проекциями наименьшего числа других компонентов,
    // из таких — ближайший к центру (скорее лежит на материале); представитель позиции —
    // экземпляр с самой левой точкой (ближе к полкам).
    static void Leaders(ksDocument2D d, IAssociationView view, List<Position> positions,
      List<Tools3D.ComponentInfo> comps, double[] box, double scale)
    {
      KompasObject kompas = KompasHost.Kompas;
      Dictionary<Tools3D.ComponentInfo, double[]> boxes = new Dictionary<Tools3D.ComponentInfo, double[]>();
      foreach (Tools3D.ComponentInfo c in comps) boxes[c] = ProjectedBox(view, c.Min, c.Max);
      List<KeyValuePair<Position, double[]>> pts = new List<KeyValuePair<Position, double[]>>();
      foreach (Position p in positions)
      {
        double[] best = null;
        int bestCover = int.MaxValue;
        foreach (Tools3D.ComponentInfo c in p.Instances)
        {
          double[] b = boxes[c];
          double cx = (b[0] + b[1]) / 2, cy = (b[2] + b[3]) / 2;
          double[] own = null;
          int ownCover = int.MaxValue;
          double ownDist = double.MaxValue;
          for (int i = 1; i <= 7; i++)
            for (int j = 1; j <= 7; j++)
            {
              double x = b[0] + (b[1] - b[0]) * i / 8.0, y = b[2] + (b[3] - b[2]) * j / 8.0;
              int cover = 0;
              foreach (KeyValuePair<Tools3D.ComponentInfo, double[]> o in boxes)
                if (o.Key != c && x > o.Value[0] && x < o.Value[1] && y > o.Value[2] && y < o.Value[3]) cover++;
              // малая добавка за правизну: при равной удалённости от центра — левая точка, выноска короче
              double dist = Math.Abs(x - cx) / (b[1] - b[0] + 1e-9) + Math.Abs(y - cy) / (b[3] - b[2] + 1e-9)
                + 0.05 * (x - b[0]) / (b[1] - b[0] + 1e-9);
              if (cover < ownCover || (cover == ownCover && dist < ownDist))
              {
                own = new[] { x, y };
                ownCover = cover;
                ownDist = dist;
              }
            }
          if (ownCover < bestCover || (ownCover == bestCover && own[0] < best[0]))
          {
            best = own;
            bestCover = ownCover;
          }
        }
        pts.Add(new KeyValuePair<Position, double[]>(p, best));
      }
      pts.Sort((u, v) => v.Value[1].CompareTo(u.Value[1]));
      double step = Math.Max(10.0 / scale, (box[3] - box[2]) / Math.Max(1, pts.Count));
      double yTop = (box[3] + box[2]) / 2 + step * (pts.Count - 1) / 2.0;
      double xShelf = box[0] - 18.0 / scale;
      for (int i = 0; i < pts.Count; i++)
      {
        ksPosLeaderParam lead = (ksPosLeaderParam)kompas.GetParamStruct((short)StructType2DEnum.ko_PosLeaderParam);
        ksTextLineParam line = (ksTextLineParam)kompas.GetParamStruct((short)StructType2DEnum.ko_TextLineParam);
        ksTextItemParam item = (ksTextItemParam)kompas.GetParamStruct((short)StructType2DEnum.ko_TextItemParam);
        ksMathPointParam mp = (ksMathPointParam)kompas.GetParamStruct((short)StructType2DEnum.ko_MathPointParam);
        lead.Init(); line.Init(); item.Init(); mp.Init();
        lead.style = ldefin2d.INDICATIN_TEXT_LINE_ARR;
        ksDynamicArray text = (ksDynamicArray)lead.GetpTextline();
        ksDynamicArray items = (ksDynamicArray)line.GetTextItemArr();
        item.s = pts[i].Key.Number.ToString(CultureInfo.InvariantCulture);
        items.ksAddArrayItem(-1, item);
        text.ksAddArrayItem(-1, line);
        lead.SetpTextline(text);
        ksDynamicArray poly = (ksDynamicArray)lead.GetpPolyline();
        ksDynamicArray branch = (ksDynamicArray)kompas.GetDynamicArray(ldefin2d.POINT_ARR);
        mp.x = pts[i].Value[0];
        mp.y = pts[i].Value[1];
        branch.ksAddArrayItem(-1, mp);
        poly.ksAddArrayItem(-1, branch);
        lead.SetpPolyline(poly);
        lead.x = xShelf;
        lead.y = yTop - i * step;
        lead.arrowType = 1;   // точка на детали (ГОСТ 2.109)
        lead.dirX = -1;
        if (d.ksPositionLeader(lead) == 0) Log.Write("позиция " + item.s + ": ksPositionLeader вернул 0");
      }
    }

    // одинаковые компоненты (тот же файл) — одна позиция; детали, затем стандартные изделия
    static List<Position> Group(List<Tools3D.ComponentInfo> comps)
    {
      Dictionary<string, Position> byKey = new Dictionary<string, Position>();
      List<Position> order = new List<Position>();
      foreach (Tools3D.ComponentInfo c in comps)
      {
        bool local = string.IsNullOrEmpty(c.File) || c.File.StartsWith(">>");
        string key = local ? "local:" + c.Name + "|" + c.File : c.File.ToLowerInvariant();
        Position p;
        if (!byKey.TryGetValue(key, out p))
        {
          p = new Position { Name = c.Name, Designation = c.Marking ?? "" };
          foreach (string s in StandardNames)
            if (c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) p.Standard = true;
          byKey[key] = p;
          order.Add(p);
        }
        p.Qty++;
        p.Instances.Add(c);
      }
      List<Position> res = order.FindAll(p => !p.Standard);
      List<Position> std = order.FindAll(p => p.Standard);
      std.Sort((u, v) => string.Compare(u.Name, v.Name, StringComparison.CurrentCulture));
      res.AddRange(std);
      for (int i = 0; i < res.Count; i++) res[i].Number = i + 1;
      return res;
    }

    static object Spec(Dictionary<string, object> a, List<Position> positions, string name, string designation, string drawingPath)
    {
      List<object> items = new List<object>
      {
        new Dictionary<string, object>
        {
          { "section", "docs" }, { "designation", designation != null ? designation + " СБ" : "" },
          { "name", "Сборочный чертёж" }, { "qty", 0 }
        }
      };
      foreach (Position p in positions)
        items.Add(new Dictionary<string, object>
        {
          { "section", p.Standard ? "standart" : "detali" }, { "designation", p.Designation },
          { "name", p.Name }, { "qty", p.Qty }
        });
      string basePath = Path.Combine(Path.GetDirectoryName(drawingPath), Path.GetFileNameWithoutExtension(drawingPath));
      Dictionary<string, object> args = new Dictionary<string, object>
      {
        { "name", name }, { "path", basePath + "_spec.cdw" }, { "items", items }, { "designation", designation }
      };
      string png = ToolRegistry.GetStr(a, "png", null);
      if (!string.IsNullOrEmpty(png))
        args["png"] = Path.Combine(Path.GetDirectoryName(Paths.Abs(png)),
          Path.GetFileNameWithoutExtension(png) + "_spec.png");
      return ToolsSpc.SpecDrawing(args);
    }
  }
}
