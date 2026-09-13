using System;
using System.Collections.Generic;
using Kompas6API5;
using KompasAPI7;
using Kompas6Constants;
using Kompas6Constants3D;

namespace KompasMcp.Tools
{
  // 3D-домен. Идиомы — из проверенных flange.cs/part3d.cs и SDK-сэмпла Step3d1 (memory: kompas3d-automation):
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
""name"":{""type"":""string"",""description"":""Имя детали (в дереве)""},
""path"":{""type"":""string"",""description"":""Полный путь сохранения .m3d (по умолчанию kompas-test\\mcp-out\\part.m3d)""}}}",
        a => CreatePart(a));

      ToolRegistry.Add("open_part",
        "Открыть существующую деталь .m3d.",
        @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}},""required"":[""path""]}",
        a => OpenPart(ToolRegistry.GetStr(a, "path")));

      ToolRegistry.Add("sketch",
        "Создать эскиз на плоскости (plane: XOY/XOZ/YOZ, по умолчанию XOY). elements: [{type:'circle',xc,yc,r} | {type:'line',x1,y1,x2,y2} | {type:'arc',xc,yc,r,x1,y1,x2,y2,direction}]. Возвращает sketchId для операций.",
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
        "Вырезать выдавливанием. mode: 'through'=сквозное (в обе стороны), 'blind'=на глубину depth.",
        @"{""type"":""object"",""properties"":{
""sketchId"":{""type"":""integer"",""description"":""По умолчанию последний эскиз""},
""mode"":{""type"":""string"",""enum"":[""through"",""blind""],""description"":""По умолчанию through""},
""depth"":{""type"":""number"",""description"":""Для mode=blind""},
""direction"":{""type"":""integer"",""description"":""1=вперёд, -1=назад (для blind), по умолчанию 1""}},
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
          doc7.Close(DocumentCloseOptions.kdDoNotSaveChanges);
          doc3D = null; doc7 = null; part = null; partPath = null;
          lastSketch = null; sketches.Clear();
          return new Dictionary<string, object> { { "closed", true } };
        });

      ToolRegistry.Add("render_png_3d",
        "Отрендерить текущую 3D-деталь в растровый файл (API-7 документный SaveAsToRasterFormat). path: .png/.bmp/.jpg по расширению.",
        @"{""type"":""object"",""properties"":{
""path"":{""type"":""string"",""description"":""По умолчанию <деталь>.png""},
""resolution"":{""type"":""integer"",""description"":""DPI, по умолчанию 96""}}}",
        a => Render3D(a));
    }

    // ---- состояние ----

    internal static object CreatePart(Dictionary<string, object> a) { return CreatePartImpl(a); }

    internal static object SavePart(Dictionary<string, object> a)
    {
      string p = ToolRegistry.GetStr(a, "path", null);
      if (p == null) p = partPath;
      if (p == null) throw new ToolException("Нет пути: укажите path или создайте деталь через create_part");
      if (!System.IO.Path.IsPathRooted(p)) p = System.IO.Path.GetFullPath(p); // относительный SaveAs молча не сохраняет
      GetDoc7().SaveAs(p);
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
      string name = ToolRegistry.GetStr(a, "name", null);
      if (name != null) part.name = name;
      partPath = ToolRegistry.GetStr(a, "path", Paths.Out("part.m3d"));
      lastSketch = null;
      sketches.Clear();
      return new Dictionary<string, object> { { "created", true }, { "name", name } };
    }

    static object OpenPart(string path)
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
        op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutExtrusion);
        ksCutExtrusionDefinition def = (ksCutExtrusionDefinition)op.GetDefinition();
        string cutMode = ToolRegistry.GetStr(a, "mode", "through");
        bool through = cutMode == "through";
        endType = (short)(through ? End_Type.etThroughAll : End_Type.etBlind);
        if (!through) depth = ToolRegistry.GetDbl(a, "depth");
        // v22, проверено матрицей: сквозной вырез = side=false + dtBoth + etThroughAll
        // (вариант flange.cs v20 side=true + dtBoth теперь режет только глубину/2,
        //  dtNormal в обеих сторонах не режет ничего)
        if (through)
        {
          def.SetSideParam(false, endType, 0, 0, false);
          def.SetSketch(sk);
          def.directionType = (short)Direction_Type.dtBoth;
        }
        else
        {
          def.SetSideParam(forward, endType, depth, 0, false);
          def.SetSketch(sk);
          def.directionType = (short)Direction_Type.dtNormal;
        }
        if (!op.Create()) throw new ToolException("cutExtrusion.Create вернул 0");
        p.RebuildModel();
        return new Dictionary<string, object> { { "cut", true }, { "mass_kg", p.GetMass() } };
      }

      depth = ToolRegistry.GetDbl(a, "depth");
      bool baseOp = ToolRegistry.GetBool(a, "base", false);
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
      return new Dictionary<string, object> { { "boss", true }, { "mass_kg", p.GetMass() } };
    }

    internal static object Revolve(Dictionary<string, object> a, bool cut)
    {
      ksPart p = GetPart();
      ksEntity sk = GetSketch(a);
      double angle = ToolRegistry.GetDbl(a, "angle", 360);

      if (cut)
      {
        ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_cutRotated);
        ksCutRotatedDefinition def = (ksCutRotatedDefinition)op.GetDefinition();
        def.directionType = (short)Direction_Type.dtNormal;
        def.SetSideParam(true, angle);
        def.SetSketch(sk);
        if (!op.Create()) throw new ToolException("cutRotated.Create вернул 0");
      }
      else
      {
        ksEntity op = (ksEntity)p.NewEntity((short)Obj3dType.o3d_bossRotated);
        ksBossRotatedDefinition def = (ksBossRotatedDefinition)op.GetDefinition();
        def.directionType = (short)Direction_Type.dtNormal;
        def.SetSideParam(true, angle);
        def.SetSketch(sk);
        if (!op.Create()) throw new ToolException("bossRotated.Create вернул 0");
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
      d7.HideAllPlanes = true;
      d7.HideAllSketches = true;
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
      if (p == null)
      {
        string baseName = partPath ?? Paths.Out("part.m3d");
        p = System.IO.Path.ChangeExtension(baseName, ".png");
      }
      object parObj = d3.RasterFormatParam();
      ksRasterFormatParam par = (ksRasterFormatParam)parObj;
      par.Init();
      par.format = 3;   // FORMAT_PNG
      par.colorBPP = 24;
      par.extResolution = ToolRegistry.GetInt(a, "resolution", 96);
      par.colorType = 0;
      bool ok = d3.SaveAsToRasterFormat(p, parObj);
      d7.HideAllPlanes = false;
      d7.HideAllSketches = false;
      if (!ok) throw new ToolException("SaveAsToRasterFormat вернул false");
      return new Dictionary<string, object> { { "png", p } };
    }

    static object EdgeOp(Dictionary<string, object> a, bool fillet)
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