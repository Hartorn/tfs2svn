namespace Tfs2Svn.TfsClient
{
    using System;
    using System.IO;
    using Microsoft.TeamFoundation.VersionControl.Client;

    internal sealed class FolderChangeHandler : AbstractChangeHandler
    {
        internal FolderChangeHandler(string remotePath, string localPath)
            : base(remotePath, localPath)
        {
        }

        protected override void AddChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);
            this.OnChangeAdded(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void BranchChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);
            this.OnChangeBranched(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void DeleteChangeElement(Changeset changeset, Change change)
        {
            string itemPath = this.GetItemPath(change.Item);
            this.OnChangeDeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void EditChangeElement(Changeset changeset, Change change)
        {
            throw new NotImplementedException("Cannot edit folder ChangeSet:" + changeset.ChangesetId + " Folder:" + this.GetItemPath(change.Item));
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
            Directory.CreateDirectory(itemPath);

            this.OnChangeUndeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }
    }
}