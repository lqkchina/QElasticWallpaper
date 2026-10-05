using System;
using System.Drawing;
using System.Windows.Forms;

namespace QElasticWallpaper.Core
{
    /// <summary>系统托盘：驻留后台、切换开关、开机自启、退出。</summary>
    public sealed class TrayHost : IDisposable
    {
        NotifyIcon _ni;
        Icon _icon;
        readonly ToolStripMenuItem _toggleItem;
        readonly ToolStripMenuItem _autoStartItem;

        public event Action ShowSettings;
        public event Action ToggleEnabled;
        public event Action ToggleAutoStart;
        public event Action ExitApp;

        public TrayHost()
        {
            _icon = MakeIcon();
            _ni = new NotifyIcon
            {
                Icon = _icon,
                Text = "Q弹桌面壁纸",
                Visible = true
            };

            var menu = new ContextMenuStrip();

            var show = new ToolStripMenuItem("打开设置…");
            show.Click += (s, e) => ShowSettings?.Invoke();

            _toggleItem = new ToolStripMenuItem("启用效果") { Checked = true };
            _toggleItem.Click += (s, e) => ToggleEnabled?.Invoke();

            _autoStartItem = new ToolStripMenuItem("开机自启") { Checked = false };
            _autoStartItem.Click += (s, e) => ToggleAutoStart?.Invoke();

            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) => ExitApp?.Invoke();

            menu.Items.Add(show);
            menu.Items.Add(_toggleItem);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _ni.ContextMenuStrip = menu;
            _ni.DoubleClick += (s, e) => ShowSettings?.Invoke();
        }

        public void SetEnabledChecked(bool on) => _toggleItem.Checked = on;
        public void SetAutoStartChecked(bool on) => _autoStartItem.Checked = on;

        public void ShowBalloon(string title, string text)
        {
            try { _ni.ShowBalloonTip(1500, title, text, ToolTipIcon.Info); }
            catch { /* 某些精简系统无气球提示，忽略 */ }
        }

        /// <summary>程序内绘制一个 16x16 的圆形图标，无需外部资源文件。</summary>
        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var pen = new Pen(Color.FromArgb(235, 150, 130), 3))
                    using (var brush = new SolidBrush(Color.FromArgb(255, 220, 205)))
                    {
                        g.DrawEllipse(pen, 2, 2, 12, 12);  // 一圈波纹
                        g.FillEllipse(brush, 4, 4, 8, 8);  // 中心的皮肤按压点
                    }
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        public void Dispose()
        {
            if (_ni != null) { _ni.Visible = false; _ni.Dispose(); _ni = null; }
            if (_icon != null) { _icon.Dispose(); _icon = null; }
        }
    }
}
