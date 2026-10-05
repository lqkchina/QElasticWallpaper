using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 参数持久化：把所有参数存成 JSON 到 %AppData%/QElasticWallpaper/config.json。
    /// 换壁纸不影响任何设置；改参数即时保存，下次启动自动恢复。
    /// </summary>
    public static class ConfigStore
    {
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QElasticWallpaper");
        static readonly string FilePath = Path.Combine(Dir, "config.json");

        public static string ConfigPath => FilePath;

        /// <summary>加载配置；文件不存在则用"默认最优"预设初始化。</summary>
        public static List<Param> Load()
        {
            List<Param> list = AppConfig.BuildDefaultParams();
            AppConfig.ApplyPreset(list, "默认最优（推荐）");

            try
            {
                if (File.Exists(FilePath))
                {
                    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath));
                    if (raw != null)
                    {
                        foreach (var p in list)
                        {
                            if (raw.TryGetValue(p.Key, out JsonElement el))
                            {
                                switch (p.Kind)
                                {
                                    case ParamKind.Bool:
                                        p.BoolValue = el.ValueKind == JsonValueKind.True;
                                        break;
                                    case ParamKind.Enum:
                                        if (el.ValueKind == JsonValueKind.Number) p.Value = el.GetInt32();
                                        else if (el.ValueKind == JsonValueKind.String) p.SetFromString(el.GetString());
                                        break;
                                    default:
                                        if (el.ValueKind == JsonValueKind.Number) p.SetFromDouble(el.GetDouble());
                                        else if (el.ValueKind == JsonValueKind.String) p.SetFromString(el.GetString());
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 配置损坏时静默回退到默认值，不影响启动
                System.Diagnostics.Debug.WriteLine("Config load failed: " + ex);
            }
            return list;
        }

        public static void Save(List<Param> list)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var dict = new Dictionary<string, object>();
                foreach (var p in list)
                {
                    switch (p.Kind)
                    {
                        case ParamKind.Bool: dict[p.Key] = p.BoolValue; break;
                        case ParamKind.Enum: dict[p.Key] = (int)p.Value; break;
                        default: dict[p.Key] = Math.Round(p.Value, 3); break;
                    }
                }
                string json = JsonSerializer.Serialize(dict,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Config save failed: " + ex);
            }
        }
    }
}
