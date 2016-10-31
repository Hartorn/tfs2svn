namespace Tfs2Svn.Subversion
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using log4net;
    using SharpSvn;

    public class SvnImporter : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SvnImporter));
        private readonly SvnClient svnClient;
        private readonly string svnPath;
        private readonly SvnRepositoryClient svnRepoClient;
        private readonly Dictionary<string, string> usernameMap = new Dictionary<string, string>();
        private bool disposedValue = false;
        private Encoding encoding;
        private string repositoryPath;
        private string workingCopyPath;

        public SvnImporter(string repositoryPath, string workingCopyPath, string svnBinFolder, Encoding encoding)
        {
            this.repositoryPath = repositoryPath.Replace("\\", "/");
            this.workingCopyPath = workingCopyPath;
            this.svnPath = svnBinFolder;
            this.encoding = encoding;
            this.svnClient = new SvnClient();
            this.svnRepoClient = new SvnRepositoryClient();
        }

        public string RepositoryPath
        {
            get { return this.repositoryPath; }
        }

        public string WorkingCopyPath
        {
            get { return this.workingCopyPath; }
        }

        public void Add(string path)
        {
            if (path != this.workingCopyPath)
            {
                this.AddMissingDirectoryIfNeeded(path);

                this.svnClient.Add(Path.Combine(path));
            }
        }

        public void AddFolder(string path)
        {
            if (path != this.workingCopyPath)
            {
                this.AddMissingDirectoryIfNeeded(path);

                SvnAddArgs addArgs = new SvnAddArgs();
                addArgs.Depth = SvnDepth.Empty;
                addArgs.ThrowOnError = false;
                addArgs.ThrowOnWarning = false;
                this.svnClient.Add(path, addArgs);
            }
        }

        public void AddUsernameMapping(string tfsUsername, string svnUsername)
        {
            this.usernameMap[tfsUsername] = svnUsername;
        }

        public void Checkout()
        {
            this.svnClient.CheckOut(new SvnUriTarget(this.repositoryPath), this.workingCopyPath);
        }

        public void CleanUp(string path)
        {
            this.svnClient.CleanUp(path);
        }

        public void Commit(string message, string committer, DateTime commitDate, int changeSet)
        {
            // clean-up message for svn and remove non-ASCII chars
            if (message != null)
            {
                message = message.Replace("\"", "\\\"").Replace("\r\n", "\n");

                // http://svnbook.red-bean.com/en/1.2/svn.advanced.l10n.html
                message = this.encoding.GetString(this.encoding.GetBytes(message));
            }

            message = string.Format(
                "[TFS Changeset #{0}]\n{1}",
                changeSet.ToString(CultureInfo.InvariantCulture),
                message);

            SvnCommitArgs commitArg = new SvnCommitArgs();
            commitArg.LogMessage = message;
            this.svnClient.Commit(this.workingCopyPath, commitArg);

            this.SetCommitAuthorAndDate(commitDate, committer);
        }

        public void CreateRepository(string repositoryPath)
        {
            this.svnRepoClient.CreateRepository(repositoryPath);
        }

        public void CreateRepository()
        {
            this.CreateRepository(this.repositoryPath);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
        }

        /// <summary>
        /// Force removal of a path.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isFolder"></param>
        public void ForceRemove(string path, bool isFolder)
        {
            SvnDeleteArgs deleteArgs = new SvnDeleteArgs();
            deleteArgs.Force = true;
            this.svnClient.Delete(path, deleteArgs);
            if (isFolder)
            {
                this.svnClient.Update(path);
            }
        }

        public void MoveFile(string oldPath, string newPath, bool isFolder)
        {
            this.AddMissingDirectoryIfNeeded(newPath);
            this.svnClient.Move(oldPath, newPath);
        }

        public void MoveServerSide(string oldPath, string newPath, int changeset, string committer, DateTime commitDate)
        {
            string oldUrl = this.repositoryPath + this.ToUrlPath(oldPath.Remove(0, this.workingCopyPath.Length));
            string newUrl = this.repositoryPath + this.ToUrlPath(newPath.Remove(0, this.workingCopyPath.Length));

            // when only casing is different, we need a server-side move/rename (because windows is case unsensitive!)
            SvnMoveArgs moveArgs = new SvnMoveArgs();
            moveArgs.LogMessage = string.Format("[TFS Changeset #{0}]\ntfs2svn: server-side rename", changeset);
            this.svnClient.RemoteMove(new Uri(oldUrl), new Uri(newPath), moveArgs);

            this.Update(); // todo: only update common rootpath of oldPath and newPath?

            this.SetCommitAuthorAndDate(commitDate, committer);
        }

        public void Remove(string path, bool isFolder)
        {
            this.svnClient.Delete(path);
            if (isFolder)
            {
                this.svnClient.Update(path);
            }
        }

        public void Update()
        {
            this.svnClient.Update(this.workingCopyPath);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.svnClient.Dispose();
                    this.svnRepoClient.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private void AddMissingDirectoryIfNeeded(string path)
        {
            string directory = Directory.GetParent(path).FullName;

            if (Directory.Exists(directory))
            {
                return;
            }

            Log.Info("Adding: " + directory);
            Directory.CreateDirectory(directory);
            string workingCopyDirectory;
            if (!this.workingCopyPath.EndsWith("\\"))
            {
                workingCopyDirectory = Directory.GetParent(this.workingCopyPath + '\\').FullName;
            }
            else
            {
                workingCopyDirectory = Directory.GetParent(this.workingCopyPath).FullName;
            }

            string[] pathParts = directory.Substring(workingCopyDirectory.Length).Split('\\');

            foreach (string pathPart in pathParts)
            {
                workingCopyDirectory += '\\';
                workingCopyDirectory += pathPart;

                SvnAddArgs addArgs = new SvnAddArgs();
                addArgs.Depth = SvnDepth.Empty;
                addArgs.ThrowOnError = false;
                addArgs.ThrowOnWarning = false;
                this.svnClient.Add(workingCopyDirectory, addArgs);
            }
        }

        private string GetMappedUsername(string committer)
        {
            foreach (string tfsUsername in this.usernameMap.Keys)
            {
                if (committer.ToLowerInvariant().Contains(tfsUsername.ToLowerInvariant()))
                {
                    return this.usernameMap[tfsUsername];
                }
            }

            return committer; // no mapping found, return committer's unmapped name
        }

        private void SetCommitAuthorAndDate(DateTime commitDate, string committer)
        {
            string username = this.GetMappedUsername(committer);
            string commitDateStr = commitDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

            this.svnClient.SetRevisionProperty(
                new Uri(this.repositoryPath),
                    SvnRevision.Head,
                    "svn:date",
                    commitDateStr);

            this.svnClient.SetRevisionProperty(
                new Uri(this.repositoryPath),
                    SvnRevision.Head,
                    "svn:author",
                    username);
        }

        private string ToUrlPath(string path)
        {
            return path.Replace("\\", "/");
        }

        // To detect redundant calls
    }
}