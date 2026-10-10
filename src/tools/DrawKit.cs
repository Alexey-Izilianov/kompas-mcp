using System;
using System.Collections.Generic;
using KompasAPI7;
using Kompas6API5;
using Kompas6Constants;

namespace KompasMcp.Tools
{
  // Оформление чертежей деталей передач на листе А3 (альбомный): масштабы ГОСТ 2.302,
  // таблица параметров в правом верхнем углу (ГОСТ 2.403–2.406), технические требования
  // под ней (ГОСТ 2.316), штамп. Координаты — мм листа: рамка 20..415 × 5..292, штамп 230..415 × 5..60.
  internal static class DrawKit
  {
    const double FrameRight = 415, FrameTop = 292, StampTop = 60;
    const double TableW = 110, TableX = FrameRight - TableW;
    // поле главного изображения вместе с размерами: левее таблицы, выше штампа
    const double ViewX0 = 30, ViewX1 = 298, ViewY0 = 68, ViewY1 = 287;

    static readonly double[] Scales = { 5, 4, 2.5, 2, 1, 0.5, 0.4, 0.25, 0.2, 0.1 };

    // Масштаб и положение вида: габарит модели [x0..x1]×[y0..y1] плюс поля под размеры
    // в мм листа (слева/справа/снизу/сверху) — наибольший стандартный масштаб, вид по центру поля.
    public static double[] Fit(double x0, double x1, double y0, double y1, double mL, double mR, double mB, double mT,
      double scale = 0)
    {
      if (scale <= 0)
        scale = StdScale(Math.Min((ViewX1 - ViewX0 - mL - mR) / (x1 - x0), (ViewY1 - ViewY0 - mB - mT) / (y1 - y0)));
      double cx = (ViewX0 + mL + ViewX1 - mR) / 2.0 - scale * (x0 + x1) / 2.0;
      double cy = (ViewY0 + mB + ViewY1 - mT) / 2.0 - scale * (y0 + y1) / 2.0;
      return new[] { cx, cy, scale };
    }

    // наибольший стандартный масштаб, не превышающий fit
    public static double StdScale(double fit)
    {
      foreach (double s in Scales) if (s <= fit) return s;
      return Scales[Scales.Length - 1];
    }

    public static string ScaleText(double s)
    {
      return s >= 1 ? Tools2D.FmtNum(s) + ":1" : "1:" + Tools2D.FmtNum(1 / s);
    }

    // Таблица параметров: колонки 75 + 35 мм, строки 8 мм, шрифт 3,5; верх и правый край — по рамке.
    // Длинные строки сужаются (ksuStr), затем уменьшается кегль. Возвращает y нижней кромки.
    public static double ParamTable(IList<string[]> rows)
    {
      const double col1 = 75, rowH = 8, h = 3.5;
      Tools2D.MakeView(TableX, FrameTop, 1.0, "Таблица параметров");
      ksDocument2D d = Tools2D.GetDoc();
      double th = rows.Count * rowH;
      for (int i = 1; i < rows.Count; i++) d.ksLineSeg(0, -i * rowH, TableW, -i * rowH, 2);
      d.ksLineSeg(0, -th, TableW, -th, 1);
      d.ksLineSeg(0, 0, 0, -th, 1);
      d.ksLineSeg(col1, 0, col1, -th, 1);
      for (int i = 0; i < rows.Count; i++)
      {
        double y = -i * rowH - rowH / 2.0 - h / 2.0;
        Text(d, 2, y, rows[i][0], col1 - 4, h);
        Text(d, col1 + 2, y, rows[i][1], TableW - col1 - 4, h);
      }
      return FrameTop - th;
    }

    // Технические требования под таблицей: нумерация, перенос по ширине колонки.
    // Возвращает false, если текст не поместился до штампа.
    public static bool TechReq(IList<string> items, double top)
    {
      if (items == null || items.Count == 0) return true;
      const double h = 3.5, lineH = 6;
      int maxChars = (int)((TableW - 4) / (0.8 * h));
      Tools2D.MakeView(TableX, top - 8, 1.0, "Технические требования");
      ksDocument2D d = Tools2D.GetDoc();
      double y = 0;
      for (int i = 0; i < items.Count; i++)
      {
        string prefix = (i + 1) + ". ";
        foreach (string line in Wrap(prefix + items[i], maxChars, prefix.Length))
        {
          d.ksText(2, y, 0, h, 1, 0, line);
          y -= lineH;
        }
      }
      return top - 8 + y > StampTop + 3;
    }

    static IEnumerable<string> Wrap(string s, int maxChars, int indent)
    {
      string pad = new string(' ', indent);
      string[] words = s.Split(' ');
      string cur = "";
      foreach (string w in words)
      {
        if (cur.Length > 0 && cur.Length + 1 + w.Length > maxChars)
        {
          yield return cur;
          cur = pad + w;
        }
        else cur = cur.Length == 0 ? w : cur + " " + w;
      }
      if (cur.Length > 0) yield return cur;
    }

    static void Text(ksDocument2D d, double x, double y, string s, double width, double h)
    {
      double need = s.Length * 0.8 * h;
      double narrow = Math.Max(0.6, Math.Min(1.0, width / need));
      if (need * narrow > width) h = Math.Max(2.5, width / (s.Length * 0.8 * narrow));
      d.ksText(x, y, 0, h, narrow, 0, s);
    }

