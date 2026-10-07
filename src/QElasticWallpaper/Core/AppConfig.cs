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
                Key = "GlobalOpacity", Label = "整体透明度", Unit = "％", Kind = ParamKind.Slider, Min = 0.05, Max = 1, Value = 1.0,
                Description = "整个效果层的不透明度。"
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
            // ---- 果冻物理参数（参考"果冻弹性壁纸"：把壁纸做成弹簧质点网格，按住拖拽形变，松手回弹） ----
            Add(list, new Param
            {
                Key = "JellyGrid", Label = "果冻网格尺寸", Unit = "px", Kind = ParamKind.Slider, Min = 12, Max = 120, Value = 24,
                Description = "壁纸被切成多细的网格来变形，越小越细腻、越耗性能。"
            });
            Add(list, new Param
            {
                Key = "JellyStiffness", Label = "弹簧刚度(回弹力度)", Kind = ParamKind.Slider, Min = 20, Max = 500, Value = 300,
                Description = "回弹力度，越大松手后弹得越猛。"
            });
            Add(list, new Param
            {
                Key = "JellyDamping", Label = "阻尼(抑制震荡)", Kind = ParamKind.Slider, Min = 0.02, Max = 0.9, Value = 0.35,
                Description = "越小回弹越久越Q；越大越快停下来。"
            });
            Add(list, new Param
            {
                Key = "JellyRadius", Label = "拖拽影响半径", Unit = "px", Kind = ParamKind.Slider, Min = 30, Max = 600, Value = 200,
                Description = "按住拖拽时影响壁纸的范围。"
            });
            Add(list, new Param
            {
                Key = "JellyStrength", Label = "拖拽强度系数", Kind = ParamKind.Slider, Min = 0.05, Max = 1, Value = 0.7,
                Description = "拖拽变形幅度，越大壁纸弯得越明显。"
            });
            Add(list, new Param
            {
                Key = "JellyMaxDisp", Label = "质点最大位移", Unit = "px", Kind = ParamKind.Slider, Min = 4, Max = 80, Value = 26,
                Description = "壁纸形变的最大偏移，防止极端扭曲。"
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
                    ["GlobalOpacity"] = 1.0, ["TargetFps"] = 60,
                    ["JellyGrid"] = 24, ["JellyStiffness"] = 300, ["JellyDamping"] = 0.35,
                    ["JellyRadius"] = 200, ["JellyStrength"] = 0.7, ["JellyMaxDisp"] = 26,
                    ["HoverGlow"] = 0, ["HoverGlowRadius"] = 60, ["HoverGlowIntensity"] = 0.12,
                    ["SoundEnabled"] = 0, ["SoundVolume"] = 0.5,
                    ["OverlayLayer"] = 0, ["LaunchAtStartup"] = 0, ["StartMinimized"] = 0,
                },
                ["猛烈Q弹"] = new Dictionary<string, double>
                {
                    ["JellyGrid"] = 22, ["JellyStiffness"] = 400, ["JellyDamping"] = 0.20,
                    ["JellyRadius"] = 240, ["JellyStrength"] = 0.9, ["JellyMaxDisp"] = 34,
                },
                ["柔和微弹"] = new Dictionary<string, double>
                {
                    ["JellyGrid"] = 30, ["JellyStiffness"] = 180, ["JellyDamping"] = 0.55,
                    ["JellyRadius"] = 150, ["JellyStrength"] = 0.45, ["JellyMaxDisp"] = 16,
                },
                ["大幅波浪"] = new Dictionary<string, double>
                {
                    ["JellyGrid"] = 26, ["JellyStiffness"] = 260, ["JellyDamping"] = 0.30,
                    ["JellyRadius"] = 320, ["JellyStrength"] = 0.8, ["JellyMaxDisp"] = 38,
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
