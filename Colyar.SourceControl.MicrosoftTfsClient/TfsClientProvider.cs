namespace Tfs2Svn.TfsClient
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Net;
    using Common;
    using log4net;
    using Microsoft.TeamFoundation.Client;
    using Microsoft.TeamFoundation.VersionControl.Client;

    public class TfsClientProvider : IDisposable
    {
        internal static readonly ChangeType ChangeMask = ChangeType.Add | ChangeType.Branch | ChangeType.Delete | ChangeType.Edit | ChangeType.Rename | ChangeType.Undelete;

        internal static readonly ChangeType FullMask = ChangeMask | ChangeType.SourceRename;

        private static readonly ILog LOG = LogManager.GetLogger(typeof(TfsClientProvider));

        private readonly FileChangeHandler fileChangeHandler;

        private readonly FolderChangeHandler folderChangeHandler;

        private readonly string remotePath;
        private readonly Uri serverUri;
        private readonly int startingChangeset;
        private readonly TfsTeamProjectCollection tfsProjectCollection;
        private readonly VersionControlServer versionControlServer;
        private bool disposedValue = false;

        public TfsClientProvider(string serverUri, string remotePath, string localPath, int fromChangeset, string tfsUsername, string tfsPassword, string tfsDomain)
        {
            this.serverUri = new Uri(serverUri);
            this.startingChangeset = fromChangeset;
            this.remotePath = remotePath;
            try
            {
                NetworkCredential tfsCredential = new NetworkCredential(tfsUsername, tfsPassword, tfsDomain);
                this.tfsProjectCollection = new TfsTeamProjectCollection(this.serverUri, tfsCredential);
                this.versionControlServer = this.tfsProjectCollection.GetService<VersionControlServer>();
            }
            catch (Exception ex)
            {
                throw new Exception("Error connecting to TFS", ex);
            }

            // clear hooked eventhandlers
            this.BeginChangeSet = null;
            this.EndChangeSet = null;
            this.ChangeSetsFound = null;

            this.fileChangeHandler = new FileChangeHandler(remotePath, localPath);
            this.folderChangeHandler = new FolderChangeHandler(remotePath, localPath);
        }

        public event ChangesetHandler BeginChangeSet;

        public event ChangesetsFoundHandler ChangeSetsFound;

        public event ChangesetHandler EndChangeSet;

        public event SinglePathHandler FileAdded
        {
            add { this.fileChangeHandler.ChangeAdded += value; }
            remove { this.fileChangeHandler.ChangeAdded -= value; }
        }

        public event SinglePathHandler FileBranched
        {
            add { this.fileChangeHandler.ChangeBranched += value; }
            remove { this.fileChangeHandler.ChangeUndeleted -= value; }
        }

        public event SinglePathHandler FileDeleted
        {
            add { this.fileChangeHandler.ChangeDeleted += value; }
            remove { this.fileChangeHandler.ChangeDeleted -= value; }
        }

        public event SinglePathHandler FileEdited
        {
            add { this.fileChangeHandler.ChangeEdited += value; }
            remove { this.fileChangeHandler.ChangeEdited -= value; }
        }

        public event DualPathHandler FileRenamed
        {
            add { this.fileChangeHandler.ChangeRenamed += value; }
            remove { this.fileChangeHandler.ChangeRenamed -= value; }
        }

        public event SinglePathHandler FileUndeleted
        {
            add { this.fileChangeHandler.ChangeUndeleted += value; }
            remove { this.fileChangeHandler.ChangeUndeleted -= value; }
        }

        public event SinglePathHandler FolderAdded
        {
            add { this.folderChangeHandler.ChangeAdded += value; }
            remove { this.folderChangeHandler.ChangeAdded -= value; }
        }

        public event SinglePathHandler FolderBranched
        {
            add { this.folderChangeHandler.ChangeBranched += value; }
            remove { this.folderChangeHandler.ChangeUndeleted -= value; }
        }

        public event SinglePathHandler FolderDeleted
        {
            add { this.folderChangeHandler.ChangeDeleted += value; }
            remove { this.folderChangeHandler.ChangeDeleted -= value; }
        }

        public event DualPathHandler FolderRenamed
        {
            add { this.folderChangeHandler.ChangeRenamed += value; }
            remove { this.folderChangeHandler.ChangeRenamed -= value; }
        }

        public event SinglePathHandler FolderUndeleted
        {
            add { this.folderChangeHandler.ChangeUndeleted += value; }
            remove { this.folderChangeHandler.ChangeUndeleted -= value; }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
        }

        public void ProcessAllChangeSets()
        {
            Changeset changeset;
            if (this.tfsProjectCollection == null)
            {
                throw new ArgumentException("Cannot call ProcessAllChangeSets() without Connecting first");
            }

            foreach (int changesetId in this.GetChangesetIds())
            {
                changeset = this.versionControlServer.GetChangeset(changesetId, true, true);

                this.BeginChangeSet?.Invoke(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);

                foreach (Change change in this.OrderChanges(changeset.Changes))
                {
                    this.ProcessChange(changeset, change);
                }

                this.EndChangeSet?.Invoke(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    if (this.tfsProjectCollection != null)
                    {
                        this.tfsProjectCollection.Dispose();
                    }
                }

                this.disposedValue = true;
            }
        }

        private IEnumerable<int> GetChangesetIds()
        {
            SortedList sortedChangesets = new SortedList();
            List<int> changesetsIds = new List<int>();

            try
            {
                ChangesetVersionSpec versionFrom = new ChangesetVersionSpec(this.startingChangeset);
                IEnumerable changesets = this.versionControlServer.QueryHistory(this.remotePath, VersionSpec.Latest, 0, RecursionType.Full, null, versionFrom, VersionSpec.Latest, int.MaxValue, false, false);

                foreach (Changeset changeset in changesets)
                {
                    changesetsIds.Add(changeset.ChangesetId);

                    // sortedChangesets.Add(changeset.ChangesetId, changeset);
                }

                changesetsIds.Sort();

                this.ChangeSetsFound?.Invoke(changesetsIds.Count); // notify the number of found changesets (used in progressbar)
            }
            catch (Exception ex)
            {
                throw new Exception("Error while executing TFS QueryHistory", ex);
            }

            return changesetsIds;
        }

        private IEnumerable OrderChanges(Change[] changes)
        {
            ArrayList undelete = new ArrayList();
            ArrayList edit = new ArrayList();
            ArrayList rename = new ArrayList();
            ArrayList branch = new ArrayList();
            ArrayList add = new ArrayList();
            ArrayList delete = new ArrayList();
            /* Gestion of file swapping */
            ArrayList editRename_FS = new ArrayList();
            ArrayList add_FS = new ArrayList();
            ArrayList delete_FS = new ArrayList();

            foreach (Change change in changes)
            {
                switch (change.ChangeType & TfsClientProvider.FullMask)
                {
                    case ChangeType.SourceRename | ChangeType.Delete:
                        delete_FS.Add(change);
                        break;

                    case ChangeType.SourceRename | ChangeType.Edit:
                    case ChangeType.SourceRename | ChangeType.Rename:
                    case ChangeType.SourceRename | ChangeType.Rename | ChangeType.Edit:
                        editRename_FS.Add(change);
                        break;

                    case ChangeType.SourceRename | ChangeType.Add:
                    case ChangeType.SourceRename | ChangeType.Add | ChangeType.Edit:
                        add_FS.Add(change);
                        break;

                    // fin de la gestion du file swapping
                    case ChangeType.Undelete:
                    case ChangeType.Undelete | ChangeType.Edit:
                        undelete.Add(change);
                        break;

                    case ChangeType.Rename:
                    case ChangeType.Rename | ChangeType.Edit:
                    case ChangeType.Rename | ChangeType.Delete:
                        rename.Add(change);

                        // no need to handle the edit here, rename will add the modified file to SVN
                        break;

                    case ChangeType.Branch:
                    case ChangeType.Branch | ChangeType.Edit:
                        branch.Add(change);
                        break;

                    case ChangeType.Add:
                    case ChangeType.Add | ChangeType.Edit:
                        add.Add(change);
                        break;

                    case ChangeType.Delete:
                        delete.Add(change);
                        break;

                    case ChangeType.Edit:
                        edit.Add(change);
                        break;

                    case ChangeType.None:
                    case 0: // ChangeType.None different from 0 ?
                        break;

                    default:
                        throw new Exception(string.Format("Unmanaged change to order: {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.FullMask));
                }
            }

            ArrayList l = new ArrayList();

            // add the elements in the order of the following commands
            l.AddRange(rename);
            l.AddRange(undelete);
            l.AddRange(add);
            l.AddRange(delete);
            l.AddRange(edit);
            l.AddRange(branch);

            l.AddRange(delete_FS);
            l.AddRange(editRename_FS);
            l.AddRange(add_FS);

            LOG.Info("Ordered Changes - Begin");
            foreach (Change change in l)
            {
                LOG.Info(string.Format("Change - Item: {0} ChangeType: {1}", change.Item, change.ChangeType));
            }

            LOG.Info("Ordered Changes - End");
            return l;
        }

        private void ProcessChange(Changeset changeset, Change change)
        {
            switch (change.Item.ItemType)
            {
                case ItemType.File:
                    // Process file change.
                    this.fileChangeHandler.ProcessChange(changeset, change);
                    break;

                case ItemType.Folder:
                    // Process folder change.
                    this.folderChangeHandler.ProcessChange(changeset, change);
                    break;
            }
        }

        // To detect redundant calls
    }
}