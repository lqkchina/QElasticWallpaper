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
            // 任何后台线程崩溃（鼠标钩子等）也会被记录，避免"双击没反应"却查不到原因
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ErrorLog.Write(e.ExceptionObject as Exception ?? new Exception("未处理异常"));

            // 单实例：已经有一个实例在跑时，通知它弹出设置窗口，然后本进程退出。
            // （否则用户重复双击会"没反应"，误以为程序坏了。）
            using (var mutex = new Mutex(true, @"Global\QElasticWallpaper_Singleton", out bool createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset,
                                     @"Global\QElasticWallpaper_ShowSettings"))
                        {
                            ev.Set(); // 唤醒第一个实例去弹出设置窗口
                        }
                    }
                    catch { }
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
                    ErrorLog.Write("【退出】程序正常退出（托盘里选了“退出程序”或被关闭）");
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                    MessageBox.Show("程序启动失败，原因已写入日志。\n\n" + ex.Message + "\n\n日志位置：\n" +
                        ErrorLog.LogFilePath, "Q弹桌面壁纸", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    controller?.Dispose();
                }
            }
        }
    }
}
