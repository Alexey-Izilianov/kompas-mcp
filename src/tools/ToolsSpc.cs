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
  // Спецификация ГОСТ 2.106-96, форма 1 — как 2D-таблица на листе А4 (книжный).
  // Нативный lt_DocSpc обследован (ToolsSpc-проба): документ создаётся, но разделы
  // не навешиваются headless (ksAddSpcDescription = 0 для всех стилей GRAPHIC.LYT),
  // поэтому таблица рисуется примитивами — проверенный путь Tools2D.
  // Сетка: ширина 185 мм (x 20..205 листа), колонки 10/10/5/65/45/10/40,
  // строки 8 мм, шапка 40 мм, записи растут вверх от шапки (шапка внизу).
  public static class ToolsSpc
  {
    // Порядок разделов ГОСТ 2.106 (по которому группируются items)
    static readonly string[] SecOrder = { "detali", "standart", "material", "docs" };
    static readonly string[] SecTitle = { "Детали", "Стандартные изделия", "Материалы", "Документация" };

    public static void Register()
    {
      ToolRegistry.Add("spec_drawing",
        "Спецификация ГОСТ 2.106 форма 1 (2D-таблица на А4 книжном .cdw). items: [{section:'detali'|'standart'|'material'|'docs', designation, name, qty, note}]. Поз. нумеруется внутри раздела; разделы выводятся в порядке ЕСДК (Детали → Стандартные → Материалы → Документация).",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""Наименование в штамп""},
""path"":{""type"":""string""},
""png"":{""type"":""string""},
""items"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{
""section"":{""type"":""string"",""enum"":[""detali"",""standart"",""material"",""docs""]},
""designation"":{""type"":""string""},
""name"":{""type"":""string""},
""qty"":{""type"":""number""},
""note"":{""type"":""string""}}}}}}",
        a => SpecDrawing(a));
    }

    static object SpecDrawing(Dictionary<string, object> a)
    {
      object itemsObj;
      List<object> items = a.TryGetValue("items", out itemsObj) ? itemsObj as List<object> : null;
      if (items == null || items.Count == 0) throw new ToolException("Нет items");

      // строки таблицы: разделы в порядке ЕСДК; поз. нумеруется сквозняком
      List<string[]> rows = new List<string[]>();
      int created = 0, pos = 0;
      foreach (string sec in SecOrder)
      {
        List<object> secItems = SectionOf(items, sec);
        if (secItems.Count == 0) continue;
        int idx = Array.IndexOf(SecOrder, sec);
        rows.Add(new string[] { "", SecTitle[idx], "", "" });
        foreach (object itObj in secItems)
        {
          Dictionary<string, object> it = itObj as Dictionary<string, object>;
          if (it == null) throw new ToolException("item должен быть объектом");
          pos++;
          double q = ToolRegistry.GetDbl(it, "qty", 1);
          rows.Add(new string[] {
            pos.ToString(CultureInfo.InvariantCulture),
            ToolRegistry.GetStr(it, "designation", ""),
            ToolRegistry.GetStr(it, "name", ""),
            q == 1 ? "1" : ToolRegistry.GetDbl(it, "qty", 1).ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ','),
            ToolRegistry.GetStr(it, "note", "")
          });
          created++;
        }
      }
      if (created == 0) throw new ToolException("Ни одного item по известным разделам (detali/standart/material/docs)");

      // ---- лист А4 книжный ----
      Dictionary<string, object> createArgs = new Dictionary<string, object>();
      string path = ToolRegistry.GetStr(a, "path", null);
      if (path != null && path.Length > 0) createArgs["path"] = path;
      string name = ToolRegistry.GetStr(a, "name", "Спецификация");
      createArgs["name"] = name;
      createArgs["format"] = "A4";
      createArgs["landscape"] = false;
      object dr = Tools2D.CreateDrawing(createArgs);

      // вид-якорь листа уже в (0,0); таблица — отдельный вид от левого нижнего угла сетки
      // (x=20 лист = левый отступ рамки ГОСТ, y=63 — выше штампа А4)
      Tools2D.MakeView(20.0, 63.0, 1.0, "Спецификация");
      ksDocument2D d = Tools2D.GetDoc();

      const int headerH = 40, rowH = 8;
      const int colFmt = 10, colZone = 10, colPos = 5, colDesig = 65, colName = 45, colQty = 10, colNote = 40;
      // x-границы колонок
      double x0 = 0, x1 = colFmt, x2 = x1 + colZone, x3 = x2 + colPos,
        x4 = x3 + colDesig, x5 = x4 + colName, x6 = x5 + colQty, x7 = x6 + colNote; // = 185

      if (rows.Count > 22)
        throw new ToolException("Строк " + rows.Count + " > 22 (ёмкость А4 без штампа) — разбейте на листы");

      // ---- шапка (внизу, 40 мм) ----
      for (int i = 0; i <= 5; i++)
        d.ksLineSeg(x0, i * 8, x7, i * 8, 1);
      // колонки: сквозные вертикали шапки + данные
      double[] vx = { x1, x2, x3, x4, x5, x6, x7 };
      double top = headerH + rows.Count * rowH;
      foreach (double x in vx)
        d.ksLineSeg(x, 0, x, top, 1);
      // внешняя рамка таблицы
      d.ksLineSeg(x0, 0, x0, top, 1);
      d.ksLineSeg(x7, headerH, x7, top, 1);

      // вертикальные заголовки (Формат/Зона/Поз./Кол./Прим.)
      // КОМПАС-эксперимент: ang у ksText — градусы (см. NOTES «Грабли»)
      const double vertAng = 90.0;
      d.ksText((x0 + x1) / 2.0 - 2.5, headerH / 2.0 - 2.5, vertAng, 5, 1, 0, "Формат");
      d.ksText((x1 + x2) / 2.0 - 2.5, headerH / 2.0 - 2.5, vertAng, 5, 1, 0, "Зона");
      d.ksText((x2 + x3) / 2.0 - 2.5, headerH / 2.0 - 2.5, vertAng, 5, 1, 0, "Поз.");
      d.ksText((x5 + x6) / 2.0 - 2.5, headerH / 2.0 - 2.5, vertAng, 5, 1, 0, "Кол.");
      d.ksText((x6 + x7) / 2.0 - 2.5, headerH / 2.0 - 2.5, vertAng, 5, 1, 0, "Прим.");
      // горизонтальные заголовки
      d.ksText(x3 + colDesig / 2.0 - 15.0, headerH / 2.0 - 1.8, 0, 5, 1, 0, "Обозначение");
      d.ksText(x4 + colName / 2.0 - 17.0, headerH / 2.0 - 1.8, 0, 5, 1, 0, "Наименование");

      // ---- строки (растут вверх от шапки), текст в последней трети строки ----
      for (int i = 0; i < rows.Count; i++)
      {
        double yb = headerH + i * rowH;  // нижняя линия строки
        double ty = yb + 2.0;
        string[] r = rows[i];
        if (r[0].Length == 0) // раздел-заголовок: текст по центру графы «Наименование»
          CellText(d, x4, colName, ty, r[1], true);
        else
        {
          d.ksText(x2 + 0.5, ty, 0, 5, 1, 0, r[0]);               // Поз.
          CellText(d, x3, colDesig, ty, r[1], false);              // Обозначение
          CellText(d, x4, colName, ty, r[2], false);               // Наименование
          CellText(d, x5, colQty, ty, r[3], true);                 // Кол.
          if (r[4].Length > 0) CellText(d, x6, colNote, ty, r[4], false); // Прим.
        }
      }

      FillStamp(name, ToolRegistry.GetStr(a, "designation", null));
      d.ksSaveDocument(Tools2D.DocPath);

      Dictionary<string, object> res = new Dictionary<string, object>();
      res["saved"] = Tools2D.DocPath;
      res["items"] = created;
      res["rows"] = rows.Count;
      Dictionary<string, object> drObj = dr as Dictionary<string, object>;
      if (drObj != null && drObj.ContainsKey("viewNumber")) res["viewNumber"] = drObj["viewNumber"];
      // PNG для визуальной проверки
      string png = ToolRegistry.GetStr(a, "png", null);
      if (png != null && png.Length > 0)
      {
        RenderPng(d, png);
        res["png"] = png;
      }
      return res;
    }

    // Текст в ячейке: ширина символа ГОСТ-курсива ≈ 0.8h; длинные строки вжимаются
    // уменьшением кегля до 2.5. centered — по центру колонки, иначе с отступом 2 мм.
    static void CellText(ksDocument2D d, double colLeft, double colW, double y, string text, bool centered)
    {
      if (text == null || text.Length == 0) return;
      double h = 5.0;
      if (text.Length * 0.8 * h > colW - 4)
        h = Math.Max(2.5, (colW - 4) / (0.8 * text.Length));
      double x;
      if (centered) x = colLeft + (colW - text.Length * 0.8 * h) / 2.0;
      else x = colLeft + 2.0;
      d.ksText(x, y, 0, h, 1, 0, text);
    }

    static List<object> SectionOf(List<object> items, string sec)
    {
      List<object> r = new List<object>();
      foreach (object o in items)
      {
        Dictionary<string, object> it = o as Dictionary<string, object>;
        if (it == null) continue;
        if (ToolRegistry.GetStr(it, "section", "detali") == sec) r.Add(it);
      }
      return r;
    }

    // Растеризация текущего .cdw (идиома Tools2D.render_png)
    static void RenderPng(ksDocument2D d, string png)
    {
      object rObj = d.RasterFormatParam();
      ksRasterFormatParam rf = (ksRasterFormatParam)rObj;
      if (rf == null) throw new ToolException("RasterFormatParam вернул null");
      rf.format = 3; // PNG
      if (!rf.Init()) throw new ToolException("raster.Init вернул false");
      rf.extResolution = 300;
      png = System.IO.Path.GetFullPath(png);
      if (!d.SaveAsToRasterFormat(png, rObj)) throw new ToolException("SaveAsToRasterFormat вернул false");
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