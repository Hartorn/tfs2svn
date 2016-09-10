using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using log4net;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace Colyar.SourceControl.MicrosoftTfsClient {
    public class TfsClientProvider : IDisposable {
        #region Public Events

        public event ChangesetHandler BeginChangeSet;
        public event ChangesetHandler EndChangeSet;
        public event ChangesetsFoundHandler ChangeSetsFound;

        public event SinglePathHandler FileAdded {
            add { this._fileChangeHandler.ChangeAdded += value; }
            remove { this._fileChangeHandler.ChangeAdded -= value; }
        }
        public event SinglePathHandler FileEdited {
            add { this._fileChangeHandler.ChangeEdited += value; }
            remove { this._fileChangeHandler.ChangeEdited -= value; }
        }
        public event SinglePathHandler FileDeleted {
            add { this._fileChangeHandler.ChangeDeleted += value; }
            remove { this._fileChangeHandler.ChangeDeleted -= value; }
        }
        public event SinglePathHandler FileUndeleted {
            add { this._fileChangeHandler.ChangeUndeleted += value; }
            remove { this._fileChangeHandler.ChangeUndeleted -= value; }
        }
        public event SinglePathHandler FileBranched {
            add { this._fileChangeHandler.ChangeBranched += value; }
            remove { this._fileChangeHandler.ChangeUndeleted -= value; }
        }
        public event DualPathHandler FileRenamed {
            add { this._fileChangeHandler.ChangeRenamed += value; }
            remove { this._fileChangeHandler.ChangeRenamed -= value; }
        }
        public event SinglePathHandler FolderAdded {
            add { this._folderChangeHandler.ChangeAdded += value; }
            remove { this._folderChangeHandler.ChangeAdded -= value; }
        }
        public event SinglePathHandler FolderDeleted {
            add { this._folderChangeHandler.ChangeDeleted += value; }
            remove { this._folderChangeHandler.ChangeDeleted -= value; }
        }
        public event SinglePathHandler FolderUndeleted {
            add { this._folderChangeHandler.ChangeUndeleted += value; }
            remove { this._folderChangeHandler.ChangeUndeleted -= value; }
        }
        public event SinglePathHandler FolderBranched {
            add { this._folderChangeHandler.ChangeBranched += value; }
            remove { this._folderChangeHandler.ChangeUndeleted -= value; }
        }
        public event DualPathHandler FolderRenamed {
            add { this._folderChangeHandler.ChangeRenamed += value; }
            remove { this._folderChangeHandler.ChangeRenamed -= value; }
        }

        #endregion

        #region Private Variables

        private static readonly ILog LOG = LogManager.GetLogger(typeof(TfsClientProvider));
        internal static readonly ChangeType CHANGE_MASK = ChangeType.Add | ChangeType.Branch | ChangeType.Delete | ChangeType.Edit | ChangeType.Rename | ChangeType.Undelete;
        internal static readonly ChangeType FULL_MASK = CHANGE_MASK | ChangeType.SourceRename;

        private readonly Uri _serverUri;
        private readonly TfsTeamProjectCollection _tfsProjectCollection;
        private readonly VersionControlServer _versionControlServer;
        private readonly int _startingChangeset;
        private readonly string _remotePath;

        private readonly FileChangeHandler _fileChangeHandler;
        private readonly FolderChangeHandler _folderChangeHandler;

        #endregion

        #region Constructors 

        public TfsClientProvider(string serverUri, string remotePath, string localPath, int fromChangeset, string tfsUsername, string tfsPassword, string tfsDomain) {
            this._serverUri = new Uri(serverUri);
            this._startingChangeset = fromChangeset;
            this._remotePath = remotePath;
            try {
                NetworkCredential tfsCredential = new NetworkCredential(tfsUsername, tfsPassword, tfsDomain);
                this._tfsProjectCollection = new TfsTeamProjectCollection(this._serverUri, tfsCredential);
                this._versionControlServer = this._tfsProjectCollection.GetService<VersionControlServer>();
            } catch (Exception ex) {
                throw new Exception("Error connecting to TFS", ex);
            }

            //clear hooked eventhandlers
            BeginChangeSet = null;
            EndChangeSet = null;
            ChangeSetsFound = null;

            this._fileChangeHandler = new FileChangeHandler(remotePath, localPath);
            this._folderChangeHandler = new FolderChangeHandler(remotePath, localPath);
        }
        #endregion

        #region Public Methods

        public void ProcessAllChangeSets() {
            Changeset changeset;
            if (this._tfsProjectCollection == null) {
                throw new ArgumentException("Cannot call ProcessAllChangeSets() without Connecting first");
            }
            foreach (int changesetId in GetChangesetIds()) {
                changeset = this._versionControlServer.GetChangeset(changesetId, true, true);

                this.BeginChangeSet?.Invoke(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);

                foreach (Change change in OrderChanges(changeset.Changes)) {
                    ProcessChange(changeset, change);
                }

                this.EndChangeSet?.Invoke(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);
            }
        }



        #endregion

        #region Private Methods

        private IEnumerable OrderChanges(Change[] changes) {
            ArrayList Undelete = new ArrayList();
            ArrayList Edit = new ArrayList();
            ArrayList Rename = new ArrayList();
            ArrayList Branch = new ArrayList();
            ArrayList Add = new ArrayList();
            ArrayList Delete = new ArrayList();
            /* Gestion of file swapping */
            ArrayList EditRename_FS = new ArrayList();
            ArrayList Add_FS = new ArrayList();
            ArrayList Delete_FS = new ArrayList();

            foreach (Change change in changes) {
                switch (change.ChangeType & TfsClientProvider.FULL_MASK) {
                    case ChangeType.SourceRename | ChangeType.Delete:
                        Delete_FS.Add(change);
                        break;
                    case ChangeType.SourceRename | ChangeType.Edit:
                    case ChangeType.SourceRename | ChangeType.Rename:
                    case ChangeType.SourceRename | ChangeType.Rename | ChangeType.Edit:
                        EditRename_FS.Add(change);
                        break;
                    case ChangeType.SourceRename | ChangeType.Add:
                    case ChangeType.SourceRename | ChangeType.Add | ChangeType.Edit:
                        Add_FS.Add(change);
                        break;
                    // fin de la gestion du file swapping
                    case ChangeType.Undelete:
                    case ChangeType.Undelete | ChangeType.Edit:
                        Undelete.Add(change);
                        break;
                    case ChangeType.Rename:
                    case ChangeType.Rename | ChangeType.Edit:
                    case ChangeType.Rename | ChangeType.Delete:
                        Rename.Add(change);
                        // no need to handle the edit here, rename will add the modified file to SVN
                        break;
                    case ChangeType.Branch:
                    case ChangeType.Branch | ChangeType.Edit:
                        Branch.Add(change);
                        break;
                    case ChangeType.Add:
                    case ChangeType.Add | ChangeType.Edit:
                        Add.Add(change);
                        break;
                    case ChangeType.Delete:
                        Delete.Add(change);
                        break;
                    case ChangeType.Edit:
                        Edit.Add(change);
                        break;
                    case ChangeType.None:
                    case 0://ChangeType.None different from 0 ?
                        break;
                    default:
                        throw new Exception(String.Format("Unmanaged change to order: {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.FULL_MASK));
                }
            }
            ArrayList l = new ArrayList();
            // add the elements in the order of the following commands
            l.AddRange(Rename);
            l.AddRange(Undelete);
            l.AddRange(Add);
            l.AddRange(Delete);
            l.AddRange(Edit);
            l.AddRange(Branch);

            l.AddRange(Delete_FS);
            l.AddRange(EditRename_FS);
            l.AddRange(Add_FS);

            LOG.Info("Ordered Changes - Begin");
            foreach (Change change in l) {
                LOG.Info(String.Format("Change - Item: {0} ChangeType: {1}", change.Item, change.ChangeType));
            }
            LOG.Info("Ordered Changes - End");
            return l;
        }

        private IEnumerable<int> GetChangesetIds() {
            SortedList sortedChangesets = new SortedList();
            List<int> changesetsIds = new List<int>();

            try {
                ChangesetVersionSpec versionFrom = new ChangesetVersionSpec(_startingChangeset);
                IEnumerable changesets = this._versionControlServer.QueryHistory(this._remotePath, VersionSpec.Latest, 0, RecursionType.Full, null, versionFrom, VersionSpec.Latest, int.MaxValue, false, false);

                foreach (Changeset changeset in changesets) {
                    changesetsIds.Add(changeset.ChangesetId);
                    //sortedChangesets.Add(changeset.ChangesetId, changeset);
                }
                changesetsIds.Sort();

                this.ChangeSetsFound?.Invoke(changesetsIds.Count); //notify the number of found changesets (used in progressbar)
            } catch (Exception ex) {
                throw new Exception("Error while executing TFS QueryHistory", ex);
            }

            return changesetsIds;
        }

        private void ProcessChange(Changeset changeset, Change change) {
            switch (change.Item.ItemType) {
                case ItemType.File:
                    // Process file change.
                    this._fileChangeHandler.ProcessChange(changeset, change);
                    break;
                case ItemType.Folder:
                    // Process folder change.
                    this._folderChangeHandler.ProcessChange(changeset, change);
                    break;
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    if (this._tfsProjectCollection != null) {
                        this._tfsProjectCollection.Dispose();
                    }
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
