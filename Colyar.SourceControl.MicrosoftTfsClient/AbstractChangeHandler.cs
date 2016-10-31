namespace Tfs2Svn.TfsClient
{
    using System;
    using System.Collections;
    using System.IO;
    using Common;
    using Microsoft.TeamFoundation.VersionControl.Client;

    internal abstract class AbstractChangeHandler
    {
        private readonly string localPath;

        private readonly string remotePath;

        protected AbstractChangeHandler(string remotePath, string localPath)
        {
            // clear hooked eventhandlers
            this.ChangeAdded = null;
            this.ChangeEdited = null;
            this.ChangeDeleted = null;
            this.ChangeUndeleted = null;
            this.ChangeBranched = null;
            this.ChangeRenamed = null;

            this.remotePath = remotePath;
            this.localPath = localPath;
        }

        public event SinglePathHandler ChangeAdded;

        public event SinglePathHandler ChangeBranched;

        public event SinglePathHandler ChangeDeleted;

        public event SinglePathHandler ChangeEdited;

        public event DualPathHandler ChangeRenamed;

        public event SinglePathHandler ChangeUndeleted;

        public void ProcessChange(Changeset changeset, Change change)
        {
            switch (change.ChangeType & TfsClientProvider.ChangeMask)
            {
                case ChangeType.Undelete:
                case ChangeType.Undelete | ChangeType.Edit:
                    // Undelete file (really just an add)
                    this.UndeleteChangeElement(changeset, change);
                    break;

                case ChangeType.Rename | ChangeType.Delete:
                    // "Delete, Rename" is possible and should be handled
                    this.DeleteChangeElement(changeset, change);
                    break;

                case ChangeType.Rename | ChangeType.Edit:
                    // "Edit, Rename" is possible and should be handled
                    this.RenameChangeElement(changeset, change);
                    this.EditChangeElement(changeset, change);
                    break;

                case ChangeType.Rename:
                    this.RenameChangeElement(changeset, change);
                    break;

                case ChangeType.Branch:
                case ChangeType.Branch | ChangeType.Edit:
                    // Branch file.
                    this.BranchChangeElement(changeset, change);
                    break;

                case ChangeType.Add:
                case ChangeType.Add | ChangeType.Edit:
                    // Add file.
                    this.AddChangeElement(changeset, change);
                    break;

                case ChangeType.Delete:
                    // Delete file.
                    this.DeleteChangeElement(changeset, change);
                    break;

                case ChangeType.Edit:
                    // Edit file.
                    this.EditChangeElement(changeset, change);
                    break;

                case ChangeType.None:
                case 0: // ChangeType.None different from 0 ?
                    break;

                default:
                    throw new Exception(string.Format("Unmanaged file change : {0}, minus mask : {1} ", change.ChangeType, change.ChangeType & TfsClientProvider.ChangeMask));
            }
        }

        protected abstract void AddChangeElement(Changeset changeset, Change change);

        protected abstract void BranchChangeElement(Changeset changeset, Change change);

        protected abstract void DeleteChangeElement(Changeset changeset, Change change);

        protected abstract void EditChangeElement(Changeset changeset, Change change);

        protected string GetItemPath(Item item)
        {
            if (!item.ServerItem.StartsWith(this.remotePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(item.ServerItem + " is not contained in " + this.remotePath);
            }

            string serverRelativePath = item.ServerItem == this.remotePath ? string.Empty : item.ServerItem.Remove(0, this.remotePath.Length + 1).Replace("/", "\\");
            return Path.Combine(this.localPath, serverRelativePath);
        }

        protected Item GetPreviousItem(Item item)
        {
            try
            {
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
                    false);

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

        protected void OnChangeAdded(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeAdded?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeBranched(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeBranched?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeDeleted(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeDeleted?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeEdited(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeEdited?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected void OnChangeRenamed(int changeSetId, string oldPath, string newPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeRenamed?.Invoke(changeSetId, oldPath, newPath, committer, comment, creationDate);
        }

        protected void OnChangeUndeleted(int changeSetId, string itemPath, string committer, string comment, DateTime creationDate)
        {
            this.ChangeUndeleted?.Invoke(changeSetId, itemPath, committer, comment, creationDate);
        }

        protected abstract void RenameChangeElement(Changeset changeset, Change change);

        protected abstract void UndeleteChangeElement(Changeset changeset, Change change);
    }
}