param([int]$NavIndex = 9, [int]$Samples = 10)

$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Cur {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
  [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }

  public static IntPtr MainWindow(uint pid, out RECT box) {
    IntPtr found = IntPtr.Zero;
    RECT best = new RECT();
    EnumWindows((win, l) => {
      uint owner;
      GetWindowThreadProcessId(win, out owner);
      if (owner != pid) return true;
      RECT r;
      GetWindowRect(win, out r);
      if ((r.Right - r.Left) > 900) { found = win; best = r; return false; }
      return true;
    }, IntPtr.Zero);
    box = best;
    return found;
  }

  public static List<IntPtr> NavItems(IntPtr parent) {
    var found = new List<IntPtr>();
    EnumChildWindows(parent, (h, l) => {
      RECT r;
      GetWindowRect(h, out r);
      if ((r.Right - r.Left) == 218 && (r.Bottom - r.Top) == 44) found.Add(h);
      return true;
    }, IntPtr.Zero);
    return found;
  }

  public static string CursorName() {
    var info = new CURSORINFO();
    info.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
    if (!GetCursorInfo(ref info)) return "unreadable";
    if (info.flags != 1) return "not-showing";
    IntPtr wait = LoadCursor(IntPtr.Zero, new IntPtr(32514));
    IntPtr appStarting = LoadCursor(IntPtr.Zero, new IntPtr(32650));
    IntPtr arrow = LoadCursor(IntPtr.Zero, new IntPtr(32512));
    if (info.hCursor == wait) return "WAIT";
    if (info.hCursor == appStarting) return "APPSTARTING";
    if (info.hCursor == arrow) return "ARROW";
    return "other(" + info.hCursor + ")";
  }
}
"@

$proc = Get-Process -Name 'PC Medic' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output 'NO PROCESS'; exit 1 }

$box = New-Object Cur+RECT
$main = [Cur]::MainWindow([uint32]$proc.Id, [ref]$box)
if ($main -eq [IntPtr]::Zero) { Write-Output 'NO MAIN WINDOW'; exit 1 }

[void][Cur]::ShowWindow($main, 9)
Start-Sleep -Milliseconds 500

$navs = @()
foreach ($item in [Cur]::NavItems($main)) {
  $r = New-Object Cur+RECT
  [Cur]::GetWindowRect($item, [ref]$r) | Out-Null
  $navs += [pscustomobject]@{ Handle = $item; Top = $r.Top }
}
$navs = $navs | Sort-Object Top
$lp = [IntPtr]((22 -shl 16) -bor 100)
[Cur]::PostMessage($navs[$NavIndex].Handle, 0x0201, [IntPtr]1, $lp) | Out-Null
Start-Sleep -Milliseconds 120
[Cur]::PostMessage($navs[$NavIndex].Handle, 0x0202, [IntPtr]0, $lp) | Out-Null

Start-Sleep -Milliseconds 900

# Park the pointer over the content area so the window's cursor applies.
[void][Cur]::SetCursorPos($box.Left + 700, $box.Top + 470)

for ($i = 1; $i -le $Samples; $i++) {
  $name = [Cur]::CursorName()
  Write-Output ("sample {0}: {1}" -f $i, $name)
  Start-Sleep -Milliseconds 700
}
