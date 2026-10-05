using System;
using System.Threading;
using System.Windows;
using QElasticWallpaper.Core;

namespace QElasticWallpaper
{
    /// <summary>程序入口（WinExe，STA 线程）。</summary>
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // 单实例：重复启动直接退出（避免两个效果层打架）
            using (var mutex = new Mutex(true, @"Global\QElasticWallpaper_Singleton", out bool createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                AppController controller = null;
                try
                {
                    controller = new AppController();
                    controller.Start();
                    app.Run();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("程序启动失败：\n" + ex, "Q弹桌面壁纸",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    controller?.Dispose();
                }
            }
        }
    }
}
