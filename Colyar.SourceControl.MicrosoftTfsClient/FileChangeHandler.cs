namespace Tfs2Svn.TfsClient
{
    using System;
    using Microsoft.TeamFoundation.VersionControl.Client;

    internal sealed class FileChangeHandler : AbstractChangeHandler
    {
        internal FileChangeHandler(string remotePath, string localPath)
            : base(remotePath, localPath)
        {
        }

        protected override void AddChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.DownloadItemFile(change, itemPath);
            this.OnChangeAdded(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void BranchChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.DownloadItemFile(change, itemPath);
            this.OnChangeBranched(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void DeleteChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.OnChangeDeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void EditChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.DownloadItemFile(change, itemPath);
            this.OnChangeEdited(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void RenameChangeElement(Changeset changeset, Change change)
        {
            string oldPath = this.GetItemPath(this.GetPreviousItem(change.Item));
            string newPath = this.GetItemPath(change.Item);
            this.OnChangeRenamed(changeset.ChangesetId, oldPath, newPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void UndeleteChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.DownloadItemFile(change, itemPath);
            this.OnChangeUndeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        private void DownloadItemFile(Change change, string targetPath)
        {
            try
            {
                // File.Delete is not needed (this is handled inside DownloadFile)
                change.Item.DownloadFile(targetPath);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error while downloading file '{0}' in Changeset #{1}.", targetPath, change.Item.ChangesetId), ex);
            }
        }
    }
}