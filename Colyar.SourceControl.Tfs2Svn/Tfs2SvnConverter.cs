namespace Tfs2Svn.Converter
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Common;
    using log4net;
    using Subversion;
    using TfsClient;

    public class Tfs2SvnConverter : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Tfs2SvnConverter));

        private readonly SvnImporter svnImporter;

        private readonly TfsClientProvider tfsClient;
        private bool createSvnFileRepository;
        private bool disposedValue = false;
        private bool doInitialCheckout;
        private Dictionary<string, string> fileSwapBackups = new Dictionary<string, string>();
        private Dictionary<string, string> renamedFolders = new Dictionary<string, string>();
        private string svnRepository;
        private string tfsRepository;
        private string tfsServer;
        private string workingCopyPath;

        public Tfs2SvnConverter(string tfsPath, string tfsRepo, string svnPath, bool createSvnFileRepository, int fromChangeset, string workingCopyPath, string svnBinFolder, bool doInitialCheckout, string tfsUsername, string tfsPassword, string tfsDomain, Encoding encoding)
        {
            this.tfsServer = tfsPath;
            this.tfsRepository = tfsRepo;
            this.svnRepository = svnPath;

            Log.Info("TFS SERVER: " + this.tfsServer);
            Log.Info("TFS REPO: " + this.tfsRepository);
            Log.Info("SVN REPO: " + this.svnRepository);

            this.tfsClient = new TfsClientProvider(this.tfsServer, this.tfsRepository, workingCopyPath, fromChangeset, tfsUsername, tfsPassword, tfsDomain);

            this.svnImporter = new SvnImporter(this.svnRepository, workingCopyPath, svnBinFolder, encoding);
            this.createSvnFileRepository = createSvnFileRepository;
            this.doInitialCheckout = doInitialCheckout;
            this.workingCopyPath = workingCopyPath;

            this.HookupTfsExporterEventHandlers();
        }

        public event ChangesetHandler BeginChangeSet
        {
            add { this.tfsClient.BeginChangeSet += value; }
            remove { this.tfsClient.BeginChangeSet -= value; }
        }

        public event ChangesetsFoundHandler ChangeSetsFound
        {
            add { this.tfsClient.ChangeSetsFound += value; }
            remove { this.tfsClient.ChangeSetsFound -= value; }
        }

        public event ChangesetHandler EndChangeSet
        {
            add { this.tfsClient.EndChangeSet += value; }
            remove { this.tfsClient.EndChangeSet -= value; }
        }

        public event SinglePathHandler FileAdded
        {
            add { this.tfsClient.FileAdded += value; }
            remove { this.tfsClient.FileAdded -= value; }
        }

        public event SinglePathHandler FileBranched
        {
            add { this.tfsClient.FileBranched += value; }
            remove { this.tfsClient.FileBranched -= value; }
        }

        public event SinglePathHandler FileDeleted
        {
            add { this.tfsClient.FileDeleted += value; }
            remove { this.tfsClient.FileDeleted -= value; }
        }

        public event SinglePathHandler FileEdited
        {
            add { this.tfsClient.FileEdited += value; }
            remove { this.tfsClient.FileEdited -= value; }
        }

        public event DualPathHandler FileRenamed
        {
            add { this.tfsClient.FileRenamed += value; }
            remove { this.tfsClient.FileRenamed -= value; }
        }

        public event SinglePathHandler FileUndeleted
        {
            add { this.tfsClient.FileUndeleted += value; }
            remove { this.tfsClient.FileUndeleted -= value; }
        }

        public event SinglePathHandler FolderAdded
        {
            add { this.tfsClient.FolderAdded += value; }
            remove { this.tfsClient.FolderAdded -= value; }
        }

        public event SinglePathHandler FolderBranched
        {
            add { this.tfsClient.FolderBranched += value; }
            remove { this.tfsClient.FolderBranched -= value; }
        }

        public event SinglePathHandler FolderDeleted
        {
            add { this.tfsClient.FolderDeleted += value; }
            remove { this.tfsClient.FolderDeleted -= value; }
        }

        public event DualPathHandler FolderRenamed
        {
            add { this.tfsClient.FolderRenamed += value; }
            remove { this.tfsClient.FolderRenamed -= value; }
        }

        public event SinglePathHandler FolderUndeleted
        {
            add { this.tfsClient.FolderUndeleted += value; }
            remove { this.tfsClient.FolderUndeleted -= value; }
        }

        public event SvnAdminEventHandler SvnAdminEvent;

        public void AddUsernameMapping(string tfsUsername, string svnUsername)
        {
            this.svnImporter.AddUsernameMapping(tfsUsername, svnUsername);
        }

        public void Convert()
        {
            // See if repository should be created (e.g. file:///c:\myrepository)
            if (this.createSvnFileRepository && this.svnRepository.StartsWith("file:///"))
            {
                string localSvnPath = this.svnRepository.Replace("file:///", string.Empty).Replace("/", "\\");

                if (!string.IsNullOrEmpty(localSvnPath))
                {
                    this.DeletePath(localSvnPath);
                }

                Log.Info("Start creating file repository " + localSvnPath);
                this.SvnAdminEvent?.Invoke("Start creating file repository " + localSvnPath);

                this.svnImporter.CreateRepository(localSvnPath);

                // Add empty Pre-RevisionPropertyChange hookfile (to make it possible to use propset)
                string hookPath = localSvnPath + "/hooks/pre-revprop-change.cmd";
                if (!File.Exists(hookPath))
                {
                    FileStream fs = File.Create(hookPath);
                    fs.Close();
                }

                Log.Info("Finished creating file repository " + localSvnPath);
                this.SvnAdminEvent?.Invoke("Finished creating file repository " + localSvnPath);
            }

            // Initial checkout?
            if (this.doInitialCheckout)
            {
                this.DeletePath(this.workingCopyPath);
                this.svnImporter.Checkout();
            }

            // Now read and process all TFS changesets
            this.tfsClient.ProcessAllChangeSets();
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.tfsClient.Dispose();
                    this.svnImporter.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private void DeletePath(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            DirectoryInfo directoryInfo = new DirectoryInfo(path);

            // unhide .svn folders
            foreach (FileInfo fileInfo in directoryInfo.GetFiles())
            {
                File.SetAttributes(fileInfo.FullName, FileAttributes.Normal);
            }

            // Delete recursively
            foreach (DirectoryInfo subDirectoryInfo in directoryInfo.GetDirectories())
            {
                this.DeletePath(subDirectoryInfo.FullName);
            }

            Directory.Delete(path, true);
        }

        private string FixPreviouslyRenamedFolder(string path)
        {
            if (path != null)
            {
                foreach (string preRenameFolder in this.renamedFolders.Keys)
                {
                    if (path.ToLowerInvariant().StartsWith(preRenameFolder.ToLowerInvariant()))
                    {
                        path = path.Remove(0, preRenameFolder.Length).Insert(0, this.renamedFolders[preRenameFolder]);

                        // note: do not break now: each next preRenameFolder must also be checked
                    }
                }
            }

            return path;
        }

        private string GetBackupFilename(string path)
        {
            return path.Insert(path.LastIndexOf(@"\") + 1, "___temp");
        }

        private void HookupTfsExporterEventHandlers()
        {
            this.tfsClient.BeginChangeSet += this.Tfs2SvnConverter_BeginChangeSet;
            this.tfsClient.EndChangeSet += this.Tfs2SvnConverter_EndChangeSet;
            this.tfsClient.FileAdded += this.Tfs2SvnConverter_FileAdded;
            this.tfsClient.FileDeleted += this.Tfs2SvnConverter_FileDeleted;
            this.tfsClient.FileEdited += this.Tfs2SvnConverter_FileEdited;
            this.tfsClient.FileRenamed += this.Tfs2SvnConverter_FileRenamed;
            this.tfsClient.FileBranched += this.Tfs2SvnConverter_FileBranched;
            this.tfsClient.FileUndeleted += this.Tfs2SvnConverter_FileUndeleted;
            this.tfsClient.FolderAdded += this.Tfs2SvnConverter_FolderAdded;
            this.tfsClient.FolderDeleted += this.Tfs2SvnConverter_FolderDeleted;
            this.tfsClient.FolderRenamed += this.Tfs2SvnConverter_FolderRenamed;
            this.tfsClient.FolderBranched += this.Tfs2SvnConverter_FolderBranched;
            this.tfsClient.FolderUndeleted += this.Tfs2SvnConverter_FolderUndeleted;
        }

        private void Tfs2SvnConverter_BeginChangeSet(int changeset, string committer, string comment, DateTime date)
        {
            this.renamedFolders.Clear();
            this.fileSwapBackups.Clear();
        }

        private void Tfs2SvnConverter_EndChangeSet(int changeset, string committer, string comment, DateTime date)
        {
            // Check if cyclic swapped files were all handled
            if (this.fileSwapBackups.Count > 0)
            {
                foreach (string destinationPath in this.fileSwapBackups.Keys)
                {
                    string sourcePath = this.fileSwapBackups[destinationPath];

                    if (!this.fileSwapBackups.ContainsKey(sourcePath))
                    {
                        throw new Exception(string.Format("Error in file-swapping; cannot continue. : File {0} not found in swap Backups., with new path {1}", sourcePath, destinationPath));
                    }

                    string sourceSourcePath = this.GetBackupFilename(sourcePath);
                    File.Delete(sourceSourcePath);
                }
            }

            this.svnImporter.Commit(comment, committer, date, changeset);
        }

        private void Tfs2SvnConverter_FileAdded(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info("Adding file " + path);

            if (!File.Exists(path))
            {
                throw new Exception("File not found in Tfs2SvnConverter_FileAdded");
            }

            this.svnImporter.Add(path);
        }

        private void Tfs2SvnConverter_FileBranched(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info("Adding branched file " + path);

            if (!File.Exists(path))
            {
                throw new Exception("File not found in Tfs2SvnConverter_FileBranched");
            }

            this.svnImporter.Add(path);
        }

        private void Tfs2SvnConverter_FileDeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info("Deleting file " + path);

            if (File.Exists(path))
            {
                this.svnImporter.Remove(path, false);
            }
        }

        private void Tfs2SvnConverter_FileEdited(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info("Editing file " + path);
        }

        private void Tfs2SvnConverter_FileRenamed(int changeset, string oldPath, string newPath, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("tfs2svn: Renaming file {0} to {1}", oldPath, newPath));

            oldPath = this.FixPreviouslyRenamedFolder(oldPath);

            if (oldPath == newPath)
            {
                return; // no need for a rename
            }

            if (!File.Exists(oldPath))
            {
                throw new Exception("File error in Tfs2SvnConverter_FileRenamed");
            }

            if (!File.Exists(newPath))
            {
                this.svnImporter.MoveFile(oldPath, newPath, false);
            }
            else
            {
                // Check if no file exists with same case (i.e.: in that case the file was renamed automatically when a parent-folder was renamed)
                if (oldPath != newPath)
                {
                    if (oldPath.ToLowerInvariant() == newPath.ToLowerInvariant())
                    {
                        // Rename with only casing different: do a server-side rename
                        this.svnImporter.MoveServerSide(oldPath, newPath, changeset, committer, date);
                    }
                    else
                    {
                        // This should be a file-swapping!!
                        Log.Warn(string.Format("Tfs2SvnConverter_FileRenamed: rename of file '{0}' to existing file '{1}'. This is only allowed in case of a 'filename-swapping'. Please check if this was the case.", oldPath, newPath));

                        if (this.fileSwapBackups.ContainsKey(newPath))
                        {
                            throw new Exception(string.Format("Problem renaming {0} to {1}. Another file was already renamed to target.", oldPath, newPath));
                        }

                        string tempNewPath = this.GetBackupFilename(newPath);
                        File.Copy(newPath, tempNewPath);

                        if (this.fileSwapBackups.ContainsKey(oldPath))
                        {
                            string tempOldPath = this.GetBackupFilename(oldPath);
                            File.Copy(tempOldPath, newPath, true);
                        }
                        else
                        {
                            File.Copy(oldPath, newPath, true);
                        }

                        this.fileSwapBackups.Add(newPath, oldPath);
                    }
                }
            }
        }

        private void Tfs2SvnConverter_FileUndeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info("Adding undeleted file " + path);

            if (!File.Exists(path))
            {
                throw new Exception("File not found in Tfs2SvnConverter_FileUndeleted");
            }

            this.svnImporter.Add(path);
        }

        private void Tfs2SvnConverter_FolderAdded(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("Adding folder {0}", path));

            if (!Directory.Exists(path))
            {
                throw new Exception("Directory not found in Tfs2SvnConverter_FolderAdded");
            }

            this.svnImporter.AddFolder(path);
        }

        private void Tfs2SvnConverter_FolderBranched(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("Adding branched folder {0}", path));

            if (!Directory.Exists(path))
            {
                throw new Exception("Directory not found in Tfs2SvnConverter_FolderBranched");
            }

            this.svnImporter.AddFolder(path);
        }

        private void Tfs2SvnConverter_FolderDeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("Deleting folder {0}", path));

            // Cannot delete workingcopy root-folder
            if (Directory.Exists(path) && path != this.workingCopyPath)
            {
                // Try to remove the path without forcing it.
                try
                {
                    this.svnImporter.Remove(path, true);
                }
                catch (Exception ex)
                {
                    this.svnImporter.CleanUp(path);

                    Log.Info(string.Format("Could not remove the path with normal methods. \n{0}", ex.Message));
                    Log.Info(string.Format("Forcing removal of {0}", path));
                    this.svnImporter.ForceRemove(path, true);
                }
            }
        }

        private void Tfs2SvnConverter_FolderRenamed(int changeset, string oldPath, string newPath, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("tfs2svn: Renaming folder {0} to {1}", oldPath, newPath));

            oldPath = this.FixPreviouslyRenamedFolder(oldPath);

            if (oldPath == newPath)
            {
                return; // no need for a rename
            }

            if (!Directory.Exists(oldPath))
            {
                if (Directory.Exists(newPath))
                {
                    // This can happen when we tried applying the current change set earlier and it
                    // failed in the middle of applying the changeset.
                    this.renamedFolders.Add(oldPath, newPath);
                    return;
                }
                else
                {
                    throw new Exception("Folder error in Tfs2SvnConverter_FolderRenamed");
                }
            }

            // Rename to an existing directory is only allowed when the casing of the folder-name was changed
            if (Directory.Exists(newPath) && oldPath.ToLowerInvariant() != newPath.ToLowerInvariant())
            {
                // Ignore. We've seen a TFS changeset like this:
                // 1. Folder A does exist
                // 2. The changeset adds folder A
                // 3. The changeset renames A to B
                // Obviously actions 2 and 3 are in the worng order in the TFS changeset.
                // Our fix for this is: The user will have to move the folder from A to B in svn and then we'll ignore
                // the resulting error here when we can't execute the move.
                this.renamedFolders.Add(oldPath, newPath);
                return;
            }

            // Folder renames must be done server-side (see 'Moving files and folders' in http://tortoisesvn.net/docs/nightly/TortoiseSVN_sk/tsvn-dug-rename.html)
            this.svnImporter.MoveServerSide(oldPath, newPath, changeset, committer, date);
            this.renamedFolders.Add(oldPath, newPath);
        }

        private void Tfs2SvnConverter_FolderUndeleted(int changeset, string path, string committer, string comment, DateTime date)
        {
            Log.Info(string.Format("Adding undeleted folder {0}", path));

            if (!Directory.Exists(path))
            {
                throw new Exception("Directory not found in Tfs2SvnConverter_FolderUndeleted");
            }

            this.svnImporter.AddFolder(path);
        }
    }
}