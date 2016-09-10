using System;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace Colyar.SourceControl.MicrosoftTfsClient {
    internal sealed class FolderChangeHandler : AbstractChangeHandler {
        #region Constructor
        internal FolderChangeHandler(string remotePath, string localPath) : base(remotePath, localPath) {
        }
        #endregion

        #region Protected Abstract Methods
        protected override void AddChangeElement(Changeset changeset, Change change) {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);
            this.OnChangeAdded(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void BranchChangeElement(Changeset changeset, Change change) {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);
            this.OnChangeBranched(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void DeleteChangeElement(Changeset changeset, Change change) {
            string itemPath = GetItemPath(change.Item);
            this.OnChangeDeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void EditChangeElement(Changeset changeset, Change change) {
            throw new NotImplementedException("Cannot edit folder ChangeSet:" + changeset.ChangesetId + " Folder:" + GetItemPath(change.Item));
        }

        protected override void RenameChangeElement(Changeset changeset, Change change) {
            string oldPath = GetItemPath(GetPreviousItem(change.Item));
            string newPath = GetItemPath(change.Item);
            this.OnChangeRenamed(changeset.ChangesetId, oldPath, newPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }

        protected override void UndeleteChangeElement(Changeset changeset, Change change) {
            string itemPath = GetItemPath(change.Item);
            Directory.CreateDirectory(itemPath);

            this.OnChangeUndeleted(changeset.ChangesetId, itemPath, changeset.Committer, changeset.Comment, changeset.CreationDate);
        }
        #endregion
    }
}
