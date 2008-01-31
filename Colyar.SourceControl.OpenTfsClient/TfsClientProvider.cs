using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Net;
using Colyar.SourceControl;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Colyar.SourceControl.TeamFoundationServer;

namespace Colyar.SourceControl.OpenTfsClient
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

        private string _serverUri;
        private string _remotePath;
        private string _localPath;
        private Microsoft.TeamFoundation.Client.TeamFoundationServer _teamFoundationServer;
        private VersionControlServer _versionControlServer;
        private int _startingChangeset;

        #endregion

        #region Public Methods

        public override void Connect(string serverUri, string remotePath, string localPath, int fromChangeset, string tfsUsername, string tfsPassword, string tfsDomain)
        {
            this._serverUri = serverUri;
            this._remotePath = remotePath;
            this._localPath = localPath;
            this._startingChangeset = fromChangeset;

            try
            {
                NetworkCredential tfsCredential = new NetworkCredential(tfsUsername, tfsPassword, tfsDomain);
                this._teamFoundationServer = new Microsoft.TeamFoundation.Client.TeamFoundationServer(this._serverUri, tfsCredential);
                this._versionControlServer = (VersionControlServer)this._teamFoundationServer.GetService(typeof(VersionControlServer));
            }
            catch (Exception ex)
            {
                throw new Exception("Error connecting to TFS", ex);
            }
        }

        public override void ProcessAllChangeSets()
        {
            if (this._teamFoundationServer == null)
                throw new ArgumentException("Cannot call ProcessAllChangeSets() without Connecting first");

            foreach (Changeset changeset in GetChangesets())
            {
                if (this.BeginChangeSet != null)
                    this.BeginChangeSet(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);

                foreach (Change change in changeset.Changes) //OrderChanges(changeset.Changes))
                    ProcessChange(changeset, change);

                if (this.EndChangeSet != null)
                    this.EndChangeSet(changeset.ChangesetId, changeset.Committer, changeset.Comment, changeset.CreationDate);
            }
        }

        #endregion

        #region Private Methods

        private IEnumerable GetChangesets()
        {
            SortedList sortedChangesets = new SortedList();

            try
            {
                ChangesetVersionSpec versionFrom = new ChangesetVersionSpec(_startingChangeset);
                IEnumerable changesets = this._versionControlServer.QueryHistory(this._remotePath, VersionSpec.Latest, 0, RecursionType.Full, null, versionFrom, VersionSpec.Latest, int.MaxValue, true, false);

                int count = 0;
                foreach (Changeset changeset in changesets)
                {
                    //WORKAROUND: OpenTF contains a bug when requesting >256 Changesets that 
                    //  causes duplicate Changesets in the result of QueryHistory. 
                    //  So we check for that here.
                    if (!sortedChangesets.ContainsKey(changeset.ChangesetId))
                    {
                        count++;
                        sortedChangesets.Add(changeset.ChangesetId, changeset);
                    }
                }

                if (this.ChangeSetsFound != null)
                    this.ChangeSetsFound(count); //notify the number of found changesets (used in progressbar)
            }
            catch (Exception ex)
            {
                throw new Exception("Error while executing TFS QueryHistory", ex);
            }

            return sortedChangesets.Values;
        }

        private void ProcessChange(Changeset changeset, Change change)
        {
            // Process file change.
            if (change.Item.ItemType == ItemType.File)
                ProcessFileChange(changeset, change);

              // Process folder change.
            else if (change.Item.ItemType == ItemType.Folder)
                ProcessFolderChange(changeset, change);
        }

        private void ProcessFileChange(Changeset changeset, Change change)
        {
            // Undelete file (really just an add)
            if ((change.ChangeType & ChangeType.Undelete) == ChangeType.Undelete)
                UndeleteFile(changeset, change);

            // Rename file.
            else if ((change.ChangeType & ChangeType.Rename) == ChangeType.Rename)
            {
                RenameFile(changeset, change);

                //"Edit, Rename" is possible and should be handled 
                if ((change.ChangeType & ChangeType.Edit) == ChangeType.Edit)
                    EditFile(changeset, change);
            }

            // Branch file.
            else if ((change.ChangeType & ChangeType.Branch) == ChangeType.Branch)
                BranchFile(changeset, change);

            // Add file.
            else if ((change.ChangeType & ChangeType.Add) == ChangeType.Add)
                AddFile(changeset, change);

            // Delete file.
            else if ((change.ChangeType & ChangeType.Delete) == ChangeType.Delete)
                DeleteFile(changeset, change);

            // Edit file.
            else if ((change.ChangeType & ChangeType.Edit) == ChangeType.Edit)
                EditFile(changeset, change);
        }

        private void ProcessFolderChange(Changeset changeset, Change change)
        {
            // Undelete folder.
            if ((change.ChangeType & ChangeType.Undelete) == ChangeType.Undelete)
                UndeleteFolder(changeset, change);

            // Rename folder.
            else if ((change.ChangeType & ChangeType.Rename) == ChangeType.Rename)
                RenameFolder(changeset, change);

            // Branch folder.
            else if ((change.ChangeType & ChangeType.Branch) == ChangeType.Branch)
                BranchFolder(changeset, change);

            // Add folder.
            else if ((change.ChangeType & ChangeType.Add) == ChangeType.Add)
                AddFolder(changeset, change);

            // Delete folder.
            else if ((change.ChangeType & ChangeType.Delete) == ChangeType.Delete)
                DeleteFolder(changeset, change);
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