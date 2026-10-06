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

                DrawPress(dc, px, py, p, e, baseColor, dark, bright, highlight, skinShading, globalOpacity);
                DrawRings(dc, px, py, p, e, baseColor, edgeSoft, globalOpacity);
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

        // ---------- 真人皮肤按压 + 果冻回弹 ----------
        void DrawPress(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, Color dark, Color bright, double highlight, double skinShading, double globalOpacity)
        {
            // 平滑按压进入（快）→ 保持 → 平滑释放，释放瞬间果冻回弹
            double pressIn = SmoothStep(0, 0.14, p);
            double release = SmoothStep(0.32, 1.0, p);
            double press = pressIn * (1 - release);

            // 果冻阻尼振荡：仅在按下后起振，做丝滑的"果冻Q弹"回弹
            double jelly = Math.Max(0, p - 0.14);
            double wobble = 1 + 0.42 * e.Bounce *
                Math.Sin(2 * Math.PI * e.Bounce * 5 * jelly) * Math.Exp(-e.Damping * 5 * jelly);

            double R = e.BaseRadius * (0.62 + 0.38 * press) * wobble;
            double alpha = e.Intensity * globalOpacity * (0.30 + 0.70 * press);
            if (R <= 0.5 || alpha <= 0.003) return;

            var c = new Point(px, py);

            // 真人皮肤按压：中心凹陷阴影 → 皮肤色 → 边缘被拉伸的隆起高光 → 透明
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(WithAlpha(dark, alpha), 0.0));
            b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, alpha * 0.62), 0.50));
            b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, alpha * 0.30), 0.72));
            b.GradientStops.Add(new GradientStop(WithAlpha(bright, alpha * 0.45 * highlight), 0.86));
            b.GradientStops.Add(new GradientStop(WithAlpha(bright, 0), 1.0));
            b.Freeze();
            dc.DrawEllipse(b, null, c, R, R);
        }

        // ---------- 柔和水波（径向软带，不再是一圈圈白圈） ----------
        void DrawRings(DrawingContext dc, double px, double py, double p, RippleEffect e,
            Color baseColor, double edgeSoft, double globalOpacity)
        {
            var c = new Point(px, py);
            int n = Math.Max(1, Math.Min(4, e.RippleCount));   // 太多环会乱，限制在 4 层内
            for (int i = 0; i < n; i++)
            {
                double delay = i * 0.16;
                double t = Math.Clamp((p - delay) / 0.72, 0, 1);
                if (t <= 0 || t >= 1) continue;

                double grow = SmoothStep(0, 1, t);
                double R = e.BaseRadius * 0.7 + e.Growth * grow;
                double a = e.Intensity * globalOpacity * (1 - grow) * (i == 0 ? 0.30 : 0.18);
                if (R <= 0.5 || a <= 0.003) continue;

                // 水波软带：径向渐变在半径 R（=椭圆边缘）处形成一条柔和的波峰
                double W = Math.Max(e.RingThickness * 0.6, R * 0.14);
                double u0 = Math.Clamp((R - W) / R, 0, 0.99);
                var b = new RadialGradientBrush();
                b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 0), 0.0));
                b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 0), u0));
                b.GradientStops.Add(new GradientStop(WithAlpha(baseColor, a), 1.0));
                b.Freeze();
                dc.DrawEllipse(b, null, c, R, R);

                // 外缘羽化：再叠一层更大的淡波，让水波边缘柔和过渡
                if (edgeSoft > 0.03)
                {
                    var ob = new RadialGradientBrush();
                    ob.GradientStops.Add(new GradientStop(WithAlpha(baseColor, a * 0.4), u0));
                    ob.GradientStops.Add(new GradientStop(WithAlpha(baseColor, 0), 1.0));
                    ob.Freeze();
                    dc.DrawEllipse(ob, null, c, R * (1.15 + 0.25 * edgeSoft), R * (1.15 + 0.25 * edgeSoft));
                }
            }
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
