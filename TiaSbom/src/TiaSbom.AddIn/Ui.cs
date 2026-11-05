using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TiaSbom.AddIn
{
    /// <summary>
    /// Runs every window on its own STA thread under a hidden topmost owner.
    /// The Add-In thread is not a Windows Forms thread and has no window of TIA to attach to, so a plain dialog
    /// may open behind TIA or not at all.
    /// </summary>
    internal static class Ui
    {
        public static T Run<T>(Func<IWin32Window, T> show)
        {
            T result = default(T);
            Exception error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    using (Form owner = CreateOwner())
                    {
                        result = show(owner);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (error != null) throw new InvalidOperationException("Window could not be shown: " + error.Message, error);
            return result;
        }

        public static DialogResult Message(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            DialogResult answer = Run(owner => MessageBox.Show(owner, text, title, buttons, icon, defaultButton));
            Log.Write($"Message '{FirstLine(text)}' -> {answer}");
            return answer;
        }

        private static Form CreateOwner()
        {
            var owner = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(1, 1),
                Opacity = 0,
                TopMost = true,
            };
            owner.Show();
            owner.Activate();
            return owner;
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end);
        }
    }
}
