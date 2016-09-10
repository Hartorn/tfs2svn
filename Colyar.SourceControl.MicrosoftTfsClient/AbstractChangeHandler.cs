using System;
using System.Collections;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace Colyar.SourceControl.MicrosoftTfsClient {
    internal abstract class AbstractChangeHandler {

        #region Protected Events
        public event SinglePathHandler ChangeAdded;
        public event SinglePathHandler ChangeEdited;
        public event SinglePathHandler ChangeDeleted;
        public event SinglePathHandler ChangeUndeleted;
        public event SinglePathHandler ChangeBranched;
        public event DualPathHandler ChangeRenamed;
        #endregion

        #region Private Fields 
        private readonly string _remotePath;
        private readonly string _localPath;

        #endregion

        #region Public Constructor
        protected AbstractChangeHandler(string remotePath, string localPath) {
            //clear hooked eventhandlers
            ChangeAdded = null;
            ChangeEdited = null;
            ChangeDeleted = null;
            ChangeUndeleted = null;
            ChangeBranched = null;
            ChangeRenamed = null;

            this._remotePath = remotePath;
            this._localPath = localPath;
        }
        #endregion

        #region Public Methods
        public void ProcessChange(Changeset changeset, Change change) {
            switch (change.ChangeType & TfsClientProvider.CHANGE_MASK) {
                case ChangeType.Undelete:
                case ChangeType.Undelete | ChangeType.Edit:
                    // Undelete file (really just an add)
                    UndeleteChangeElement(changeset, change);
                    break;
                case ChangeType.Rename | ChangeType.Delete:
                    // "Delete, Rename" is possible and should be handled
                    DeleteChangeElement(changeset, change);
                    break;
                case ChangeType.Rename | ChangeType.Edit:
                    //"Edit, Rename" is possible and should be handled
                    RenameChangeElement(changeset, change);
                    EditChangeElement(changeset, change);
                    break;
                case ChangeType.Rename:
                    RenameChangeElement(changeset, change);
                    break;
                case ChangeType.Branch:
                case ChangeType.Branch | ChangeType.Edit:
                    // Branch file.
                    BranchChangeElement(changeset, change);
                    break;
                case ChangeType.Add:
                case ChangeType.Add | ChangeType.Edit:
                    // Add file.
                    AddChangeElement(changeset, change);
                    break;
                case ChangeType.Delete:
                    // Delete file.
                    DeleteChangeElement(changeset, change);
                    break;
                case ChangeType.Edit:
                    // Edit file.
                    EditChangeElement(changeset, change);
                    break;
                case ChangeType.None:
                case 0://ChangeType.None different from 0 ?
                    break;
                default:
                    throw new Exception(String.Format("Unmanaged file change : {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.CHANGE_MASK));
            }
        }
        #endregion

        #region Abstract methods
        protected abstract void UndeleteChangeElement(Changeset changeset, Change change);
        protected abstract void DeleteChangeElement(Changeset changeset, Change change);
        protected abstract void RenameChangeElement(Changeset changeset, Change change);
        protected abstract void EditChangeElement(Changeset changeset, Change change);
        protected abstract void BranchChangeElement(Changeset changeset, Change change);
        protected abstract void AddChangeElement(Changeset changeset, Change change);
        #endregion

        #region Protected Methods

        protected void OnChangeAdded(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate) {
            this.ChangeAdded?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeBranched(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate) {
            this.ChangeBranched?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeDeleted(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate) {
            this.ChangeDeleted?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }
        protected void OnChangeUndeleted(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate) {
            this.ChangeUndeleted?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeEdited(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate) {
            this.ChangeEdited?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeRenamed(int changeSetId, string oldPath, string newPath, string committer, string comment, DateTime creationDate) {
            this.ChangeRenamed?.Invoke(changeSetId, oldPath, newPath, committer, comment, creationDate);
        }

        protected string GetItemPath(Item item) {
            if (!item.ServerItem.StartsWith(this._remotePath, StringComparison.OrdinalIgnoreCase))
                throw new Exception(item.ServerItem + " is not contained in " + this._remotePath);
            string serverRelativePath = item.ServerItem == this._remotePath ? "" : item.ServerItem.Remove(0, this._remotePath.Length + 1).Replace("/", "\\");
            return Path.Combine(this._localPath, serverRelativePath);
        }

        protected Item GetPreviousItem(Item item) {
            try {
                IEnumerable changesets = item.VersionControlServer.QueryHistory(
                    item.ServerItem,
                    new ChangesetVersionSpec(item.ChangesetId),
                    0,
                    RecursionType.None,
                    null,
                    new ChangesetVersionSpec(1),
                    new ChangesetVersionSpec(item.ChangesetId - 1),
                    int.MaxValue,
                    true,
                    false
                 );

                foreach (Changeset changeset in changesets) {
                    return changeset.Changes[0].Item;
                }
                return item.VersionControlServer.GetItem(item.ItemId, item.ChangesetId - 1, false);
            } catch (Exception ex) {
                throw new Exception("Error while executing GetPreviousItem", ex);
            }
        }

        #endregion
    }
}
