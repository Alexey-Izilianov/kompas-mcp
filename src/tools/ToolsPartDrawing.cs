using System;
using System.Collections.Generic;
using System.IO;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;

namespace KompasMcp.Tools
{
  // Чертёж детали по 3D-модели (.m3d): ассоциативные виды в проекционной связи (главный, сверху —
  // под ним, слева — справа), осевые линии, разрез А-А вместо одного из видов, габаритные размеры,
  // штамп из свойств модели. Остальные размеры наносит модель: view_project переводит точки модели
  // в координаты вида и делает вид текущим, дальше работают обычные 2D-инструменты.
  public static class ToolsPartDrawing
  {
    // поле видов на А3 (как у сборочного чертежа): ниже графы 26, левее штампа — до нижней рамки
    const double FieldX0 = 25, FieldX1 = 410, FieldY1 = 274, StampX = 225, BottomLeft = 12, BottomRight = 90;
    const double Margin = 16, Gap = 30;

    // виды последнего part_drawing: front/top/left → номер вида в чертеже drawingPath
    static readonly Dictionary<string, int> viewNumbers = new Dictionary<string, int>();
    static string drawingPath;

    public static void Register()
    {
      ToolRegistry.Add("part_drawing",
        "Чертёж детали по 3D-модели .m3d (ГОСТ 2.305): ассоциативные виды в проекционной связи (front — главный, top — под ним, left — справа) без невидимых линий, с осевыми линиями; section — какой вид заменить разрезом А-А через середину детали (линия разреза — на соседнем виде); габаритные размеры, технические требования, штамп (наименование, обозначение, материал — из модели). Масштаб подбирается по листу А3. Остальные размеры: view_project → lin_dim/diam_dim/rad_dim в координатах вида. Модель должна быть сохранена.",
        @"{""type"":""object"",""properties"":{
""part"":{""type"":""string"",""description"":""Путь к детали .m3d (по умолчанию — текущая деталь; перед чертежом она сохраняется)""},
""views"":{""type"":""array"",""items"":{""type"":""string"",""enum"":[""front"",""top"",""left""]},""description"":""Виды (по умолчанию [front, top]); front есть всегда""},
""section"":{""type"":""string"",""enum"":[""front"",""top"",""left""],""description"":""Вид, который показать разрезом А-А через середину детали (для деталей с отверстиями и полостями)""},
""scale"":{""type"":""number"",""description"":""Масштаб (по умолчанию — наибольший стандартный, при котором виды помещаются)""},
""name"":{""type"":""string"",""description"":""Наименование (по умолчанию — из модели)""},
""designation"":{""type"":""string"",""description"":""Обозначение (по умолчанию — из модели)""},
""material"":{""type"":""string"",""description"":""Материал (по умолчанию — из модели)""},
""tech"":{""type"":""array"",""items"":{""type"":""string""},""description"":""Технические требования""},
""path"":{""type"":""string"",""description"":""Путь .cdw (по умолчанию <деталь>.cdw)""},
""png"":{""type"":""string"",""description"":""PNG-рендер чертежа""}}}",
        a => PartDrawing(a));

