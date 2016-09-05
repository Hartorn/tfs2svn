using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using log4net;
using SharpSvn;

namespace Colyar.SourceControl.Subversion {
    public class SvnImporter : IDisposable {
        #region Private Variables

        private string _repositoryPath;
        private string _workingCopyPath;
        private Encoding _encoding;
        private readonly string _svnPath;
        private readonly Dictionary<string, string> _usernameMap = new Dictionary<string, string>();
        private static readonly ILog log = LogManager.GetLogger(typeof(SvnImporter));
        private readonly SvnClient _svnClient;
        private readonly SvnRepositoryClient _svnRepoClient;

        #endregion

        #region Public Properties

        public string WorkingCopyPath {
            get { return this._workingCopyPath; }
        }

        public string RepositoryPath {
            get { return this._repositoryPath; }
        }

        #endregion

        #region Public Constructor

        public SvnImporter(string repositoryPath, string workingCopyPath, string svnBinFolder, Encoding encoding) {
            this._repositoryPath = repositoryPath.Replace("\\", "/");
            this._workingCopyPath = workingCopyPath;
            this._svnPath = svnBinFolder;
            this._encoding = encoding;
            this._svnClient = new SvnClient();
            this._svnRepoClient = new SvnRepositoryClient();
        }

        #endregion

        #region Public Methods

        public void CreateRepository(string repositoryPath) {
            this._svnRepoClient.CreateRepository(repositoryPath);
        }

        public void CreateRepository() {
            CreateRepository(this._repositoryPath);
        }

        public void Checkout(string repositoryPath, string workingCopyPath) {
            this._repositoryPath = repositoryPath;
            this._workingCopyPath = workingCopyPath;

            Checkout();
        }

        public void Checkout() {
            this._svnClient.CheckOut(new SvnUriTarget(this._repositoryPath), this._workingCopyPath);
        }

        public void Update() {
            this._svnClient.Update(this._workingCopyPath);
        }

        public void Commit(string message, string committer, DateTime commitDate, int changeSet) {
            // clean-up message for svn and remove non-ASCII chars
            if (message != null) {
                message = message.Replace("\"", "\\\"").Replace("\r\n", "\n");
                // http://svnbook.red-bean.com/en/1.2/svn.advanced.l10n.html
                message = this._encoding.GetString(this._encoding.GetBytes(message));
            }

            message = String.Format("[TFS Changeset #{0}]\n{1}",
                changeSet.ToString(CultureInfo.InvariantCulture),
                message);

            SvnCommitArgs commitArg = new SvnCommitArgs();
            commitArg.LogMessage = message;
            this._svnClient.Commit(this._workingCopyPath, commitArg);

            SetCommitAuthorAndDate(commitDate, committer);
        }

        public void Add(string path) {
            if (path != this._workingCopyPath) {
                AddMissingDirectoryIfNeeded(path);

                this._svnClient.Add(Path.Combine(path));
            }
        }
        public void AddFolder(string path) {
            if (path != this._workingCopyPath) {
                AddMissingDirectoryIfNeeded(path);

                SvnAddArgs addArgs = new SvnAddArgs();
                addArgs.Depth = SvnDepth.Empty;
                addArgs.ThrowOnError = false;
                addArgs.ThrowOnWarning = false;
                this._svnClient.Add(path, addArgs);
            }
        }

        public void Remove(string path, bool isFolder) {
            this._svnClient.Delete(path);
            if (isFolder) {
                this._svnClient.Update(path);
            }
        }

        /// <summary>
        /// Cleanup a path.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isFolder"></param>
        public void CleanUp(string path) {
            this._svnClient.CleanUp(path);
        }

        /// <summary>
        /// Force removal of a path.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isFolder"></param>
        public void ForceRemove(string path, bool isFolder) {

            SvnDeleteArgs deleteArgs = new SvnDeleteArgs();
            deleteArgs.Force = true;
            this._svnClient.Delete(path, deleteArgs);
            if (isFolder) {
                this._svnClient.Update(path);
            }
        }

        public void MoveFile(string oldPath, string newPath, bool isFolder) {
            AddMissingDirectoryIfNeeded(newPath);
            this._svnClient.Move(oldPath, newPath);
        }
        public void MoveServerSide(string oldPath, string newPath, int changeset, string committer, DateTime commitDate) {
            string oldUrl = _repositoryPath + ToUrlPath(oldPath.Remove(0, _workingCopyPath.Length));
            string newUrl = _repositoryPath + ToUrlPath(newPath.Remove(0, _workingCopyPath.Length));

            //when only casing is different, we need a server-side move/rename (because windows is case unsensitive!)
            SvnMoveArgs moveArgs = new SvnMoveArgs();
            moveArgs.LogMessage = String.Format("[TFS Changeset #{0}]\ntfs2svn: server-side rename", changeset);
            this._svnClient.RemoteMove(new Uri(oldUrl), new Uri(newPath), moveArgs);


            Update(); //todo: only update common rootpath of oldPath and newPath?

            SetCommitAuthorAndDate(commitDate, committer);
        }

        public void AddUsernameMapping(string tfsUsername, string svnUsername) {
            this._usernameMap[tfsUsername] = svnUsername;
        }
        #endregion

        #region Private Methods

        private void AddMissingDirectoryIfNeeded(string path) {
            string directory = Directory.GetParent(path).FullName;

            if (Directory.Exists(directory))
                return;

            log.Info("Adding: " + directory);
            Directory.CreateDirectory(directory);
            string workingCopyDirectory;
            if (!_workingCopyPath.EndsWith("\\")) {
                workingCopyDirectory = Directory.GetParent(_workingCopyPath + '\\').FullName;
            } else {
                workingCopyDirectory = Directory.GetParent(_workingCopyPath).FullName;
            }

            string[] pathParts = directory.Substring(workingCopyDirectory.Length).Split('\\');

            foreach (string pathPart in pathParts) {
                workingCopyDirectory += '\\';
                workingCopyDirectory += pathPart;

                SvnAddArgs addArgs = new SvnAddArgs();
                addArgs.Depth = SvnDepth.Empty;
                addArgs.ThrowOnError = false;
                addArgs.ThrowOnWarning = false;
                this._svnClient.Add(workingCopyDirectory, addArgs);
            }
        }
        private void SetCommitAuthorAndDate(DateTime commitDate, string committer) {
            string username = GetMappedUsername(committer);
            string commitDateStr = commitDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

            this._svnClient.SetRevisionProperty(new Uri(this._repositoryPath),
                    SvnRevision.Head,
                    "svn:date",
                    commitDateStr
                    );

            this._svnClient.SetRevisionProperty(new Uri(this._repositoryPath),
                    SvnRevision.Head,
                    "svn:author",
                    username
                );



        }

        private string ToUrlPath(string path) {
            return path.Replace("\\", "/");
        }

        private string GetMappedUsername(string committer) {
            foreach (string tfsUsername in _usernameMap.Keys)
                if (committer.ToLowerInvariant().Contains(tfsUsername.ToLowerInvariant()))
                    return _usernameMap[tfsUsername];

            return committer; //no mapping found, return committer's unmapped name
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    this._svnClient.Dispose();
                    this._svnRepoClient.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose() {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

        #endregion
    }
}
