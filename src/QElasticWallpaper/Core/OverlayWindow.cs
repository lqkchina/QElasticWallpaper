using System;
using System.Windows;
using System.Windows.Media;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 透明效果层窗口：铺满整个虚拟桌面。
    /// 默认尝试嵌入桌面壁纸宿主（WorkerW），垫在图标之下、壁纸之上；
    /// 嵌入失败时退化为 HWND_BOTTOM 置底层，效果依然显示。
    /// 用 CompositionTarget.Rendering 每帧重绘 Q弹按压效果，和壁纸内容无关，
    /// 所以"壁纸随时更换"完全不影响效果。
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        readonly RippleController _ctrl;
        readonly DrawingVisual _visual;
        readonly VisualHost _host;
        readonly bool _embedBelowIcons;
        double _lastFrameMs = 0;
        readonly double _originX, _originY;
        double _dpiX = 1.0, _dpiY = 1.0;   // PixelsPerDip，初始化时缓存，跨线程安全
        IntPtr _hwnd = IntPtr.Zero;

        // ---- 诊断字段 ----
        public bool EmbedTried;      // 是否尝试过嵌入桌面壁纸层
        public bool EmbedSuccess;    // 是否成功嵌入（图标之下）
        public bool FallbackBottom;  // 是否退化为置底显示

        /// <summary>诊断：效果层当前状态。</summary>
        public string Summary
        {
            get
            {
                string layer = _embedBelowIcons ? "图标之下" : "置顶";
                string emb;
                if (!_embedBelowIcons) emb = "置顶模式";
                else if (EmbedSuccess) emb = "已嵌入桌面壁纸层(图标之下)";
                else if (FallbackBottom) emb = "嵌入失败→置底显示(效果在图标上方)";
                else emb = "未知";
                return $"{layer}  {emb}  窗口句柄:0x{_hwnd.ToInt64():X}  缩放:{_dpiX:0.##}  {SystemParameters.VirtualScreenWidth:0}×{SystemParameters.VirtualScreenHeight:0}";
            }
        }

        public OverlayWindow(RippleController ctrl, bool embedBelowIcons)
        {
            _ctrl = ctrl;
            _embedBelowIcons = embedBelowIcons;

            // 全透明、无边框、不抢焦点、不进任务栏
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;   // 关键：绝对不拦截任何点击
            ShowActivated = false;

            // 铺满所有显示器组成的虚拟桌面
            _originX = SystemParameters.VirtualScreenLeft;
            _originY = SystemParameters.VirtualScreenTop;
            Left = _originX;
            Top = _originY;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _visual = new DrawingVisual();
            _host = new VisualHost(_visual);
            Content = _host;

            SourceInitialized += OnSourceInitialized;
            CompositionTarget.Rendering += OnRendering;
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            _hwnd = hwnd;

            // 缓存本窗口 DPI，供 PhysicalToDip 从任意线程安全调用。
            // PixelsPerDip = 窗口DPI / 96；取不到时按 96（100% 缩放）处理。
            uint winDpi = Native.GetDpiForWindow(hwnd);
            if (winDpi == 0) winDpi = 96;
            _dpiX = winDpi / 96.0;
            _dpiY = winDpi / 96.0;

            // 告诉控制器本窗口的缩放，并立刻截一次壁纸做"形变"素材
            _ctrl.DpiX = _dpiX;
            _ctrl.DpiY = _dpiY;
            _ctrl.CaptureWallpaper();

            if (_embedBelowIcons)
            {
                // 首选：塞进桌面壁纸宿主，垫在图标下面
                EmbedTried = true;
                if (!TryEmbedBelowIcons(hwnd))
                {
                    // 退化：作为普通窗口置底（效果仍会显示，只是盖在图标上）
                    FallbackBottom = true;
                    SetWindowPosBottom(hwnd);
                }
                else
                {
                    EmbedSuccess = true;
                }
            }
            // 置顶模式：什么都不做，由 AppController 设置 Topmost=true
        }

        void SetWindowPosBottom(IntPtr hwnd)
        {
            Native.SetWindowPos(hwnd, Native.HWND_BOTTOM,
                0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE |
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 把窗口设为桌面图标所在的 WorkerW 的子窗口，并排到最底，
        /// 从而实现"效果在图标之下、壁纸之上"。
        /// </summary>
        bool TryEmbedBelowIcons(IntPtr hwnd)
        {
            try
            {
                IntPtr workerW = Native.FindWorkerW();
                if (workerW == IntPtr.Zero) return false;

                if (!Native.SetParent(hwnd, workerW)) return false;

                // 子窗口坐标相对父窗口（即桌面壁纸区）客户区，从 (0,0) 铺满虚拟屏幕
                int devW = (int)(SystemParameters.VirtualScreenWidth * _dpiX);
                int devH = (int)(SystemParameters.VirtualScreenHeight * _dpiY);
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM,
                    0, 0, devW, devH,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                return true;
            }
            catch
            {
                return false;
            }
        }

        void OnRendering(object sender, EventArgs e)
        {
            // 统一使用"系统纪元毫秒"作为唯一时钟（和效果记录的 StartMs 一致），
            // 避免不同时钟错位导致动画永远停在第一帧。
            double now = RippleController.NowMs();

            // 按 TargetFps 限帧
            double frameMs = 1000.0 / Math.Max(1, _ctrl.GetFps());
            if (now - _lastFrameMs < frameMs) return;
            _lastFrameMs = now;

            using (DrawingContext dc = _visual.RenderOpen())
            {
                _ctrl.Draw(dc, _originX, _originY, now);
            }
            _host.InvalidateVisual(); // 触发下一帧实际渲染
        }

        protected override void OnClosed(EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            base.OnClosed(e);
        }

        /// <summary>
        /// 把鼠标钩子给出的物理像素坐标换算成 WPF 的 DIP 屏幕坐标。
        /// 高分屏（缩放 &gt; 100%）时两者不一致，必须换算，否则效果会偏移。
        /// </summary>
        public (double X, double Y) PhysicalToDip(double physX, double physY)
        {
            return (physX / _dpiX, physY / _dpiY);
        }
    }

    /// <summary>把 DrawingVisual 挂进可视树的宿主元素。</summary>
    public sealed class VisualHost : FrameworkElement
    {
        readonly DrawingVisual _visual;

        public VisualHost(DrawingVisual v)
        {
            _visual = v;
            AddVisualChild(v);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;
    }
}
