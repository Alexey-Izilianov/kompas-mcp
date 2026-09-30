param([int]$targetPid = 0)
Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinEnum {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  public static List<string> For(uint pid) {
    List<string> res = new List<string>();
    EnumWindows((h, l) => {
      uint p;
      GetWindowThreadProcessId(h, out p);
      if (p == pid) {
        StringBuilder sb = new StringBuilder(512);
        GetWindowTextW(h, sb, 512);
        res.Add((IsWindowVisible(h) ? "VISIBLE " : "hidden  ") + h + " " + sb);
      }
      return true;
    }, IntPtr.Zero);
    return res;
  }
}
"@
$pidToCheck = $targetPid
[WinEnum]::For($pidToCheck)