using Colyar.SourceControl.TeamFoundationServer;
using log4net;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Colyar.SourceControl.MicrosoftTfsClient
{
    public class TfsClientProvider : TfsClientProviderBase
    {
        #region Public Events

        public override event ChangesetHandler BeginChangeSet;
        public override event ChangesetHandler EndChangeSet;
        public override event SinglePathHandler FileAdded;
        public override event SinglePathHandler FileEdited;
        public override event SinglePathHandler FileDeleted;
        public override event SinglePathHandler FileUndeleted;
        public override event SinglePathHandler FileBranched;
        public override event DualPathHandler FileRenamed;
        public override event SinglePathHandler FolderAdded;
        public override event SinglePathHandler FolderDeleted;
        public override event SinglePathHandler FolderUndeleted;
        public override event SinglePathHandler FolderBranched;
        public override event DualPathHandler FolderRenamed;
        public override event ChangesetsFoundHandler ChangeSetsFound;

        #endregion

        #region Private Variables

        private Uri _serverUri;
        private string _remotePath;
        private string _localPath;
        //private Microsoft.TeamFoundation.Client.TeamFoundationServer _teamFoundationServer;
        private TfsTeamProjectCollection _tfsProjectCollection;
        private VersionControlServer _versionControlServer;
        private int _startingChangeset;
        private static readonly ILog log = LogManager.GetLogger(typeof(TfsClientProvider));
        private static readonly ChangeType changeMask = ChangeType.Add | ChangeType.Branch | ChangeType.Delete | ChangeType.Edit | ChangeType.Rename | ChangeType.Undelete;
        private static readonly ChangeType fullMask = changeMask | ChangeType.SourceRename;
        #endregion

        #region Public Methods

        public override void Connect(string serverUri, string remotePath, string localPath, int fromChangeset, string tfsUsername, string tfsPassword, string tfsDomain)
        {
            this._serverUri = new Uri(serverUri);
            this._remotePath = remotePath;
            this._localPath = localPath;
            this._startingChangeset = fromChangeset;

            try
            {
                NetworkCredential tfsCredential = new NetworkCredential(tfsUsername, tfsPassword, tfsDomain);
                //this._teamFoundationServer = new Microsoft.TeamFoundation.Client.TeamFoundationServer(this._serverUri, tfsCredential);
                this._tfsProjectCollection = new TfsTeamProjectCollection(this._serverUri, tfsCredential);
                this._versionControlServer = this._tfsProjectCollection.GetService<VersionControlServer>();
            }
            catch (Exception ex)
            {
                throw new Exception("Error connecting to TFS", ex);
            }

            //clear hooked eventhandlers
            BeginChangeSet = null;
            EndChangeSet = null;
            FileAdded = null;
            FileEdited = null;
            FileDeleted = null;
            FileUndeleted = null;
            FileBranched = null;
            FileRenamed = null;
            FolderAdded = null;
            FolderDeleted = null;
            FolderUndeleted = null;
            FolderBranched = null;
            FolderRenamed = null;
            ChangeSetsFound = null;
        }

        public override void ProcessAllChangeSets()
        {
            Changeset changeset;
            if (this._tfsProjectCollection == null)
            {
                throw new ArgumentException("Cannot call ProcessAllChangeSets() without Connecting first");
            }
            foreach (int changesetId in GetChangesetIds())
            {
                changeset = this._versionControlServer.GetChangeset(changesetId, true, true);

                if (this.BeginChangeSet != null)
                {
                    this.BeginChangeSet(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);
                }
                foreach (Change change in OrderChanges(changeset.Changes))
                {
                    ProcessChange(changeset, change);
                }
                if (this.EndChangeSet != null)
                {
                    this.EndChangeSet(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);
                }
            }
        }

        private IEnumerable OrderChanges(Change[] changes)
        {
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

            foreach (Change change in changes)
            {
                switch (change.ChangeType & TfsClientProvider.fullMask)
                {
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
                        throw new Exception(String.Format("Unmanaged change to order: {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.fullMask));
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

            log.Info("Ordered Changes - Begin");
            foreach (Change change in l)
            {
                log.Info(String.Format("Change - Item: {0} ChangeType: {1}", change.Item, change.ChangeType));
            }
            log.Info("Ordered Changes - End");
            return l;
        }

        #endregion

        #region Private Methods

        private IEnumerable<int> GetChangesetIds()
        {
            SortedList sortedChangesets = new SortedList();
            List<int> changesetsIds = new List<int>();

            try
            {
                ChangesetVersionSpec versionFrom = new ChangesetVersionSpec(_startingChangeset);
                IEnumerable changesets = this._versionControlServer.QueryHistory(this._remotePath, VersionSpec.Latest, 0, RecursionType.Full, null, versionFrom, VersionSpec.Latest, int.MaxValue, false, false);

                foreach (Changeset changeset in changesets)
                {
                    changesetsIds.Add(changeset.ChangesetId);
                    //sortedChangesets.Add(changeset.ChangesetId, changeset);
                }
                changesetsIds.Sort();
                if (this.ChangeSetsFound != null)
                    this.ChangeSetsFound(changesetsIds.Count); //notify the number of found changesets (used in progressbar)
            }
            catch (Exception ex)
            {
                throw new Exception("Error while executing TFS QueryHistory", ex);
            }

            return changesetsIds;
        }

        private void ProcessChange(Changeset changeset, Change change)
        {
            switch (change.Item.ItemType)
            {
                case ItemType.File:
                    // Process file change.
                    ProcessFileChange(changeset, change);
                    break;
                case ItemType.Folder:
                    // Process folder change.
                    ProcessFolderChange(changeset, change);
                    break;
            }
        }

        private void ProcessFileChange(Changeset changeset, Change change)
        {
            switch (change.ChangeType & TfsClientProvider.changeMask)
            {
                case ChangeType.Undelete:
                case ChangeType.Undelete | ChangeType.Edit:
                    // Undelete file (really just an add)
                    UndeleteFile(changeset, change);
                    break;
                case ChangeType.Rename | ChangeType.Delete:
                    // "Delete, Rename" is possible and should be handled
                    DeleteFile(changeset, change);
                    break;
                case ChangeType.Rename | ChangeType.Edit:
                    //"Edit, Rename" is possible and should be handled
                    RenameFile(changeset, change);
                    EditFile(changeset, change);
                    break;
                case ChangeType.Rename:
                    RenameFile(changeset, change);
                    break;
                case ChangeType.Branch:
                    // Branch file.
                    BranchFile(changeset, change);
                    break;
                case ChangeType.Add:
                case ChangeType.Add | ChangeType.Edit:
                    // Add file.
                    AddFile(changeset, change);
                    break;
                case ChangeType.Delete:
                    // Delete file.
                    DeleteFile(changeset, change);
                    break;
                case ChangeType.Edit:
                    // Edit file.
                    EditFile(changeset, change);
                    break;
                case ChangeType.None:
                case 0://ChangeType.None different from 0 ?
                    break;
                default:
                    throw new Exception(String.Format("Unmanaged file change : {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.changeMask));
            }
        }

        private void ProcessFolderChange(Changeset changeset, Change change)
        {
            switch (change.ChangeType & TfsClientProvider.changeMask)
            {
                case ChangeType.Undelete:
                    // Undelete folder (really just an add)
                    UndeleteFolder(changeset, change);
                    break;
                case ChangeType.Rename | ChangeType.Delete:
                    // "Delete, Rename" is possible and should be handled
                    DeleteFolder(changeset, change);
                    break;
                case ChangeType.Rename:
                    RenameFolder(changeset, change);
                    break;
                case ChangeType.Branch:
                    // Branch folder.
                    BranchFolder(changeset, change);
                    break;
                case ChangeType.Add:
                    // Add folder.
                    AddFolder(changeset, change);
                    break;
                case ChangeType.Delete:
                    // Delete folder.
                    DeleteFolder(changeset, change);
                    break;
                case ChangeType.None:
                    break;
                default:
                    throw new Exception(String.Format("Unmanaged folder change : {0}", change.ChangeType));
            }
        }

        private void AddFile(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            DownloadItemFile(change, itemPath);

            if (this.FileAdded != null)
                this.FileAdded(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void BranchFile(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            DownloadItemFile(change, itemPath);

            if (this.FileBranched != null)
                this.FileBranched(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void UndeleteFile(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            DownloadItemFile(change, itemPath);

            if (this.FileUndeleted != null)
                this.FileUndeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void DeleteFile(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);

            if (this.FileDeleted != null)
                this.FileDeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void EditFile(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            DownloadItemFile(change, itemPath);

            if (this.FileEdited != null)
                this.FileEdited(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void DownloadItemFile(Change change, string targetPath)
        {
            try
            {
                //File.Delete is not needed (this is handled inside DownloadFile)
                change.Item.DownloadFile(targetPath);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Error while downloading file '{0}' in Changeset #{1}.", targetPath, change.Item.ChangesetId), ex);
            }
        }

        private void RenameFile(Changeset changeset, Change change)
        {
            string oldPath = GetItemPath(GetPreviousItem(change.Item));
            string newPath = GetItemPath(change.Item);

            if (this.FileRenamed != null)
                this.FileRenamed(changeset.ChangesetId, oldPath, newPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void AddFolder(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);

            if (this.FolderAdded != null)
                this.FolderAdded(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void BranchFolder(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);

            if (this.FolderBranched != null)
                this.FolderBranched(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void UndeleteFolder(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);

            if (this.FolderUndeleted != null)
                this.FolderUndeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void DeleteFolder(Changeset changeset, Change change)
        {
            string itemPath = GetItemPath(change.Item);

            if (this.FolderDeleted != null)
                this.FolderDeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void RenameFolder(Changeset changeset, Change change)
        {
            string oldPath = GetItemPath(GetPreviousItem(change.Item));
            string newPath = GetItemPath(change.Item);

            if (this.FolderRenamed != null)
                this.FolderRenamed(changeset.ChangesetId, oldPath, newPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private string GetItemPath(Item item)
        {
            if (!item.ServerItem.ToLowerInvariant().StartsWith(this._remotePath.ToLowerInvariant()))
                throw new Exception(item.ServerItem + " is not contained in " + this._remotePath);

            return String.Concat(this._localPath, item.ServerItem.Remove(0, this._remotePath.Length).Replace("/", "\\"));
            //return this._localPath + item.ServerItem.Replace(this._remotePath, "").Replace("/", "\\");
            //TODO: maybe use System.IO.Path.Combine()
        }

        private Item GetPreviousItem(Item item)
        {
            try
            {
                IEnumerable changesets = item.VersionControlServer.QueryHistory(
                    item.ServerItem, new ChangesetVersionSpec(item.ChangesetId), 0, RecursionType.None, null,
                    new ChangesetVersionSpec(1), new ChangesetVersionSpec(item.ChangesetId - 1), int.MaxValue,
                    true, false);

                foreach (Changeset changeset in changesets)
                {
                    return changeset.Changes[0].Item;
                }
                return item.VersionControlServer.GetItem(item.ItemId, item.ChangesetId - 1, false);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while executing GetPreviousItem", ex);
            }
        }

        #endregion
    }
}
