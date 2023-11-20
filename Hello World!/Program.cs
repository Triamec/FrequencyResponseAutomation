using System;
using System.Windows.Forms;

namespace Triamec.Tam.Samples {
    using static Application;

    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            EnableVisualStyles();
            SetCompatibleTextRenderingDefault(false);
            Run(new HelloWorldForm());
        }
    }
}