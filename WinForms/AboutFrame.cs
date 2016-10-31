namespace Tfs2Svn.Winforms
{
    using System;
    using System.Reflection;
    using System.Windows.Forms;

    public partial class AboutFrame : Form
    {
        public AboutFrame()
        {
            this.InitializeComponent();
        }

        private void AboutFrame_Load(object sender, EventArgs e)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            this.lblVersion.Text = "tfs2svn " + assembly.GetName().Version;
        }
    }
}