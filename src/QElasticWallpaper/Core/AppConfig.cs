using System;
using System.Collections.Generic;

namespace QElasticWallpaper.Core
{
    /// <summary>参数控件种类。</summary>
    public enum ParamKind
    {
        /// <summary>开关（是否启用某功能）</summary>
        Bool,
        /// <summary>滑块（连续数值）</summary>
        Slider,
        /// <summary>下拉选项（枚举）</summary>
        Enum
    }

    /// <summary>一个可调节参数。</summary>
    public class Param
    {
        public string Key;          // 内部唯一键，用于持久化
        public string Label;        // 界面显示名
        public string Unit;         // 单位（如 px、ms、%）
        public ParamKind Kind;
        public double Min, Max;
        public double Value;
        public bool BoolValue;      // Kind==Bool 时使用
        public string[] EnumValues; // Kind==Enum 时使用
        public string Description;  // 悬停提示

        public double Slider => Value;
        public bool Flag => BoolValue;
        public int Index => (int)Value;

        public void SetFromDouble(double v)
        {
            if (Kind == ParamKind.Bool) BoolValue = v > 0.5;
            else Value = Math.Clamp(v, Min, Max);
        }

        public void SetFromString(string s)
        {
            if (Kind == ParamKind.Bool) BoolValue = string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            else if (Kind == ParamKind.Enum)
            {
                for (int i = 0; i < EnumValues.Length; i++)
                    if (string.Equals(EnumValues[i], s, StringComparison.OrdinalIgnoreCase)) { Value = i; break; }
            }
            else if (double.TryParse(s, out double d)) SetFromDouble(d);
        }

        public Param Clone()
        {
            var p = (Param)MemberwiseClone();
            if (EnumValues != null) p.EnumValues = (string[])EnumValues.Clone();
            return p;
        }
    }

