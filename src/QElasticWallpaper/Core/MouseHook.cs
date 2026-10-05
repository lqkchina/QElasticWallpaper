using System;
using System.Runtime.InteropServices;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 全局低级鼠标钩子（WH_MOUSE_LL）。
    /// 不管效果层垫在桌面下面多少层，都能捕获"左键按下"，再判断是否点在桌面上。
    /// 独立线程里跑，不阻塞 UI 渲染。
    /// </summary>
    public sealed class MouseHook : IDisposable
    {
        public event Action<Native.POINT> LeftDown;
        public event Action<Native.POINT> MouseMove;

        IntPtr _hook = IntPtr.Zero;
        Native.LowLevelMouseProc _proc;
        System.Threading.Thread _thread;
        volatile bool _running;

        /// <summary>当前触发模式：0=仅桌面 1=桌面+应用背景 2=任意左键。</summary>
        public volatile int TriggerMode;

        /// <summary>
        /// 透明效果层的原生窗口句柄。
        /// 效果层铺在桌面上时，点击壁纸空白处会落到它上面（而非桌面壳窗口），
        /// 判断"是否桌面"时要把自己也算进去，否则会漏触发。
        /// </summary>
        public IntPtr OverlayHandle = IntPtr.Zero;

        public void Start(int triggerMode)
        {
            TriggerMode = triggerMode;
            _running = true;
            _thread = new System.Threading.Thread(HookLoop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        void HookLoop()
        {
            _proc = HookCallback;
            using (var module = System.Diagnostics.Process.GetCurrentProcess().MainModule)
            {
                IntPtr hMod = Native.GetModuleHandle(module?.ModuleName);
                _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, hMod, 0);
            }

            var msg = new Native.MSG();
            while (_running && _hook != IntPtr.Zero)
            {
                int r = Native.GetMessage(out msg, IntPtr.Zero, 0, 0);
                if (r <= 0) break;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }

            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                switch (wParam.ToInt64())
                {
                    case Native.WM_LBUTTONDOWN:
                        if (ShouldTrigger(data.pt))
                            LeftDown?.Invoke(data.pt);
                        break;
                    case Native.WM_MOUSEMOVE:
                        MouseMove?.Invoke(data.pt);
                        break;
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>某坐标是否属于"桌面区域"（含我们自己的透明效果层）。</summary>
        bool IsDesktopArea(Native.POINT pt)
        {
            if (Native.IsDesktopPoint(pt)) return true;

            // 透明效果层铺在桌面之上，点击壁纸空白处会落到它上面，同样算桌面
            if (OverlayHandle != IntPtr.Zero)
            {
                IntPtr win = Native.WindowFromPoint(pt);
                if (win == OverlayHandle) return true;
                IntPtr root = Native.GetAncestor(win, Native.GA_ROOT);
                if (root == OverlayHandle) return true;
            }
            return false;
        }

        bool ShouldTrigger(Native.POINT pt)
        {
            if (TriggerMode == 2) return true;                 // 任意左键
            if (TriggerMode == 0) return IsDesktopArea(pt);    // 仅桌面壁纸
            // 模式 1：桌面，或当前没有聚焦的应用窗口（即点的是桌面/无窗口区域）
            if (IsDesktopArea(pt)) return true;
            IntPtr fg = Native.GetForegroundWindow();
            string cls = Native.ClassNameOf(fg);
            return cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView";
        }

        public void Dispose()
        {
            _running = false;
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
