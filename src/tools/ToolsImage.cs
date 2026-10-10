using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KompasMcp.Tools
{
  // Просмотр изображений чертежей для модели: целиком с координатной сеткой или увеличенный
  // фрагмент. Клиент получает картинку как MCP-изображение. Мелкие размеры и выноски на скане
  // модель читает с ошибками, а на увеличенном фрагменте — уверенно.
  public static class ToolsImage
  {
    const int Grid = 10;

    public static void Register()
    {
      ToolRegistry.Add("image_view",
        "Показать изображение чертежа (png/jpg/bmp/tif/gif): без region — целиком с координатной сеткой 10×10 (подписи 0…1 по осям), с region — увеличенный фрагмент. Порядок чтения чертежа: сначала целиком, затем каждую область с размерами, выносками, надписями — увеличенно, пока все числа не прочитаны уверенно.",
        @"{""type"":""object"",""properties"":{
""path"":{""type"":""string"",""description"":""Файл изображения""},
""region"":{""type"":""array"",""items"":{""type"":""number""},""description"":""Фрагмент [x0, y0, x1, y1] в долях ширины и высоты (0…1, начало — левый верхний угол), как на сетке""},
""grid"":{""type"":""boolean"",""description"":""Наложить сетку (по умолчанию — только для целого изображения)""},
""size"":{""type"":""integer"",""description"":""Длинная сторона результата, пикселей (по умолчанию 1400, 400…2000)""}},
""required"":[""path""]}",
        a => View(a));
    }

    static object View(Dictionary<string, object> a)
    {
      string path = Paths.Abs(ToolRegistry.GetStr(a, "path"));
      if (!File.Exists(path)) throw new ToolException("Нет файла: " + path);
      double[] r = { 0, 0, 1, 1 };
      object o;
      List<object> reg = a.TryGetValue("region", out o) ? o as List<object> : null;
      if (reg != null)
      {
        if (reg.Count != 4) throw new ToolException("region: нужно [x0, y0, x1, y1]");
        for (int i = 0; i < 4; i++) r[i] = Math.Max(0, Math.Min(1, ToolRegistry.Number(reg[i])));
        if (r[2] - r[0] < 0.01 || r[3] - r[1] < 0.01) throw new ToolException("region: пустой или слишком малый фрагмент");
      }
      bool grid = ToolRegistry.GetBool(a, "grid", reg == null);
      int size = Math.Max(400, Math.Min(2000, ToolRegistry.GetInt(a, "size", 1400)));

      // WPF-рендер требует STA-поток; stdio-цикл в режиме панели работает не в нём
      byte[] png = null;
      string info = null;
      Exception error = null;
      Thread t = new Thread(() =>
      {
        try { png = Render(path, r, grid, size, out info); }
        catch (Exception e) { error = e; }
      });
      t.SetApartmentState(ApartmentState.STA);
      t.Start();
      t.Join();
      if (error != null) throw new ToolException("Не удалось прочитать изображение: " + error.Message);
      return ToolContent.Of(ToolContent.Image(png, "image/png"), ToolContent.Text(info));
    }

    static byte[] Render(string path, double[] r, bool grid, int size, out string info)
    {
      BitmapImage src = new BitmapImage();
      src.BeginInit();
      src.CacheOption = BitmapCacheOption.OnLoad;
      src.UriSource = new Uri(path);
      src.EndInit();
      int w = src.PixelWidth, h = src.PixelHeight;
      Int32Rect crop = new Int32Rect((int)(r[0] * w), (int)(r[1] * h),
        Math.Max(1, (int)((r[2] - r[0]) * w)), Math.Max(1, (int)((r[3] - r[1]) * h)));
      BitmapSource part = new CroppedBitmap(src, crop);

      double k = (double)size / Math.Max(crop.Width, crop.Height);
      int ow = (int)Math.Round(crop.Width * k), oh = (int)Math.Round(crop.Height * k);
      DrawingVisual dv = new DrawingVisual();
      RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
      using (DrawingContext dc = dv.RenderOpen())
      {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ow, oh));
        dc.DrawImage(part, new Rect(0, 0, ow, oh));
        if (grid) DrawGrid(dc, r, ow, oh);
      }
      RenderTargetBitmap rtb = new RenderTargetBitmap(ow, oh, 96, 96, PixelFormats.Pbgra32);
      rtb.Render(dv);
      PngBitmapEncoder enc = new PngBitmapEncoder();
      enc.Frames.Add(BitmapFrame.Create(rtb));
      using (MemoryStream ms = new MemoryStream())
      {
        enc.Save(ms);
        info = string.Format(CultureInfo.InvariantCulture,
          "Исходник {0}×{1} px; фрагмент [{2:0.###}, {3:0.###}, {4:0.###}, {5:0.###}] = {6}×{7} px, показан с увеличением ×{8:0.#}.{9}",
          w, h, r[0], r[1], r[2], r[3], crop.Width, crop.Height, k,
          k < 1.5 && Math.Max(crop.Width, crop.Height) < 900
            ? " Исходник мелкий: мелкие надписи могут не читаться — уточните сомнительные числа у пользователя."
            : "");
        return ms.ToArray();
      }
    }

    // Сетка в координатах исходного изображения (доли 0…1): линии через 0.1, подписи по краям.
    static void DrawGrid(DrawingContext dc, double[] r, int ow, int oh)
    {
      Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 220, 40, 40)), 1);
      Typeface tf = new Typeface("Segoe UI");
      for (int i = 0; i <= Grid; i++)
      {
        double v = (double)i / Grid;
        if (v >= r[0] && v <= r[2])
        {
          double x = (v - r[0]) / (r[2] - r[0]) * ow;
          dc.DrawLine(pen, new Point(x, 0), new Point(x, oh));
          Label(dc, tf, v, x + 2, 2);
        }
        if (v >= r[1] && v <= r[3])
        {
          double y = (v - r[1]) / (r[3] - r[1]) * oh;
          dc.DrawLine(pen, new Point(0, y), new Point(ow, y));
          Label(dc, tf, v, 2, y + 2);
        }
      }
    }

    static void Label(DrawingContext dc, Typeface tf, double v, double x, double y)
    {
      FormattedText ft = new FormattedText(v.ToString("0.0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, tf, 13, Brushes.White, 1.0);
      dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 200, 30, 30)), null, new Rect(x, y, ft.Width + 4, ft.Height));
      dc.DrawText(ft, new Point(x + 2, y));
    }
  }

  // Результат инструмента из нескольких элементов MCP (текст, изображения) — Rpc отдаёт его как есть.
  public class ToolContent
  {
    public readonly List<object> Items = new List<object>();

    public static ToolContent Of(params object[] items)
    {
      ToolContent c = new ToolContent();
      c.Items.AddRange(items);
      return c;
    }

    public static Dictionary<string, object> Text(string text)
    {
      return new Dictionary<string, object> { { "type", "text" }, { "text", text } };
    }

    public static Dictionary<string, object> Image(byte[] data, string mime)
    {
      return new Dictionary<string, object> { { "type", "image" }, { "data", Convert.ToBase64String(data) }, { "mimeType", mime } };
    }
  }
}
