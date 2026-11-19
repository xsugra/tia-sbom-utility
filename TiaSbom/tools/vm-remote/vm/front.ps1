# Test helper: brings windows whose title starts with $Title in front of everything (to photograph a hidden window).
param([string]$Title = 'SBOM Export', [string]$Shot = '')
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class Front {
  delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  public static int Raise(string title) { int n = 0;
    EnumWindows((h, l) => { var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (IsWindowVisible(h) && sb.ToString().StartsWith(title)) { SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 3); SetForegroundWindow(h); n++; }
      return true; }, IntPtr.Zero); return n; } }
'@
"raised: " + [Front]::Raise($Title)
Start-Sleep 1
if ($Shot) { & 'C:/TiaSbomTest/run/shot.ps1' -Path "C:/TiaSbomTest/shots/$Shot.png" }
