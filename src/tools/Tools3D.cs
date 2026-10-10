using System;
using System.Collections.Generic;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using Kompas6Constants3D;

namespace KompasMcp.Tools
{
  // 3D-домен. Идиомы — из проверенных flange.cs/part3d.cs и SDK-сэмпла Step3d1:
  //  - документ детали только через API-7 Documents.Add(ksDocumentPart, true) (true = невидимо, false → null);
  //  - doc3D = kompas.ActiveDocument3D(), part = doc3D.GetPart(pTop_Part=-1) (НЕ pNew_Part);
  //  - эскиз: o3d_sketch + SetPlane + Create + BeginEdit/EndEdit;
  //  - выдавливание: o3d_baseExtrusion / o3d_bossExtrusion + SetSideParam(forward, etBlind/etThroughAll, depth, 0, false);
  //  - вырез: o3d_cutExtrusion + SetSideParam(false, etThroughAll, 0, 0, false) + dtBoth (v22: side=true режет глубину/2);
  //  - вращение: o3d_bossRotated + SetSideParam(true, угол) + SetSketch (Step3d1);
  //  - фаска/скругление: o3d_chamfer/o3d_fillet + SetChamferParam/radius + array() ← рёбра по точкам;
  //  - материал/масса: part.SetMaterial(name, плотность), part.GetMass(); затем part.RebuildModel().
  public static class Tools3D
  {
    static ksDocument3D doc3D;
    static IKompasDocument3D doc7;
    static ksPart part;
    static string partPath;
    static ksEntity lastSketch;                 // последний созданный эскиз
    internal static ksEntity LastSketch { get { return lastSketch; } }
    static readonly Dictionary<int, ksEntity> sketches = new Dictionary<int, ksEntity>();
    static int sketchSeq;

    // Сброс при Stop/Detach: поля держат COM-ссылки на закрытый документ.
    public static void Reset()
    {
      doc3D = null;
      doc7 = null;
      part = null;
      partPath = null;
      lastSketch = null;
      sketches.Clear();
      sketchSeq = 0;
    }

    public static void Register()
    {
      ToolRegistry.Add("create_part",
        "Создать новую деталь (3D). Становится активной для остальных 3D-tools.",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""Наименование детали""},
""designation"":{""type"":""string"",""description"":""Обозначение детали""},
""path"":{""type"":""string"",""description"":""Путь сохранения .m3d (по умолчанию mcp-out\\part.m3d рядом с сервером)""}}}",
        a => CreatePart(a));

      ToolRegistry.Add("open_part",
        "Открыть существующую деталь .m3d.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}},""required"":[""path""]}",
        a => OpenPart(ToolRegistry.GetStr(a, "path")));

      ToolRegistry.Add("sketch",
        "Создать эскиз на плоскости (plane: XOY/XOZ/YOZ, по умолчанию XOY). elements: [{type:'circle',xc,yc,r} | {type:'line',x1,y1,x2,y2} | {type:'arc',xc,yc,r,x1,y1,x2,y2,direction}]. Возвращает sketchId для операций. " +
        "Внимание: локальные оси эскиза на XOZ/YOZ повёрнуты относительно глобальных X/Y — при сомнениях используйте XOY со смещением (offset).",
        @"{""type"":""object"",""properties"":{
""plane"":{""type"":""string"",""enum"":[""XOY"",""XOZ"",""YOZ""],""description"":""По умолчанию XOY""},
""elements"":{""type"":""array"",""items"":{""type"":""object""}}},
""required"":[""elements""]}",
        a => Sketch(a));

      ToolRegistry.Add("extrude_boss",
        "Приклеить выдавливанием эскиз (sketchId; по умолчанию последний). depth — глубина, direction: 1=вперёд от плоскости, -1=назад.",
        @"{""type"":""object"",""properties"":{
""sketchId"":{""type"":""integer"",""description"":""По умолчанию последний эскиз""},
""depth"":{""type"":""number""},
""direction"":{""type"":""integer"",""description"":""По умолчанию 1""},
""base"":{""type"":""boolean"",""description"":""true = базовая операция (первая), false = приклеенная (по умолчанию)""}},
""required"":[""depth""]}",
        a => Extrude(a, false));

      ToolRegistry.Add("extrude_cut",
        "Вырезать выдавливанием. mode: 'through'=сквозное (в обе стороны), 'blind'=на глубину depth. " +
        "Сторона выреза подбирается автоматически (тул проверяет массу); если вырез не удалил материала — вернётся ошибка.",
        @"{""type"":""object"",""properties"":{
""sketchId"":{""type"":""integer"",""description"":""По умолчанию последний эскиз""},
""mode"":{""type"":""string"",""enum"":[""through"",""blind""],""description"":""По умолчанию through""},
""depth"":{""type"":""number"",""description"":""Для mode=blind""},
""direction"":{""type"":""integer"",""description"":""Подсказка стороны для blind (1/-1), по умолчанию 1 — при неудаче сторона подбирается автоматически""}},
""required"":[]}",
        a => Extrude(a, true));

      ToolRegistry.Add("revolve_boss",
        "Приклеить вращением эскиза вокруг оси. angle — угол в градусах (360 = полный).",
        @"{""type"":""object"",""properties"":{
""sketchId"":{""type"":""integer"",""description"":""По умолчанию последний эскиз""},
""angle"":{""type"":""number"",""description"":""По умолчанию 360""}},
""required"":[]}",
        a => Revolve(a, false));

      ToolRegistry.Add("revolve_cut",
        "Вырезать вращением эскиза.",
        @"{""type"":""object"",""properties"":{
""sketchId"":{""type"":""integer"",""description"":""По умолчанию последний эскиз""},
""angle"":{""type"":""number"",""description"":""По умолчанию 360""}}}",
        a => Revolve(a, true));

      ToolRegistry.Add("hole",
        "Простое отверстие: круг r в эскизе на плоскости и сквозной вырез в обе стороны (сахар для sketch+extrude_cut).",
        @"{""type"":""object"",""properties"":{
""plane"":{""type"":""string"",""enum"":[""XOY"",""XOZ"",""YOZ""],""description"":""По умолчанию XOY""},
""xc"":{""type"":""number""},""yc"":{""type"":""number""},""r"":{""type"":""number""}},
""required"":[""xc"",""yc"",""r""]}",
        a =>
        {
          Dictionary<string, object> sketchArgs = new Dictionary<string, object>();
          sketchArgs["plane"] = ToolRegistry.GetStr(a, "plane", "XOY");
          Dictionary<string, object> circle = new Dictionary<string, object>();
          circle["type"] = "circle";
          circle["xc"] = ToolRegistry.GetDbl(a, "xc");
          circle["yc"] = ToolRegistry.GetDbl(a, "yc");
          circle["r"] = ToolRegistry.GetDbl(a, "r");
          sketchArgs["elements"] = new List<object> { circle };
          object sk = Sketch(sketchArgs);
          Dictionary<string, object> dict = (Dictionary<string, object>)sk;
          Dictionary<string, object> cutArgs = new Dictionary<string, object>();
          cutArgs["sketchId"] = dict["sketchId"];
          cutArgs["mode"] = "through";
          return Extrude(cutArgs, true);
        });

      ToolRegistry.Add("fillet",
        "Скругление рёбер. Ребро выбирается точкой (x,y,z) в системе координат модели — можно несколько (points).",
        @"{""type"":""object"",""properties"":{
""radius"":{""type"":""number""},
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}},""description"":""[[x,y,z],...]""},
""tangent"":{""type"":""boolean"",""description"":""Распространить на касательные рёбра (по умолчанию false)""}},
""required"":[""radius"",""points""]}",
        a => EdgeOp(a, true));

      ToolRegistry.Add("chamfer",
        "Фаска на рёбрах. points — точки на рёбрах [[x,y,z],...]. length1/length2 — катеты.",
        @"{""type"":""object"",""properties"":{
""points"":{""type"":""array"",""items"":{""type"":""array"",""items"":{""type"":""number""}}},
""length1"":{""type"":""number"",""description"":""По умолчанию 1""},
""length2"":{""type"":""number"",""description"":""По умолчанию = length1""},
""tangent"":{""type"":""boolean"",""description"":""По умолчанию false""}},
""required"":[""points""]}",
        a => EdgeOp(a, false));

      ToolRegistry.Add("set_material",
        "Задать материал детали (имя, плотность кг/м³) и пересчитать.",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""напр. 'Сталь 45 ГОСТ 1050-88'""},
