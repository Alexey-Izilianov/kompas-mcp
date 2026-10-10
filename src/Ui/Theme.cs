// Палитра панели «Давинчи». Кисти создаются один раз; смена темы меняет только их
// цвета — все элементы, ссылающиеся на кисти, перерисовываются сами. Акцент — сангина
// (красный мел рисунков Леонардо), остальное — тёплые нейтральные тона.
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace KompasMcp.Ui
{
  public sealed class Theme
  {
    public readonly SolidColorBrush Window = new SolidColorBrush();
    public readonly SolidColorBrush Surface = new SolidColorBrush();     // поле ввода, код
    public readonly SolidColorBrush Border = new SolidColorBrush();
    public readonly SolidColorBrush FocusBorder = new SolidColorBrush();
    public readonly SolidColorBrush Text = new SolidColorBrush();
    public readonly SolidColorBrush Muted = new SolidColorBrush();
    public readonly SolidColorBrush Accent = new SolidColorBrush();
    public readonly SolidColorBrush AccentText = new SolidColorBrush();
    public readonly SolidColorBrush UserBubble = new SolidColorBrush();
    public readonly SolidColorBrush Hover = new SolidColorBrush();
    public readonly SolidColorBrush Pressed = new SolidColorBrush();
    public readonly SolidColorBrush Selection = new SolidColorBrush();
    public readonly SolidColorBrush ScrollThumb = new SolidColorBrush();
    public readonly SolidColorBrush Error = new SolidColorBrush();
    public readonly SolidColorBrush Ok = new SolidColorBrush();
    public readonly SolidColorBrush Idle = new SolidColorBrush();

    public bool Dark { get; private set; }

    public void Apply(bool dark)
    {
      Dark = dark;
      if (dark)
      {
        Set(Window, 0x1D1D1F); Set(Surface, 0x2A2A2D); Set(Border, 0x3A3A3E); Set(FocusBorder, 0x5A5A60);
        Set(Text, 0xECEAE6); Set(Muted, 0xA29E98); Set(Accent, 0xC06F55); Set(AccentText, 0xFFFFFF);
        Set(UserBubble, 0x3A2D29); Set(Hover, 0x313134); Set(Pressed, 0x3A3A3E); Set(Selection, 0x7A4A3B);
        Set(ScrollThumb, 0x4A4A50); Set(Error, 0xF2B8B5); Set(Ok, 0x6CC08B); Set(Idle, 0x6E6A65);
      }
      else
      {
        Set(Window, 0xFBFAF8); Set(Surface, 0xF2F0EC); Set(Border, 0xE2DFD9); Set(FocusBorder, 0xB9B4AC);
        Set(Text, 0x1F1D1A); Set(Muted, 0x75716B); Set(Accent, 0x9A4A33); Set(AccentText, 0xFFFFFF);
        Set(UserBubble, 0xF3E9E2); Set(Hover, 0xECE9E4); Set(Pressed, 0xE2DFD9); Set(Selection, 0xE7C9BC);
        Set(ScrollThumb, 0xCFCBC4); Set(Error, 0xB3261E); Set(Ok, 0x3F8A5C); Set(Idle, 0xA8A39C);
      }
      Refresh();
    }

    static void Set(SolidColorBrush b, int rgb)
    {
      b.Color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    // Ресурсы для шаблонов (DynamicResource в Styles). Только КОПИИ: кисть, найденную шаблоном
    // через ресурсы, WPF замораживает — живую кисть после этого перекрасить нельзя (падение при
    // смене темы). При смене темы копии заменяются новыми, DynamicResource подхватывает их сам.
    ResourceDictionary published;

    public void Publish(ResourceDictionary res)
    {
      published = res;
      Refresh();
    }

    void Refresh()
    {
      if (published == null) return;
      Put("Window", Window); Put("Surface", Surface); Put("Border", Border); Put("FocusBorder", FocusBorder);
      Put("Text", Text); Put("Muted", Muted); Put("Accent", Accent); Put("AccentText", AccentText);
      Put("Hover", Hover); Put("Pressed", Pressed); Put("Selection", Selection); Put("ScrollThumb", ScrollThumb);
    }

    void Put(string key, SolidColorBrush live)
    {
      SolidColorBrush copy = new SolidColorBrush(live.Color);
      copy.Freeze();
      published[key] = copy;
    }

    // ---- сохранённые настройки: HKCU\Software\KompasMcp\Davinci ----

    const string Key = @"Software\KompasMcp\Davinci";

    // Режим темы: "system" (как в Windows), "light", "dark".
    public static string LoadMode()
    {
      string saved = Read("Theme");
      return saved == "dark" || saved == "light" ? saved : "system";
    }

    public static void SaveMode(string mode) { Write("Theme", mode); }

    public static bool IsDarkMode(string mode)
    {
      return mode == "dark" || (mode != "light" && SystemDark());
    }

    public static bool LoadDark() { return IsDarkMode(LoadMode()); }

    // Тема приложений Windows
    static bool SystemDark()
    {
      try
      {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
          object v = k == null ? null : k.GetValue("AppsUseLightTheme");
          return v is int && (int)v == 0;
        }
      }
      catch { return false; }
    }

    // «Поверх всех окон» — по умолчанию выключено
    public static bool LoadPinned() { return Read("Pinned") == "1"; }
    public static void SavePinned(bool pinned) { Write("Pinned", pinned ? "1" : "0"); }

    public static Rect? LoadBounds()
    {
      string s = Read("Bounds");
      if (s == null) return null;
      string[] p = s.Split(';');
      double x, y, w, h;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      if (p.Length != 4 || !double.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out x)
          || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out y)
          || !double.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out w)
          || !double.TryParse(p[3], System.Globalization.NumberStyles.Float, inv, out h)) return null;
      return new Rect(x, y, w, h);
    }

    public static void SaveBounds(Rect r)
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      Write("Bounds", string.Join(";", new[] { r.X.ToString(inv), r.Y.ToString(inv), r.Width.ToString(inv), r.Height.ToString(inv) }));
    }

    static string Read(string name)
    {
      try
      {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
          return k == null ? null : k.GetValue(name) as string;
      }
      catch { return null; }
    }

    static void Write(string name, string value)
    {
      try
      {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
          k.SetValue(name, value);
      }
      catch (Exception e) { Log.Error("panel.settings", e); }
    }
  }
}
