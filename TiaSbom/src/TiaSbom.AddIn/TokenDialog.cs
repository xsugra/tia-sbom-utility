using System.Drawing;
using System.Windows.Forms;

namespace TiaSbom.AddIn
{
    /// <summary>Asked once per user/PC; the token is then kept DPAPI-encrypted by TokenStore.</summary>
    internal static class TokenDialog
    {
        public static string Ask(IWin32Window owner)
        {
            using (var form = new Form
            {
                Text = "SBOM Export – Gitea token",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(420, 120),
                MinimizeBox = false,
                MaximizeBox = false,
            })
            {
                var label = new Label { Text = "Paste your Gitea access token (write access to the SBOM repository):", Location = new Point(12, 12), AutoSize = true };
                var box = new TextBox { Location = new Point(12, 40), Width = 396, UseSystemPasswordChar = true };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(252, 80) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(333, 80) };
                form.Controls.AddRange(new Control[] { label, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                DialogResult answer = form.ShowDialog(owner);
                Log.Write($"Token dialog -> {answer}");
                return answer == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
