using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
#if  NETCORE
[assembly:System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
namespace TS4SimRipper
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Register encoding provider for legacy encodings (ISO-8859-8, etc.)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Replace the generic WinForms "Continue/Quit" crash dialog with a friendlier message.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ErrorReporter.ShowUnhandled(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) ErrorReporter.ShowUnhandled(ex);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorReporter.ShowUnhandled(e.Exception);
                e.SetObserved();
            };

            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
