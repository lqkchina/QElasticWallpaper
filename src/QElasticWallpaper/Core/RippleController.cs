using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 渲染控制器：维护所有正在播放的效果，在效果层上每帧绘制。
    /// 负责"真人皮肤按压 + Q弹回弹 + 波纹扩散"的视觉数学。
    /// </summary>
    public sealed class RippleController
    {
        readonly List<Param> _cfg;
        readonly List<RippleEffect> _active = new List<RippleEffect>();
        readonly Random _rnd = new Random();

        // 悬停光晕跟随的最近鼠标位置
        public Native.POINT LastMouse;
        public bool HasMouse;

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

                DrawPress(dc, px, py, p, e, baseColor, dark, bright, highlight, skinShading, globalOpacity);
                DrawRings(dc, px, py, p, e, baseColor, edgeSoft, globalOpacity);
            }

            // 悬停光晕
            if (B("HoverGlow") && HasMouse)
            {
                double hr = P("HoverGlowRadius");
                double ha = P("HoverGlowIntensity") * globalOpacity;
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * ha), 255, 255, 255), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                g.Freeze();
                dc.DrawEllipse(g, null,
                    new Point(LastMouse.X - originX, LastMouse.Y - originY), hr, hr);
            }
        }

        // ---------- 按压的"皮肤凹陷 + Q弹回弹" ----------
        void DrawPress(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, Color dark, Color bright, double highlight, double skinShading, double globalOpacity)
        {
            // 阻尼正弦振荡：Bounce 次回弹，Damping 控制衰减
            double osc = Math.Sin(2 * Math.PI * e.Bounce * p) * Math.Exp(-e.Damping * p);
            double pressR = e.BaseRadius * (1 + 0.35 * osc);
            double alpha = e.Intensity * (1 - p); // 峰值后逐渐消散

            var center = new Point(px, py);

            // 皮肤凹陷：径向渐变（中心暗→边缘透明），模拟按进去的阴影
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(WithAlpha(dark, alpha * globalOpacity), 0.0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, alpha * 0.72 * globalOpacity), 0.5));
            brush.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 0), 1.0));
            brush.Freeze();
            dc.DrawEllipse(brush, null, center, pressR, pressR);

            // 皮肤被拉伸的高光边缘（更亮、更薄），让"真人皮肤"感更真实
            if (highlight > 0.01)
            {
                var hp = new Pen(new SolidColorBrush(WithAlpha(bright, alpha * highlight * globalOpacity)), 2);
                hp.Freeze();
                dc.DrawEllipse(null, hp, center, pressR * 0.98, pressR * 0.98);
            }

            // 一层淡淡的扩散辉光
            if (skinShading > 0.02)
            {
                var glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(WithAlpha(bright, alpha * 0.15 * skinShading * globalOpacity), 0.55));
                glow.GradientStops.Add(new GradientStop(WithAlpha(bright, 0), 1.0));
                glow.Freeze();
                dc.DrawEllipse(glow, null, center, pressR * 1.9, pressR * 1.9);
            }
        }

        // ---------- 向外扩散的波纹环（同样带 Q 弹振荡） ----------
        void DrawRings(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, double edgeSoft, double globalOpacity)
        {
            var center = new Point(px, py);
            int n = e.RippleCount;
            for (int i = 0; i < n; i++)
            {
                double fi = n == 1 ? 0.5 : i / (double)(n - 1);
                double ringDur = e.DurationMs * 0.72;
                double start = e.StartMs + fi * e.DurationMs * 0.32; // 逐环错开出现
                double t = (NowMs() - start) / ringDur;
                if (t < 0 || t > 1) continue;

                // 半径：基准 + 扩散 + 果冻振荡
                double rt = e.BaseRadius + e.Growth * t;
                rt += e.BaseRadius * 0.25 * Math.Sin(2 * Math.PI * e.Bounce * t) * Math.Exp(-e.Damping * t);
                if (rt <= 0) continue;

                double alpha = e.Intensity * (1 - t) * 0.55 * globalOpacity;
                double th = Math.Max(1, e.RingThickness * (1 - 0.5 * t));

                var pen = new Pen(new SolidColorBrush(WithAlpha(baseColor, alpha)), th);
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                pen.Freeze();
                dc.DrawEllipse(null, pen, center, rt, rt);

                // 边缘柔化：再叠一圈更淡更宽的晕
                if (edgeSoft > 0.02)
                {
                    var soft = new Pen(new SolidColorBrush(WithAlpha(baseColor, alpha * 0.35)), th * (2 + 3 * edgeSoft));
                    soft.Freeze();
                    dc.DrawEllipse(null, soft, center, rt, rt);
                }
            }
        }

        // ---------- 颜色工具 ----------
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
