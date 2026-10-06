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

                DrawJelly(dc, px, py, p, e, globalOpacity);
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

        // ---------- 果冻按压 + 回弹：一个透明、光泽、强烈Q弹的果冻包 ----------
        // 不是皮肤、不是水波、不是圆圈 —— 就是一块透明的果冻：被按进去会凹，
        // 松手带光泽来回Q弹（压扁→鼓起→回荡），全程只有果冻在动。
        void DrawJelly(DrawingContext dc, double px, double py, double p, RippleEffect e,
            double globalOpacity)
        {
            // 按压进入 + 果冻阻尼震荡（按下、回弹全程都Q弹）+ 释放淡出
            double tIn = SmoothStep(0, 0.10, p);                 // 按压进入
            double tJ = Math.Max(0, p - 0.05);                   // 起振点
            double decay = Math.Exp(-e.Damping * 2.0 * tJ);      // 果冻阻尼衰减（放慢，弹得久）
            double wob = decay * (Math.Sin(2 * Math.PI * e.Bounce * 2.2 * tJ)
                                  + 0.35 * Math.Sin(2 * Math.PI * e.Bounce * 4.6 * tJ)); // 双频，更Q
            double fade = 1 - SmoothStep(0.75, 1.0, p);          // 释放淡出

            double R = e.BaseRadius * (0.55 + 0.45 * tIn) * (1 + 0.30 * wob) * (0.55 + 0.45 * fade);
            double alpha = e.Intensity * globalOpacity * (0.35 + 0.65 * tIn) * fade;
            if (R <= 1.0 || alpha <= 0.003 || fade <= 0.002) return;

            var c = new Point(px, py);

            // 果冻被按扁又弹起：竖直方向的果冻形变（压扁→拉长→回荡）
            double squash = 1 + 0.20 * Math.Cos(2 * Math.PI * e.Bounce * 2.2 * tJ) * decay;
            double rx = R, ry = R / squash;

            // 1) 半透明果冻体（壁纸从里面透出来）
            var body = new RadialGradientBrush();
            body.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(255, 244, 235), alpha * 0.16), 0.0));
            body.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(255, 244, 235), alpha * 0.08), 0.55));
            body.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(255, 244, 235), alpha * 0.22), 1.0));
            body.Freeze();
            dc.DrawEllipse(body, null, c, rx, ry);

            // 2) 边缘柔影（下半稍暗），让果冻有立体厚度
            var edge = new RadialGradientBrush();
            edge.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(140, 120, 110), 0), 0.0));
            edge.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(140, 120, 110), 0), 0.72));
            edge.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(140, 120, 110), alpha * 0.12), 1.0));
            edge.Freeze();
            dc.DrawEllipse(edge, null, c, rx, ry);

            // 3) 移动的光泽高光点（果冻的Q弹光泽），随震荡滑动
            double gx = px - rx * 0.30 + rx * 0.14 * wob;
            double gy = py - ry * 0.32 + ry * 0.10 * wob;
            double gr = Math.Max(rx, ry) * 0.42;
            var gloss = new RadialGradientBrush();
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha * 0.85), 255, 255, 255), 0.0));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha * 0.30), 255, 255, 255), 0.5));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            gloss.Freeze();
            dc.DrawEllipse(gloss, null, new Point(gx, gy), gr, gr);

            // 4) 按压点的小凹坑（中心微暗），表现"被按进去"
            var pit = new RadialGradientBrush();
            pit.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(160, 140, 130), alpha * 0.16), 0.0));
            pit.GradientStops.Add(new GradientStop(WithAlpha(Color.FromRgb(160, 140, 130), 0), 0.5));
            pit.Freeze();
            dc.DrawEllipse(pit, null, c, rx * 0.6, ry * 0.6);
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
                    var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        bmp.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    _desktop = src;
                }
                LastWallpaperCaptureMs = (long)NowMs();
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