    /// <summary>
    /// 全部可调参数 + 预设。所有参数都在这里登记，
    /// 设置窗口会自动遍历生成控件，保证"所有参数都可手动调节"。
    /// </summary>
    public static class AppConfig
    {
        public static List<Param> BuildDefaultParams()
        {
            var list = new List<Param>();

            Add(list, new Param
            {
                Key = "Enabled", Label = "启用效果", Kind = ParamKind.Bool, BoolValue = true,
                Description = "总开关。关闭后点击桌面不再出现任何效果。"
            });
            Add(list, new Param
            {
                Key = "TriggerMode", Label = "触发范围", Kind = ParamKind.Enum,
                EnumValues = new[] { "仅桌面壁纸", "桌面+应用背景", "任意左键点击" },
                Description = "仅桌面壁纸：只在壁纸空白处点击触发；任意左键点击：点哪里都触发。"
            });
            Add(list, new Param
            {
                Key = "RippleCount", Label = "波纹层数", Unit = "层", Kind = ParamKind.Slider, Min = 1, Max = 16, Value = 3,
                Description = "一次点击向外扩散的柔和水波带数量，越多越饱满（建议 2~4 最干净）。"
            });
            Add(list, new Param
            {
                Key = "BaseRadius", Label = "按压基准半径", Unit = "px", Kind = ParamKind.Slider, Min = 15, Max = 400, Value = 55,
                Description = "手指按压形成的初始皮肤凹陷大小。"
            });
            Add(list, new Param
            {
                Key = "RadiusGrowth", Label = "波纹扩散距离", Unit = "px", Kind = ParamKind.Slider, Min = 0, Max = 1200, Value = 320,
                Description = "水波从中心向外扩散的行程长度，越大波及范围越广。"
            });
            Add(list, new Param
            {
                Key = "RingThickness", Label = "波纹粗细", Unit = "px", Kind = ParamKind.Slider, Min = 1, Max = 60, Value = 16,
                Description = "水波带的宽度，越大越柔和越像水波。"
            });
            Add(list, new Param
            {
                Key = "Intensity", Label = "整体强度", Unit = "％", Kind = ParamKind.Slider, Min = 0.05, Max = 1, Value = 0.9,
                Description = "所有效果的最大透明度/浓度，越大越明显。"
            });
            Add(list, new Param
            {
                Key = "Bounce", Label = "Q弹回弹次数", Kind = ParamKind.Slider, Min = 0, Max = 12, Value = 4,
                Description = "皮肤按压后回弹振荡的次数，越大越'果冻感'。设为 0 则只压不回弹。"
            });
            Add(list, new Param
            {
                Key = "Damping", Label = "回弹衰减", Kind = ParamKind.Slider, Min = 0.2, Max = 8, Value = 1.8,
                Description = "振荡衰减速度。越小回弹越久越软；越大回弹越快越硬。"
            });
            Add(list, new Param
            {
                Key = "Duration", Label = "单次动画时长", Unit = "ms", Kind = ParamKind.Slider, Min = 150, Max = 4000, Value = 1200,
                Description = "一次按压从出现到完全消散的总时间。"
            });
            Add(list, new Param
            {
                Key = "PressDepth", Label = "按压凹陷深度", Unit = "％", Kind = ParamKind.Slider, Min = 0, Max = 1, Value = 0.6,
                Description = "中心暗色凹陷的深浅，模拟手指按进皮肤的阴影。"
            });
            Add(list, new Param
            {
                Key = "SkinShading", Label = "真人皮肤感", Unit = "％", Kind = ParamKind.Slider, Min = 0, Max = 1, Value = 0.7,
                Description = "0 = 纯色扁平波纹；1 = 皮肤般的柔光渐变与高光，更像真人皮肤按压。"
            });
            Add(list, new Param
            {
                Key = "Highlight", Label = "高光强度", Unit = "％", Kind = ParamKind.Slider, Min = 0, Max = 1, Value = 0.55,
                Description = "按压边缘被拉伸皮肤的亮圈强度。"
            });
            Add(list, new Param
            {
                Key = "EdgeSoftness", Label = "边缘柔化", Unit = "％", Kind = ParamKind.Slider, Min = 0, Max = 1, Value = 0.7,
                Description = "水波边缘的羽化程度，越大越柔和。"
            });
            Add(list, new Param
            {
                Key = "GlobalOpacity", Label = "整体透明度", Unit = "％", Kind = ParamKind.Slider, Min = 0.05, Max = 1, Value = 1.0,
                Description = "整个效果层的不透明度。"
            });
            Add(list, new Param
            {
                Key = "RandomVariation", Label = "随机波动", Unit = "％", Kind = ParamKind.Slider, Min = 0, Max = 0.8, Value = 0.2,
                Description = "每次点击在半径/强度/时长上加一点随机差异，避免千篇一律。"
            });
            Add(list, new Param
            {
                Key = "MaxEffects", Label = "同屏效果上限", Kind = ParamKind.Slider, Min = 1, Max = 100, Value = 30,
                Description = "快速连续点击时，最多同时存在的效果数量，防卡顿。"
            });
            Add(list, new Param
            {
                Key = "TargetFps", Label = "渲染帧率", Unit = "fps", Kind = ParamKind.Slider, Min = 20, Max = 240, Value = 60,
                Description = "动画渲染帧率，越高越流畅、越耗电。"
            });
            Add(list, new Param
            {
                Key = "HoverGlow", Label = "悬停光晕", Kind = ParamKind.Bool, BoolValue = false,
                Description = "鼠标悬停在桌面时显示一圈淡淡光晕跟随。"
            });
            Add(list, new Param
            {
                Key = "HoverGlowRadius", Label = "光晕半径", Unit = "px", Kind = ParamKind.Slider, Min = 20, Max = 300, Value = 60,
                Description = "悬停光晕的半径。"
            });
            Add(list, new Param
            {
                Key = "HoverGlowIntensity", Label = "光晕强度", Unit = "％", Kind = ParamKind.Slider, Min = 0.03, Max = 0.5, Value = 0.12,
                Description = "悬停光晕的透明度。"
            });
            Add(list, new Param
            {
                Key = "SoundEnabled", Label = "按压音效", Kind = ParamKind.Bool, BoolValue = false,
                Description = "按压时播放系统提示音（可选）。"
            });
            Add(list, new Param
            {
                Key = "SoundVolume", Label = "音效音量", Unit = "％", Kind = ParamKind.Slider, Min = 0.05, Max = 1, Value = 0.5,
                Description = "音效音量。"
            });
            Add(list, new Param
            {
                Key = "OverlayLayer", Label = "效果层位置", Kind = ParamKind.Enum,
                EnumValues = new[] { "桌面图标之下", "置顶显示" },
                Description = "桌面图标之下：效果垫在图标后面、贴住壁纸（默认）；置顶显示：效果盖在最上层。"
            });
            Add(list, new Param
            {
                Key = "LaunchAtStartup", Label = "开机自启", Kind = ParamKind.Bool, BoolValue = false,
                Description = "开机时自动启动。"
            });
            Add(list, new Param
            {
                Key = "StartMinimized", Label = "启动即最小化", Kind = ParamKind.Bool, BoolValue = false,
                Description = "启动后只驻留托盘，不弹设置窗口。"
            });

            return list;
        }

