namespace Tfs2Svn.Winforms
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;
    using log4net;
    using log4net.Config;
    using Properties;
    using Tfs2Svn.Converter;

    public partial class MainForm : Form
    {
        private static readonly IList<Encoding> ENCODINGS = new List<Encoding>() { Encoding.ASCII, Encoding.UTF7, Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 };

        // Static log4net logger instance
        private static readonly ILog Log = LogManager.GetLogger(typeof(MainForm));

        private static ProgressTimeEstimator progressTimeEstimator;
        private int lastSuccesfulChangeset = -1;

        public MainForm()
        {
            this.InitializeComponent();

            // load initial settings
            this.tbTFSUrl.Text = Settings.Default.TFSUrl;
            this.tbTFSRepo.Text = Settings.Default.TFSRepo;
            this.tbSVNUrl.Text = Settings.Default.SVNUrl;
            this.tbTFSUsername.Text = Settings.Default.TFSUsername;
            this.tbTFSDomain.Text = Settings.Default.TFSDomain;
            this.tbChangesetStart.Text = Settings.Default.FromChangeset.ToString();
            this.tbWorkingCopyFolder.Text = Settings.Default.WorkingCopyPath;
            this.cbDoInitialCheckout.Checked = Settings.Default.DoInitialCheckout;

            this.encodingDropdown.Items.AddRange(ENCODINGS.Select(enc => enc.EncodingName).ToArray());
            string encodingSelected = Settings.Default.Encoding ?? Encoding.ASCII.EncodingName;
            for (int i = 0; i < this.encodingDropdown.Items.Count; i++)
            {
                if (((string)this.encodingDropdown.Items[i]) == encodingSelected)
                {
                    this.encodingDropdown.SelectedIndex = i;
                    break;
                }
            }

            this.encodingDropdown.SelectedIndexChanged += this.encodingDropdown_SelectedIndexChanged;

            // init log4net
            XmlConfigurator.Configure();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new AboutFrame().ShowDialog();
        }

        private void AddListboxLine(string message)
        {
            this.lstStatus.Items.Add(message);
            this.lstStatus.SetSelected(this.lstStatus.Items.Count - 1, true);
            this.lstStatus.SetSelected(this.lstStatus.Items.Count - 1, false);
        }

        private void AddMovementLine(int changeset, string type, string action, string newPath, string oldPath, Color color)
        {
            this.BeginInvoke(
                new MethodInvoker(() =>
                {
                    ListViewItem listViewItem = new ListViewItem(new string[] { changeset.ToString(), type, action, newPath, oldPath });
                    listViewItem.ForeColor = color;
                    this.lstMovement.Items.Add(listViewItem);
                    this.lstMovement.Items[this.lstMovement.Items.Count - 1].EnsureVisible();
                }));
        }

        private void AddUsernameMappings(Tfs2SvnConverter tfs2svnConverter)
        {
            StringCollection mappings = Settings.Default.TFS2SVNUserMappings;
            foreach (string mapping in mappings)
            {
                string tfsUserName = mapping.Split(';')[0];
                string svnUserName = mapping.Split(';')[1];

                tfs2svnConverter.AddUsernameMapping(tfsUserName, svnUserName);
            }
        }

        private void AppendListboxText(string message)
        {
            this.lstStatus.Items[this.lstStatus.Items.Count - 1] = this.lstStatus.Items[this.lstStatus.Items.Count - 1] + message;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.progressBar1.Value = 0;
            this.lastSuccesfulChangeset = -1;
            this.button1.Enabled = false;
            this.lstStatus.Items.Clear();
            this.lstMovement.Items.Clear();

            Thread thread = new Thread(this.DoWork);
            thread.Priority = ThreadPriority.Normal;
            thread.Start();
        }

        private void DoWork(object obj)
        {
            try
            {
                string tfsUrl = Settings.Default.TFSUrl = this.tbTFSUrl.Text;
                string tfsRepo = Settings.Default.TFSRepo = this.tbTFSRepo.Text;
                string svnUrl = Settings.Default.SVNUrl = this.tbSVNUrl.Text;
                int startChangeset = Settings.Default.FromChangeset = int.Parse(this.tbChangesetStart.Text);
                string workingCopyFolder = Settings.Default.WorkingCopyPath = this.tbWorkingCopyFolder.Text;
                workingCopyFolder = workingCopyFolder.Replace("[MyDocuments]", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                bool doInitialCheckout = Settings.Default.DoInitialCheckout = this.cbDoInitialCheckout.Checked;
                string tfsUsername = Settings.Default.TFSUsername = this.tbTFSUsername.Text;
                string tfsDomain = Settings.Default.TFSDomain = this.tbTFSDomain.Text;
                string tfsPassword = this.tbTFSPassword.Text;

                Encoding encoding = ENCODINGS.Where(enc => enc.EncodingName == Settings.Default.Encoding).Single();

                // Settings.Default.Encoding = encoding.EncodingName;
                Settings.Default.Save(); // save settings

                // starting converting
                Log.Info(string.Format("======== Starting tfs2svn converting from server {0} repo {1} to {2}", tfsUrl, tfsRepo, svnUrl));
                this.BeginInvoke(
                    new MethodInvoker(() => { this.AddListboxLine("Starting converting from TFS to SVN"); }));

                using (Tfs2SvnConverter tfs2svnConverter = new Tfs2SvnConverter(tfsUrl, tfsRepo, svnUrl, this.cbCreateRepository.Enabled && this.cbCreateRepository.Checked, startChangeset, workingCopyFolder, Settings.Default.SvnBinFolder, doInitialCheckout, tfsUsername, tfsPassword, tfsDomain, encoding))
                {
                    this.HookupEventHandlers(tfs2svnConverter);
                    this.AddUsernameMappings(tfs2svnConverter);
                    tfs2svnConverter.Convert();
                }

                // done converting
                Log.Info("======== Finished tfs2svn converting");
                this.BeginInvoke(
                    new MethodInvoker(() => { this.AddListboxLine("Finished converting!"); }));
            }
            catch (Exception ex)
            {
                Log.Error("Exception while converting", ex);
                this.BeginInvoke(
                    new MethodInvoker(() =>
                    {
                        this.AddListboxLine("!!!ERROR(S) FOUND");
                        MessageBox.Show(ex.ToString(), "Error found");
                    }));
            }
            finally
            {
                this.BeginInvoke(
                    new MethodInvoker(() => { this.button1.Enabled = true; }));

                this.BeginInvoke(
                    new MethodInvoker(() =>
                    {
                        if (this.lastSuccesfulChangeset > -1 && Settings.Default.FromChangeset > 1 && MessageBox.Show("Update 'Start on Changeset#' for next incremental update (#" + (this.lastSuccesfulChangeset + 1).ToString() + ")?", "Update starting Changeset#?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            Settings.Default.FromChangeset = this.lastSuccesfulChangeset + 1;
                            this.tbChangesetStart.Text = Settings.Default.FromChangeset.ToString();
                            Settings.Default.Save();
                        }
                    }));
            }
        }

        private void encodingDropdown_SelectedIndexChanged(object sender, EventArgs e)
        {
            // set the choosen tfsclient provider
            string encName = (string)this.encodingDropdown.SelectedItem;
            Settings.Default.Encoding = encName;
            Settings.Default.Save();
        }

        private void HookupEventHandlers(Tfs2SvnConverter tfs2svnConverter)
        {
            tfs2svnConverter.BeginChangeSet += this.Tfs2svnConverter_BeginChangeSet;
            tfs2svnConverter.EndChangeSet += this.Tfs2svnConverter_EndChangeSet;
            tfs2svnConverter.ChangeSetsFound += this.Tfs2svnConverter_ChangeSetsFound;
            tfs2svnConverter.SvnAdminEvent += this.Tfs2svnConverter_SvnAdminEvent;
            tfs2svnConverter.FileAdded += this.Tfs2svnConverter_FileAdded;
            tfs2svnConverter.FileBranched += this.Tfs2svnConverter_FileBranched;
            tfs2svnConverter.FileDeleted += this.Tfs2svnConverter_FileDeleted;
            tfs2svnConverter.FileEdited += this.Tfs2svnConverter_FileEdited;
            tfs2svnConverter.FileRenamed += this.Tfs2svnConverter_FileRenamed;
            tfs2svnConverter.FolderAdded += this.Tfs2svnConverter_FolderAdded;
            tfs2svnConverter.FolderBranched += this.Tfs2svnConverter_FolderBranched;
            tfs2svnConverter.FolderDeleted += this.Tfs2svnConverter_FolderDeleted;
            tfs2svnConverter.FolderRenamed += this.Tfs2svnConverter_FolderRenamed;
            tfs2svnConverter.FolderUndeleted += this.Tfs2svnConverter_FolderUndeleted;
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void tbSVNUrl_TextChanged(object sender, EventArgs e)
        {
            this.cbCreateRepository.Enabled = this.tbSVNUrl.Text.StartsWith("file:///");
        }

        private void Tfs2svnConverter_BeginChangeSet(int changeset, string committer, string comment, DateTime date)
        {
            Log.Info("Processing TFS Changeset #" + changeset);

            // Show message in listBox
            this.BeginInvoke(
                new MethodInvoker(() => { this.AddListboxLine(string.Format("Processing TFS Changeset: {0} ...", changeset)); }));
        }

        private void Tfs2svnConverter_ChangeSetsFound(int totalChangesets)
        {
            progressTimeEstimator = new ProgressTimeEstimator(DateTime.Now, totalChangesets);

            // Set progressbar
            this.BeginInvoke(
                new MethodInvoker(() =>
                {
                    this.progressBar1.Maximum = totalChangesets;
                    this.progressBar1.Minimum = 0;
                    this.progressBar1.Step = 1;
                }));
        }

        private void Tfs2svnConverter_EndChangeSet(int changeset, string committer, string comment, DateTime date)
        {
            Log.Info("Finished processing Changeset #" + changeset);
            progressTimeEstimator.Update();

            // Show message in listBox
            this.BeginInvoke(
                new MethodInvoker(() =>
                {
                    this.AppendListboxText(" done");
                    this.progressBar1.Value++;
                }));

            this.BeginInvoke(
                new MethodInvoker(() => { this.lblTimeRemaining.Text = progressTimeEstimator.GetApproxTimeRemaining(); }));

            this.lastSuccesfulChangeset = changeset;
        }

        private void Tfs2svnConverter_FileAdded(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "File", "Add", path, string.Empty, Color.Green);
        }

        private void Tfs2svnConverter_FileBranched(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "File", "Branch", path, string.Empty, Color.Orange);
        }

        private void Tfs2svnConverter_FileDeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "File", "Delete", path, string.Empty, Color.Red);
        }

        private void Tfs2svnConverter_FileEdited(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "File", "Edit", path, string.Empty, Color.Blue);
        }

        private void Tfs2svnConverter_FileRenamed(int changeset, string oldPath, string newPath, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "File", "Rename", newPath, oldPath, Color.YellowGreen);
        }

        private void Tfs2svnConverter_FolderAdded(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "Folder", "Add", path, string.Empty, Color.Green);
        }

        private void Tfs2svnConverter_FolderBranched(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "Folder", "Branch", path, string.Empty, Color.Orange);
        }

        private void Tfs2svnConverter_FolderDeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "Folder", "Delete", path, string.Empty, Color.Red);
        }

        private void Tfs2svnConverter_FolderRenamed(int changeset, string oldPath, string newPath, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "Folder", "Rename", newPath, oldPath, Color.YellowGreen);
        }

        private void Tfs2svnConverter_FolderUndeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            this.AddMovementLine(changeset, "Folder", "Undelete", path, string.Empty, Color.Pink);
        }

        private void Tfs2svnConverter_SvnAdminEvent(string svnAdminMessage)
        {
            // Show message in listBox
            this.BeginInvoke(
                new MethodInvoker(() => { this.AddListboxLine(svnAdminMessage); }));
        }

        private void Tfs2svnConverter_SvnAuthenticationRetry(string command, int retryCount)
        {
            this.AddMovementLine(-1, "Authentication failed! Retrying...", string.Empty, string.Empty, string.Empty, Color.DarkRed);
        }
    }
}