using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 渲染控制器：维护所有正在播放的效果，在效果层上每帧绘制。
    /// 负责"真人皮肤按压 + 果冻Q弹回弹 + 壁纸形变"的视觉数学。
    /// </summary>
    public sealed class RippleController
    {
        readonly List<Param> _cfg;
        readonly List<RippleEffect> _active = new List<RippleEffect>();
        readonly Random _rnd = new Random();

        // 捕获下来的壁纸（物理像素）。点击时把这一小块壁纸做"凸透镜鼓起 + 果冻回弹"，
        // 让壁纸真的被按变形，而不是在它上面画圆圈。
        BitmapSource _desktop;
        public double DpiX = 1.0, DpiY = 1.0;
        public long LastWallpaperCaptureMs;

        // 悬停光晕跟随的最近鼠标位置
        public Native.POINT LastMouse;
        public bool HasMouse;

        // 悬停光晕的平滑跟随位置（做果冻呼吸，让壁纸"动起来"）
        double _hoverX, _hoverY;
        bool _hoverValid;

        // ---- 诊断统计 ----
        public long TotalClicks;            // 钩子送到这里准备触发的点击次数
        public long SpawnedEffects;         // 实际生成的效果数
        public long BlockedByDisabled;      // 因"启用效果=关"被拦下的点击数
        public int ActiveCount { get { lock (_active) return _active.Count; } }

        public RippleController(List<Param> cfg) => _cfg = cfg;

        double P(string k) => Get(k).Value;
        bool B(string k) => Get(k).BoolValue;

        Param Get(string k) => _cfg.Find(p => p.Key == k);

        /// <summary>在屏幕坐标 (x,y) 生成一次点击效果。</summary>
        public void Spawn(double x, double y)
        {
            TotalClicks++;
            if (!B("Enabled"))
            {
                BlockedByDisabled++;
                return;
            }

            double duration = P("Duration");
            double variation = P("RandomVariation");
            var e = new RippleEffect(x, y, NowMs(), duration);

            // 把当前参数 + 随机波动快照进效果
            double v = 1 + (_rnd.NextDouble() - 0.5) * 2 * variation;
            e.BaseRadius = P("BaseRadius") * v;
            e.Growth = P("RadiusGrowth") * v;
            e.RingThickness = P("RingThickness");
            e.Intensity = P("Intensity");
            e.Bounce = P("Bounce");
            e.Damping = P("Damping");
            e.RippleCount = (int)Math.Max(1, P("RippleCount"));
            e.DurationMs = duration * (1 + (_rnd.NextDouble() - 0.5) * 2 * variation * 0.5);

            lock (_active)
            {
                _active.Add(e);
                int max = (int)P("MaxEffects");
                while (_active.Count > max) _active.RemoveAt(0); // 防爆：丢弃最老的
            }
            SpawnedEffects++;
        }

        public void SetMouse(Native.POINT pt)
        {
            LastMouse = pt;
            HasMouse = true;
        }

        /// <summary>目标渲染帧率。</summary>
        public double GetFps() => P("TargetFps");

        /// <summary>诊断：点击/效果统计。</summary>
        public string Stats =>
            $"收到点击:{TotalClicks}  已生成效果:{SpawnedEffects}  被禁用拦截:{BlockedByDisabled}  当前活动:{ActiveCount}";

        public static double NowMs()
            => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 绘制整帧。originX/originY 是效果层窗口左上角在屏幕上的坐标，
        /// 效果坐标是屏幕坐标，需要换算到窗口本地坐标。
        /// </summary>
        public void Draw(DrawingContext dc, double originX, double originY, double nowMs)
        {
            lock (_active)
            {
                _active.RemoveAll(e => e.Finished || nowMs - e.StartMs >= e.DurationMs);
            }

            double globalOpacity = P("GlobalOpacity");
            double skinShading = P("SkinShading");
            double pressDepth = P("PressDepth");
            double highlight = P("Highlight");
            double edgeSoft = P("EdgeSoftness");

            // 基准色：越接近 1 越像皮肤，越接近 0 越接近暖白
            var baseColor = LerpColor(Color.FromRgb(226, 172, 148),
                                      Color.FromRgb(240, 210, 200), skinShading);
            var dark = Darken(baseColor, pressDepth);
            var bright = Lighten(baseColor, 0.35);

            List<RippleEffect> snap;
            lock (_active) snap = new List<RippleEffect>(_active);

            foreach (var e in snap)
            {
                double p = Math.Clamp((nowMs - e.StartMs) / e.DurationMs, 0, 1);
                double px = e.X - originX;
                double py = e.Y - originY;

                if (_desktop != null)
                    DrawPressDeform(dc, px, py, p, e, baseColor, dark, bright, highlight, pressDepth, globalOpacity);
                else
                    DrawPressFallback(dc, px, py, p, e, baseColor, dark, bright, highlight, globalOpacity);
            }

            // 悬停果冻光晕（让壁纸"动起来"）：平滑跟随鼠标 + 轻微呼吸起伏
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

        // ---------- 壁纸"凸透镜鼓起 + 果冻回弹"（真·壁纸形变，不是画圆圈） ----------
        void DrawPressDeform(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, Color dark, Color bright, double highlight, double pressDepth, double globalOpacity)
        {
            // 平滑按压进入（快）→ 保持 → 平滑释放
            double pressIn = SmoothStep(0, 0.14, p);
            double release = SmoothStep(0.32, 1.0, p);
            double press = pressIn * (1 - release);

            // 果冻阻尼振荡：仅按下后起振，做丝滑的"果冻Q弹"回弹（放大倍率来回起伏）
            double jelly = Math.Max(0, p - 0.14);
            double wobble = Math.Sin(2 * Math.PI * e.Bounce * 4 * jelly) * Math.Exp(-e.Damping * 4 * jelly);

            double R = e.BaseRadius * (0.72 + 0.28 * press) * (1 + 0.18 * wobble);
            double bulge = e.Intensity * press * 0.32 * (1 + 0.55 * wobble);   // 中心凸起(放大)强度
            if (R <= 1.0 || bulge <= 0.001 || press <= 0.002) return;

            // 用多圈同心采样做"中心放大、边缘还原"的平滑透镜变形（无缝衔接，不露圈）
            double vsW = SystemParameters.VirtualScreenWidth;
            double vsH = SystemParameters.VirtualScreenHeight;
            var rect = new Rect(0, 0, vsW, vsH);
            int N = 10;
            for (int i = 0; i < N; i++)
            {
                double fr0 = (double)i / N, fr1 = (double)(i + 1) / N;
                double fr = (fr0 + fr1) / 2;
                double s = 1 + bulge * (1 - fr) * (1 - fr);
                if (Math.Abs(s - 1) < 0.005) continue;

                var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Point(px, py), R * fr1, R * fr1),
                    new EllipseGeometry(new Point(px, py), R * fr0, R * fr0));
                dc.PushClip(ring);
                dc.PushTransform(ScaleAround(px, py, s));
                dc.DrawImage(_desktop, rect);
                dc.Pop();
                dc.Pop();
            }

            // 按压阴影（中心略暗，模拟按下去的深度）+ 边缘被拉伸的柔光高光
            double alpha = e.Intensity * globalOpacity * press * 0.55;
            if (alpha > 0.003)
            {
                var c = new Point(px, py);
                var sh = new RadialGradientBrush();
                sh.GradientStops.Add(new GradientStop(WithAlpha(dark, alpha), 0.0));
                sh.GradientStops.Add(new GradientStop(WithAlpha(dark, alpha * 0.25), 0.45));
                sh.GradientStops.Add(new GradientStop(WithAlpha(dark, 0), 0.8));
                sh.Freeze();
                dc.DrawEllipse(sh, null, c, R * 0.8, R * 0.8);

                if (highlight > 0.02)
                {
                    var hi = new RadialGradientBrush();
                    hi.GradientStops.Add(new GradientStop(WithAlpha(bright, 0), 0.55));
                    hi.GradientStops.Add(new GradientStop(WithAlpha(bright, alpha * highlight * 0.7), 0.88));
                    hi.GradientStops.Add(new GradientStop(WithAlpha(bright, 0), 1.0));
                    hi.Freeze();
                    dc.DrawEllipse(hi, null, c, R, R);
                }
            }
        }

        // ---------- 兜底：万一壁纸没截到，画一个柔和的皮肤凹陷（同样无圆圈） ----------
        void DrawPressFallback(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, Color dark, Color bright, double highlight, double globalOpacity)
        {
            double pressIn = SmoothStep(0, 0.14, p);
            double release = SmoothStep(0.32, 1.0, p);
            double press = pressIn * (1 - release);

            double jelly = Math.Max(0, p - 0.14);
            double wobble = 1 + 0.42 * e.Bounce *
                Math.Sin(2 * Math.PI * e.Bounce * 5 * jelly) * Math.Exp(-e.Damping * 5 * jelly);

            double R = e.BaseRadius * (0.62 + 0.38 * press) * wobble;
            double alpha = e.Intensity * globalOpacity * (0.30 + 0.70 * press);
            if (R <= 0.5 || alpha <= 0.003) return;

            var c = new Point(px, py);
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(WithAlpha(dark, alpha), 0.0));
            b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, alpha * 0.62), 0.50));
            b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, alpha * 0.30), 0.72));
            b.GradientStops.Add(new GradientStop(WithAlpha(bright, alpha * 0.45 * highlight), 0.86));
            b.GradientStops.Add(new GradientStop(WithAlpha(bright, 0), 1.0));
            b.Freeze();
            dc.DrawEllipse(b, null, c, R, R);
        }

        /// <summary>把整个屏幕(壁纸)截下来，作为形变素材。失败则 _desktop 保持为空并走兜底。</summary>
        public void CaptureWallpaper()
        {
            try
            {
                int w = (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * DpiX);
                int h = (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * DpiY);
                int x = (int)SystemParameters.VirtualScreenLeft;
                int y = (int)SystemParameters.VirtualScreenTop;
                if (w <= 0 || h <= 0) return;

                using (var bmp = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            IntPtr screen = Native.GetDC(IntPtr.Zero);
                            try { Native.BitBlt(hdc, 0, 0, w, h, screen, x, y, Native.SRCCOPY); }
                            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
                        }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    var src = Imaging.CreateBitmapSourceFromHBitmap(
                        bmp.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    _desktop = src;
                }
                LastWallpaperCaptureMs = NowMs();
            }
            catch { _desktop = null; }
        }

        static MatrixTransform ScaleAround(double cx, double cy, double s)
        {
            var m = Matrix.Identity;
            m.Translate(-cx, -cy);
            m.Scale(s, s);
            m.Translate(cx, cy);
            return new MatrixTransform(m);
        }

        // ---------- 颜色工具 ----------
        static double SmoothStep(double a, double b, double x)
        {
            double t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        static Color Darken(Color c, double f)
            => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

        static Color Lighten(Color c, double f)
            => Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * f),
                (byte)Math.Min(255, c.G + (255 - c.G) * f),
                (byte)Math.Min(255, c.B + (255 - c.B) * f));

        static Color WithAlpha(Color c, double a)
        {
            a = Math.Clamp(a, 0, 1);
            return Color.FromArgb((byte)(255 * a), c.R, c.G, c.B);
        }
    }
}
