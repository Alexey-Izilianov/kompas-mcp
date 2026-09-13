// Спавнер видимого КОМПАСа для тестов: создаёт экземпляр, делает видимым,
// печатает PID и завершается — КОМПАС остаётся работать (для attach-тестов).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kompas6API5;

class KompasSpawn
{
  static int Main()
  {
    var before = new HashSet<int>();
    foreach (Process pr in Process.GetProcessesByName("KOMPAS")) before.Add(pr.Id);

    var k = (KompasObject)Activator.CreateInstance(Type.GetTypeFromProgID("KOMPAS.Application.5"));
    k.Visible = true;
    Console.WriteLine("KOMPAS_VISIBLE_OK");
    System.Threading.Thread.Sleep(3000);

    foreach (Process pr in Process.GetProcessesByName("KOMPAS"))
      if (!before.Contains(pr.Id)) { Console.WriteLine("PID=" + pr.Id); return 0; }
    Console.WriteLine("PID=?");
    return 1;
  }
}