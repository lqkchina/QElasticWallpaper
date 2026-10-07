using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// Win32 原生 API 封装：全局鼠标钩子、窗口置底、桌面(壁纸层)识别等。
    /// 这些是让效果层"垫在桌面图标下面、压住壁纸上面"并捕获桌面点击的关键。
    /// </summary>
    public static class Native
    {
        // ---------- 鼠标钩子 ----------
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_MOUSEMOVE = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // ---------- 窗口与桌面 ----------
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        // ---------- 窗口扩展样式（鼠标穿透用） ----------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        /// <summary>取窗口扩展样式（32/64 位通用）。</summary>
        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>设置窗口扩展样式（32/64 位通用）。</summary>
        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        // ---------- 全局按键状态（判断左键是否按住，纯全局、不依赖 WinForms/WPF 焦点） ----------
        public const int VK_LBUTTON = 0x01;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const uint GW_CHILD = 5;
        public const uint GW_HWNDNEXT = 2;

        // 置底 / 置顶
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOZORDER = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        // 取指定窗口的 DPI（Win10 1607+）。用于把物理像素换算成 WPF 的 DIP。
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        // ---------- 消息循环（低层钩子所在线程必须跑一个消息循环才能收到回调） ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        // ---------- WorkerW 查找（可选：把效果层真正塞进桌面壁纸层下面） ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam,
            IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        public const uint WM_SPAWN_WORKERW = 0x052C;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>取窗口类名（最多 256 字符）。</summary>
        public static string ClassNameOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>判断某个屏幕坐标是否落在"桌面(壁纸)"区域。</summary>
        public static bool IsDesktopPoint(POINT pt)
        {
            IntPtr win = WindowFromPoint(pt);
            if (win == IntPtr.Zero) return false;
            IntPtr root = GetAncestor(win, GA_ROOT);
            string cls = ClassNameOf(root);
            return cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView";
        }

        /// <summary>
        /// 找到承载壁纸的 WorkerW 窗口句柄。
        /// 它和 Progman 一起，才是真正的"桌面壁纸宿主"。找不到时返回 0。
        /// </summary>
        public static IntPtr FindWorkerW()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                // 通知 Progman 生成/初始化 WorkerW
                SendMessageTimeout(progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
            }

            IntPtr workerW = IntPtr.Zero;
            while (true)
            {
                workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                if (workerW == IntPtr.Zero) break;

                // 找到那个下面挂着 SHELLDLL_DefView（桌面图标列表）的 WorkerW，它就是壁纸宿主
                IntPtr defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) return workerW;
            }
            return IntPtr.Zero;
        }

        // ---------- 屏幕捕获（把壁纸截下来，做"壁纸真的被按变形"的效果） ----------
        public const int SRCCOPY = 0x00CC0020;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
            IntPtr hdcSrc, int x1, int y1, int rop);
    }
}
