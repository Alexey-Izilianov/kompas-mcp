// GetActiveObject-тест: подключение к УЖЕ запущенному КОМПАСу через ROT
// (Marshal.GetActiveObject по ProgID KOMPAS.Application.5/.7) без всякой библиотеки.
using System;
using System.Runtime.InteropServices;
using Kompas6API5;

class GetActiveTest
{
  static int Main()
  {
    Console.WriteLine("kompas pids: " + string.Join(",",
      Array.ConvertAll(System.Diagnostics.Process.GetProcessesByName("KOMPAS"), p => (object)p.Id)));

    // API-5
    try
    {
      object o = Marshal.GetActiveObject("KOMPAS.Application.5");
      KompasObject k = (KompasObject)o;
      Console.WriteLine("API5: получен KompasObject, Visible=" + k.Visible +
        ", hInstance? ksConfirmCursorPos test skip");
      try { k.ksMessage("Давинчи: подключение через ROT работает!"); }
      catch (Exception e) { Console.WriteLine("ksMessage EXC: " + e.Message); }
      Marshal.ReleaseComObject(k);
    }
    catch (Exception e) { Console.WriteLine("API5 GetActiveObject EXC: " + e.Message); }

    // API-7
    try
    {
      object o = Marshal.GetActiveObject("KOMPAS.Application.7");
      Console.WriteLine("API7: получен объект типа " + o.GetType().FullName);
      Marshal.ReleaseComObject(o);
    }
    catch (Exception e) { Console.WriteLine("API7 GetActiveObject EXC: " + e.Message); }
    return 0;
  }
}