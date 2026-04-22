using System;
using System.Globalization;
using System.IO;
using Blish_HUD;

namespace TyriasGPS
{
    public static class LogHelper
    {
        private static readonly string CultureName = CultureInfo.CurrentCulture.Name;
        private const long MaxLogFileSizeBytes = 10485760;

        private static string GetLogFolderPath()
        {
            return DirectoryUtil.RegisterDirectory("Tyrias-GPS");
        }

        private static string GetLogFilePath(string logFileName)
        {
            return Path.Combine(GetLogFolderPath(), logFileName);
        }

        private static string FormatTimestamp(DateTime dateTime)
        {
            return dateTime.ToString("G", CultureInfo.CurrentCulture);
        }

        private static void RotateLogIfNeeded(string logPath)
        {
            try
            {
                if (File.Exists(logPath))
                {
                    FileInfo fileInfo = new FileInfo(logPath);
                    if (fileInfo.Length >= MaxLogFileSizeBytes)
                    {
                        string dateString = DateTime.Now.ToString("d", CultureInfo.CurrentCulture).Replace("/", string.Empty).Replace("-", string.Empty);
                        string fileName = Path.GetFileNameWithoutExtension(logPath);
                        string extension = Path.GetExtension(logPath);
                        string archivedPath = Path.Combine(
                            Path.GetDirectoryName(logPath),
                            string.Format("{0}-{1}{2}", fileName, dateString, extension));

                        if (File.Exists(archivedPath))
                        {
                            File.Delete(archivedPath);
                        }

                        File.Move(logPath, archivedPath);
                    }
                }
            }
            catch
            {
            }
        }

        public static void Log(string message, string logFileName = "tyrias-gps.log")
        {
            try
            {
                string logPath = GetLogFilePath(logFileName);
                RotateLogIfNeeded(logPath);
                string entry = string.Format("[{0}] [{1}] {2}{3}", FormatTimestamp(DateTime.Now), CultureName, message, Environment.NewLine);
                File.AppendAllText(logPath, entry);
            }
            catch
            {
            }
        }

        public static void LogException(Exception exception, string context = null, string logFileName = "tyrias-gps.log")
        {
            try
            {
                string logPath = GetLogFilePath(logFileName);
                RotateLogIfNeeded(logPath);
                string contextText = string.IsNullOrWhiteSpace(context) ? string.Empty : string.Format("Context: {0} - ", context);
                string entry = string.Format("[{0}] [{1}] ERROR {2}{3}{4}", FormatTimestamp(DateTime.Now), CultureName, contextText, exception, Environment.NewLine);
                File.AppendAllText(logPath, entry);
            }
            catch
            {
            }
        }
    }
}
