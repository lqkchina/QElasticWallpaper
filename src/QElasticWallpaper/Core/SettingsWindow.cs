using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 设置窗口：遍历 AppConfig 里登记的所有参数自动生成控件，
    /// 无论以后加多少参数都自动出现在这里。改动即时生效并保存。
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        readonly List<Param> _cfg;
        readonly Action _onChanged;
        readonly ComboBox _presetBox;
        readonly TextBlock _pathText;

        public SettingsWindow(List<Param> cfg, Action onChanged)
        {
            _cfg = cfg;
            _onChanged = onChanged;

            Title = "Q弹桌面壁纸 · 设置";
            Width = 560;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));

            var root = new DockPanel { Margin = new Thickness(16) };

            // ---------- 顶部：标题 + 预设 + 操作按钮 ----------
            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            top.Children.Add(new TextBlock
            {
                Text = "Q弹桌面壁纸 · 全部参数",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var presetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            presetRow.Children.Add(new TextBlock
            {
                Text = "预设方案：", VerticalAlignment = VerticalAlignment.Center
            });
            _presetBox = new ComboBox { Width = 220, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var name in AppConfig.Presets.Keys) _presetBox.Items.Add(name);
            _presetBox.SelectionChanged += (s, e) =>
            {
                if (_presetBox.SelectedItem is string name)
                {
                    AppConfig.ApplyPreset(_cfg, name);
                    Rebuild();
                    _onChanged?.Invoke();
                }
            };
            presetRow.Children.Add(_presetBox);
            DockPanel.SetDock(presetRow, DockPanel.Dock.Top);
            top.Children.Add(presetRow);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            btnRow.Children.Add(MakeButton("恢复默认最优", () => ApplyPreset("默认最优（推荐）")));
            btnRow.Children.Add(MakeButton("重置当前页", ResetToFactory));
            btnRow.Children.Add(MakeButton("立即保存", () => { ConfigStore.Save(_cfg); ShowTip("已保存到配置文件"); }));
            btnRow.Children.Add(MakeButton("退出程序", () => Application.Current.Shutdown()));
            top.Children.Add(btnRow);

            _pathText = new TextBlock
            {
                Text = "配置文件：\n" + ConfigStore.ConfigPath,
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            top.Children.Add(_pathText);

            // ---------- 中部：可滚动参数列表 ----------
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(scroll, DockPanel.Dock.Top);

            _panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            scroll.Content = _panel;

            DockPanel.SetDock(top, DockPanel.Dock.Top);
            root.Children.Add(top);
            root.Children.Add(scroll);

            Content = root;
            Rebuild();

            // _panel 已就绪，安全地默认选中"默认最优"，避免下拉框空白
            if (_presetBox.Items.Count > 0) _presetBox.SelectedIndex = 0;
        }

        StackPanel _panel;

        void ApplyPreset(string name)
        {
            AppConfig.ApplyPreset(_cfg, name);
            if (_presetBox.SelectedItem as string != name) _presetBox.SelectedItem = name;
            Rebuild();
            _onChanged?.Invoke();
        }

        void ResetToFactory()
        {
            var fresh = AppConfig.BuildDefaultParams();
            for (int i = 0; i < _cfg.Count && i < fresh.Count; i++) CopyParam(fresh[i], _cfg[i]);
            Rebuild();
            _onChanged?.Invoke();
        }

        static void CopyParam(Param from, Param to)
        {
            to.Value = from.Value;
            to.BoolValue = from.BoolValue;
        }

        void Rebuild()
        {
            _panel.Children.Clear();
            foreach (var p in _cfg) BuildRow(p);
        }

        void BuildRow(Param p)
        {
            var border = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 235)),
                BorderThickness = new Thickness(1)
            };
            var panel = new DockPanel();

            var label = new TextBlock
            {
                Text = p.Label + (p.Unit != null ? $"（{p.Unit}）" : ""),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = p.Description
            };
            panel.Children.Add(label);

            switch (p.Kind)
            {
                case ParamKind.Bool:
                    {
                        var cb = new CheckBox
                        {
                            IsChecked = p.BoolValue,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 0)
                        };
                        cb.Checked += (s, e) => { p.BoolValue = true; NotifyChanged(p); };
                        cb.Unchecked += (s, e) => { p.BoolValue = false; NotifyChanged(p); };
                        panel.Children.Add(cb);
                        DockPanel.SetDock(cb, DockPanel.Dock.Right);
                        break;
                    }
                case ParamKind.Enum:
                    {
                        var combo = new ComboBox
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Width = 220,
                            Margin = new Thickness(0, 0, 0, 0)
                        };
                        foreach (var v in p.EnumValues) combo.Items.Add(v);
                        combo.SelectedIndex = (int)p.Value;
                        combo.SelectionChanged += (s, e) =>
                        {
                            if (combo.SelectedIndex >= 0)
                            {
                                p.Value = combo.SelectedIndex;
                                NotifyChanged(p);
                            }
                        };
                        panel.Children.Add(combo);
                        DockPanel.SetDock(combo, DockPanel.Dock.Right);
                        break;
                    }
                case ParamKind.Slider:
                    {
                        var valText = new TextBlock
                        {
                            MinWidth = 58,
                            TextAlignment = TextAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0)
                        };
                        var slider = new Slider
                        {
                            Minimum = p.Min,
                            Maximum = p.Max,
                            Value = p.Value,
                            Width = 260,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0)
                        };
                        UpdateValText(valText, p);
                        slider.ValueChanged += (s, e) =>
                        {
                            if (!slider.IsInitialized) return;
                            p.Value = e.NewValue;
                            UpdateValText(valText, p);
                            NotifyChanged(p);
                        };
                        panel.Children.Add(valText);
                        DockPanel.SetDock(valText, DockPanel.Dock.Right);
                        panel.Children.Add(slider);
                        DockPanel.SetDock(slider, DockPanel.Dock.Right);
                        break;
                    }
            }

            border.Child = panel;
            _panel.Children.Add(border);
        }

        static void UpdateValText(TextBlock t, Param p)
        {
            t.Text = p.Unit == "％" ? (p.Value * 100).ToString("0") + "%"
                     : p.Unit == "fps" ? ((int)p.Value).ToString()
                     : p.Unit == "px" || p.Unit == "ms" ? ((int)p.Value).ToString()
                     : p.Value.ToString("0.##");
        }

        void NotifyChanged(Param p)
        {
            ConfigStore.Save(_cfg);
            _onChanged?.Invoke();
        }

        Button MakeButton(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4)
            };
            b.Click += (s, e) => action();
            return b;
        }

        void ShowTip(string msg) => MessageBox.Show(this, msg, "Q弹桌面壁纸");
    }
}