      ToolRegistry.Add("view_project",
        "Координаты точек 3D-модели на виде чертежа, построенного part_drawing; вид становится текущим — следующие lin_dim, diam_dim, rad_dim, line, text рисуют в нём в этих координатах (мм модели, масштаб вида учтён). Для размеров по модели: взять точки рёбер/центров из построения детали, спроецировать, нанести размер. Над разрезом стоит обозначение «А-А» — размеры над ним относить на 15 мм и больше (dy у lin_dim — мм листа).",
        @"{""type"":""object"",""properties"":{
""view"":{""type"":""string"",""enum"":[""front"",""top"",""left""],""description"":""Вид (разрез А-А называется по виду, который он заменил)""},
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}},""description"":""Точки модели [x, y, z]""}},
""required"":[""view"",""points""]}",
        a => ViewProject(a));
    }

    static object PartDrawing(Dictionary<string, object> a)
    {
      string src = ToolRegistry.GetStr(a, "part", null);
      if (src == null)
      {
        if (Tools3D.PartPath == null) throw new ToolException("Нет текущей детали — укажите part (путь к .m3d)");
        Tools3D.SavePart(new Dictionary<string, object>());
        src = Tools3D.PartPath;
      }
      src = Paths.Abs(src);
      if (!File.Exists(src)) throw new ToolException("Нет файла детали: " + src);

      // свойства и габарит модели
      Tools3D.OpenPart(src);
      ksPart part = Tools3D.GetPart();
      double x1, y1, z1, x2, y2, z2;
      part.GetGabarit(false, false, out x1, out y1, out z1, out x2, out y2, out z2);
      double[] gmin = { x1, y1, z1 }, gmax = { x2, y2, z2 };
      string name = ToolRegistry.GetStr(a, "name", null) ?? part.name;
      string designation = ToolRegistry.GetStr(a, "designation", null) ?? NullIfEmpty(part.marking);
      string material = ToolRegistry.GetStr(a, "material", null) ?? NullIfEmpty(part.material);

      string section = ToolRegistry.GetStr(a, "section", null);
      List<string> views = ViewList(a, section);

      string path = ToolRegistry.GetStr(a, "path", null) ??
        Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + ".cdw");
      Tools2D.CreateDrawing(new Dictionary<string, object> { { "path", path }, { "name", name } });
      IKompasDocument2D doc7 = (IKompasDocument2D)KompasHost.App7.ActiveDocument;
      IViews coll = doc7.ViewsAndLayersManager.Views;

      // виды в масштабе 1: по ним узнаём габариты проекций, затем раскладываем. Разрез строится
      // после раскладки на месте обычного вида: буквы линии разреза КОМПАС ставит при её создании
      // и при последующем переносе и масштабировании вида уводит их от линии.
      Dictionary<string, IAssociationView> av = new Dictionary<string, IAssociationView>();
      foreach (string v in views) av[v] = NewView(coll, src, v);
      Dictionary<string, double[]> box = new Dictionary<string, double[]>();
      foreach (KeyValuePair<string, IAssociationView> kv in av)
        box[kv.Key] = ToolsAsmDrawing.ProjectedBox(kv.Value, gmin, gmax);

      double[] f = box["front"], t = box.ContainsKey("top") ? box["top"] : null, l = box.ContainsKey("left") ? box["left"] : null;
      double wF = f[1] - f[0], hF = f[3] - f[2];
      double wL = l != null ? l[1] - l[0] : 0, hT = t != null ? t[3] - t[2] : 0;
      double scale = ToolRegistry.GetDbl(a, "scale", 0);
      if (scale <= 0)
      {
        double fit = Math.Min((FieldX1 - FieldX0 - 2 * Margin - (l != null ? Gap : 0)) / (wF + wL),
                              (FieldY1 - BottomLeft - 2 * Margin - (t != null ? Gap : 0) - (section == "left" ? 12 : 0)) / (hF + hT));
        if (t != null) fit = Math.Min(fit, (StampX - FieldX0 - 2 * Margin) / Math.Max(wF, t[1] - t[0]));
        if (l != null) fit = Math.Min(fit, (FieldY1 - BottomRight - 2 * Margin) / Math.Max(hF, l[3] - l[2]));
        scale = DrawKit.StdScale(fit);
      }

      // проекционная связь: у главного и вида сверху общая ось x, у главного и вида слева — ось y
      double xF = FieldX0 + Margin - scale * f[0];
      // над главным видом — верхний штрих вертикальной линии разреза
      double yF = FieldY1 - Margin - (section == "left" ? 12 : 0) - scale * f[3];
      ToolsAsmDrawing.Place(av["front"], xF, yF, scale);
      if (t != null) ToolsAsmDrawing.Place(av["top"], xF, yF + scale * f[2] - Gap - scale * t[3], scale);
      if (l != null) ToolsAsmDrawing.Place(av["left"], xF + scale * f[1] + Gap - scale * l[0], yF, scale);
      if (section != null)
      {
        IView plain = (IView)av[section];
        double px = plain.X, py = plain.Y;
        ((IDrawingObject)plain).Delete();
        av[section] = SectionView(coll, av[CutBase(section)], section, gmin, gmax, scale);
        ToolsAsmDrawing.Place(av[section], px, py, scale);
      }

      // габаритные размеры: ширина (под видом — над ним обозначение разреза) и высота — на главном
      // виде, глубина — на виде сверху или слева
      ksDocument2D d = Tools2D.GetDoc();
      d.ksOpenView(((IView)av["front"]).Number);
      // под главным видом при разрезе на месте вида слева проходит нижний штрих линии разреза
      DrawKit.Dim(f[0], f[2], f[1], f[2], false, section == "left" ? -30 : -10);
      DrawKit.Dim(f[1], f[2], f[1], f[3], true, 10);
      if (t != null)
      {
        d.ksOpenView(((IView)av["top"]).Number);
        DrawKit.Dim(t[1], t[2], t[1], t[3], true, 10);
      }
      else if (l != null)
      {
        d.ksOpenView(((IView)av["left"]).Number);
        DrawKit.Dim(l[0], l[2], l[1], l[2], false, -10);
      }

      viewNumbers.Clear();
      Dictionary<string, object> res = new Dictionary<string, object>();
      List<object> viewInfo = new List<object>();
      foreach (KeyValuePair<string, IAssociationView> kv in av)
      {
        IView iv = (IView)kv.Value;
        viewNumbers[kv.Key] = iv.Number;
        double[] b = box[kv.Key];
        viewInfo.Add(new Dictionary<string, object>
        {
          { "view", kv.Key }, { "section", kv.Key == section }, { "number", iv.Number },
          { "sheet_box", new List<object> { iv.X + scale * b[0], iv.X + scale * b[1], iv.Y + scale * b[2], iv.Y + scale * b[3] } }
        });
      }
      drawingPath = Tools2D.DocPath;
      res["views"] = viewInfo;
      res["gabarit"] = new List<object> { x2 - x1, y2 - y1, z2 - z1 };
      List<string> tech = DrawKit.TechItems(a, "Неуказанные предельные отклонения размеров: H14, h14, ±IT14/2.");
      DrawKit.Finish(new Dictionary<string, object>(a) { { "designation", designation } }, res, name, material, scale,
        tech, DrawKit.TechTopAboveStamp(tech));
      return res;
    }

    static List<string> ViewList(Dictionary<string, object> a, string section)
    {
      object o;
      List<object> raw = a.TryGetValue("views", out o) ? o as List<object> : null;
      List<string> res = new List<string> { "front" };
      if (raw == null) res.Add("top");
      else
        foreach (object v in raw)
        {
          string s = v.ToString();
          if (s != "front" && s != "top" && s != "left") throw new ToolException("Вид " + s + ": допустимы front, top, left");
          if (!res.Contains(s)) res.Add(s);
        }
      if (section != null)
      {
        if (section != "front" && section != "top" && section != "left")
          throw new ToolException("section: допустимы front, top, left");
        if (!res.Contains(section)) res.Add(section);
        if (!res.Contains(CutBase(section))) res.Add(CutBase(section));
      }
      return res;
    }

    // Вид, на котором проводится линия разреза: для разреза на месте главного — вид сверху,
    // для разрезов на месте видов сверху и слева — главный.
    static string CutBase(string section) { return section == "front" ? "top" : "front"; }

    static IAssociationView NewView(IViews coll, string src, string v)
    {
      IAssociationView view = (IAssociationView)coll.Add(LtViewType.vt_Arbitrary);
      view.SourceFileName = src;
      view.ProjectionName = ToolsAsmDrawing.Projection(v);
      view.HiddenLinesVisible = false;
      view.CenterLinesVisible = true;
      IView iv = (IView)view;
      iv.X = 0; iv.Y = 0; iv.Scale = 1; iv.Name = ToolsAsmDrawing.ViewName(v);
      if (!((IDrawingObject)view).Update()) throw new ToolException("Не удалось построить вид " + v);
      HideDesignation(view);
      return view;
    }

    // Линия разреза через середину габарита на базовом виде и вид-разрез по ней.
    static IAssociationView SectionView(IViews coll, IAssociationView baseView, string section, double[] gmin, double[] gmax,
      double scale)
    {
      double[] b = ToolsAsmDrawing.ProjectedBox(baseView, gmin, gmax);
      double cx = (b[0] + b[1]) / 2, cy = (b[2] + b[3]) / 2, ext = 6 / scale;   // 6 мм листа за контур
      // направление линии задаёт сторону стрелок — взгляд того вида, который заменяет разрез
      double[] pts = section == "left"
        ? new[] { cx, b[2] - ext, cx, b[3] + ext }
        : new[] { b[1] + ext, cy, b[0] - ext, cy };
      ((IView)baseView).Current = true;
      ICutLine cut = ((ISymbols2DContainer)baseView).CutLines.Add();
      cut.Points = pts;
      if (!((IDrawingObject)cut).Update()) throw new ToolException("Не удалось построить линию разреза");
      // X1..Y2 — левый нижний угол букв (не концы линии; буква ~6×10 мм листа). Штрих уходит от
      // конца наружу на 12 мм, стрелка длиной 14 мм — в 9 мм от конца. Горизонтальная линия —
      // буквы над штрихами; вертикальная — слева от хвостов стрелок, по их высоте.
      double mm = 1 / scale;
      if (section == "left")
      {
        cut.X1 = pts[0] - 22 * mm; cut.Y1 = pts[1] - 14 * mm;
        cut.X2 = pts[2] - 22 * mm; cut.Y2 = pts[3] + 4 * mm;
      }
      else { cut.X1 = pts[0]; cut.Y1 = pts[1] + 7 * mm; cut.X2 = pts[2]; cut.Y2 = pts[3] + 7 * mm; }
      ((IDrawingObject)cut).Update();

      IAssociationView view = (IAssociationView)coll.Add(LtViewType.vt_Section);
      view.BaseObject = (IDrawingObject)cut;
      view.Section = false;              // разрез, а не сечение: видимые линии за плоскостью сохраняются
      view.HiddenLinesVisible = false;
      view.CenterLinesVisible = true;
      IView iv = (IView)view;
      iv.X = 0; iv.Y = 0; iv.Scale = 1;
      if (!((IDrawingObject)view).Update()) throw new ToolException("Не удалось построить разрез");
      try { ((IViewDesignation)view).ShowScale = false; }   // «А-А» без масштаба: он такой же, как у листа
      catch (Exception e) { Log.Write("обозначение разреза: " + e.Message); }
      return view;
    }

    static void HideDesignation(IAssociationView view)
    {
      try
      {
        IViewDesignation des = (IViewDesignation)view;
        des.ShowName = false;
        des.ShowScale = false;
      }
      catch (Exception e) { Log.Write("обозначение вида: " + e.Message); }
    }

    static object ViewProject(Dictionary<string, object> a)
    {
      string v = ToolRegistry.GetStr(a, "view");
      int number;
      if (drawingPath == null || Tools2D.DocPath != drawingPath || !viewNumbers.TryGetValue(v, out number))
        throw new ToolException("Вид " + v + " не найден: view_project работает с чертежом, только что построенным part_drawing");
      IKompasDocument2D doc7 = (IKompasDocument2D)KompasHost.App7.ActiveDocument;
      IAssociationView view = (IAssociationView)doc7.ViewsAndLayersManager.Views.ViewByNumber[number];
      Tools2D.GetDoc().ksOpenView(number);

      object o;
      List<object> pts = a.TryGetValue("points", out o) ? o as List<object> : null;
      if (pts == null || pts.Count == 0) throw new ToolException("points: нужен массив точек [x, y, z]");
      List<object> res = new List<object>();
      foreach (object p in pts)
      {
        List<object> c = p as List<object>;
        if (c == null || c.Count != 3) throw new ToolException("Точка должна быть [x, y, z]");
        double[] q = ToolsAsmDrawing.Project(view, ToolRegistry.Number(c[0]), ToolRegistry.Number(c[1]), ToolRegistry.Number(c[2]));
        res.Add(new List<object> { Math.Round(q[0], 3), Math.Round(q[1], 3) });
      }
      return new Dictionary<string, object> { { "view", v }, { "points", res } };
    }

    static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
  }
}
