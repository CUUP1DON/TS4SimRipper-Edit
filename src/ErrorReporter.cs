using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TS4SimRipper
{
    internal static class ErrorReporter
    {
        private static readonly object LogLock = new object();

        private static string GetLogDirectory()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "TS4SimRipper", "logs");
        }

        internal static void ShowWarning(IWin32Window owner, string title, string message)
        {
            ShowMessage(owner, title, message, MessageBoxIcon.Warning);
        }

        internal static void ShowError(IWin32Window owner, string title, string message, Exception ex)
        {
            string logPath = null;
            if (ex != null)
            {
                logPath = TryWriteExceptionLog(ex, title);
            }

            string fullMessage = message;
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                fullMessage += Environment.NewLine + Environment.NewLine + "Details were saved to:" + Environment.NewLine + logPath;
            }

            ShowMessage(owner, title, fullMessage, MessageBoxIcon.Error);
        }

        internal static void ShowUnhandled(Exception ex)
        {
            try
            {
                ShowError(null,
                    "TS4 SimRipper - Unexpected error",
                    "Something went wrong, but TS4 SimRipper can usually keep running.\n\nIf you can reproduce this, please share the log file path shown below.",
                    ex);
            }
            catch
            {
                // Last-resort: avoid crash loops from the error handler itself.
            }
        }

        private static void ShowMessage(IWin32Window owner, string title, string message, MessageBoxIcon icon)
        {
            if (owner != null)
            {
                MessageBox.Show(owner, message, title, MessageBoxButtons.OK, icon);
                return;
            }

            MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
        }

        private static string TryWriteExceptionLog(Exception ex, string context)
        {
            try
            {
                lock (LogLock)
                {
                    string logDir = GetLogDirectory();
                    Directory.CreateDirectory(logDir);

                    string fileName = "TS4SimRipper_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                    string path = Path.Combine(logDir, fileName);

                    var sb = new StringBuilder();
                    sb.AppendLine("TS4 SimRipper error log");
                    sb.AppendLine("Time: " + DateTime.Now.ToString("O"));
                    if (!string.IsNullOrWhiteSpace(context)) sb.AppendLine("Context: " + context);
                    sb.AppendLine();
                    sb.AppendLine(ex.ToString());

                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                    return path;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
