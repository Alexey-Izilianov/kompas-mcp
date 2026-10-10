using System;
using System.Collections.Generic;
using System.Globalization;
using Kompas6API5;
using Kompas6Constants;
using KompasAPI7;
using KAPITypes;
using KompasMcp;

namespace KompasMcp.Tools
{
  // Спецификация ГОСТ 2.106-96, форма 1 — 2D-таблица на листе А4 (книжный). Нативный
  // lt_DocSpc headless не годится (разделы не навешиваются, ksAddSpcDescription = 0), поэтому
  // таблица рисуется примитивами. Графы 6/6/8/70/63/10/22 мм (x 20..205 листа), заголовок 15 мм
  // под графой 26 рамки (левый верхний угол, 14 мм), строки 8 мм вниз до штампа. Разделы — в порядке ГОСТ 2.106; заголовок
  // раздела подчёркнут, над ним и под ним по свободной строке; у документации нет позиции.
  public static class ToolsSpc
  {
    static readonly string[] SecOrder = { "docs", "sborka", "detali", "standart", "prochie", "material" };
    static readonly string[] SecTitle =
      { "Документация", "Сборочные единицы", "Детали", "Стандартные изделия", "Прочие изделия", "Материалы" };

    const double Left = 20, Top = 278, Bottom = 60, HeaderH = 15, RowH = 8, TextH = 3.5;
    static readonly double[] Cols = { 6, 6, 8, 70, 63, 10, 22 };

