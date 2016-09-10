using System;
using System.Reflection;
using System.Windows.Forms;

namespace tfs2svn.Winforms {
    public partial class frmAbout : Form {
        public frmAbout() {
            InitializeComponent();
        }

        private void frmAbout_Load(object sender, EventArgs e) {
            Assembly assembly = Assembly.GetExecutingAssembly();
            lblVersion.Text = "tfs2svn " + assembly.GetName().Version;
        }
    }
}