    public static void Stamp(string description, string designation, string material, double scale)
    {
      try
      {
        IKompasDocument2D doc7 = (IKompasDocument2D)KompasHost.App7.ActiveDocument;
        if (doc7 == null) return;
        IStamp stamp = (IStamp)((ILayoutSheet)doc7.LayoutSheets.ItemByNumber[1]).Stamp;
        Set(stamp, ksStampEnum.ksStDescription, description);
        Set(stamp, ksStampEnum.ksStPartNumber, designation);
        Set(stamp, ksStampEnum.ksStMaterial, material);
        Set(stamp, ksStampEnum.ksStScale, ScaleText(scale));
        Set(stamp, ksStampEnum.ksStSheetNumber, "1");
        Set(stamp, ksStampEnum.ksStNumberOfSheets, "1");
        stamp.Update();
      }
      catch (Exception e) { Log.Write("штамп: " + e.Message); }
    }

    static void Set(IStamp stamp, ksStampEnum cell, string value)
    {
      if (!string.IsNullOrEmpty(value)) stamp.Text[(int)cell].Str = value;
    }

    // ---- геометрия в текущем виде ----

    public static void Poly(IList<double[]> pts, int style, bool closed, bool mirrorY = false)
    {
      ksDocument2D d = Tools2D.GetDoc();
      double s = mirrorY ? -1 : 1;
      int n = closed ? pts.Count : pts.Count - 1;
      for (int i = 0; i < n; i++)
      {
        double[] a = pts[i], b = pts[(i + 1) % pts.Count];
        d.ksLineSeg(a[0], s * a[1], b[0], s * b[1], style);
      }
    }

    // Штриховка многоугольника отдельным блоком без обводки (обводку рисует вызывающий)
    public static void Hatch(IList<double[]> pts, bool mirrorY = false)
    {
      double s = mirrorY ? -1 : 1;
      List<object> segs = new List<object>(pts.Count);
      for (int i = 0; i < pts.Count; i++)
      {
        double[] a = pts[i], b = pts[(i + 1) % pts.Count];
        segs.Add(new Dictionary<string, object>
        {
          { "type", "line" }, { "x1", a[0] }, { "y1", s * a[1] }, { "x2", b[0] }, { "y2", s * b[1] }
        });
      }
      Tools2D.Hatch(new Dictionary<string, object>
      {
        { "contour", segs }, { "angle", 45.0 }, { "step", 2.5 }, { "x0", pts[0][0] }, { "y0", s * pts[0][1] },
        { "outline", false }
      });
    }

    // Линейный размер; vertical — размерная линия вертикальна. off — смещение размерной линии
    // от первой точки в мм листа: dx/dy у КОМПАСа «бумажные» и абсолютные (вертикальный размер
    // сдвигается по x: + вправо; горизонтальный — по y: + вверх), масштаб вида на них не влияет.
    public static void Dim(double x1, double y1, double x2, double y2, bool vertical, double off,
      bool diameter = false)
    {
      Dictionary<string, object> a = new Dictionary<string, object>
      {
        { "x1", x1 }, { "y1", y1 }, { "x2", x2 }, { "y2", y2 }, { "ang", vertical ? 90.0 : 0.0 },
        { "dx", vertical ? off : 0.0 }, { "dy", vertical ? 0.0 : off }
      };
      if (diameter) a["sign"] = 1;
      Tools2D.LinDim(a);
    }

    // Волнистая линия обрыва (тонкая) от a до b: синусоида с амплитудой amp мм вида
    public static List<double[]> Wavy(double[] a, double[] b, double amp)
    {
      double dx = b[0] - a[0], dy = b[1] - a[1], len = Math.Sqrt(dx * dx + dy * dy);
      int n = Math.Max(8, (int)(len / (amp * 1.5)));
      double nx = -dy / len, ny = dx / len;
      List<double[]> pts = new List<double[]>(n + 1);
      for (int i = 0; i <= n; i++)
      {
        double t = (double)i / n, w = amp * Math.Sin(t * Math.PI * Math.Max(2, (int)(len / (amp * 6))));
        pts.Add(new[] { a[0] + dx * t + nx * w, a[1] + dy * t + ny * w });
      }
      return pts;
    }

    public static void Finish(Dictionary<string, object> a, Dictionary<string, object> res, string description,
      string material, double scale, IList<string> tech, double tableBottom)
    {
      if (!TechReq(tech, tableBottom)) res["warning"] = "технические требования не поместились над штампом";
      Stamp(ToolRegistry.GetStr(a, "description", description), ToolRegistry.GetStr(a, "designation", null),
        ToolRegistry.GetStr(a, "material", material), scale);
      res["path"] = Tools2D.DocPath;
      res["scale"] = ScaleText(scale);
      Tools2D.GetDoc().ksSaveDocument(Tools2D.DocPath);
      string png = ToolRegistry.GetStr(a, "png", null);
      if (!string.IsNullOrEmpty(png))
        res["png"] = ((Dictionary<string, object>)Tools2D.RenderPng(new Dictionary<string, object>
          { { "path", png }, { "resolution", 150 } }))["png"];
    }

    // Технические требования: свои (tech) или типовые
    public static List<string> TechItems(Dictionary<string, object> a, params string[] defaults)
    {
      object o;
      List<object> custom = a.TryGetValue("tech", out o) ? o as List<object> : null;
      List<string> res = new List<string>();
      if (custom != null) foreach (object s in custom) res.Add(s.ToString());
      else res.AddRange(defaults);
      return res;
    }
  }
}
