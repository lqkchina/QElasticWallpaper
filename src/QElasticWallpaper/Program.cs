using System;
using System.Threading;
using System.Windows;
using QElasticWallpaper.Core;

namespace QElasticWallpaper
{
    /// <summary>程序入口（WinExe，STA 线程）。</summary>
    public static class Program
    {
        static Mutex _mutex;

        [STAThread]
        public static void Main()
        {
            // 进程一启动就记录：哪怕在很早的阶段崩溃，error.log 也能留痕，
            // 好分辨"程序根本没起来"还是"起来后崩了"。
            ErrorLog.Write("【启动】进程已启动");

            // 任何后台线程崩溃（鼠标钩子等）也会被记录
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ErrorLog.Write(e.ExceptionObject as Exception ?? new Exception("未处理异常"));

            // 单实例（尽量）：已经有一个实例在跑时，通知它弹出设置窗口，本进程退出。
            // 用会话内命名（不带 Global\），避免权限问题；
            // 就算单实例机制本身出错，也照常往下跑——宁可开两个，也比"没反应"强。
            if (!TryAcquireSingleInstance())
            {
                try
                {
                    using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset,
                                 "QElasticWallpaper_ShowSettings"))
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
                try { _mutex?.ReleaseMutex(); } catch { }
            }
        }

        /// <summary>尝试获取"我是第一个实例"。失败时返回 true（照常运行）。</summary>
        static bool TryAcquireSingleInstance()
        {
            try
            {
                _mutex = new Mutex(true, "QElasticWallpaper_Singleton", out bool createdNew);
                if (createdNew) return true;      // 新建并持有 → 第一个实例
                _mutex.Dispose();                 // 已存在 → 不是第一个
                _mutex = null;
                return false;
            }
            catch
            {
                return true;                       // 单实例机制异常，照常启动
            }
        }
    }
}