""density"":{""type"":""number"",""description"":""кг/м³, напр. 7850""}},
""required"":[""name"",""density""]}",
        a => SetMaterialOp(a));

      ToolRegistry.Add("mass_properties",
        "Масса и габариты детали (после Rebuild).",
        "{}",
        a => { GetPart().RebuildModel(); return MassDict(); });

      ToolRegistry.Add("rebuild",
        "Перестроить модель.",
        "{}",
        a => { GetPart().RebuildModel(); return new Dictionary<string, object> { { "rebuilt", true } }; });

      ToolRegistry.Add("save_part",
        "Сохранить деталь. path можно опустить — сохранит по пути создания/открытия.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
        a => SavePart(a));

      ToolRegistry.Add("close_part",
        "Закрыть деталь без сохранения.",
        "{}",
        a =>
        {
          if (doc7 == null) throw new ToolException("Нет открытой детали");
          CloseCurrentDoc();
          return new Dictionary<string, object> { { "closed", true } };
        });

      ToolRegistry.Add("render_png_3d",
        "Отрендерить текущую 3D-деталь/сборку в растровый файл (API-7 документный SaveAsToRasterFormat). path: .png/.bmp/.jpg по расширению, view: стандартный вид top/front/back/left/right/bottom (по умолчанию текущая аксонометрия).",
        @"{""type"":""object"",""properties"":{
""path"":{""type"":""string"",""description"":""По умолчанию <деталь>.png""},
""resolution"":{""type"":""integer"",""description"":""DPI, по умолчанию 96""},
""view"":{""type"":""string"",""description"":""Стандартный вид: top/front/back/left/right/bottom""}}}",
        a => Render3D(a));

      // ---- сборки (.a3d) ----

      ToolRegistry.Add("create_assembly",
        "Создать новую сборку (3D, .a3d). Становится активным документом для assembly_* и остальных 3D-tools.",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""Наименование сборки""},
