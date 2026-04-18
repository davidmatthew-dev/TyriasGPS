using System;
using System.Globalization;
using System.IO;

namespace TyriasGPS
{
    public static class LogHelper
    {
        private static readonly string LogFolderName = "logs";
        private static readonly string CultureName = CultureInfo.CurrentCulture.Name;

        private static string GetLogFilePath(string logFileName)
        {
            string logsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFolderName);
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            return Path.Combine(logsPath, logFileName);
        }

        private static string FormatTimestamp(DateTime dateTime)
        {
            return dateTime.ToString("G", CultureInfo.CurrentCulture);
        }

        public static void Log(string message, string logFileName = "actions.log")
        {
            try
            {
                string logPath = GetLogFilePath(logFileName);
                string entry = string.Format("[{0}] [{1}] {2}{3}", FormatTimestamp(DateTime.Now), CultureName, message, Environment.NewLine);
                File.AppendAllText(logPath, entry);
            }
            catch
            {
            }
        }

        public static void LogException(Exception exception, string context = null, string logFileName = "actions.log")
        {
            try
            {
                string logPath = GetLogFilePath(logFileName);
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
