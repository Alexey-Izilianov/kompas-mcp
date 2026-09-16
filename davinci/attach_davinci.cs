// Подключение библиотеки «Давинчи» к ЗАПУЩЕННОМУ КОМПАСу юзера.
// Основной путь — нативный: ksAttachKompasLibrary(DavinciNative.rtw)
// (именно он подтвердился; managed-COM .rtw КОМПАС не грузит).
// Использование: attach_davinci.exe  (КОМПАС должен быть запущен)
using System;
using Kompas6API5;

class AttachDavinci
{
  static int Main(string[] args)
  {
    string dir = System.IO.Path.GetDirectoryName(
      System.Reflection.Assembly.GetExecutingAssembly().Location);
    string rtw = System.IO.Path.Combine(dir, "DavinciNative.rtw");
    KompasObject k;
    try
    {
      k = (KompasObject)System.Runtime.InteropServices.Marshal.GetActiveObject("KOMPAS.Application.5");
    }
    catch (Exception e)
    {
      Console.WriteLine("КОМПАС не запущен (" + e.Message + ") — запустите КОМПАС и повторите");
      return 1;
    }
    try
    {
      int libId = k.ksAttachKompasLibrary(rtw);
      Console.WriteLine("ksAttachKompasLibrary -> libraryId=" + libId);
      if (libId == 0)
      {
        Console.WriteLine("КОМПАС не подключил библиотеку — смотрите %TEMP%\\kompas-davinci\\davinci.log");
        return 2;
      }
      Console.WriteLine("OK — проверьте меню «Прикладные библиотеки» → «Давинчи»");
      return 0;
    }
    catch (Exception e)
    {
      Console.WriteLine("attach EXC: " + e.Message +
        (e.InnerException == null ? "" : " / " + e.InnerException.Message));
      return 3;
    }
  }
}