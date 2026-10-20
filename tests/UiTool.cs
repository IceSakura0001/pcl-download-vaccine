using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

class UiTool
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    static Process FindProc(string nameOrPid)
    {
        int pid;
        if (int.TryParse(nameOrPid, out pid)) return Process.GetProcessById(pid);
        var list = Process.GetProcessesByName(nameOrPid);
        if (list.Length == 0) throw new Exception("process not found: " + nameOrPid);
        return list[0];
    }

    static int Main(string[] args)
    {
        SetProcessDPIAware();
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 1) { Usage(); return 2; }
        try
        {
            switch (args[0])
            {
                case "list": return List(args);
                case "shot": return Shot(args);
                case "dump": return Dump(args);
                case "click": return Click(args);
                case "restore": return Restore(args);
                case "rect": return RectCmd(args);
                case "drop": return Drop(args);
                case "screen": return ScreenCmd();
                case "movewin": return MoveWin(args);
                case "drag": return DragGesture(args);
                case "top": return TopMost(args, true);
                case "notop": return TopMost(args, false);
                default: Usage(); return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR " + ex.Message);
            return 1;
        }
    }

    static void Usage()
    {
        Console.WriteLine("UiTool list <processName|pid>");
        Console.WriteLine("UiTool restore <processName|pid>");
        Console.WriteLine("UiTool rect <processName|pid>");
        Console.WriteLine("UiTool shot <processName|pid> <out.png> [--fullscreen]");
        Console.WriteLine("UiTool dump <processName|pid> [out.txt] [max]");
        Console.WriteLine("UiTool click <processName|pid> <name>");
    }

    // ---- simulated WM_DROPFILES drop (for automated testing of the drop handler) ----
    [StructLayout(LayoutKind.Sequential)]
    struct DROPFILES { public uint pFiles; public int x; public int y; public int fNC; public int fWide; }

    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    static int Drop(string[] args)
    {
        var p = FindProc(args[1]);
        var files = new List<string>();
        for (int i = 2; i < args.Length; i++) files.Add(Path.GetFullPath(args[i]));
        foreach (var f in files) if (!File.Exists(f) && !Directory.Exists(f)) { Console.WriteLine("path not found: " + f); return 3; }
        var hwnd = p.MainWindowHandle;
        if (hwnd == IntPtr.Zero) { Console.WriteLine("no main window"); return 4; }

        var sb = new StringBuilder();
        foreach (var f in files) sb.Append(f).Append('\0');
        sb.Append('\0');
        var pathBytes = Encoding.Unicode.GetBytes(sb.ToString());
        int headerSize = Marshal.SizeOf(typeof(DROPFILES));
        int total = headerSize + pathBytes.Length + 4;
        var mem = GlobalAlloc(0x0042, (UIntPtr)total); // GHND
        if (mem == IntPtr.Zero) { Console.WriteLine("GlobalAlloc failed"); return 5; }
        var ptr = GlobalLock(mem);
        try
        {
            var header = new DROPFILES();
            header.pFiles = (uint)headerSize;
            header.fWide = 1;
            Marshal.StructureToPtr(header, ptr, false);
            Marshal.Copy(pathBytes, 0, new IntPtr(ptr.ToInt64() + headerSize), pathBytes.Length);
        }
        finally { GlobalUnlock(mem); }
        SendMessage(hwnd, 0x0233, mem, IntPtr.Zero);
        Thread.Sleep(300);
        Console.WriteLine("dropped " + files.Count + " file(s) -> hwnd 0x" + hwnd.ToInt64().ToString("X"));
        return 0;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static int ScreenCmd()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        Console.WriteLine("screen=" + b.Width + "x" + b.Height + " at " + b.X + "," + b.Y);
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        Console.WriteLine("virtual=" + vs.Width + "x" + vs.Height + " at " + vs.X + "," + vs.Y);
        return 0;
    }

    static int MoveWin(string[] args)
    {
        var p = FindProc(args[1]);
        int x = int.Parse(args[2]), y = int.Parse(args[3]);
        var h = p.MainWindowHandle;
        if (h == IntPtr.Zero) { Console.WriteLine("no main window"); return 1; }
        if (IsIconic(h)) { ShowWindow(h, 9); Thread.Sleep(300); }
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004); // NOSIZE | NOZORDER
        Thread.Sleep(300);
        RECT r; GetWindowRect(h, out r);
        Console.WriteLine("moved to " + r.Left + "," + r.Top + " size " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
        return 0;
    }

    static int DragGesture(string[] args)
    {        int x1 = int.Parse(args[1]), y1 = int.Parse(args[2]), x2 = int.Parse(args[3]), y2 = int.Parse(args[4]);
        int steps = args.Length > 5 ? int.Parse(args[5]) : 30;
        SetCursorPos(x1, y1);
        Thread.Sleep(250);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
        Thread.Sleep(350);
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            SetCursorPos(x, y);
            Thread.Sleep(30);
        }
        Thread.Sleep(500);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // LEFTUP
        Thread.Sleep(700);
        Console.WriteLine("drag " + x1 + "," + y1 + " -> " + x2 + "," + y2);
        return 0;
    }

    static int TopMost(string[] args, bool top)
    {
        var p = FindProc(args[1]);
        var h = p.MainWindowHandle;
        if (h == IntPtr.Zero) { Console.WriteLine("no main window"); return 1; }
        SetWindowPos(h, top ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
        if (top) SetForegroundWindow(h);
        Console.WriteLine((top ? "topmost " : "normal ") + (top ? "0x" : "0x") + h.ToInt64().ToString("X"));
        return 0;
    }

    static int List(string[] args)
    {
        var p = FindProc(args[1]);
        Console.WriteLine("pid=" + p.Id + " name=" + p.ProcessName + " mainHandle=" + p.MainWindowHandle + " title=" + p.MainWindowTitle);
        var handles = new List<IntPtr>();
        var sb = new StringBuilder(512);
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)p.Id) handles.Add(h);
            return true;
        }, IntPtr.Zero);
        foreach (var h in handles)
        {
            RECT r;
            GetWindowRect(h, out r);
            sb.Length = 0;
            int n = GetWindowText(h, sb, sb.Capacity);
            Console.WriteLine(string.Format("h=0x{0:X} visible={1} iconic={2} rect={3},{4},{5},{6} size={7}x{8} title={9}",
                h.ToInt64(), IsWindowVisible(h), IsIconic(h), r.Left, r.Top, r.Right, r.Bottom,
                r.Right - r.Left, r.Bottom - r.Top, sb.ToString()));
        }
        return 0;
    }

    static int Restore(string[] args)
    {
        var p = FindProc(args[1]);
        var h = p.MainWindowHandle;
        if (h == IntPtr.Zero) { Console.WriteLine("no main window"); return 1; }
        ShowWindow(h, 9);
        Thread.Sleep(200);
        SetForegroundWindow(h);
        Thread.Sleep(300);
        RECT r; GetWindowRect(h, out r);
        Console.WriteLine("restored h=0x" + h.ToInt64().ToString("X") + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
        return 0;
    }

    static int RectCmd(string[] args)
    {
        var p = FindProc(args[1]);
        var h = p.MainWindowHandle;
        RECT r, c;
        GetWindowRect(h, out r);
        GetClientRect(h, out c);
        Console.WriteLine("window=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom + " size=" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
        Console.WriteLine("client=" + c.Right + "x" + c.Bottom);
        return 0;
    }

    static int Shot(string[] args)
    {
        var p = FindProc(args[1]);
        string outPath = args.Length > 2 ? args[2] : "shot.png";
        bool full = args.Length > 3 && args[3] == "--fullscreen";
        Bitmap bmp;
        if (full)
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            bmp = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.X, b.Y, 0, 0, bmp.Size);
        }
        else
        {
            var h = p.MainWindowHandle;
            if (IsIconic(h)) { ShowWindow(h, 9); Thread.Sleep(500); }
            RECT r; GetWindowRect(h, out r);
            int w = r.Right - r.Left, hh = r.Bottom - r.Top;
            bmp = new Bitmap(w, hh);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(h, hdc, 2);
                g.ReleaseHdc(hdc);
            }
        }
        bmp.Save(outPath, ImageFormat.Png);
        bmp.Dispose();
        Console.WriteLine("saved " + outPath);
        return 0;
    }

    static int Dump(string[] args)
    {
        var p = FindProc(args[1]);
        var h = p.MainWindowHandle;
        if (IsIconic(h)) { ShowWindow(h, 9); Thread.Sleep(500); }
        var root = AutomationElement.FromHandle(h);
        Console.WriteLine("root: " + root.Current.ControlType.ProgrammaticName + " | " + root.Current.ClassName + " | " + root.Current.Name);
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        int max = args.Length > 3 ? int.Parse(args[3]) : 400;
        var lines = new List<string>();
        int i = 0;
        foreach (AutomationElement e in all)
        {
            if (i++ > max) break;
            try
            {
                var c = e.Current;
                var r = c.BoundingRectangle;
                lines.Add(string.Format("{0} | class={1} | id={2} | name={3} | rect={4},{5},{6},{7}",
                    c.ControlType.ProgrammaticName, c.ClassName, c.AutomationId, c.Name,
                    (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
            }
            catch { }
        }
        foreach (var l in lines) Console.WriteLine(l);
        if (args.Length > 2) File.WriteAllLines(args[2], lines, Encoding.UTF8);
        return 0;
    }

    static int Click(string[] args)
    {
        var p = FindProc(args[1]);
        string name = args[2];
        var h = p.MainWindowHandle;
        if (IsIconic(h)) { ShowWindow(h, 9); Thread.Sleep(500); }
        var root = AutomationElement.FromHandle(h);
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement hit = null;
        foreach (AutomationElement e in all)
        {
            try { if (e.Current.Name == name) { hit = e; break; } } catch { }
        }
        if (hit == null) { Console.WriteLine("element not found: " + name); return 3; }
        var rect = hit.Current.BoundingRectangle;
        int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        Console.WriteLine("click " + name + " at " + x + "," + y + " rect=" + rect);
        SetCursorPos(x, y);
        Thread.Sleep(250);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        return 0;
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
}
