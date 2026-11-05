using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TiaSbom.Core;

namespace TiaSbom.AddIn
{
    /// <summary>
    /// Shown when the TIA project has no "Family": pick an existing Gitea project or enter a new key.
    /// </summary>
    internal static class RepoPickerDialog
    {
        /// <returns>Normalized project key, or null when cancelled.</returns>
        public static string Ask(IWin32Window owner, string projectName, string suggestion, IList<string> existing)
        {
            using (var form = new Form
            {
                Text = "SBOM Export – project key",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(460, 190),
                MinimizeBox = false,
                MaximizeBox = false,
            })
            {
                var info = new Label
                {
                    Text = $"Project \"{projectName}\" has no Family set.\n\n" +
                           "Choose the existing Gitea project it belongs to, or type a new key\n" +
                           "(e.g. job number). A new repository is created automatically.",
                    Location = new Point(12, 12),
                    AutoSize = true,
                };
                var combo = new ComboBox
                {
                    Location = new Point(12, 90),
                    Width = 436,
                    DropDownStyle = ComboBoxStyle.DropDown,
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                };
                foreach (string name in existing) combo.Items.Add(name);
                combo.Text = suggestion ?? string.Empty;

                var preview = new Label { Location = new Point(12, 120), AutoSize = true, ForeColor = SystemColors.GrayText };
                var ok = new Button { Text = "Export", DialogResult = DialogResult.OK, Location = new Point(292, 150) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(373, 150) };

                void Update()
                {
                    string key = ProjectKey.Normalize(combo.Text);
                    ok.Enabled = key != null;
                    preview.Text = key == null ? "Enter a key."
                        : existing.Contains(key) ? $"Existing repository: {key}"
                        : $"New repository will be created: {key}";
                }
                combo.TextChanged += (s, e) => Update();
                combo.SelectedIndexChanged += (s, e) => Update();
                Update();

                form.Controls.AddRange(new Control[] { info, combo, preview, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                DialogResult answer = form.ShowDialog(owner);
                Log.Write($"Project key dialog -> {answer}");
                return answer == DialogResult.OK ? ProjectKey.Normalize(combo.Text) : null;
            }
        }
    }
}
