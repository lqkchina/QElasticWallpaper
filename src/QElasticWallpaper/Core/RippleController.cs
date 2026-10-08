using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 渲染控制器：把整张壁纸做成一张"果冻布"（弹簧质点网格）。
    /// 按住左键拖拽 → 壁纸跟着手指弯下去（局部凹陷+周围鼓起）；
    /// 松手 → 弹簧把它弹回原位，带阻尼来回Q弹震荡。
    /// 参考实现："果冻弹性壁纸" 的做法 —— 壁纸 → 果冻层 → 图标。
    /// </summary>
    public sealed class RippleController
    {
        readonly List<Param> _cfg;

        // ---- 壁纸素材（DIP 对齐，BGRA 像素）----
        BitmapSource _desktop;      // 物理像素截图
        byte[] _src;                // 缩放成 DIP 后的壁纸像素
        int _sw, _sh;               // DIP 尺寸
        bool _sheetReady;
        public double DpiX = 1.0, DpiY = 1.0;
        public long LastWallpaperCaptureMs;

        // ---- 果冻物理网格（弹簧质点）----
        double _cell; int _gw, _gh;
        double[] _nodeX, _nodeY;    // 质点静止位置（DIP 屏幕坐标）
        double[] _dx, _dy;          // 位移
        double[] _vx, _vy;          // 速度
        long _lastFrameMs;

        // ---- 壁纸更换检测（避免每次点击都重截、也能及时跟上新壁纸）----
        string _wpPath = "";
        long _wpMtime;

        // ---- 渲染缓冲 ----
        WriteableBitmap _patch;
        byte[] _patchPix;
        int _patchW = -1, _patchH = -1;
        bool _anyDisp;

        // ---- 悬停光晕 ----
        public Native.POINT LastMouse;
        public bool HasMouse;
        double _hoverX, _hoverY;
        bool _hoverValid;

        // ---- 诊断统计 ----
        public long TotalClicks;
        public long SpawnedEffects;
        public long BlockedByDisabled;
        public int ActiveCount { get { return _anyDisp ? 1 : 0; } }

        public RippleController(List<Param> cfg) => _cfg = cfg;

        double P(string k) => Get(k).Value;
        bool B(string k) => Get(k).BoolValue;
        Param Get(string k) => _cfg.Find(p => p.Key == k);

        /// <summary>在屏幕坐标 (x,y) 开始一次按压（具体按压/拖拽由每帧读取鼠标状态驱动）。</summary>
        public void Spawn(double x, double y)
        {
            TotalClicks++;
            if (!B("Enabled")) { BlockedByDisabled++; return; }
            SpawnedEffects++;
            if (!_sheetReady) BuildSheet();
        }

        public void SetMouse(Native.POINT pt)
        {
            LastMouse = pt;
            HasMouse = true;
        }

        public double GetFps() => P("TargetFps");

        public string Stats =>
            $"收到点击:{TotalClicks}  生成效果:{SpawnedEffects}  禁用拦截:{BlockedByDisabled}  果冻网格:{_gw}x{_gh}  形变中:{(_anyDisp ? "是" : "否")}";

        public static double NowMs()
            => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 把壁纸做成形变素材。
        /// 首选【直接读壁纸图片文件】并按屏幕"填充"绘制 —— 素材是纯壁纸图片，
        /// 不含我们的效果层、也不含打开的窗口：既不"图中图中图"，也不会黑块。
        /// 读不到壁纸文件时（纯色/第三方壁纸软件）退回全屏截图兜底。
        /// 只更新像素；果冻网格只在首次/分辨率或网格尺寸变化时才重建，
        /// 正在进行的震荡不会被打断，多点按压也能在同一张网格上叠加、互相影响。
        /// </summary>
        public void CaptureWallpaper()
        {
            try
            {
                if (TryLoadWallpaperFile())
                {
                    LastWallpaperCaptureMs = (long)NowMs();
                    UpdateSheetPixels();
                    return;
                }

                // 兜底：全屏截图（含窗口，但不至于黑块）
                int w = (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * DpiX);
                int h = (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * DpiY);
                if (w <= 0 || h <= 0) return;
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) return;
                try
                {
                    using (var bmp = new System.Drawing.Bitmap(w, h))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(bmp))
                        {
                            IntPtr hdc = g.GetHdc();
                            try { Native.BitBlt(hdc, 0, 0, w, h, screen,
                                (int)SystemParameters.VirtualScreenLeft, (int)SystemParameters.VirtualScreenTop, Native.SRCCOPY); }
                            finally { g.ReleaseHdc(hdc); }
                        }
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            bmp.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        _desktop = src;
                    }
                }
                finally { Native.ReleaseDC(IntPtr.Zero, screen); }
                LastWallpaperCaptureMs = (long)NowMs();
                UpdateSheetPixels();
            }
            catch { _desktop = null; }
        }

        /// <summary>直接读当前壁纸图片文件，按屏幕"填充"画到屏幕大小。成功返回 true。</summary>
        bool TryLoadWallpaperFile()
        {
            try
            {
                string path = Native.GetWallpaperPath();
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;

                int w = (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * DpiX);
                int h = (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * DpiY);
                if (w <= 0 || h <= 0) return false;

                using (var src = new System.Drawing.Bitmap(path))
                {
                    if (src.Width <= 0 || src.Height <= 0) return false;
                    using (var canvas = new System.Drawing.Bitmap(w, h))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(canvas))
                        {
                            g.Clear(System.Drawing.Color.Black);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            // 填充(Fill)：等比缩放、居中裁剪铺满屏幕
                            float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
                            int dw = (int)Math.Ceiling(src.Width * scale);
                            int dh = (int)Math.Ceiling(src.Height * scale);
                            int dx = (w - dw) / 2, dy = (h - dh) / 2;
                            g.DrawImage(src, dx, dy, dw, dh);
                        }
                        var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            canvas.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        _desktop = bs;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>把壁纸缩放成 DIP 像素；网格只在必要时重建，保留正在进行的位移。</summary>
        void UpdateSheetPixels()
        {
            try
            {
                if (_desktop == null) return;
                var tb = new TransformedBitmap(_desktop, new ScaleTransform(1.0 / Math.Max(0.1, DpiX), 1.0 / Math.Max(0.1, DpiY)));
                int sw = (int)Math.Round(tb.Width);
                int sh = (int)Math.Round(tb.Height);
                if (sw <= 8 || sh <= 8) return;

                var px = new byte[sw * sh * 4];
                tb.CopyPixels(px, sw * 4, 0);
                _src = px; _sw = sw; _sh = sh;

                _cell = Math.Max(12, P("JellyGrid"));
                int gw = (int)(sw / _cell) + 2;
                int gh = (int)(sh / _cell) + 2;
                if (!_sheetReady || gw != _gw || gh != _gh)
                    BuildSheet();   // 首次或网格尺寸变了才重建（重建会清零位移）
            }
            catch { }
        }

        /// <summary>用当前 _sw/_sh/_cell 重建果冻网格（位移清零）。</summary>
        void BuildSheet()
        {
            try
            {
                if (_sw <= 8 || _sh <= 8) return;
                _gw = (int)(_sw / _cell) + 2;
                _gh = (int)(_sh / _cell) + 2;
                int n = _gw * _gh;
                _nodeX = new double[n]; _nodeY = new double[n];
                _dx = new double[n]; _dy = new double[n];
                _vx = new double[n]; _vy = new double[n];
                for (int j = 0; j < _gh; j++)
                    for (int i = 0; i < _gw; i++)
                    {
                        int idx = j * _gw + i;
                        _nodeX[idx] = i * _cell;
                        _nodeY[idx] = j * _cell;
                    }
                _sheetReady = true;
                _lastFrameMs = (long)NowMs();
            }
            catch { _sheetReady = false; }
        }

        /// <summary>
        /// 定期调用：检测用户是否换了壁纸。只有检测到变化才重新截屏更新素材，
        /// 平时几乎零开销，且不打断正在进行的震荡。
        /// </summary>
        public void RefreshWallpaper()
        {
            try
            {
                string path = Native.GetWallpaperPath();
                long mt = 0;
                if (!string.IsNullOrEmpty(path))
                {
                    try { mt = System.IO.File.GetLastWriteTimeUtc(path).Ticks; }
                    catch { }
                }
                if (path == _wpPath && mt == _wpMtime) return; // 没换壁纸
                _wpPath = path; _wpMtime = mt;
                CaptureWallpaper();
            }
            catch { }
        }

        /// <summary>
        /// 绘制整帧。originX/originY 是效果层窗口左上角在屏幕上的坐标（效果坐标是屏幕坐标）。
        /// </summary>
        public void Draw(DrawingContext dc, double originX, double originY, double nowMs)
        {
            if (_sheetReady)
            {
                PhysicsStep(nowMs);
                RenderPatch(dc, originX, originY);
            }

            double globalOpacity = P("GlobalOpacity");

            // 悬停光晕：平滑跟随鼠标 + 轻微呼吸
            if (B("HoverGlow") && HasMouse)
            {
                double targetX = LastMouse.X - originX, targetY = LastMouse.Y - originY;
                if (!_hoverValid) { _hoverX = targetX; _hoverY = targetY; _hoverValid = true; }
                else
                {
                    double k = Math.Clamp(0.22, 0.02, 1);
                    _hoverX += (targetX - _hoverX) * k;
                    _hoverY += (targetY - _hoverY) * k;
                }

                double hr = P("HoverGlowRadius");
                double ha = P("HoverGlowIntensity") * globalOpacity;
                double pulse = 0.72 + 0.28 * Math.Sin(2 * Math.PI * 0.7 * nowMs / 1000.0);
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * ha * pulse), 255, 240, 226), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * ha * pulse * 0.4), 255, 240, 226), 0.62));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 240, 226), 1));
                g.Freeze();
                dc.DrawEllipse(g, null, new Point(_hoverX, _hoverY), hr, hr);
            }
            else if (!HasMouse)
            {
                _hoverValid = false;
            }
        }

        // ---------- 果冻物理：弹簧质点 ----------
        void PhysicsStep(double nowMs)
        {
            double dt = _lastFrameMs <= 0 ? 0.016 : (nowMs - _lastFrameMs) / 1000.0;
            _lastFrameMs = (long)nowMs;
            dt = Math.Clamp(dt, 0.008, 0.05);

            bool btnDown = B("Enabled") && HasMouse &&
                           (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
            double R = P("JellyRadius");
            double strength = P("JellyStrength");
            double maxDisp = P("JellyMaxDisp");

            if (btnDown)
            {
                double px = LastMouse.X, py = LastMouse.Y;
                double r2 = R * R;
                int iMin = Math.Max(0, (int)((px - R) / _cell));
                int iMax = Math.Min(_gw - 1, (int)((px + R) / _cell));
                int jMin = Math.Max(0, (int)((py - R) / _cell));
                int jMax = Math.Min(_gh - 1, (int)((py + R) / _cell));
                for (int j = jMin; j <= jMax; j++)
                    for (int i = iMin; i <= iMax; i++)
                    {
                        int idx = j * _gw + i;
                        double ox = _nodeX[idx] - px, oy = _nodeY[idx] - py;
                        double dist2 = ox * ox + oy * oy;
                        if (dist2 > r2 || dist2 < 1e-6) continue;
                        double u = Math.Sqrt(dist2) / R;
                        double fall = (1 - u) * (1 - u);
                        double mag = strength * maxDisp * Math.Sin(Math.PI * u);   // 周围鼓起、边缘归零
                        double inv = 1.0 / Math.Sqrt(dist2);
                        double tx = ox * inv * mag, ty = oy * inv * mag;
                        // 强跟随手指：把质点拉向目标鼓包
                        double kFollow = 26;
                        _vx[idx] += (tx - _dx[idx]) * kFollow * dt;
                        _vy[idx] += (ty - _dy[idx]) * kFollow * dt;
                    }
            }

            // 弹簧回弹 + 阻尼
            double kSpring = P("JellyStiffness") * 0.12;
            double kDamp = P("JellyDamping") * 18;
            double damp = Math.Exp(-kDamp * dt);
            _anyDisp = false;
            int n = _gw * _gh;
            for (int idx = 0; idx < n; idx++)
            {
                if (Math.Abs(_dx[idx]) < 0.02 && Math.Abs(_dy[idx]) < 0.02 &&
                    Math.Abs(_vx[idx]) < 0.05 && Math.Abs(_vy[idx]) < 0.05) continue;

                _vx[idx] += (-_dx[idx]) * kSpring * dt;
                _vy[idx] += (-_dy[idx]) * kSpring * dt;
                _vx[idx] *= damp;
                _vy[idx] *= damp;
                _dx[idx] += _vx[idx] * dt;
                _dy[idx] += _vy[idx] * dt;

                double d2 = _dx[idx] * _dx[idx] + _dy[idx] * _dy[idx];
                if (d2 > maxDisp * maxDisp)
                {
                    double s = maxDisp / Math.Sqrt(d2);
                    _dx[idx] *= s; _dy[idx] *= s;
                    _vx[idx] *= 0.6; _vy[idx] *= 0.6;
                }
                if (d2 < 0.02 && _vx[idx] * _vx[idx] + _vy[idx] * _vy[idx] < 0.2)
                {
                    _dx[idx] = _dy[idx] = _vx[idx] = _vy[idx] = 0;
                    continue;
                }
                _anyDisp = true;
            }
        }

        // ---------- 把变形的壁纸渲染到效果层 ----------
        void RenderPatch(DrawingContext dc, double originX, double originY)
        {
            if (!_anyDisp) return;

            // 计算发生位移的区域（质点的静止位置范围 + 一格余量）
            int x0 = _sw, y0 = _sh, x1 = 0, y1 = 0;
            bool any = false;
            int n = _gw * _gh;
            for (int idx = 0; idx < n; idx++)
            {
                if (Math.Abs(_dx[idx]) < 0.4 && Math.Abs(_dy[idx]) < 0.4) continue;
                double nx = _nodeX[idx], ny = _nodeY[idx];
                int xi = (int)nx, yi = (int)ny;
                if (xi < x0) x0 = xi; if (yi < y0) y0 = yi;
                if (xi > x1) x1 = xi; if (yi > y1) y1 = yi;
                any = true;
            }
            if (!any) return;

            x0 = Math.Max(0, (int)(x0 - _cell));
            y0 = Math.Max(0, (int)(y0 - _cell));
            x1 = Math.Min(_sw - 1, (int)(x1 + _cell));
            y1 = Math.Min(_sh - 1, (int)(y1 + _cell));
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            if (bw <= 0 || bh <= 0) return;

            if (_patch == null || _patchW != bw || _patchH != bh)
            {
                _patch = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Bgra32, null);
                _patchPix = new byte[bw * bh * 4];
                _patchW = bw; _patchH = bh;
            }

            // 逐像素：按位移场重采样壁纸（双线性），写出变形的果冻画面
            for (int py = 0; py < bh; py++)
            {
                double sy0 = y0 + py;
                int row = py * bw;
                for (int px = 0; px < bw; px++)
                {
                    double sx0 = x0 + px;
                    double ddx, ddy;
                    DispAt(sx0, sy0, out ddx, out ddy);
                    double sx = sx0 - ddx;
                    double sy = sy0 - ddy;
                    if (sx < 0) sx = 0; else if (sx > _sw - 1.0001) sx = _sw - 1.0001;
                    if (sy < 0) sy = 0; else if (sy > _sh - 1.0001) sy = _sh - 1.0001;

                    int xi = (int)sx, yi = (int)sy;
                    double fx = sx - xi, fy = sy - yi;
                    int p00 = (yi * _sw + xi) * 4;
                    int p10 = p00 + 4;
                    int p01 = p00 + _sw * 4;
                    int p11 = p01 + 4;
                    int po = (row + px) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double top = _src[p00 + c] + (_src[p10 + c] - _src[p00 + c]) * fx;
                        double bot = _src[p01 + c] + (_src[p11 + c] - _src[p01 + c]) * fx;
                        double v = top + (bot - top) * fy;
                        _patchPix[po + c] = (byte)(v + 0.5);
                    }
                }
            }

            _patch.WritePixels(new Int32Rect(0, 0, bw, bh), _patchPix, bw * 4, 0);
            dc.DrawImage(_patch, new Rect(x0 - originX, y0 - originY, bw, bh));
        }

        /// <summary>双线性插值取 (x,y) 处的网格位移。</summary>
        void DispAt(double x, double y, out double ddx, out double ddy)
        {
            int i = (int)(x / _cell);
            int j = (int)(y / _cell);
            if (i < 0) i = 0; else if (i > _gw - 2) i = _gw - 2;
            if (j < 0) j = 0; else if (j > _gh - 2) j = _gh - 2;
            double fx = (x - i * _cell) / _cell;
            double fy = (y - j * _cell) / _cell;
            if (fx < 0) fx = 0; else if (fx > 1) fx = 1;
            if (fy < 0) fy = 0; else if (fy > 1) fy = 1;

            int a = j * _gw + i;
            int b = a + 1;
            int c = a + _gw;
            int d = c + 1;
            double dxTop = _dx[a] + (_dx[b] - _dx[a]) * fx;
            double dxBot = _dx[c] + (_dx[d] - _dx[c]) * fx;
            double dyTop = _dy[a] + (_dy[b] - _dy[a]) * fx;
            double dyBot = _dy[c] + (_dy[d] - _dy[c]) * fx;
            ddx = dxTop + (dxBot - dxTop) * fy;
            ddy = dyTop + (dyBot - dyTop) * fy;
        }
    }
}
