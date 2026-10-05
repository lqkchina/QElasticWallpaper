using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 主控制器：负责启动效果层、挂载鼠标钩子、托管托盘、开机自启，
    /// 并把设置窗口的改动即时应用到运行时。
    /// </summary>
    public sealed class AppController : IDisposable
    {
        List<Param> _cfg;
        RippleController _ctrl;
        OverlayWindow _overlay;
        MouseHook _hook;
        TrayHost _tray;
        SettingsWindow _settings;

        const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string StartupValue = "QElasticWallpaper";

        public void Start()
        {
            _cfg = ConfigStore.Load();
            _ctrl = new RippleController(_cfg);

            // OverlayLayer：0=桌面图标之下（尝试嵌入桌面壁纸宿主，失败退化为置底）
            //               1=置顶显示
            bool embedBelowIcons = (int)Get("OverlayLayer").Value == 0;
            _overlay = new OverlayWindow(_ctrl, embedBelowIcons);
            _overlay.Show();

            // 置顶模式：把效果层升到最上层
            ApplyLayerParam();

            // 全局鼠标钩子：捕获桌面左键点击并触发效果
            _hook = new MouseHook();
            _hook.TriggerMode = (int)Get("TriggerMode").Value;
            _hook.OverlayHandle = new System.Windows.Interop.WindowInteropHelper(_overlay).Handle;
            _hook.LeftDown += pt =>
            {
                var overlay = _overlay;
                if (overlay != null)
                    overlay.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() =>
                        {
                            var dip = overlay.PhysicalToDip(pt.X, pt.Y);
                            _ctrl.Spawn(dip.X, dip.Y);
                            if (Get("SoundEnabled").BoolValue)
                                System.Media.SystemSounds.Asterisk.Play();
                        }));
            };
            _hook.MouseMove += pt =>
            {
                if (_overlay != null)
                {
                    var dip = _overlay.PhysicalToDip(pt.X, pt.Y);
                    _ctrl.SetMouse(new Native.POINT { X = (int)dip.X, Y = (int)dip.Y });
                }
            };
            _hook.Start(_hook.TriggerMode);

            // 托盘
            _tray = new TrayHost();
            _tray.ShowSettings += () => ShowSettings();
            _tray.ToggleEnabled += () =>
            {
                var p = Get("Enabled");
                p.BoolValue = !p.BoolValue;
                _tray.SetEnabledChecked(p.BoolValue);
                ConfigStore.Save(_cfg);
            };
            _tray.ToggleAutoStart += () =>
            {
                bool next = !Get("LaunchAtStartup").BoolValue;
                Get("LaunchAtStartup").BoolValue = next;
                SetStartup(next);
                _tray.SetAutoStartChecked(next);
                ConfigStore.Save(_cfg);
            };
            _tray.ExitApp += () => Application.Current.Shutdown();
            _tray.SetEnabledChecked(Get("Enabled").BoolValue);
            _tray.SetAutoStartChecked(Get("LaunchAtStartup").BoolValue);
            _tray.ShowBalloon("Q弹桌面壁纸已运行",
                "在桌面点击鼠标左键试试皮肤按压的Q弹效果；\n右键托盘图标可随时调节参数。");

            // 开机自启按配置初始化
            if (Get("LaunchAtStartup").BoolValue)
                SetStartup(true);

            // 启动即最小化则不弹设置窗口
            if (!Get("StartMinimized").BoolValue)
                ShowSettings();
        }

        Param Get(string key) => _cfg.Find(p => p.Key == key);

        void ApplyLayerParam()
        {
            if (_overlay == null) return;
            int layer = (int)Get("OverlayLayer").Value;
            _overlay.Topmost = layer == 1; // 1 = 置顶显示
        }

        void ShowSettings()
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(_cfg, OnSettingsChanged);
                _settings.Closed += (s, e) => _settings = null;
            }
            _settings.Show();
            _settings.Activate();
        }

        void OnSettingsChanged()
        {
            if (_hook != null) _hook.TriggerMode = (int)Get("TriggerMode").Value;
            ApplyLayerParam();
        }

        void SetStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe)) key.SetValue(StartupValue, "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue(StartupValue, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Startup registry failed: " + ex);
            }
        }

        public void Dispose()
        {
            _hook?.Dispose();
            _hook = null;
            _tray?.Dispose();
            _tray = null;
            _overlay?.Close();
            _overlay = null;
        }
    }
}
