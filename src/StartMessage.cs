using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TS4SimRipper
{
    public partial class StartMessage : Form
    {
        public StartMessage()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            this.CenterToScreen();
        }

        public void SetIndeterminate(string status)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetIndeterminate(status)));
                return;
            }

            statusLabel.Text = status ?? string.Empty;
            etaLabel.Text = string.Empty;
            progressBar.Style = ProgressBarStyle.Marquee;
        }

        public void SetProgress(int completed, int total, string status, TimeSpan? eta)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetProgress(completed, total, status, eta)));
                return;
            }

            if (total < 0) total = 0;
            if (completed < 0) completed = 0;
            if (completed > total) completed = total;

            statusLabel.Text = status ?? string.Empty;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(total, 1);
            progressBar.Value = Math.Min(completed, progressBar.Maximum);

            if (eta.HasValue && eta.Value > TimeSpan.Zero)
            {
                etaLabel.Text = "Estimated time remaining: " + FormatEta(eta.Value);
            }
            else
            {
                etaLabel.Text = string.Empty;
            }
        }

        private static string FormatEta(TimeSpan eta)
        {
            if (eta.TotalHours >= 1)
            {
                return string.Format("{0}h {1}m", (int)eta.TotalHours, eta.Minutes);
            }
            if (eta.TotalMinutes >= 1)
            {
                return string.Format("{0}m {1}s", (int)eta.TotalMinutes, eta.Seconds);
            }
            return string.Format("{0}s", Math.Max(0, eta.Seconds));
        }
    }
}