""designation"":{""type"":""string"",""description"":""Обозначение сборки""},
""path"":{""type"":""string"",""description"":""Путь сохранения .a3d (по умолчанию mcp-out\\assembly.a3d рядом с сервером)""}}}",
        a => CreateAssembly(a));

      ToolRegistry.Add("open_assembly",
        "Открыть существующую сборку .a3d.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}},""required"":[""path""]}",
        a => OpenPart(ToolRegistry.GetStr(a, "path")));

      ToolRegistry.Add("assembly_add_component",
        "Вставить компонент (файл .m3d/.a3d) в текущую сборку. external: true=ссылкой на внешний файл, false=телом.",
        @"{""type"":""object"",""properties"":{
""path"":{""type"":""string"",""description"":""Файл компонента""},
""external"":{""type"":""boolean"",""description"":""По умолчанию false (телом)""}},
""required"":[""path""]}",
        a => AddComponent(a));

      ToolRegistry.Add("assembly_components",
        "Список компонентов текущей сборки.",
        "{}",
        a => ComponentsList());

      ToolRegistry.Add("assembly_place",
        "Разместить компонент сборки: положение (x,y,z) и поворот вокруг глобальной оси Z (град). Компонент — по name (как в assembly_components) или index.",
        @"{""type"":""object"",""properties"":{
""name"":{""type"":""string"",""description"":""Имя компонента (без index)""},
""index"":{""type"":""integer"",""description"":""Индекс компонента в сборке (с 0)""},
""x"":{""type"":""number""},""y"":{""type"":""number""},""z"":{""type"":""number""},
""angle"":{""type"":""number"",""description"":""Поворот вокруг Z, град. (по умолчанию 0)""}}}",
        a => PlaceComponent(a));
    }

    // ---- состояние ----

    internal static object CreatePart(Dictionary<string, object> a) { return CreatePartImpl(a); }

    // ---- сборки ----

    internal static object CreateAssembly(Dictionary<string, object> a)
    {
      IApplication app7 = KompasHost.App7;
      object docObj = app7.Documents.Add(DocumentTypeEnum.ksDocumentAssembly, true);
      if (docObj == null) throw new ToolException("Documents.Add(assembly) вернул null");
      doc7 = (IKompasDocument3D)docObj;
      doc3D = (ksDocument3D)KompasHost.Kompas.ActiveDocument3D();
      part = (ksPart)doc3D.GetPart((int)Part_Type.pTop_Part);
      if (part == null) throw new ToolException("GetPart(pTop_Part) вернул null для сборки");
      SetIdentity(a);
      partPath = ToolRegistry.GetStr(a, "path", Paths.Out("assembly.a3d"));
      lastSketch = null;
      sketches.Clear();
      sketchSeq = 0;
      Log.Write("create_assembly: " + part.name + " -> " + partPath);
      return new Dictionary<string, object> { { "assembly", part.name }, { "path", partPath } };
    }

    // Вставка компонента: возвращает сам компонент (ksPart) — нужен для placement.
    // Паттерн КОМПАС: сначала placeholder pNew_Part, затем SetPartFromFile на него,
    // после чего slot и есть вставленный компонент.
    internal static ksPart AddComponentImpl(string path, bool external)
    {
      if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.GetFullPath(path);
      if (!System.IO.File.Exists(path)) throw new ToolException("Файл компонента не найден: " + path);
      ksPart slot = (ksPart)GetDoc3D().GetPart((int)Part_Type.pNew_Part);
      if (slot == null) throw new ToolException("GetPart(pNew_Part) вернул null (не сборка?)");
      bool ok = GetDoc3D().SetPartFromFile(path, slot, external);
      if (!ok) throw new ToolException("SetPartFromFile вернул false: " + path);
      return slot;
    }

    static object AddComponent(Dictionary<string, object> a)
    {
      string path = ToolRegistry.GetStr(a, "path");
      if (path == null) throw new ToolException("Нет path (.m3d/.a3d компонента)");
      bool external = ToolRegistry.GetBool(a, "external", false);
      AddComponentImpl(path, external);
      try { GetPart().RebuildModel(); } catch (Exception e) { Log.Error("assembly rebuild", e); }
      Dictionary<string, object> res = (Dictionary<string, object>)ComponentsList();
      res["added"] = path;
      return res;
    }

    // ---- размещение компонентов сборки ----

    static ksPart FindComponent(Dictionary<string, object> a)
    {
      ksPartCollection col = (ksPartCollection)GetDoc3D().PartCollection(true);
      if (col == null) throw new ToolException("PartCollection вернул null");
      object nameObj;
      if (a.TryGetValue("name", out nameObj) && nameObj != null)
      {
        string name = ToolRegistry.GetStr(a, "name");
        ksPart p = (ksPart)col.GetByName(name, false, true);
        if (p == null) throw new ToolException("Компонент не найден по имени: " + name);
        return p;
      }
      int index = ToolRegistry.GetInt(a, "index", -1);
      if (index < 0 || index >= col.GetCount())
        throw new ToolException("Нет поля name или index вне диапазона 0.." + (col.GetCount() - 1));
      ksPart c = (ksPart)col.GetByIndex(index);
      if (c == null) throw new ToolException("GetByIndex(" + index + ") вернул null");
      return c;
    }

    internal static void PlacePart(ksPart c, double x, double y, double z, double angleDeg)
    {
      double ph = angleDeg * Math.PI / 180.0;
      PlacePartAxes(c, new[] { x, y, z },
        new[] { Math.Cos(ph), Math.Sin(ph), 0.0 }, new[] { -Math.Sin(ph), Math.Cos(ph), 0.0 });
    }

    // ox/oy — локальные оси OX/OY компонента в глобальных координатах (OZ = OX x OY).
    // Поочерёдный SetAxis годится только для поворота вокруг Z: при выводе OY из плоскости XY
    // КОМПАС перестраивает систему по старой OZ, поэтому обе оси задаются разом (SetAxes)
    // и сверяются чтением назад. GetAxis, вопреки справке, отдаёт точку «начало + орт».
    internal static void PlacePartAxes(ksPart c, double[] origin, double[] ox, double[] oy)
    {
      ksPlacement pl = (ksPlacement)c.GetPlacement();
      if (pl == null) throw new ToolException("GetPlacement вернул null");
      if (!pl.SetOrigin(origin[0], origin[1], origin[2])) throw new ToolException("SetOrigin вернул false");
      if (!pl.SetAxes(ox[0], ox[1], ox[2], oy[0], oy[1], oy[2])) throw new ToolException("SetAxes вернул false");
      if (!AxisIs(pl, 0, origin, ox) || !AxisIs(pl, 1, origin, oy))
        throw new ToolException("SetAxes: оси компонента не совпали с заданными");
      if (!c.UpdatePlacement()) throw new ToolException("UpdatePlacement вернул false");
    }

    static bool AxisIs(ksPlacement pl, int type, double[] origin, double[] v)
    {
      double x, y, z;
      pl.GetAxis(out x, out y, out z, type);
      return Math.Abs(x - origin[0] - v[0]) + Math.Abs(y - origin[1] - v[1]) + Math.Abs(z - origin[2] - v[2]) < 1e-6;
    }

    // Закрыть активный 3D-документ (деталь/сборку) и сбросить состояние.
    internal static void CloseCurrentDoc()
    {
      if (doc7 != null)
      {
        try { doc7.Close(DocumentCloseOptions.kdDoNotSaveChanges); }
        catch (Exception e) { Log.Error("close doc", e); }
      }
      doc7 = null; doc3D = null; part = null; partPath = null;
      lastSketch = null; sketches.Clear();
    }

    static object PlaceComponent(Dictionary<string, object> a)
    {
      ksPart c = FindComponent(a);
      PlacePart(c,
        ToolRegistry.GetDbl(a, "x", 0),
        ToolRegistry.GetDbl(a, "y", 0),
        ToolRegistry.GetDbl(a, "z", 0),
        ToolRegistry.GetDbl(a, "angle", 0));
      try { GetPart().RebuildModel(); } catch (Exception e) { Log.Error("assembly rebuild", e); }
      Dictionary<string, object> res = (Dictionary<string, object>)ComponentsList();
      string nm = "?";
      try { nm = c.name; } catch { }
      res["placed"] = nm;
      return res;
    }

    internal class ComponentInfo
    {
      public string Name, File, Marking;
      public double[] Min, Max;   // габарит в координатах сборки
    }

    // Компоненты открытой сборки с файлами и габаритами; min/max — габарит всей сборки
    internal static List<ComponentInfo> ComponentInfos(out double[] min, out double[] max)
    {
      ksPartCollection col = (ksPartCollection)GetDoc3D().PartCollection(true);
      if (col == null) throw new ToolException("PartCollection вернул null");
      double x1, y1, z1, x2, y2, z2;
      GetPart().GetGabarit(false, false, out x1, out y1, out z1, out x2, out y2, out z2);
      min = new[] { x1, y1, z1 };
      max = new[] { x2, y2, z2 };
      List<ComponentInfo> list = new List<ComponentInfo>();
      for (int i = 0; i < col.GetCount(); i++)
      {
        ksPart p = (ksPart)col.GetByIndex(i);
        p.GetGabarit(false, false, out x1, out y1, out z1, out x2, out y2, out z2);
        ComponentInfo c = new ComponentInfo { Min = new[] { x1, y1, z1 }, Max = new[] { x2, y2, z2 } };
        try { c.Name = p.name; } catch { c.Name = "?"; }
        try { c.File = p.fileName; } catch { c.File = ""; }
        try { c.Marking = p.marking; } catch { c.Marking = ""; }
        list.Add(c);
      }
      return list;
    }

    static object ComponentsList()
    {
      ksPartCollection col = (ksPartCollection)GetDoc3D().PartCollection(true);
      if (col == null) throw new ToolException("PartCollection вернул null");
      int n = col.GetCount();
      var list = new List<object>();
      for (int i = 0; i < n; i++)
      {
        ksPart p = (ksPart)col.GetByIndex(i);
        var info = new Dictionary<string, object>();
        try { info["name"] = p.name; }
        catch { info["name"] = "?"; }
        list.Add(info);
      }
      return new Dictionary<string, object> { { "count", n }, { "components", list } };
    }

    // Наименование и обозначение пишутся в документ только после part.Update() —
    // без него файл сохранялся с именем по умолчанию «Деталь».
    static void SetIdentity(Dictionary<string, object> a)
    {
      string name = ToolRegistry.GetStr(a, "name", null), designation = ToolRegistry.GetStr(a, "designation", null);
      if (name == null && designation == null) return;
      if (name != null) part.name = name;
      if (designation != null) part.marking = designation;
      part.Update();
    }

    internal static object SavePart(Dictionary<string, object> a)
    {
      string p = ToolRegistry.GetStr(a, "path", null);
      if (p == null) p = partPath;
      if (p == null) throw new ToolException("Нет пути: укажите path или создайте деталь через create_part");
      if (!System.IO.Path.IsPathRooted(p)) p = System.IO.Path.GetFullPath(p); // относительный SaveAs молча не сохраняет
      // в несуществующую папку SaveAs тоже молча ничего не пишет
      System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
      GetDoc7().SaveAs(p);
      if (!System.IO.File.Exists(p)) throw new ToolException("КОМПАС не сохранил файл: " + p);
      partPath = p;
      return new Dictionary<string, object> { { "saved", p } };
    }

    internal static object SetMaterialOp(Dictionary<string, object> a)
    {
      GetPart().SetMaterial(ToolRegistry.GetStr(a, "name"), ToolRegistry.GetDbl(a, "density"));
      GetPart().RebuildModel();
      return MassDict();
    }

    internal static string PartPath { get { return partPath; } }

    internal static double CurrentMass()
    {
      ksPart p = GetPart();
      double m = p.GetMass();
      if (m == 0) { p.RebuildModelEx(true); m = p.GetMass(); }
      return m;
    }

    static ksDocument3D GetDoc3D()
    {
      if (doc3D == null) throw new ToolException("Нет открытой детали — вызовите create_part или open_part");
      return doc3D;
    }

    internal static ksPart GetPart()
    {
      if (part == null) throw new ToolException("Нет открытой детали — вызовите create_part или open_part");
      return part;
    }

    static IKompasDocument3D GetDoc7()
    {
      if (doc7 == null) throw new ToolException("Нет открытой детали");
      return doc7;
    }

    static ksEntity GetSketch(Dictionary<string, object> a)
    {
      if (a.ContainsKey("sketchId") && a["sketchId"] != null)
      {
        int id = ToolRegistry.GetInt(a, "sketchId");
        ksEntity e;
        if (sketches.TryGetValue(id, out e)) return e;
        throw new ToolException("Нет эскиза с id=" + id);
      }
      if (lastSketch != null) return lastSketch;
      throw new ToolException("Нет эскизов — вызовите sketch");
    }

    internal static Dictionary<string, object> MassDict()
    {
      ksPart p = GetPart();
      var d = new Dictionary<string, object>();
      d["mass_kg"] = p.GetMass();
      return d;
    }

    // ---- инструменты ----

    static object CreatePartImpl(Dictionary<string, object> a)
    {
      IApplication app7 = KompasHost.App7;
      object docObj = app7.Documents.Add(DocumentTypeEnum.ksDocumentPart, true);
      if (docObj == null) throw new ToolException("Documents.Add вернул null (невидимое создание деталей должно идти с visible=true)");
      doc7 = (IKompasDocument3D)docObj;
      doc3D = (ksDocument3D)KompasHost.Kompas.ActiveDocument3D();
      part = (ksPart)doc3D.GetPart((int)Part_Type.pTop_Part);
      if (part == null) throw new ToolException("GetPart(pTop_Part) вернул null");
      SetIdentity(a);
      partPath = ToolRegistry.GetStr(a, "path", Paths.Out("part.m3d"));
      lastSketch = null;
      sketches.Clear();
      return new Dictionary<string, object> { { "created", true }, { "name", part.name } };
    }

    internal static object OpenPart(string path)
    {
      if (path == null || path.Length == 0) throw new ToolException("Нет path");
      if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.GetFullPath(path);
      IApplication app7 = KompasHost.App7;
      object docObj = app7.Documents.Open(path, false, false);
      if (docObj == null) throw new ToolException("Documents.Open вернул null: " + path);
      doc7 = (IKompasDocument3D)docObj;
      doc3D = (ksDocument3D)KompasHost.Kompas.ActiveDocument3D();
      part = (ksPart)doc3D.GetPart((int)Part_Type.pTop_Part);
      if (part == null) throw new ToolException("GetPart(pTop_Part) вернул null");
      partPath = path;
      lastSketch = null;
      sketches.Clear();
      return new Dictionary<string, object> { { "opened", path } };
    }

    internal static ksEntity PlaneByName(ksPart p, string plane)
    {
      short t;
      switch (plane)
      {
        case "XOZ": t = (short)Obj3dType.o3d_planeXOZ; break;
        case "YOZ": t = (short)Obj3dType.o3d_planeYOZ; break;
        default: t = (short)Obj3dType.o3d_planeXOY; break;
      }
      ksEntity e = (ksEntity)p.GetDefaultEntity(t);
      if (e == null) throw new ToolException("Не найдена плоскость " + plane);
      return e;
    }

    internal static object Sketch(Dictionary<string, object> a)
    {
      ksPart p = GetPart();
      ksEntity plane = PlaneByName(p, ToolRegistry.GetStr(a, "plane", "XOY"));
      object offObj;
      if (a.TryGetValue("offset", out offObj))
      {
        double off = ToolRegistry.GetDbl(a, "offset");
        ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_planeOffset);
        ksPlaneOffsetDefinition pd = (ksPlaneOffsetDefinition)op.GetDefinition();
        pd.SetPlane(plane);
        pd.direction = off >= 0;
        pd.offset = Math.Abs(off);
        if (!op.Create()) throw new ToolException("planeOffset.Create вернул 0");
        plane = op;
      }

      ksEntity sk = (ksEntity)p.NewEntity((short)Obj3dType.o3d_sketch);
      ksSketchDefinition skDef = (ksSketchDefinition)sk.GetDefinition();
      skDef.SetPlane(plane);
      sk.Create();
      ksDocument2D d2d = (ksDocument2D)skDef.BeginEdit();
      try
      {
        object elsObj;
        List<object> elements = a.TryGetValue("elements", out elsObj) ? elsObj as List<object> : null;
        if (elements == null || elements.Count == 0) throw new ToolException("Пустой elements");
        foreach (object elObj in elements)
        {
          Dictionary<string, object> el = elObj as Dictionary<string, object>;
          if (el == null) throw new ToolException("Элемент должен быть объектом");
          string type = ToolRegistry.GetStr(el, "type", "line");
          switch (type)
          {
            case "circle":
              d2d.ksCircle(ToolRegistry.GetDbl(el, "xc"), ToolRegistry.GetDbl(el, "yc"),
                ToolRegistry.GetDbl(el, "r"), 1);
              break;
            case "line":
              d2d.ksLineSeg(ToolRegistry.GetDbl(el, "x1"), ToolRegistry.GetDbl(el, "y1"),
                ToolRegistry.GetDbl(el, "x2"), ToolRegistry.GetDbl(el, "y2"), 1);
              break;
            case "arc":
              d2d.ksArcByPoint(ToolRegistry.GetDbl(el, "xc"), ToolRegistry.GetDbl(el, "yc"),
                ToolRegistry.GetDbl(el, "r"), ToolRegistry.GetDbl(el, "x1"), ToolRegistry.GetDbl(el, "y1"),
                ToolRegistry.GetDbl(el, "x2"), ToolRegistry.GetDbl(el, "y2"),
                (short)ToolRegistry.GetInt(el, "direction", 1), 1);
              break;
            default:
              throw new ToolException("Неизвестный элемент эскиза: " + type);
          }
        }
      }
      finally
      {
        skDef.EndEdit();
      }

      int id = ++sketchSeq;
      sketches[id] = sk;
      lastSketch = sk;
      return new Dictionary<string, object> { { "sketchId", id } };
    }

    // ---- программные построения для генераторов (зубчатые, крепёж) ----

    internal static ksEntity OffsetPlane(string basePlane, double off)
    {
      ksPart p = GetPart();
      ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_planeOffset);
      ksPlaneOffsetDefinition pd = (ksPlaneOffsetDefinition)op.GetDefinition();
      pd.SetPlane(PlaneByName(p, basePlane));
      pd.direction = off >= 0;
      pd.offset = Math.Abs(off);
      if (!op.Create()) throw new ToolException("planeOffset.Create вернул 0");
      return op;
    }

    // Эскиз из замкнутых ломаных; точки — в координатах эскиза.
    // На XOZ глобальная Z = −y эскиза (проверено габаритом), x эскиза = глобальная X.
    internal static ksEntity PolySketch(ksEntity plane, params IList<double[]>[] contours)
    {
      ksPart p = GetPart();
      ksEntity sk = (ksEntity)p.NewEntity((short)Obj3dType.o3d_sketch);
      ksSketchDefinition def = (ksSketchDefinition)sk.GetDefinition();
      def.SetPlane(plane);
      sk.Create();
      ksDocument2D d = (ksDocument2D)def.BeginEdit();
      try
      {
        foreach (IList<double[]> c in contours)
          for (int i = 0; i < c.Count; i++)
          {
            double[] a = c[i], b = c[(i + 1) % c.Count];
            d.ksLineSeg(a[0], a[1], b[0], b[1], 1);
          }
      }
      finally { def.EndEdit(); }
      lastSketch = sk;
      return sk;
    }

    // Тело или вырез вращения вокруг глобальной Z; профиль — замкнутая ломаная (r ≥ 0, z).
    // Эскиз на XOZ (y эскиза = −z) с осевой линией стиля 3 по оси вращения: без неё
    // bossRotated/cutRotated.Create возвращает 0.
    internal static object RevolveZ(IList<double[]> rz, bool cut)
    {
      ksPart p = GetPart();
      ksEntity sk = (ksEntity)p.NewEntity((short)Obj3dType.o3d_sketch);
      ksSketchDefinition def = (ksSketchDefinition)sk.GetDefinition();
      def.SetPlane(PlaneByName(p, "XOZ"));
      sk.Create();
      ksDocument2D d = (ksDocument2D)def.BeginEdit();
      double zMin = double.MaxValue, zMax = double.MinValue;
      try
      {
        for (int i = 0; i < rz.Count; i++)
        {
          double[] a = rz[i], b = rz[(i + 1) % rz.Count];
          d.ksLineSeg(a[0], -a[1], b[0], -b[1], 1);
          zMin = Math.Min(zMin, a[1]);
          zMax = Math.Max(zMax, a[1]);
        }
        d.ksLineSeg(0, -zMin + 1, 0, -zMax - 1, 3);
      }
      finally { def.EndEdit(); }
      lastSketch = sk;
      return Revolve(new Dictionary<string, object> { { "angle", 360.0 } }, cut);
    }

    // Тело вращения круга (xc, yc, r) эскиза на плоскости plane вокруг осевой линии эскиза
    // from→to на angle градусов. Сторону поворота задаёт reverse (направление осевой линии
    // на неё не влияет — проверено пробой).
    internal static void RevolveCircle(ksEntity plane, double xc, double yc, double r,
      double[] from, double[] to, double angle, bool reverse = false)
    {
      ksPart p = GetPart();
      ksEntity sk = (ksEntity)p.NewEntity((short)Obj3dType.o3d_sketch);
      ksSketchDefinition def = (ksSketchDefinition)sk.GetDefinition();
      def.SetPlane(plane);
      sk.Create();
      ksDocument2D d = (ksDocument2D)def.BeginEdit();
      try
      {
        d.ksCircle(xc, yc, r, 1);
        d.ksLineSeg(from[0], from[1], to[0], to[1], 3);
      }
      finally { def.EndEdit(); }
      lastSketch = sk;
      double mass0 = p.GetMass();
      ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossRotated);
      ksBossRotatedDefinition rd = (ksBossRotatedDefinition)op.GetDefinition();
      rd.directionType = (short)Direction_Type.dtNormal;
      rd.SetSideParam(!reverse, angle);
      rd.SetSketch(sk);
      if (!op.Create()) throw new ToolException("bossRotated.Create вернул 0");
      p.RebuildModel();
      if (p.GetMass() < mass0 + 1e-9) throw new ToolException("вращение ничего не добавило");
    }

    // Масса тела, построенного кинематикой: КОМПАС отдаёт 0, пока файл не переоткрыт —
    // тогда сохраняем, закрываем и открываем снова (документ остаётся активным).
    internal static double ReliableMass()
    {
      double m = CurrentMass();
      if (m > 0) return m;
      string p = partPath;
      SavePart(new Dictionary<string, object>());
      CloseCurrentDoc();
      OpenPart(p);
      return CurrentMass();
    }

    // Цилиндрическая спираль от плоскости plane вдоль её нормали, ось — через начало координат,
    // старт — точка (diam/2, 0). turnDir = true даёт правую винтовую линию (против часовой
    // при взгляде с конца нормали), false — левую: проверено пробой, справка SDK неоднозначна.
    internal static ksEntity Spiral(ksEntity plane, double diam, double step, double turns, bool rightHand)
    {
      ksEntity sp = (ksEntity)GetPart().NewEntity((short)Obj3dType.o3d_cylindricSpiral);
      ksCylindricSpiralDefinition def = (ksCylindricSpiralDefinition)sp.GetDefinition();
      def.SetPlane(plane);
      def.SetLocation(0, 0);
      def.buildMode = 0;   // по шагу и числу витков
      def.buildDir = true;
      def.turnDir = rightHand;
      def.step = step;
      def.turn = turns;
      def.diam = diam;
      if (!sp.Create()) throw new ToolException("cylindricSpiral.Create вернул 0");
      return sp;
    }

    // Кинематическая операция: профиль сохраняет угол с траекторией (винтовая поверхность по спирали).
    internal static ksEntity Evolution(ksEntity sketch, ksEntity path, bool cut)
    {
      ksPart p = GetPart();
      double mass0 = p.GetMass();
      ksEntity op;
      if (cut)
      {
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutEvolution);
        ksCutEvolutionDefinition def = (ksCutEvolutionDefinition)op.GetDefinition();
        def.cut = true;
        def.sketchShiftType = 1;
        def.SetSketch(sketch);
        ((ksEntityCollection)def.PathPartArray()).Add(path);
      }
      else
      {
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossEvolution);
        ksBossEvolutionDefinition def = (ksBossEvolutionDefinition)op.GetDefinition();
        def.sketchShiftType = 1;
        def.SetSketch(sketch);
        ((ksEntityCollection)def.PathPartArray()).Add(path);
      }
      if (!op.Create()) throw new ToolException((cut ? "cutEvolution" : "bossEvolution") + ".Create вернул 0");
      p.RebuildModel();
      if (cut && p.GetMass() >= mass0 - 1e-9)
        throw new ToolException("кинематический вырез ничего не удалил — профиль вне тела");
      return op;
    }

    // Элемент по сечениям; эскизы — в порядке следования вдоль тела.
    internal static ksEntity Loft(IList<ksEntity> sections, bool cut, bool baseOp)
    {
      ksPart p = GetPart();
      double mass0 = p.GetMass();
      ksEntity op;
      ksEntityCollection sks;
      if (cut)
      {
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutLoft);
        sks = (ksEntityCollection)((ksCutLoftDefinition)op.GetDefinition()).Sketchs();
      }
      else if (baseOp)
      {
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_baseLoft);
        sks = (ksEntityCollection)((ksBaseLoftDefinition)op.GetDefinition()).Sketchs();
      }
      else
      {
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossLoft);
        sks = (ksEntityCollection)((ksBossLoftDefinition)op.GetDefinition()).Sketchs();
      }
      foreach (ksEntity s in sections) sks.Add(s);
      if (!op.Create()) throw new ToolException((cut ? "cutLoft" : "loft") + ".Create вернул 0");
      p.RebuildModel();
      double mass = p.GetMass();
      if (cut ? mass >= mass0 - 1e-9 : mass <= mass0 + 1e-9)
        throw new ToolException("операция по сечениям не изменила тело");
      return op;
    }

    // Круговой массив операции вокруг глобальной оси Z: count экземпляров с равным шагом.
    internal static void CircularCopyZ(ksEntity operation, int count)
    {
      if (count < 2) return;
      ksPart p = GetPart();
      ksEntity arr = (ksEntity)p.NewEntity((short)Obj3dType.o3d_circularCopy);
      ksCircularCopyDefinition def = (ksCircularCopyDefinition)arr.GetDefinition();
      if (!def.SetAxis(p.GetDefaultEntity((short)Obj3dType.o3d_axisOZ)))
        throw new ToolException("circularCopy.SetAxis вернул false");
      def.count1 = 1;
      def.SetCopyParamAlongDir(count, 360.0 / count, false, false);
      ((ksEntityCollection)def.GetOperationArray()).Add(operation);
      if (!arr.Create()) throw new ToolException("circularCopy.Create вернул 0");
      p.RebuildModel();
    }

    internal static object Extrude(Dictionary<string, object> a, bool cut)
    {
      ksPart p = GetPart();
      ksEntity sk = GetSketch(a);
      ksEntity op;
      short endType;
      double depth = 0;
      bool forward = ToolRegistry.GetInt(a, "direction", 1) > 0;

      if (cut)
      {
        string cutMode = ToolRegistry.GetStr(a, "mode", "through");
        bool through = cutMode == "through";
        endType = (short)(through ? End_Type.etThroughAll : End_Type.etBlind);
        if (!through) depth = ToolRegistry.GetDbl(a, "depth");
        // v22: вырез капризен к стороне и типу направления — Create возвращает 1,
        // но материал не трогается (dtNormal+blind уводит вырез "в никуда").
        // Проверено матрицей: сквозной надёжно работает как side=false + dtBoth.
        // Стратегия: перебор вариантов с контролем массы; пустой вырез = не успех.
        int[,] variants = through
          ? new int[,] { { 0, (short)Direction_Type.dtBoth }, { 1, (short)Direction_Type.dtBoth } }
          : new int[,] { { forward ? 1 : 0, (short)Direction_Type.dtNormal },
                         { forward ? 0 : 1, (short)Direction_Type.dtNormal },
                         { 1, (short)Direction_Type.dtBoth } };
        double mass0 = p.GetMass();
        bool done = false;
        for (int i = 0; i < variants.GetLength(0) && !done; i++)
        {
          op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutExtrusion);
          ksCutExtrusionDefinition def = (ksCutExtrusionDefinition)op.GetDefinition();
          def.SetSideParam(variants[i, 0] == 1, endType, depth, 0, false);
          def.SetSketch(sk);
          def.directionType = (short)variants[i, 1];
          if (!op.Create()) continue;
          p.RebuildModel();
          if (p.GetMass() < mass0 - 1e-9) done = true; // материал реально убран
        }
        if (!done)
          throw new ToolException("вырез ничего не удалил (масса не изменилась): " +
            "эскиз, вероятно, вне материала или параллелен телу — переместите эскиз на тело");
        return new Dictionary<string, object> { { "cut", true }, { "mass_kg", p.GetMass() } };
      }

      depth = ToolRegistry.GetDbl(a, "depth");
      bool baseOp = ToolRegistry.GetBool(a, "base", false);
      double massBefore = p.GetMass();
      op = (ksEntity)p.NewEntity((short)(baseOp ? Obj3dType.o3d_baseExtrusion : Obj3dType.o3d_bossExtrusion));
      // определения не взаимозаменяемы: bossExtrusion отдаёт ksBossExtrusionDefinition,
      // QI к ksBaseExtrusionDefinition падает (E_NOINTERFACE)
      // directionType=dtReverse на base/bossExtrusion валит Create (проверено) -
      // выдавливание назад не поддерживаем: эскиз на смещённой плоскости (offset) + прямое выдавливание
      if (!forward) throw new ToolException("direction=-1 не поддерживается; используйте sketch с offset и direction=1");
      if (baseOp)
      {
        ksBaseExtrusionDefinition bdef = (ksBaseExtrusionDefinition)op.GetDefinition();
        bdef.SetSideParam(forward, (short)End_Type.etBlind, depth, 0, false);
        bdef.SetSketch(sk);
      }
      else
      {
        ksBossExtrusionDefinition bdef = (ksBossExtrusionDefinition)op.GetDefinition();
        bdef.SetSideParam(forward, (short)End_Type.etBlind, depth, 0, false);
        bdef.SetSketch(sk);
      }
      if (!op.Create()) throw new ToolException("extrusion.Create вернул 0");
      p.RebuildModel();
      double massNow = p.GetMass();
      if (massNow < massBefore + 1e-9)
        throw new ToolException("выдавливание ничего не добавило (масса не изменилась) — эскиз, вероятно, уже совпадает с телом");
      return new Dictionary<string, object> { { "boss", true }, { "mass_kg", massNow } };
    }

    internal static object Revolve(Dictionary<string, object> a, bool cut)
    {
      ksPart p = GetPart();
      ksEntity sk = GetSketch(a);
      double angle = ToolRegistry.GetDbl(a, "angle", 360);

      if (cut)
      {
        double mass0 = p.GetMass();
        bool done = false;
        for (int i = 0; i < 2 && !done; i++)
        {
          ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutRotated);
          ksCutRotatedDefinition def = (ksCutRotatedDefinition)op.GetDefinition();
          def.directionType = (short)Direction_Type.dtNormal;
          def.SetSideParam(i == 0, angle);
          def.SetSketch(sk);
          if (!op.Create()) continue;
          p.RebuildModel();
          if (p.GetMass() < mass0 - 1e-9) done = true;
        }
        if (!done)
          throw new ToolException("вырез вращением ничего не удалил (масса не изменилась) — проверьте расположение эскиза");
      }
      else
      {
        double mass0 = p.GetMass();
        ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossRotated);
        ksBossRotatedDefinition def = (ksBossRotatedDefinition)op.GetDefinition();
        def.directionType = (short)Direction_Type.dtNormal;
        def.SetSideParam(true, angle);
        def.SetSketch(sk);
        if (!op.Create()) throw new ToolException("bossRotated.Create вернул 0");
        p.RebuildModel();
        if (p.GetMass() < mass0 + 1e-9)
          throw new ToolException("операция вращения ничего не добавила (масса не изменилась)");
      }
      p.RebuildModel();
      var res = new Dictionary<string, object>();
      res[cut ? "cutRotated" : "bossRotated"] = true;
      res["mass_kg"] = p.GetMass();
      return res;
    }

    // Выбор рёбер точками: для каждой точки — свой EntityCollection(o3d_edge).SelectByPoint.
    static List<ksEntity> PickEdges(ksPart p, List<object> points)
    {
      var picked = new List<ksEntity>();
      foreach (object ptObj in points)
      {
        List<object> pt = ptObj as List<object>;
        if (pt == null || pt.Count < 3) throw new ToolException("Точка ребра должна быть тройкой [x,y,z]");
        ksEntityCollection edges = (ksEntityCollection)p.EntityCollection((short)Obj3dType.o3d_edge);
        if (edges == null) throw new ToolException("EntityCollection(o3d_edge) вернул null");
        if (edges.SelectByPoint(ToDbl(pt[0]), ToDbl(pt[1]), ToDbl(pt[2])))
        {
          for (int i = 0; i < edges.GetCount(); i++)
            picked.Add((ksEntity)edges.GetByIndex(i));
        }
      }
      if (picked.Count == 0) throw new ToolException("Ни одного ребра не выбрано по точкам (проверьте координаты)");
      return picked;
    }

    static double ToDbl(object o)
    {
      if (o is double) return (double)o;
      if (o is int) return (int)o;
      if (o is long) return (long)o;
      double d;
      if (double.TryParse(o.ToString(), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
      throw new ToolException("Не число: " + o);
    }

    // Рецепт рендера (render_test.cs, вариант A работает даже в невидимом приложении):
    // скрыть плоскости/эскизы, активировать фрейм + ZoomAll + RefreshWindow + пауза,
    // затем API-5 ksDocument3D.SaveAsToRasterFormat (format=3 PNG). Без активации фрейма растр белый.
    static object Render3D(Dictionary<string, object> a)
    {
      ksDocument3D d3 = GetDoc3D();
      IKompasDocument3D d7 = GetDoc7();
      // вся вспомогательная геометрия: плоскости, эскизы, спирали-траектории, начало координат;
      // HideInComponentsMode переключает действие флагов на вставки сборки — прячем и там, и там
      HideAux(d7, true);
      DocumentFrames frames = d7.DocumentFrames;
      if (frames.Count > 0)
      {
        IDocumentFrame frame = (IDocumentFrame)frames[0];
        frame.Active = true;
        frame.ZoomPrevNextOrAll(ZoomTypeEnum.ksZoomAll);
        frame.RefreshWindow();
        System.Threading.Thread.Sleep(1000);
      }

      string p = ToolRegistry.GetStr(a, "path", null);
      if (p == null) p = System.IO.Path.ChangeExtension(partPath ?? Paths.Out("part.m3d"), ".png");
      // Грабля: КОМПАС резолвит относительные пути от СВОЕГО cwd —
      // растр уезжает непонятно куда. Только абсолютные пути.
      if (!System.IO.Path.IsPathRooted(p)) p = System.IO.Path.GetFullPath(p);

      // необязательный стандартный вид: top/front/back/left/right/bottom —
      // проекция на соответствующую плоскость модели (вид — экранная система
      // координат: экранная X и экранная Y как векторы в координатах модели).
      // По умолчанию рендерится текущая (аксонометрия).
      string view = ToolRegistry.GetStr(a, "view", null);
      if (view != null)
      {
        double x1 = 0, y1 = 0, z1 = 0, x2 = 0, y2 = 0, z2 = 0;
        switch (view.ToLower())
        {
          case "top": x1 = 1; y2 = 1; break;       // экран = (X, Y), взгляд вдоль -Z
          case "bottom": x1 = 1; y2 = -1; break;
          case "front": x1 = 1; z2 = 1; break;     // экран = (X, Z)
          case "back": x1 = -1; z2 = 1; break;
          case "left": y1 = 1; z2 = 1; break;      // экран = (Y, Z)
          case "right": y1 = -1; z2 = 1; break;
          default: throw new ToolException("Неизвестный вид: " + view + " (top/front/back/left/right/bottom)");
        }
        ksViewProjectionCollection vpCol = (ksViewProjectionCollection)d3.GetViewProjectionCollection();
        object vpObj = vpCol != null ? vpCol.NewViewProjection() : null;
        ksViewProjection vp = (ksViewProjection)vpObj;
        if (vp == null) throw new ToolException("NewViewProjection вернул null");
        ksPlacement vpPl = (ksPlacement)vp.GetPlacement();
        if (vpPl == null) throw new ToolException("viewProjection.GetPlacement вернул null");
        vpPl.SetOrigin(0, 0, 0);
        vpPl.SetAxis(x1, y1, z1, 0);
        vpPl.SetAxis(x2, y2, z2, 1);
        if (!vp.SetCurrent()) throw new ToolException("viewProjection.SetCurrent вернул false");
        try { GetPart().RebuildModel(); } catch (Exception e) { Log.Error("render view rebuild", e); }
      }
      object parObj = d3.RasterFormatParam();
      ksRasterFormatParam par = (ksRasterFormatParam)parObj;
      par.Init();
      par.format = 3;   // FORMAT_PNG
      par.colorBPP = 24;
      par.extResolution = ToolRegistry.GetInt(a, "resolution", 96);
      par.colorType = 0;
      bool ok = d3.SaveAsToRasterFormat(p, parObj);
      HideAux(d7, false);
      if (!ok) throw new ToolException("SaveAsToRasterFormat вернул false");
      return new Dictionary<string, object> { { "png", p } };
    }

    static void HideAux(IKompasDocument3D d7, bool hide)
    {
      d7.HideInComponentsMode = true;
      d7.HideAllAuxiliaryGeom = hide;
      d7.HideInComponentsMode = false;
      d7.HideAllAuxiliaryGeom = hide;
    }

    internal static object EdgeOp(Dictionary<string, object> a, bool fillet)
    {
      ksPart p = GetPart();
      object ptsObj;
      List<object> points = a.TryGetValue("points", out ptsObj) ? ptsObj as List<object> : null;
      if (points == null || points.Count == 0) throw new ToolException("Нет points");
      List<ksEntity> edges = PickEdges(p, points);

      ksEntity op = (ksEntity)p.NewEntity((short)(fillet ? Obj3dType.o3d_fillet : Obj3dType.o3d_chamfer));
      if (fillet)
      {
        ksFilletDefinition def = (ksFilletDefinition)op.GetDefinition();
        def.radius = ToolRegistry.GetDbl(a, "radius");
        def.tangent = ToolRegistry.GetBool(a, "tangent", false);
        ksEntityCollection arr = (ksEntityCollection)def.array();
        foreach (ksEntity e in edges) arr.Add(e);
        if (!op.Create()) throw new ToolException("fillet.Create вернул 0");
      }
      else
      {
        ksChamferDefinition def = (ksChamferDefinition)op.GetDefinition();
        def.tangent = ToolRegistry.GetBool(a, "tangent", false);
        double l1 = ToolRegistry.GetDbl(a, "length1", 1);
        def.SetChamferParam(true, l1, ToolRegistry.GetDbl(a, "length2", l1));
        ksEntityCollection arr = (ksEntityCollection)def.array();
        foreach (ksEntity e in edges) arr.Add(e);
        if (!op.Create()) throw new ToolException("chamfer.Create вернул 0");
      }
      p.RebuildModel();
      var res = new Dictionary<string, object>();
      res[fillet ? "fillet" : "chamfer"] = true;
      res["edges"] = edges.Count;
      res["mass_kg"] = p.GetMass();
      return res;
    }
  }
}