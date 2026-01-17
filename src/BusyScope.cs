using System;
using System.Windows.Forms;

namespace TS4SimRipper
{
    internal sealed class BusyScope : IDisposable
    {
        private readonly Control owner;
        private readonly Label workingLabel;
        private readonly Cursor previousCursor;
        private readonly string previousLabelText;
        private bool disposed;

        internal BusyScope(Control owner, Label workingLabel)
        {
            this.owner = owner;
            this.workingLabel = workingLabel;
            this.previousCursor = Cursor.Current;
            this.previousLabelText = workingLabel != null ? workingLabel.Text : null;

            if (this.owner != null) this.owner.UseWaitCursor = true;
            Application.UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;

            if (this.workingLabel != null && !this.workingLabel.IsDisposed)
            {
                this.workingLabel.Visible = true;
                this.workingLabel.Refresh();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (workingLabel != null && !workingLabel.IsDisposed)
            {
                if (previousLabelText != null) workingLabel.Text = previousLabelText;
                workingLabel.Visible = false;
            }

            if (owner != null) owner.UseWaitCursor = false;
            Application.UseWaitCursor = false;
            Cursor.Current = previousCursor;
        }
    }
}
