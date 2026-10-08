using System;
using System.Collections.Generic;
using System.Threading;
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
        EventWaitHandle _showEvent;
        System.Windows.Threading.DispatcherTimer _capTimer;
        bool _wpHooked;

        const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string StartupValue = "QElasticWallpaper";

        static string VersionTag =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

        public void Start()
        {
            ErrorLog.Write("【启动】程序开始初始化，版本 " + VersionTag);
            _cfg = ConfigStore.Load();
            _ctrl = new RippleController(_cfg);

            // OverlayLayer：0=桌面图标之下（尝试嵌入桌面壁纸宿主，失败退化为置底）
            //               1=置顶显示
            bool embedBelowIcons = (int)Get("OverlayLayer").Value == 0;
            _overlay = new OverlayWindow(_ctrl, embedBelowIcons);
            _overlay.Show();
            ErrorLog.Write("【启动】效果层已创建并显示");

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
                            // 点击不再重截/重建网格：多点按压在同一张果冻网格上叠加，
                            // 丝滑过渡、互相影响；壁纸更新交给下面的定时检测。
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
            ErrorLog.Write("【启动】鼠标钩子线程已启动");

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

            // 单实例监听：用户重复双击 EXE 时（新进程只会发出信号就退出），
            // 在这里把设置窗口调到前台，而不是"没反应"。
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                @"Global\QElasticWallpaper_ShowSettings", out _);
            var ui = Application.Current?.Dispatcher;
            var ev = _showEvent;
            var listener = new Thread(() =>
            {
                while (ev != null)
                {
                    try { ev.WaitOne(); } catch { break; }
                    try { ui?.BeginInvoke(new Action(ShowSettings)); } catch { }
                }
            });
            listener.IsBackground = true;
            listener.Start();

            // 启动即最小化则不弹设置窗口
            if (!Get("StartMinimized").BoolValue)
            {
                ErrorLog.Write("【启动】准备显示设置窗口");
                ShowSettings();
            }

            // 壁纸无缝实时更新：Windows 换壁纸时会广播 UserPreferenceChanged(Wallpaper) 事件，
            // 收到就立刻刷新素材（瞬间、无延迟、不打断正在进行的震荡）。
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWallpaperEvent;
                _wpHooked = true;
            }
            catch { _wpHooked = false; }

            // 低频兜底：个别第三方壁纸软件换壁纸不广播系统事件，每 5 秒检查一次路径有没有变
            //（RefreshWallpaper 内部先比对路径+时间戳，没换就什么都不做，几乎零开销）
            _capTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _capTimer.Tick += (s, e) => _ctrl.RefreshWallpaper();
            _capTimer.Start();

            ErrorLog.Write("【启动】初始化完成，程序进入运行状态");
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
                _settings = new SettingsWindow(_cfg, OnSettingsChanged, BuildDiagnostics);
                _settings.Closed += (s, e) => _settings = null;
            }
            _settings.Show();
            _settings.Activate();
        }

        /// <summary>生成诊断文本：程序开着但不生效时，复制这段发给我即可定位。</summary>
        public string BuildDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            sb.AppendLine("==== Q弹桌面壁纸 运行诊断 ====");
            sb.AppendLine("版本: " + asm.Version);
            sb.AppendLine("系统: " + Environment.OSVersion.VersionString);
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("配置路径: " + ConfigStore.ConfigPath);
            sb.AppendLine();
            sb.AppendLine("启用效果: " + Get("Enabled").BoolValue);
            sb.AppendLine("触发模式: " + (Get("TriggerMode").Value == 2 ? "任意左键(2)"
                : Get("TriggerMode").Value == 1 ? "桌面+应用背景(1)" : "仅桌面(0)"));
            sb.AppendLine("效果层位置: " + (Get("OverlayLayer").Value == 1 ? "置顶(1)" : "图标之下(0)"));
            sb.AppendLine("声音: " + (Get("SoundEnabled").BoolValue ? "开" : "关"));
            sb.AppendLine();
            sb.AppendLine("效果层: " + (_overlay?.Summary ?? "(未创建)"));
            sb.AppendLine("鼠标钩子: " + (_hook?.Summary ?? "(未创建)"));
            sb.AppendLine("点击统计: " + (_ctrl?.Stats ?? "(未创建)"));
            sb.AppendLine();
            sb.AppendLine("==== 错误日志 ====");
            string log = ErrorLog.ReadAll();
            sb.AppendLine(string.IsNullOrWhiteSpace(log) ? "(暂无错误记录)" : log);
            return sb.ToString();
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
            if (_wpHooked)
            {
                try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWallpaperEvent; } catch { }
                _wpHooked = false;
            }
            if (_capTimer != null) { try { _capTimer.Stop(); } catch { } _capTimer = null; }
            if (_showEvent != null) { try { _showEvent.Dispose(); } catch { } _showEvent = null; }
            _hook?.Dispose();
            _hook = null;
            _tray?.Dispose();
            _tray = null;
            _overlay?.Close();
            _overlay = null;
        }

        /// <summary>Windows 换壁纸事件：收到立即刷新壁纸素材（回 UI 线程执行，避免并发）。</summary>
        void OnWallpaperEvent(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.Wallpaper) return;
            var ctrl = _ctrl;
            var ui = Application.Current?.Dispatcher;
            if (ctrl != null && ui != null)
                ui.BeginInvoke(new Action(() => ctrl.RefreshWallpaper()));
        }
    }
}
