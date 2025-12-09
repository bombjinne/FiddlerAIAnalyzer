using System;
using System.IO;
using Newtonsoft.Json;

namespace FiddlerAIAnalyzerPlugin
{
    /// <summary>
    /// 插件配置项
    /// </summary>
    public class PluginSettings
    {
        public string ApiKey { get; set; } = "";
        public bool IncludeUrl { get; set; } = true;
        public bool IncludeRequestHeaders { get; set; } = true;
        public bool IncludeRequestBody { get; set; } = true;
        public bool IncludeResponseHeaders { get; set; } = true;
        public bool IncludeResponseBody { get; set; } = true;
        public bool ExcludeBinaryContent { get; set; } = true;

        #region 配置文件管理

        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FiddlerAIPlugin"
        );

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        /// <summary>
        /// 保存当前实例到配置文件
        /// </summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory); // 确保目录存在
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                // 可选：记录日志或弹窗提示
                System.Diagnostics.Debug.WriteLine($"[AIPlugin] 保存配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从配置文件加载配置项
        /// </summary>
        /// <returns>返回加载的配置实例，如果失败则返回默认实例</returns>
        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonConvert.DeserializeObject<PluginSettings>(json) ?? new PluginSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIPlugin] 加载配置失败: {ex.Message}");
            }

            // 返回默认配置
            return new PluginSettings();
        }

        #endregion
    }
}