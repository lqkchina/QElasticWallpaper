using System;

namespace QElasticWallpaper.Core
{
    /// <summary>一次点击产生的单个效果（按压 + 波纹）。</summary>
    public sealed class RippleEffect
    {
        public double X, Y;        // 屏幕坐标（物理像素）
        public double StartMs;     // 起始时间
        public double DurationMs;

        // 由当前参数快照而来，避免动画过程中参数突变导致跳变
        public double BaseRadius;
        public double Growth;
        public double RingThickness;
        public double Intensity;
        public double Bounce;
        public double Damping;
        public int RippleCount;
        public bool Finished;

        public RippleEffect(double x, double y, double nowMs, double durationMs)
        {
            X = x; Y = y; StartMs = nowMs; DurationMs = durationMs;
        }
    }
}
