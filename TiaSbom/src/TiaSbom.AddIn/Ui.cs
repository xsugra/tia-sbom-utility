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
            // Not MessageBox: on V21 the Add-In runs in its own process, and a MessageBox opened behind TIA without a
            // taskbar button (2026-11-18). The user saw nothing and thought the export did nothing.
            DialogResult answer = Run(owner => ShowMessage(owner, text, title, buttons, icon, defaultButton));
            Log.Write($"Message '{FirstLine(text)}' -> {answer}");
            return answer;
        }

        private static DialogResult ShowMessage(IWin32Window owner, string text, string title, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
            })
            {
                var layout = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill };
                Icon systemIcon = IconOf(icon);
                if (systemIcon != null)
                    layout.Controls.Add(new PictureBox { Image = systemIcon.ToBitmap(), Size = new Size(32, 32), Margin = new Padding(0, 0, 12, 0) }, 0, 0);
                layout.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 6, 0, 12) }, 1, 0);

                var buttonRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
                var results = buttons == MessageBoxButtons.YesNo
                    ? new[] { DialogResult.No, DialogResult.Yes }
                    : new[] { DialogResult.OK };
                Button defaultChoice = null;
                foreach (DialogResult result in results)
                {
                    var button = new Button { Text = result.ToString(), DialogResult = result, Width = 88 };
                    buttonRow.Controls.Add(button);
                    bool isDefault = buttons == MessageBoxButtons.YesNo
                        ? (defaultButton == MessageBoxDefaultButton.Button2) == (result == DialogResult.No)
                        : true;
                    if (isDefault) defaultChoice = button;
                }
                layout.Controls.Add(buttonRow, 0, 1);
                layout.SetColumnSpan(buttonRow, 2);
                form.Controls.Add(layout);

                form.AcceptButton = defaultChoice;
                form.CancelButton = buttons == MessageBoxButtons.YesNo ? (IButtonControl)buttonRow.Controls[0] : defaultChoice;
                form.Shown += (s, e) => defaultChoice?.Focus();
                // Ctrl+C copies the text like a MessageBox does, so users can paste the error code into a mail.
                form.KeyPreview = true;
                form.KeyDown += (s, e) =>
                {
                    if (!e.Control || e.KeyCode != Keys.C) return;
                    try { Clipboard.SetText(title + "\r\n\r\n" + text.Replace("\n", "\r\n")); }
                    catch (Exception ex) { Log.Write("Copy to clipboard failed: " + ex.Message); } // the V17 sandbox may refuse
                };
                BringToFront(form);
                return form.ShowDialog(owner);
            }
        }

        private static Icon IconOf(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Error: return SystemIcons.Error;
                case MessageBoxIcon.Warning: return SystemIcons.Warning;
                case MessageBoxIcon.Information: return SystemIcons.Information;
                case MessageBoxIcon.Question: return SystemIcons.Question;
                default: return null;
            }
        }

        /// <summary>
        /// An owned dialog is above its owner, but not necessarily above TIA: on the V21 VM the project key dialog
        /// opened behind TIA and was reachable only from the taskbar (2026-11-18, second export in a row).
        /// TopMost puts it in the topmost band itself, Activate gives it the keyboard.
        /// </summary>
        public static void BringToFront(Form form)
        {
            form.TopMost = true;
            form.ShowInTaskbar = true;
            form.Shown += (s, e) =>
            {
                form.Activate();
                form.BringToFront();
                Log.Write($"Window shown: {form.Text}");
            };
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
