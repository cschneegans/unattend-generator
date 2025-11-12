using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace Schneegans.Unattend
{
    public partial class MainForm : Form
    {
        private TextBox wingetPackageTextBox;
        private Button addButton;
        private Button removeButton;
        private Button generateButton;
        private ListBox packagesListBox;
        private PropertyGrid propertyGrid;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.wingetPackageTextBox = new TextBox();
            this.addButton = new Button();
            this.removeButton = new Button();
            this.generateButton = new Button();
            this.packagesListBox = new ListBox();
            this.SuspendLayout();

            // wingetPackageTextBox
            this.wingetPackageTextBox.Location = new System.Drawing.Point(6, 185);
            this.wingetPackageTextBox.Name = "wingetPackageTextBox";
            this.wingetPackageTextBox.Size = new System.Drawing.Size(248, 20);

            // addButton
            this.addButton.Location = new System.Drawing.Point(260, 183);
            this.addButton.Name = "addButton";
            this.addButton.Size = new System.Drawing.Size(75, 23);
            this.addButton.Text = "Ajouter";
            this.addButton.UseVisualStyleBackColor = true;
            this.addButton.Click += new System.EventHandler(this.AddButton_Click);

            // removeButton
            this.removeButton.Location = new System.Drawing.Point(278, 39);
            this.removeButton.Name = "removeButton";
            this.removeButton.Size = new System.Drawing.Size(75, 23);
            this.removeButton.Text = "Supprimer";
            this.removeButton.UseVisualStyleBackColor = true;
            this.removeButton.Click += new System.EventHandler(this.RemoveButton_Click);

            // generateButton
            this.generateButton.Location = new System.Drawing.Point(12, 226);
            this.generateButton.Name = "generateButton";
            this.generateButton.Size = new System.Drawing.Size(340, 23);
            this.generateButton.Text = "Générer le fichier autounattend.xml...";
            this.generateButton.UseVisualStyleBackColor = true;
            this.generateButton.Click += new System.EventHandler(this.GenerateButton_Click);

            // packagesListBox
            this.packagesListBox.FormattingEnabled = true;
            this.packagesListBox.Location = new System.Drawing.Point(6, 6);
            this.packagesListBox.Name = "packagesListBox";
            this.packagesListBox.Size = new System.Drawing.Size(248, 173);

            // TabControl
            TabControl tabControl = new TabControl();
            tabControl.Location = new System.Drawing.Point(12, 12);
            tabControl.Size = new System.Drawing.Size(340, 208);

            // Winget Tab
            TabPage wingetTabPage = new TabPage("Winget");
            wingetTabPage.Controls.Add(this.wingetPackageTextBox);
            wingetTabPage.Controls.Add(this.addButton);
            wingetTabPage.Controls.Add(this.removeButton);
            wingetTabPage.Controls.Add(this.packagesListBox);

            // Settings Tab
            TabPage settingsTabPage = new TabPage("Settings");
            this.propertyGrid = new PropertyGrid();
            this.propertyGrid.Dock = DockStyle.Fill;
            this.propertyGrid.SelectedObject = Configuration.Default;
            settingsTabPage.Controls.Add(this.propertyGrid);

            tabControl.TabPages.Add(wingetTabPage);
            tabControl.TabPages.Add(settingsTabPage);

            // MainForm
            this.ClientSize = new System.Drawing.Size(364, 261);
            this.Controls.Add(tabControl);
            this.Controls.Add(this.generateButton);
            this.Name = "MainForm";
            this.Text = "Unattend Generator";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            string packageName = wingetPackageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(packageName))
            {
                MessageBox.Show("Veuillez entrer un nom de paquet Winget.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (packagesListBox.Items.Contains(packageName))
            {
                MessageBox.Show("Ce paquet est déjà dans la liste.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (WingetPackageExists(packageName))
            {
                packagesListBox.Items.Add(packageName);
                wingetPackageTextBox.Clear();
            }
            else
            {
                MessageBox.Show($"Le paquet Winget '{packageName}' n'a pas été trouvé.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            if (packagesListBox.SelectedItem != null)
            {
                packagesListBox.Items.Remove(packagesListBox.SelectedItem);
            }
            else
            {
                MessageBox.Show("Veuillez sélectionner un paquet à supprimer.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void GenerateButton_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "XML files (*.xml)|*.xml";
                saveFileDialog.FileName = "autounattend.xml";
                saveFileDialog.Title = "Enregistrer le fichier autounattend.xml";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    GenerateAutounattendXml(saveFileDialog.FileName);
                }
            }
        }

        private bool WingetPackageExists(string packageName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"search \"{packageName}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output.Contains(packageName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Winget n'est pas installé ou n'est pas dans le PATH.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void GenerateAutounattendXml(string filePath)
        {
            UnattendGenerator generator = new UnattendGenerator();
            var packages = packagesListBox.Items.Cast<string>().ToImmutableList();

            Configuration config = (Configuration)propertyGrid.SelectedObject;
            config = config with
            {
                Winget = new WingetSettings(Packages: packages)
            };

            XmlDocument xml = generator.GenerateXml(config);
            File.WriteAllBytes(filePath, UnattendGenerator.Serialize(xml));
            MessageBox.Show($"Le fichier autounattend.xml a été généré avec succès à l'emplacement :\n{filePath}", "Succès", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
