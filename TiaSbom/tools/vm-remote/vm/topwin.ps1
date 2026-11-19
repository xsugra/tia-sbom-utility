# Lists visible top-level windows in z-order (top first): topmost flag, owner, rectangle, title.
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class TopWin {
  delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int c);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  struct R { public int L, T, Ri, B; }
  public static List<string> List() {
    var list = new List<string>(); int i = 0;
    EnumWindows((h, l) => { i++; if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      int ex = GetWindowLong(h, -20); R r; GetWindowRect(h, out r); uint pid; GetWindowThreadProcessId(h, out pid);
      if (sb.Length > 0) list.Add(string.Format("{0,4} pid={1} topmost={2} owner={3} rect={4},{5},{6},{7} '{8}'", i, pid, (ex & 8) != 0, GetWindow(h, 4), r.L, r.T, r.Ri, r.B, sb));
      return true; }, IntPtr.Zero);
    return list; } }
'@
[TopWin]::List()
