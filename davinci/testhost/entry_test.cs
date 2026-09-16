// Имитация КОМПАСа: нативный LoadLibrary(.rtw) + GetProcAddress + вызов
// экспортов. Запускается из папки БЕЗ interop-DLL — воспроизводит условия
// KOMPAS.Exe (проверка резолвинга сборок). Использование: entry_test.exe <путь.rtw>
using System;
using System.Runtime.InteropServices;
using System.Text;

class EntryTest
{
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern IntPtr LoadLibraryW(string path);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
  static extern IntPtr GetProcAddress(IntPtr h, string name);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int LoadStringW(IntPtr h, uint id, StringBuilder buf, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern IntPtr LoadMenuW(IntPtr h, uint id);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetMenuItemCount(IntPtr menu);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern bool GetMenuStringW(IntPtr menu, uint item, StringBuilder buf, int max, uint flags);

  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  delegate uint LibIdDelegate();
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  delegate int NoArgDelegate();
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  delegate int NotifyEntryDelegate(IntPtr app);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  delegate void LibEntryDelegate(uint comm);

  static int Main(string[] args)
  {
    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
    {
      Console.WriteLine("UNHANDLED: " + e.ExceptionObject);
      Environment.ExitCode = 5;
    };
    if (args.Length != 1) { Console.WriteLine("нужен путь к .rtw"); return 1; }
    IntPtr h = LoadLibraryW(args[0]);
    if (h == IntPtr.Zero) { Console.WriteLine("LoadLibrary failed: " + Marshal.GetLastWin32Error()); return 2; }
    Console.WriteLine("LoadLibrary OK: 0x" + h.ToString("x"));

    StringBuilder sb = new StringBuilder(256);
    LoadStringW(h, 100, sb, 256);
    Console.WriteLine("STRINGTABLE[100] = \"" + sb + "\"");
    IntPtr menu = LoadMenuW(h, 100);
    if (menu != IntPtr.Zero)
    {
      int n = GetMenuItemCount(menu);
      Console.WriteLine("MENU 100: " + n + " пунктов");
      for (uint i = 0; i < n; i++)
      {
        GetMenuStringW(menu, i, sb, 256, 0x400 /*MF_BYPOSITION*/);
        Console.WriteLine("  [" + i + "] \"" + sb + "\"");
      }
    }
    else Console.WriteLine("MENU 100: не найден");

    IntPtr p = GetProcAddress(h, "LibIsOnApplication7");
    Console.WriteLine("LibIsOnApplication7 -> " + (p != IntPtr.Zero
      ? ((NoArgDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(NoArgDelegate)))().ToString() : "нет экспорта"));
    p = GetProcAddress(h, "LibInterfaceNotifyEntry");
    if (p != IntPtr.Zero)
      Console.WriteLine("LibInterfaceNotifyEntry(0) -> " +
        ((NotifyEntryDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(NotifyEntryDelegate)))(IntPtr.Zero));

    p = GetProcAddress(h, "LIBRARYENTRY");
    if (p == IntPtr.Zero) { Console.WriteLine("LIBRARYENTRY: экспорта нет"); return 3; }
    Console.WriteLine("вызываю LIBRARYENTRY(2)…");
    ((LibEntryDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(LibEntryDelegate)))(2);
    Console.WriteLine("LIBRARYENTRY(2) — вернулся без падения");
    return 0;
  }
}