    public static void Register()
    {
      ToolRegistry.Add("spec_drawing",
        "Спецификация ГОСТ 2.106 форма 1 (2D-таблица на А4 книжном .cdw). items: [{section:'docs'|'sborka'|'detali'|'standart'|'prochie'|'material', format, designation, name, qty, note}]. Разделы выводятся в порядке ГОСТ 2.106, позиции нумеруются сквозь разделы (кроме документации); до 25 строк на листе.",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""Наименование изделия в штамп""},
""designation"":{""type"":""string"",""description"":""Обозначение изделия в штамп""},
""path"":{""type"":""string""},
""png"":{""type"":""string""},
""items"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{
""section"":{""type"":""string"",""enum"":[""docs"",""sborka"",""detali"",""standart"",""prochie"",""material""]},
""format"":{""type"":""string"",""description"":""Формат документа, напр. A3""},
""designation"":{""type"":""string""},
""name"":{""type"":""string""},
""qty"":{""type"":""number""},
""note"":{""type"":""string""}}}}},
""required"":[""items""]}",
        a => SpecDrawing(a));
    }

    class Row
    {
      public string Format = "", Pos = "", Designation = "", Name = "", Qty = "", Note = "";
      public bool Title;
    }

    static object SpecDrawing(Dictionary<string, object> a)
    {
      object itemsObj;
      List<object> items = a.TryGetValue("items", out itemsObj) ? itemsObj as List<object> : null;
      if (items == null || items.Count == 0) throw new ToolException("Нет items");

      List<Row> rows = new List<Row>();
      int pos = 0, created = 0;
      for (int si = 0; si < SecOrder.Length; si++)
      {
        List<Dictionary<string, object>> sec = SectionOf(items, SecOrder[si]);
        if (sec.Count == 0) continue;
        if (rows.Count > 0) rows.Add(new Row());
        rows.Add(new Row { Name = SecTitle[si], Title = true });
        rows.Add(new Row());
        foreach (Dictionary<string, object> it in sec)
        {
          Row r = new Row
          {
            Format = ToolRegistry.GetStr(it, "format", ""),
            Designation = ToolRegistry.GetStr(it, "designation", ""),
            Name = ToolRegistry.GetStr(it, "name", ""),
            Note = ToolRegistry.GetStr(it, "note", "")
          };
          double q = ToolRegistry.GetDbl(it, "qty", SecOrder[si] == "docs" ? 0 : 1);
          if (q > 0) r.Qty = q.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');
          if (SecOrder[si] != "docs") r.Pos = (++pos).ToString(CultureInfo.InvariantCulture);
          rows.Add(r);
          created++;
        }
      }
      if (created == 0) throw new ToolException("Ни одного item по известным разделам (docs/sborka/detali/standart/prochie/material)");
      int capacity = (int)((Top - HeaderH - Bottom) / RowH);
      if (rows.Count > capacity) throw new ToolException("Строк " + rows.Count + " > " + capacity + " (ёмкость первого листа) — сократите состав");

      string name = ToolRegistry.GetStr(a, "name", "Спецификация");
      Dictionary<string, object> createArgs = new Dictionary<string, object>
        { { "name", name }, { "format", "A4" }, { "landscape", false } };
      string path = ToolRegistry.GetStr(a, "path", null);
      if (!string.IsNullOrEmpty(path)) createArgs["path"] = path;
      Tools2D.CreateDrawing(createArgs);
      Tools2D.MakeView(Left, 0, 1.0, "Спецификация");
      ksDocument2D d = Tools2D.GetDoc();

      double[] x = new double[Cols.Length + 1];
      for (int i = 0; i < Cols.Length; i++) x[i + 1] = x[i] + Cols[i];
      double yHead = Top - HeaderH;
      // разлиновка: графы — основными линиями на всю высоту, строки — тонкими
      foreach (double xi in x) d.ksLineSeg(xi, Bottom, xi, Top, 1);
      d.ksLineSeg(0, yHead, x[x.Length - 1], yHead, 1);
      for (double y = yHead - RowH; y > Bottom + 0.1; y -= RowH) d.ksLineSeg(0, y, x[x.Length - 1], y, 2);

      // заголовок: узкие графы — вертикальным текстом (угол ksText — в градусах)
      string[] head = { "Формат", "Зона", "Поз.", "Обозначение", "Наименование", "Кол.", "Примечание" };
      for (int i = 0; i < head.Length; i++)
      {
        if (Cols[i] <= 10)
          d.ksText((x[i] + x[i + 1]) / 2.0 + TextH / 2.0 - 0.5, yHead + 1.5, 90, TextH, 1, 0, head[i]);
        else if (i == head.Length - 1)
        {
          Centered(d, x[i], Cols[i], yHead + 8.5, "Приме-");
          Centered(d, x[i], Cols[i], yHead + 3.0, "чание");
        }
        else Centered(d, x[i], Cols[i], yHead + (HeaderH - TextH) / 2.0, head[i]);
      }

      for (int i = 0; i < rows.Count; i++)
      {
        Row r = rows[i];
        double yb = yHead - (i + 1) * RowH + (RowH - TextH) / 2.0;
        if (r.Title)
        {
          double w = Centered(d, x[4], Cols[4], yb, r.Name);
          d.ksLineSeg(x[4] + (Cols[4] - w) / 2.0, yb - 1.0, x[4] + (Cols[4] + w) / 2.0, yb - 1.0, 2);
          continue;
        }
        Centered(d, x[0], Cols[0], yb, r.Format);
        Centered(d, x[2], Cols[2], yb, r.Pos);
        Cell(d, x[3], Cols[3], yb, r.Designation);
        Cell(d, x[4], Cols[4], yb, r.Name);
        Centered(d, x[5], Cols[5], yb, r.Qty);
        Cell(d, x[6], Cols[6], yb, r.Note);
      }

      FillStamp(name, ToolRegistry.GetStr(a, "designation", null));
      d.ksSaveDocument(Tools2D.DocPath);
      Dictionary<string, object> res = new Dictionary<string, object>
      {
        { "saved", Tools2D.DocPath }, { "items", created }, { "rows", rows.Count }, { "capacity", capacity }
      };
      string png = ToolRegistry.GetStr(a, "png", null);
      if (!string.IsNullOrEmpty(png))
        res["png"] = ((Dictionary<string, object>)Tools2D.RenderPng(new Dictionary<string, object>
          { { "path", png }, { "resolution", 150 } }))["png"];
      return res;
    }

    // ширина символа шрифта ГОСТ ≈ 0.6h (замер по рендеру); длинные строки сужаются (ksuStr до 0.6),
    // затем мельчают
    const double CharW = 0.6;

    static double Fit(string text, double width, out double h, out double narrow)
    {
      h = TextH;
      double need = text.Length * CharW * h;
      narrow = Math.Max(0.6, Math.Min(1.0, width / need));
      if (need * narrow > width) h = Math.Max(2.0, width / (text.Length * CharW * narrow));
      return text.Length * CharW * h * narrow;
    }

    static void Cell(ksDocument2D d, double left, double w, double y, string text)
    {
      if (string.IsNullOrEmpty(text)) return;
      double h, narrow;
      Fit(text, w - 3, out h, out narrow);
      d.ksText(left + 1.5, y, 0, h, narrow, 0, text);
    }

    static double Centered(ksDocument2D d, double left, double w, double y, string text)
    {
      if (string.IsNullOrEmpty(text)) return 0;
      double h, narrow;
      double tw = Fit(text, w - 2, out h, out narrow);
      d.ksText(left + (w - tw) / 2.0, y, 0, h, narrow, 0, text);
      return tw;
    }

    static List<Dictionary<string, object>> SectionOf(List<object> items, string sec)
    {
      List<Dictionary<string, object>> r = new List<Dictionary<string, object>>();
      foreach (object o in items)
      {
        Dictionary<string, object> it = o as Dictionary<string, object>;
        if (it == null) throw new ToolException("item должен быть объектом");
        if (ToolRegistry.GetStr(it, "section", "detali") == sec) r.Add(it);
      }
      return r;
    }

    // Штамп через API-7 (идиома Tools2D.FillStamp)
    static void FillStamp(string description, string partNumber)
    {
      try
      {
        IApplication app7 = KompasHost.App7;
        IKompasDocument2D doc7 = (IKompasDocument2D)app7.ActiveDocument;
        if (doc7 == null) return;
        ILayoutSheet sheet = (ILayoutSheet)doc7.LayoutSheets.ItemByNumber[1];
        IStamp stamp = (IStamp)sheet.Stamp;
        if (partNumber != null && partNumber.Length > 0)
          stamp.Text[(int)ksStampEnum.ksStPartNumber].Str = partNumber; // графа 2 — обозначение
        if (description != null && description.Length > 0)
          stamp.Text[(int)ksStampEnum.ksStDescription].Str = description; // графа 1 — наименование
        stamp.Text[(int)ksStampEnum.ksStSheetNumber].Str = "1";
        stamp.Text[(int)ksStampEnum.ksStNumberOfSheets].Str = "1";
        stamp.Update();
      }
      catch (Exception e)
      {
        Log.Write("штамп спецификации: " + e.Message); // не критично
      }
    }
  }
}