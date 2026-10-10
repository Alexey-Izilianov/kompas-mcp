// Markdown ответа модели → FlowDocument: заголовки, абзацы, вложенные списки, цитаты,
// блоки кода, таблицы, горизонтальная черта; в строке — жирный, курсив, `код`, ссылки.
// Пути к файлам (C:\…\деталь.m3d) становятся ссылками: щелчок открывает файл.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KompasMcp.Ui
{
  static class Markdown
  {
    public static readonly FontFamily UiFont = new FontFamily("Segoe UI");
    public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas");

    static readonly Regex ListRe = new Regex(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$");
    static readonly Regex HeadingRe = new Regex(@"^(#{1,6})\s+(.*?)\s*#*$");
    static readonly Regex RuleRe = new Regex(@"^(\*{3,}|-{3,}|_{3,})$");
    static readonly Regex TableSepRe = new Regex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$");
    const string PathPattern = @"[A-Za-z]:[\\/][^\s""'<>|*?`]+?\.(?:cdw|m3d|a3d|frw|spw|kdw|png|jpg|jpeg|bmp|dxf|pdf|txt|md|json|csv|xlsx|docx)";
    static readonly Regex PathRe = new Regex("^" + PathPattern + "$", RegexOptions.IgnoreCase);
    // 1 `код` | 2 **жирный** | 3 __жирный__ | 4 *курсив* | 5 _курсив_ | 6,7 [текст](адрес) | 8 путь к файлу
    static readonly Regex InlineRe = new Regex(
      @"`([^`]+)`|\*\*(.+?)\*\*|__(.+?)__|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)" +
      @"|\[([^\]]+)\]\(([^)\s]+)\)|(" + PathPattern + @")(?=[\s.,;:!?)\]»]|$)",
      RegexOptions.IgnoreCase);

    public static FlowDocument Build(string md, Theme t)
    {
      FlowDocument doc = new FlowDocument();
      doc.PagePadding = new Thickness(0);
      doc.FontFamily = UiFont;
      doc.FontSize = 14;
      doc.LineHeight = 21;
      doc.Foreground = t.Text;
      doc.TextAlignment = TextAlignment.Left;

      string[] lines = (md ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
      int i = 0;
      while (i < lines.Length)
      {
        string trimmed = lines[i].Trim();
        if (trimmed.Length == 0) { i++; continue; }

        if (trimmed.StartsWith("```"))
        {
          List<string> code = new List<string>();
          i++;
          while (i < lines.Length && !lines[i].Trim().StartsWith("```")) code.Add(lines[i++]);
          i++; // закрывающая ```
          doc.Blocks.Add(CodeBlock(string.Join("\n", code.ToArray()), t));
          continue;
        }

        Match h = HeadingRe.Match(trimmed);
        if (h.Success)
        {
          int level = h.Groups[1].Value.Length;
          Paragraph p = new Paragraph();
          p.FontWeight = FontWeights.SemiBold;
          p.FontSize = level == 1 ? 17 : level == 2 ? 15.5 : 14.5;
          p.Margin = new Thickness(0, 10, 0, 4);
          AddInlines(p.Inlines, h.Groups[2].Value, t);
          doc.Blocks.Add(p);
          i++;
          continue;
        }

        if (RuleRe.IsMatch(trimmed))
        {
          doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = t.Border, Margin = new Thickness(0, 6, 0, 6) }));
          i++;
          continue;
        }

        if (trimmed.StartsWith(">"))
        {
          Paragraph q = new Paragraph();
          q.Foreground = t.Muted;
          q.BorderBrush = t.Border;
          q.BorderThickness = new Thickness(3, 0, 0, 0);
          q.Padding = new Thickness(10, 1, 0, 1);
          q.Margin = new Thickness(0, 0, 0, 8);
          bool first = true;
          while (i < lines.Length && lines[i].Trim().StartsWith(">"))
          {
            if (!first) q.Inlines.Add(new LineBreak());
            AddInlines(q.Inlines, lines[i].Trim().Substring(1).TrimStart(), t);
            first = false;
            i++;
          }
          doc.Blocks.Add(q);
          continue;
        }

        if (trimmed.StartsWith("|") && i + 1 < lines.Length && TableSepRe.IsMatch(lines[i + 1].Trim()))
        {
          doc.Blocks.Add(TableBlock(lines, ref i, t));
          continue;
        }

        if (ListRe.IsMatch(lines[i]))
        {
          doc.Blocks.Add(ListBlock(lines, ref i, t));
          continue;
        }

        // абзац: строки до пустой или до начала другого блока; переводы строк сохраняются
        Paragraph para = new Paragraph();
        para.Margin = new Thickness(0, 0, 0, 8);
        bool firstLine = true;
        while (i < lines.Length)
        {
          string s = lines[i].Trim();
          if (s.Length == 0 || s.StartsWith("```") || s.StartsWith(">") || HeadingRe.IsMatch(s) || RuleRe.IsMatch(s)
              || ListRe.IsMatch(lines[i]) || (s.StartsWith("|") && i + 1 < lines.Length && TableSepRe.IsMatch(lines[i + 1].Trim())))
            break;
          if (!firstLine) para.Inlines.Add(new LineBreak());
          AddInlines(para.Inlines, s, t);
          firstLine = false;
          i++;
        }
        doc.Blocks.Add(para);
      }
      // последний блок без нижнего отступа — сообщение не «висит» над следующим
      if (doc.Blocks.LastBlock != null) doc.Blocks.LastBlock.Margin = new Thickness(
        doc.Blocks.LastBlock.Margin.Left, doc.Blocks.LastBlock.Margin.Top, doc.Blocks.LastBlock.Margin.Right, 0);
      return doc;
    }

    static Block CodeBlock(string code, Theme t)
    {
      Paragraph p = new Paragraph(new Run(code));
      p.FontFamily = MonoFont;
      p.FontSize = 12.5;
      p.LineHeight = 18;
      p.Background = t.Surface;
      p.Padding = new Thickness(10, 8, 10, 8);
      p.Margin = new Thickness(0, 2, 0, 10);
      return p;
    }

    // Список с вложенностью по отступу; строки-продолжения с отступом дописываются к пункту.
    static Block ListBlock(string[] lines, ref int i, Theme t)
    {
      List<KeyValuePair<int, List>> stack = new List<KeyValuePair<int, List>>();
      List root = null;
      Paragraph lastPara = null;
      while (i < lines.Length)
      {
        Match m = ListRe.Match(lines[i]);
        if (!m.Success)
        {
          string s = lines[i];
          if (lastPara != null && s.Trim().Length > 0 && (s.StartsWith("  ") || s.StartsWith("\t")))
          {
            lastPara.Inlines.Add(new LineBreak());
            AddInlines(lastPara.Inlines, s.Trim(), t);
            i++;
            continue;
          }
          break;
        }
        int indent = m.Groups[1].Value.Replace("\t", "    ").Length;
        string marker = m.Groups[2].Value;
        bool ordered = char.IsDigit(marker[0]);
        while (stack.Count > 0 && indent < stack[stack.Count - 1].Key) stack.RemoveAt(stack.Count - 1);
        if (stack.Count == 0 || indent > stack[stack.Count - 1].Key)
        {
          List list = new List();
          list.MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
          list.Padding = new Thickness(20, 0, 0, 0);
          list.Margin = new Thickness(0, 0, 0, stack.Count == 0 ? 8 : 2);
          int start;
          if (ordered && int.TryParse(marker.TrimEnd('.', ')'), out start) && start > 1) list.StartIndex = start;
          if (stack.Count == 0) root = list;
          else
          {
            List parent = stack[stack.Count - 1].Value;
            if (parent.ListItems.Count == 0) parent.ListItems.Add(new ListItem());
            parent.ListItems.LastListItem.Blocks.Add(list);
          }
          stack.Add(new KeyValuePair<int, List>(indent, list));
        }
        lastPara = new Paragraph();
        lastPara.Margin = new Thickness(0, 0, 0, 3);
        AddInlines(lastPara.Inlines, m.Groups[3].Value, t);
        stack[stack.Count - 1].Value.ListItems.Add(new ListItem(lastPara));
        i++;
      }
      return root;
    }

    static Block TableBlock(string[] lines, ref int i, Theme t)
    {
      string[] head = Cells(lines[i]);
      i += 2; // заголовок и разделитель
      Table table = new Table();
      table.CellSpacing = 0;
      table.Margin = new Thickness(0, 2, 0, 10);
      table.FontSize = 13;
      for (int c = 0; c < head.Length; c++) table.Columns.Add(new TableColumn());
      TableRowGroup group = new TableRowGroup();
      table.RowGroups.Add(group);
      group.Rows.Add(Row(head, t, true));
      while (i < lines.Length && lines[i].Trim().StartsWith("|"))
      {
        string[] cells = Cells(lines[i]);
        string[] row = new string[head.Length];
        for (int c = 0; c < row.Length; c++) row[c] = c < cells.Length ? cells[c] : "";
        group.Rows.Add(Row(row, t, false));
        i++;
      }
      return table;
    }

    static TableRow Row(string[] cells, Theme t, bool header)
    {
      TableRow row = new TableRow();
      foreach (string text in cells)
      {
        Paragraph p = new Paragraph();
        p.Margin = new Thickness(0);
        if (header) p.FontWeight = FontWeights.SemiBold;
        AddInlines(p.Inlines, text, t);
        TableCell cell = new TableCell(p);
        cell.Padding = new Thickness(0, 4, 12, 4);
        cell.BorderBrush = t.Border;
        cell.BorderThickness = new Thickness(0, 0, 0, 1);
        row.Cells.Add(cell);
      }
      return row;
    }

    static string[] Cells(string line)
    {
      string s = line.Trim();
      if (s.StartsWith("|")) s = s.Substring(1);
      if (s.EndsWith("|")) s = s.Substring(0, s.Length - 1);
      string[] parts = s.Split('|');
      for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim();
      return parts;
    }

    // ---- строчная разметка ----

    static void AddInlines(InlineCollection target, string text, Theme t)
    {
      int pos = 0;
      foreach (Match m in InlineRe.Matches(text))
      {
        if (m.Index > pos) target.Add(new Run(text.Substring(pos, m.Index - pos)));
        pos = m.Index + m.Length;
        if (m.Groups[1].Success)
        {
          string code = m.Groups[1].Value;
          if (PathRe.IsMatch(code)) target.Add(FileLink(code, t, true));
          else target.Add(CodeRun(code, t));
        }
        else if (m.Groups[2].Success || m.Groups[3].Success)
        {
          Bold b = new Bold();
          AddInlines(b.Inlines, m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value, t);
          target.Add(b);
        }
        else if (m.Groups[4].Success || m.Groups[5].Success)
        {
          Italic it = new Italic();
          AddInlines(it.Inlines, m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value, t);
          target.Add(it);
        }
        else if (m.Groups[6].Success) target.Add(Link(m.Groups[6].Value, m.Groups[7].Value, t, false));
        else if (m.Groups[8].Success) target.Add(FileLink(m.Groups[8].Value, t, false));
      }
      if (pos < text.Length) target.Add(new Run(text.Substring(pos)));
    }

    static Inline CodeRun(string code, Theme t)
    {
      Run r = new Run(code);
      r.FontFamily = MonoFont;
      r.FontSize = 12.5;
      r.Background = t.Surface;
      return r;
    }

    static Inline FileLink(string path, Theme t, bool mono)
    {
      Hyperlink link = Link(path, path, t, mono);
      link.ToolTip = "Открыть файл";
      return link;
    }

    static Hyperlink Link(string text, string target, Theme t, bool mono)
    {
      Run run = new Run(text);
      if (mono) { run.FontFamily = MonoFont; run.FontSize = 12.5; }
      Hyperlink link = new Hyperlink(run);
      link.Foreground = t.Accent;
      link.Cursor = System.Windows.Input.Cursors.Hand;
      link.Click += delegate { Open(target); };
      return link;
    }

    static void Open(string target)
    {
      try
      {
        bool isPath = PathRe.IsMatch(target);
        if (isPath && !System.IO.File.Exists(target))
        {
          Log.Write("panel: файл по ссылке не найден: " + target);
          return;
        }
        if (isPath || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
          Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
      }
      catch (Exception e) { Log.Error("panel.open " + target, e); }
    }
  }
}
