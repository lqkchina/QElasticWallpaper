using System;
using System.IO;

namespace QElasticWallpaper.Core
{
    /// <summary>
    /// 极简错误日志：程序启动或后台出错时把原因写到
    /// %AppData%\QElasticWallpaper\error.log，方便排查"双击没反应"。
    /// </summary>
    public static class ErrorLog
    {
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QElasticWallpaper");
        static readonly string LogFile = Path.Combine(Dir, "error.log");

        public static string LogFilePath => LogFile;

        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { /* 日志本身失败也忽略，绝不影响主流程 */ }
        }

        public static void Write(Exception ex) => Write(ex?.ToString() ?? "未知异常");
    }
}