        static void Add(List<Param> list, Param p) => list.Add(p);

        /// <summary>预设：键 = 预设名，值 = 需要覆盖的参数。</summary>
        public static readonly Dictionary<string, Dictionary<string, double>> Presets =
            new Dictionary<string, Dictionary<string, double>>
            {
                ["默认最优（推荐）"] = new Dictionary<string, double>
                {
                    ["Enabled"] = 1, ["TriggerMode"] = 0,
                    ["RippleCount"] = 3, ["BaseRadius"] = 55, ["RadiusGrowth"] = 320, ["RingThickness"] = 16,
                    ["Intensity"] = 0.9, ["Bounce"] = 4, ["Damping"] = 1.8, ["Duration"] = 1200,
                    ["PressDepth"] = 0.6, ["SkinShading"] = 0.7, ["Highlight"] = 0.55, ["EdgeSoftness"] = 0.7,
                    ["GlobalOpacity"] = 1.0, ["RandomVariation"] = 0.2, ["MaxEffects"] = 30, ["TargetFps"] = 60,
                    ["HoverGlow"] = 0, ["HoverGlowRadius"] = 60, ["HoverGlowIntensity"] = 0.12,
                    ["SoundEnabled"] = 0, ["SoundVolume"] = 0.5,
                    ["OverlayLayer"] = 0, ["LaunchAtStartup"] = 0, ["StartMinimized"] = 0,
                },
                ["极致Q弹果冻"] = new Dictionary<string, double>
                {
                    ["RippleCount"] = 8, ["BaseRadius"] = 55, ["RadiusGrowth"] = 420, ["RingThickness"] = 9,
                    ["Intensity"] = 0.95, ["Bounce"] = 6, ["Damping"] = 1.2, ["Duration"] = 1500,
                    ["PressDepth"] = 0.75, ["SkinShading"] = 0.85, ["Highlight"] = 0.7, ["EdgeSoftness"] = 0.7,
                    ["RandomVariation"] = 0.35, ["MaxEffects"] = 40,
                },
                ["柔和淡雅"] = new Dictionary<string, double>
                {
                    ["RippleCount"] = 3, ["BaseRadius"] = 60, ["RadiusGrowth"] = 220, ["RingThickness"] = 3,
                    ["Intensity"] = 0.5, ["Bounce"] = 2, ["Damping"] = 3.2, ["Duration"] = 800,
                    ["PressDepth"] = 0.35, ["SkinShading"] = 0.5, ["Highlight"] = 0.3, ["EdgeSoftness"] = 0.8,
                    ["GlobalOpacity"] = 0.7, ["RandomVariation"] = 0.1,
                },
                ["鲜艳活力"] = new Dictionary<string, double>
                {
                    ["RippleCount"] = 6, ["BaseRadius"] = 38, ["RadiusGrowth"] = 360, ["RingThickness"] = 8,
                    ["Intensity"] = 0.9, ["Bounce"] = 4, ["Damping"] = 2.0, ["Duration"] = 1000,
                    ["PressDepth"] = 0.7, ["SkinShading"] = 0.6, ["Highlight"] = 0.6, ["EdgeSoftness"] = 0.35,
                    ["RandomVariation"] = 0.25, ["MaxEffects"] = 35,
                },
                ["极简克制"] = new Dictionary<string, double>
                {
                    ["RippleCount"] = 1, ["BaseRadius"] = 80, ["RadiusGrowth"] = 160, ["RingThickness"] = 2,
                    ["Intensity"] = 0.4, ["Bounce"] = 1, ["Damping"] = 3.5, ["Duration"] = 650,
                    ["PressDepth"] = 0.3, ["SkinShading"] = 0.4, ["Highlight"] = 0.25, ["EdgeSoftness"] = 0.9,
                    ["GlobalOpacity"] = 0.65, ["RandomVariation"] = 0.05, ["MaxEffects"] = 12,
                },
            };

        /// <summary>按预设覆盖参数。</summary>
        public static void ApplyPreset(List<Param> list, string presetName)
        {
            if (!Presets.TryGetValue(presetName, out var overrides)) return;
            foreach (var kv in overrides)
            {
                var p = list.Find(x => x.Key == kv.Key);
                p?.SetFromDouble(kv.Value);
            }
        }
    }
}
