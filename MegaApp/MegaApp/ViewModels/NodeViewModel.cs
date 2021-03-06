﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Storage;
using mega;
using MegaApp.Classes;
using MegaApp.Database;
using MegaApp.Enums;
using MegaApp.Extensions;
using MegaApp.Interfaces;
using MegaApp.MegaApi;
using MegaApp.Services;
using MegaApp.Views;
using MegaApp.ViewModels.Dialogs;
using MegaApp.ViewModels.SharedFolders;

namespace MegaApp.ViewModels
{
    /// <summary>
    /// ViewModel of the main MEGA datatype (MNode)
    /// </summary>
    public abstract class NodeViewModel : BaseNodeViewModel, IMegaNode
    {
        // Offset DateTime value to calculate the correct creation and modification time
        private static readonly DateTime OriginalDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        protected NodeViewModel(MegaSDK megaSdk, AppInformation appInformation, MNode megaNode, FolderViewModel parent,
            ObservableCollection<IBaseNode> parentCollection = null, ObservableCollection<IBaseNode> childCollection = null)
            : base(megaSdk)
        {
            this.AccessLevel = new AccessLevelViewModel();
            
            this.Parent = parent;
            this.ParentContainerType = parent != null ? Parent.Type : ContainerType.FileLink;
            this.ParentCollection = parentCollection;
            this.ChildCollection = childCollection;

            this.CopyOrMoveCommand = new RelayCommand(CopyOrMove);
            this.DownloadCommand = new RelayCommand(Download);
            this.GetLinkCommand = new RelayCommandAsync<bool,bool>(GetLinkAsync);
            this.ImportCommand = new RelayCommand(Import);
            this.PreviewCommand = new RelayCommand(Preview);
            this.RemoveCommand = new RelayCommand(Remove);
            this.RemoveLinkCommand = new RelayCommandAsync<bool>(RemoveLinkAsync);
            this.RenameCommand = new RelayCommand(Rename);
            this.RestoreCommand = new RelayCommand(Restore);
            this.OpenInformationPanelCommand = new RelayCommand(OpenInformationPanel);

            Update(megaNode);
            SetDefaultValues();

            Transfer = new TransferObjectModel(megaSdk, this, MTransferType.TYPE_DOWNLOAD, LocalDownloadPath);
            OfflineTransfer = new TransferObjectModel(megaSdk, this, MTransferType.TYPE_DOWNLOAD, OfflinePath);
        }

        private void ParentOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(this.Parent.Folder))
            {
                OnPropertyChanged(nameof(this.Parent));
                OnPropertyChanged(nameof(this.NodeBinding));
            }
        }

        #region Commands

        public ICommand CopyOrMoveCommand { get; }
        public ICommand DownloadCommand { get; set; }
        public ICommand GetLinkCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand RemoveLinkCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand OpenInformationPanelCommand { get; set; }

        #endregion

        #region Private Methods

        private void SetDefaultValues()
        {
            this.DisplayMode = NodeDisplayMode.Normal;

            if (this.Type == MNodeType.TYPE_FOLDER) return;

            if (FileService.FileExists(this.ThumbnailPath))
                this.ThumbnailImageUri = new Uri(this.ThumbnailPath);
            else
                this.DefaultImagePathData = ImageService.GetDefaultFileTypePathData(this.Name);
        }

        /// <summary>
        /// Get the thumbnail of the node
        /// </summary>
        private async void GetThumbnail()
        {
            if (FileService.FileExists(this.ThumbnailPath))
            {
                this.ThumbnailImageUri = new Uri(this.ThumbnailPath);
            }
            else if (Convert.ToBoolean(this.MegaSdk.isLoggedIn()) || this.ParentContainerType == ContainerType.FolderLink)
            {
                var getThumbnail = new GetThumbnailRequestListenerAsync();
                var result = await getThumbnail.ExecuteAsync(() =>
                {
                    this.MegaSdk.getThumbnail(this.OriginalMNode, 
                        this.ThumbnailPath, getThumbnail);
                });
                
                if(result)
                    UiService.OnUiThread(() => this.ThumbnailImageUri = new Uri(this.ThumbnailPath));
            }
        }

        /// <summary>
        /// Convert the MEGA time to a C# DateTime object in local time
        /// </summary>
        /// <param name="time">MEGA time</param>
        /// <returns>DateTime object in local time</returns>
        private static DateTime ConvertDateToString(ulong time)
        {
            return OriginalDateTime.AddSeconds(time).ToLocalTime();
        }

        #endregion

        #region IBaseNode Interface

        public override bool IsFolder => this.Type == MNodeType.TYPE_FOLDER;

        #endregion

        public NodeViewModel NodeBinding => this;

        private int _childFiles;
        public int ChildFiles
        {
            get { return _childFiles; }
            set { SetField(ref _childFiles, value); }
        }

        private int _childFolders;
        public int ChildFolders
        {
            get { return _childFolders; }
            set { SetField(ref _childFolders, value); }
        }

        public virtual Uri PreviewImageUri
        {
            get
            {
                return ((this is ImageNodeViewModel) && (this as ImageNodeViewModel) != null) ?
                    (this as ImageNodeViewModel).PreviewImageUri : null;
            }

            set
            {
                if((this is ImageNodeViewModel) && (this as ImageNodeViewModel != null))
                    (this as ImageNodeViewModel).PreviewImageUri = value;
            }
        }        

        #region IMegaNode Interface

        public async void Rename()
        {
            await RenameAsync();
        }

        public async Task RenameAsync()
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return;

            if (this.Parent?.FolderRootNode == null)
            {
                OnUiThread(async () =>
                {
                    await DialogService.ShowAlertAsync(
                        ResourceService.AppMessages.GetString("AM_RenameNodeFailed_Title"),
                        ResourceService.AppMessages.GetString("AM_RenameNodeFailed"));
                });
                return;
            }

            await DialogService.ShowInputAsyncActionDialogAsync(
                ResourceService.UiResources.GetString("UI_Rename"),
                ResourceService.UiResources.GetString("UI_TypeNewName"),
                async (string inputName) =>
                {
                    if (string.IsNullOrWhiteSpace(inputName)) return false;
                    if (this.Name.Equals(inputName)) return true;

                    if(SdkService.ExistsNodeByName(this.Parent.FolderRootNode.OriginalMNode, inputName, this.OriginalMNode.isFolder()))
                    {
                        DialogService.SetInputDialogWarningMessage(this.OriginalMNode.isFolder() ?
                            ResourceService.AppMessages.GetString("AM_FolderAlreadyExists") :
                            ResourceService.AppMessages.GetString("AM_FileAlreadyExists"));
                        return false;
                    }

                    var rename = new RenameNodeRequestListenerAsync();
                    var newName = await rename.ExecuteAsync(() =>
                        this.MegaSdk.renameNode(this.OriginalMNode, inputName, rename));

                    if (string.IsNullOrEmpty(newName))
                    {
                        DialogService.SetInputDialogWarningMessage(ResourceService.AppMessages.GetString("AM_RenameNodeFailed"));
                        return false;
                    }

                    OnUiThread(() => this.Name = newName);

                    return true;
                },
                new InputDialogSettings
                {
                    InputText = this.Name,
                    IsTextSelected = true,
                    IgnoreExtensionInSelection = this.OriginalMNode.isFile()
                });
        }

        private void CopyOrMove()
        {
            // In case that copy or move a single node using the flyout menu and the selected 
            // nodes list is empty, we need add the current node to the selected nodes
            if (this.Parent != null && !this.Parent.ItemCollection.IsMultiSelectActive)
            {
                if (!this.Parent.ItemCollection.HasSelectedItems)
                    this.Parent.ItemCollection.SelectedItems.Add(this);
            }

            if (this.Parent != null && this.Parent.CopyOrMoveCommand.CanExecute(null))
                this.Parent.CopyOrMoveCommand.Execute(null);
        }

        /// <summary>
        /// Move the node from its current location to a new folder destination
        /// </summary>
        /// <param name="newParentNode">The new destination folder</param>
        /// <returns>Result of the action</returns>
        public async Task<NodeActionResult> MoveAsync(MNode newParentNode)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return NodeActionResult.NotOnline;

            if (MegaSdk.checkMove(OriginalMNode, newParentNode).getErrorCode() != MErrorType.API_OK)
                return NodeActionResult.Failed;

            var moveNode = new MoveNodeRequestListenerAsync();
            var result = await moveNode.ExecuteAsync(() =>
                MegaSdk.moveNode(OriginalMNode, newParentNode, moveNode));

            return result ? NodeActionResult.Succeeded : NodeActionResult.Failed;
        }

        /// <summary>
        /// Copy the node from its current location to a new folder destination
        /// </summary>
        /// <param name="newParentNode">The new destination folder</param>
        /// <returns>Result of the action</returns>
        public async Task<NodeActionResult> CopyAsync(MNode newParentNode)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return NodeActionResult.NotOnline;

            var copyNode = new CopyNodeRequestListenerAsync();
            var result = await copyNode.ExecuteAsync(() =>
                MegaSdk.copyNode(OriginalMNode, newParentNode, copyNode));

            return result ? NodeActionResult.Succeeded : NodeActionResult.Failed;
        }

        /// <summary>
        /// Import the node from its current location to a new folder destination
        /// </summary>
        /// <param name="newParentNode">The root node of the destination folder</param>
        /// <returns>Result of the action</returns>
        public async Task<NodeActionResult> ImportAsync(MNode newParentNode)
        {
            // User must be online to perform this operation
            if ((this.Parent?.Type != ContainerType.FolderLink) && !await IsUserOnlineAsync())
                return NodeActionResult.NotOnline;

            var copyNode = new CopyNodeRequestListenerAsync();
            var result = await copyNode.ExecuteAsync(() =>
            {
                SdkService.MegaSdk.copyNode(
                    SdkService.MegaSdkFolderLinks.authorizeNode(OriginalMNode),
                    newParentNode, copyNode);
            });

            return result ? NodeActionResult.Succeeded : NodeActionResult.Failed;
        }

        private void Preview()
        {
            this.Parent.FocusedNode = this;

            // Navigate to the preview page
            OnUiThread(() =>
            {
                var parameters = new Dictionary<NavigationParamType, object>();
                parameters.Add(NavigationParamType.Data, this.Parent);

                NavigateService.Instance.Navigate(typeof(PreviewImagePage), true,
                    NavigationObject.Create(this.GetType(), NavigationActionType.Default, parameters));
            });
        }

        private void OpenInformationPanel()
        {
            if(Parent != null)
            {
                if ((this is ImageNodeViewModel) && (this as ImageNodeViewModel != null))
                    (this as ImageNodeViewModel).InViewingRange = true;

                this.Parent.FocusedNode = this;

                if (this.Parent.OpenInformationPanelCommand.CanExecute(null))
                    this.Parent.OpenInformationPanelCommand.Execute(null);
            }
        }

        private async void Remove()
        {
            if (this.Parent != null && this.Parent.ItemCollection.MoreThanOneSelected)
            {
                if (this.Parent.RemoveCommand.CanExecute(null))
                    this.Parent.RemoveCommand.Execute(null);
                return;
            }
            await RemoveAsync();
        }

        public async Task<bool> RemoveAsync(bool isMultiSelect = false)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return false;

            if (this.OriginalMNode == null) return false;

            if (!isMultiSelect && !(this is IncomingSharedFolderNodeViewModel))
            {
                string title, message;
                switch (this.Parent?.Type)
                {
                    case ContainerType.CloudDrive:
                    case ContainerType.CameraUploads:
                    case ContainerType.ContactInShares:
                    case ContainerType.InShares:
                    case ContainerType.OutShares:
                        title = ResourceService.AppMessages.GetString("AM_MoveToRubbishBinQuestion_Title");
                        message = string.Format(ResourceService.AppMessages.GetString("AM_MoveToRubbishBinQuestion"), this.Name);
                        break;

                    case ContainerType.RubbishBin:
                        title = ResourceService.AppMessages.GetString("AM_RemoveItemQuestion_Title");
                        message = string.Format(ResourceService.AppMessages.GetString("AM_RemoveItemQuestion"), this.Name);
                        break;

                    default:
                        return false;
                }

                var dialogResult = await DialogService.ShowOkCancelAsync(title, message);

                if (!dialogResult) return true;
            }

            bool result;
            if(this is IncomingSharedFolderNodeViewModel)
            {
                var leaveShare = new RemoveNodeRequestListenerAsync();
                result = await leaveShare.ExecuteAsync(() =>
                    this.MegaSdk.remove(this.OriginalMNode, leaveShare));
            }
            else
            {
                switch (this.Parent?.Type)
                {
                    case ContainerType.CloudDrive:
                    case ContainerType.CameraUploads:
                    case ContainerType.ContactInShares:
                    case ContainerType.InShares:
                    case ContainerType.OutShares:
                        var moveNode = new MoveNodeRequestListenerAsync();
                        result = await moveNode.ExecuteAsync(() =>
                            this.MegaSdk.moveNode(this.OriginalMNode, this.MegaSdk.getRubbishNode(), moveNode));
                        break;

                    case ContainerType.RubbishBin:
                        var removeNode = new RemoveNodeRequestListenerAsync();
                        result = await removeNode.ExecuteAsync(() =>
                            this.MegaSdk.remove(this.OriginalMNode, removeNode));
                        break;

                    default:
                        return false;
                }

                if (result)
                    this.Parent.ClosePanels();
            }

            return result;
        }

        /// <summary>
        /// Restore node(s) from the rubbish bin
        /// </summary>
        private async void Restore()
        {
            if (this.Parent != null && this.Parent.ItemCollection.MoreThanOneSelected)
            {
                if (this.Parent.RestoreCommand.CanExecute(null))
                    this.Parent.RestoreCommand.Execute(null);
                return;
            }

            var result = await this.MoveAsync(this.RestoreNode);

            if (result != NodeActionResult.Succeeded)
            {
                await DialogService.ShowAlertAsync(
                    ResourceService.AppMessages.GetString("AM_RestoreFromRubbishBinFailed_Title"),
                    string.Format(ResourceService.AppMessages.GetString("AM_RestoreFromRubbishBinFailed"), this.Name));
            }
        }

        public async Task<bool> GetLinkAsync(bool showLinkDialog = true)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return false;

            if (this.OriginalMNode.isExported())
            {
                this.ExportLink = this.OriginalMNode.getPublicLink(true);
            }
            else
            {
                var exportNode = new ExporNodeRequestListenerAsync();
                this.ExportLink = await exportNode.ExecuteAsync(() =>
                {
                    this.MegaSdk.exportNode(this.OriginalMNode, exportNode);
                });

                if (string.IsNullOrEmpty(this.ExportLink))
                {
                    OnUiThread(async () =>
                    {
                        await DialogService.ShowAlertAsync(
                            ResourceService.AppMessages.GetString("AM_GetLinkFailed_Title"),
                            ResourceService.AppMessages.GetString("AM_GetLinkFailed"));
                    });
                    return false;
                };
            }

            this.IsExported = true;

            if (showLinkDialog)
                OnUiThread(() => DialogService.ShowShareLink(this));

            return true;
        }

        public async void SetLinkExpirationTime(long expireTime)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync() || expireTime < 0) return;

            var exportNode = new ExporNodeRequestListenerAsync();
            this.ExportLink = await exportNode.ExecuteAsync(() =>
            {
                this.MegaSdk.exportNodeWithExpireTime(this.OriginalMNode, expireTime, exportNode);
            });

            if (string.IsNullOrEmpty(this.ExportLink))
            {
                OnUiThread(async () =>
                {
                    await DialogService.ShowAlertAsync(
                        ResourceService.AppMessages.GetString("AM_SetLinkExpirationTimeFailed_Title"),
                        ResourceService.AppMessages.GetString("AM_SetLinkExpirationTimeFailed"));
                });
                return;
            };

            this.IsExported = true;
        }

        public async Task<bool> RemoveLinkAsync()
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync() || !this.IsExported) return false;

            var disableExportNode = new DisableExportRequestListenerAsync();
            var result = await disableExportNode.ExecuteAsync(() =>
            {
                this.MegaSdk.disableExport(this.OriginalMNode, disableExportNode);
            });

            if(!result)
            {
                OnUiThread(async () =>
                {
                    await DialogService.ShowAlertAsync(
                        ResourceService.AppMessages.GetString("AM_RemoveLinkFailed_Title"),
                        ResourceService.AppMessages.GetString("AM_RemoveLinkFailed"));
                });
                return false;
            }

            this.IsExported = false;
            this.ExportLink = null;

            return true;
        }

        private void Download()
        {
            if (this.Parent != null && this.Parent.ItemCollection.IsMultiSelectActive)
            {
                if(this.Parent.DownloadCommand.CanExecute(null))
                    this.Parent.DownloadCommand.Execute(null);
                return;
            }
            Download(TransferService.MegaTransfers);
        }

        public async void Download(TransferQueue transferQueue)
        {
            // User must be online to perform this operation
            if ((this.Parent?.Type != ContainerType.FolderLink) && !await IsUserOnlineAsync()) return;
            if (transferQueue == null) return;

            var downloadFolder = await FolderService.SelectFolder();
            if (downloadFolder == null) return;
            if (!await TransferService.CheckExternalDownloadPathAsync(downloadFolder.Path)) return;

            this.Transfer.ExternalDownloadPath = downloadFolder.Path;
            transferQueue.Add(this.Transfer);
            this.Transfer.StartTransfer();

            // If is a file or folder link, navigate to the Cloud Drive page
            if (this.ParentContainerType == ContainerType.FileLink ||
                this.ParentContainerType == ContainerType.FolderLink)
            {
                OnUiThread(() =>
                {
                    NavigateService.Instance.Navigate(typeof(CloudDrivePage), false,
                        NavigationObject.Create(this.GetType()));
                });
            }
        }

        private void Import()
        {
            if (this.Parent == null) return;

            if (this.Parent.ImportCommand.CanExecute(null))
                this.Parent.ImportCommand.Execute(null);
        }

        /// <summary>
        /// Update core data associated with the SDK MNode object
        /// </summary>
        /// <param name="megaNode">Node to update</param>
        /// <param name="externalUpdate">Indicates if is an update external to the app. For example from an `onNodesUpdate`</param>
        public virtual void Update(MNode megaNode, bool externalUpdate = false)
        {
            this.OriginalMNode = megaNode;
            this.Handle = megaNode.getHandle();
            this.RestoreHandle = megaNode.getRestoreHandle();
            this.RestoreNode = this.MegaSdk.getNodeByHandle(megaNode.getRestoreHandle());
            this.Base64Handle = megaNode.getBase64Handle();
            this.Type = megaNode.getType();
            this.Name = megaNode.getName();
            this.Size = this.MegaSdk.getSize(megaNode);
            this.CreationTime = ConvertDateToString(megaNode.getCreationTime()).DateToString();
            this.TypeText = this.GetTypeText();
            this.LinkExpirationTime = megaNode.getExpirationTime();
            this.AccessLevel.AccessType = (MShareType)this.MegaSdk.getAccess(megaNode);

            // Needed to filtering when the change is done inside the app or externally and is received by an `onNodesUpdate`
            if (!externalUpdate || megaNode.hasChanged((int)MNodeChangeType.CHANGE_TYPE_PUBLIC_LINK))
            {
                this.IsExported = megaNode.isExported();
                this.ExportLink = this.IsExported ? megaNode.getPublicLink(true) : null;
            }

            if (this.Type == MNodeType.TYPE_FILE)
                this.ModificationTime = ConvertDateToString(megaNode.getModificationTime()).DateToString();
            else
                this.ModificationTime = this.CreationTime;

            if (ParentContainerType != ContainerType.FileLink && ParentContainerType != ContainerType.FolderLink)
                CheckAndUpdateOffline(megaNode);
        }

        private void CheckAndUpdateOffline(MNode megaNode)
        {
            var offlineNodePath = OfflineService.GetOfflineNodePath(megaNode);

            if ((megaNode.getType() == MNodeType.TYPE_FILE && FileService.FileExists(offlineNodePath)) ||
                (megaNode.getType() == MNodeType.TYPE_FOLDER && FolderService.FolderExists(offlineNodePath)) ||
                TransferService.ExistPendingNodeOfflineTransfer(this))
            {
                if (SavedForOfflineDB.ExistsNodeByLocalPath(offlineNodePath))
                    SavedForOfflineDB.UpdateNode(megaNode);
                else
                    SavedForOfflineDB.InsertNode(megaNode);

                this.IsSavedForOffline = true;
                return;
            }

            if (SavedForOfflineDB.ExistsNodeByLocalPath(offlineNodePath))
                SavedForOfflineDB.DeleteNodeByLocalPath(offlineNodePath);

            this.IsSavedForOffline = false;
        }

        public async void SaveForOffline()
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return;

            var offlineParentNodePath = OfflineService.GetOfflineParentNodePath(this.OriginalMNode);
            
            if (!FolderService.FolderExists(offlineParentNodePath))
                FolderService.CreateFolder(offlineParentNodePath);

            var existingNode = SavedForOfflineDB.SelectNodeByFingerprint(MegaSdk.getNodeFingerprint(this.OriginalMNode));
            if (existingNode != null)
            {
                bool result = this.IsFolder ?
                    await FolderService.CopyFolderAsync(existingNode.LocalPath, offlineParentNodePath) :
                    await FileService.CopyFileAsync(existingNode.LocalPath, offlineParentNodePath);

                if (result) SavedForOfflineDB.InsertNode(this.OriginalMNode);
            }
            else
            {
                TransferService.MegaTransfers.Add(this.OfflineTransfer);
                this.OfflineTransfer.StartTransfer(true);
            }

            this.IsSavedForOffline = true;

            OfflineService.CheckOfflineNodePath(this.OriginalMNode);
        }

        /// <summary>
        /// Remove the node from the offline section.
        /// </summary>
        /// <returns>TRUE if the node was successfully removed or FALSE in other case.</returns>
        public bool RemoveFromOffline()
        {
            var nodePath = OfflineService.GetOfflineNodePath(this.OriginalMNode);
            var parentNodePath = OfflineService.GetOfflineParentNodePath(this.OriginalMNode);

            // Search if the file has a pending transfer for offline and cancel it on this case                
            TransferService.CancelPendingNodeOfflineTransfers(this);

            bool result = true;
            if (this.IsFolder)
            {
                result &= OfflineService.RemoveFolderFromOfflineDB(nodePath);
                result &= FolderService.DeleteFolder(nodePath, true);
            }
            else
            {
                result &= SavedForOfflineDB.DeleteNodeByLocalPath(nodePath);
                result &= FileService.DeleteFile(nodePath);
            }

            result &= OfflineService.CleanOfflineFolderNodePath(parentNodePath);
            this.IsSavedForOffline = !result;

            return result;
        }

        private string GetTypeText()
        {
            if (this.IsFolder)
                return this.FolderLabelText;

            string extension = Path.GetExtension(this.Name);
            if (string.IsNullOrEmpty(extension))
                return this.UnknownLabelText;

            extension = extension.TrimStart('.');
            char[] ext = extension.ToCharArray();
            ext[0] = char.ToUpper(ext[0]);

            switch (FileService.GetFileType(this.Name))
            {
                case FileType.TYPE_FILE:
                    return new string(ext) + "/" + this.FileLabelText;

                case FileType.TYPE_IMAGE:
                    return new string(ext) + "/" + this.ImageLabelText;

                case FileType.TYPE_AUDIO:
                    return new string(ext) + "/" + this.AudioLabelText;

                case FileType.TYPE_VIDEO:
                    return new string(ext) + "/" + this.VideoLabelText;

                case FileType.TYPE_UNKNOWN:
                default:
                    return new string(ext) + "/" + this.UnknownLabelText;
            }
        }

        public override void SetThumbnailImage()
        {
            if (this.Type == MNodeType.TYPE_FOLDER) return;

            if (this.ThumbnailImageUri != null) return;

            if (this.IsImage || this.OriginalMNode.hasThumbnail())
            {
                GetThumbnail();
            }
        }

        /// <summary>
        /// Unique identifier of the node.
        /// </summary>
        public ulong Handle { get; set; }

        /// <summary>
        /// Handle of the previous parent of this node.
        /// </summary>
        public ulong RestoreHandle { get; set; }

        /// <summary>
        /// Previous parent of this node.
        /// </summary>
        public MNode RestoreNode { get; set; }

        private FolderViewModel _parent;
        public FolderViewModel Parent
        {
            get { return _parent; }
            set
            {
                if (_parent != null)
                    _parent.PropertyChanged -= ParentOnPropertyChanged;

                SetField(ref _parent, value);

                if (_parent != null)
                    _parent.PropertyChanged += ParentOnPropertyChanged;
            }
        }

        public MNodeType Type { get; private set; }

        private string _typeText;
        public string TypeText
        {
            get { return _typeText; }
            set { SetField(ref _typeText, value); }
        }

        public ContainerType ParentContainerType { get; private set; }

        private bool _isSavedForOffline;
        public bool IsSavedForOffline
        {
            get { return _isSavedForOffline; }
            set
            {
                SetField(ref _isSavedForOffline, value);
                OnPropertyChanged(nameof(this.IsExportedOrSavedForOffline));
            }
        }

        private bool _isExported;
        public bool IsExported
        {
            get { return _isExported; }
            set
            {
                SetField(ref _isExported, value);
                OnPropertyChanged(nameof(this.NodeBinding), nameof(this.GetLinkText),
                    nameof(this.IsExportedOrSavedForOffline));
            }
        }

        public bool IsExportedOrSavedForOffline => this.IsExported || this.IsSavedForOffline;

        public TransferObjectModel Transfer { get; set; }

        public TransferObjectModel OfflineTransfer { get; set; }

        public MNode OriginalMNode { get; private set; }

        private AccessLevelViewModel _accessLevel;
        /// <summary>
        /// Access level to the node
        /// </summary>
        public AccessLevelViewModel AccessLevel
        {
            get { return _accessLevel; }
            set
            {
                SetField(ref _accessLevel, value);
                OnPropertyChanged(nameof(this.HasReadPermissions), 
                    nameof(this.HasReadWritePermissions),
                    nameof(this.HasFullAccessPermissions));
            }
        }

        /// <summary>
        /// Specifies if the node has read permissions
        /// </summary>
        public bool HasReadPermissions => this.AccessLevel == null ? false :
            (int)this.AccessLevel?.AccessType >= (int)MShareType.ACCESS_READ;

        /// <summary>
        /// Specifies if the node has read & write permissions
        /// </summary>
        public bool HasReadWritePermissions => this.AccessLevel == null ? false :
            (int)this.AccessLevel?.AccessType >= (int)MShareType.ACCESS_READWRITE;

        /// <summary>
        /// Specifies if the node has full access permissions
        /// </summary>
        public bool HasFullAccessPermissions => this.AccessLevel == null ? false :
            (int)this.AccessLevel?.AccessType >= (int)MShareType.ACCESS_FULL;

        #endregion

        #region Properties

        public AccountDetailsViewModel AccountDetails => AccountService.AccountDetails;

        /// <summary>
        /// Indicates if the node can be restored to its previous location
        /// </summary>
        public bool CanRestore => this.MegaSdk.isInRubbish(this.OriginalMNode) &&
            this.RestoreNode != null && this.MegaSdk.isInCloud(this.RestoreNode);

        private string _exportLink;
        public string ExportLink
        {
            get { return _exportLink; }
            set { SetField(ref _exportLink, value); }
        }

        public bool LinkWithExpirationTime => (LinkExpirationTime > 0) ? true : false;

        private long _linkExpirationTime;
        public long LinkExpirationTime
        {
            get { return _linkExpirationTime; }
            set
            {
                SetField(ref _linkExpirationTime, value);
                OnPropertyChanged("LinkWithExpirationTime");
                OnPropertyChanged("LinkExpirationDate");
            }
        }

        public DateTimeOffset? LinkExpirationDate
        {
            get
            {
                DateTime? _linkExpirationDate;
                if (LinkExpirationTime > 0)
                    _linkExpirationDate = OriginalDateTime.AddSeconds(LinkExpirationTime);
                else
                    _linkExpirationDate = null;

                return _linkExpirationDate;
            }
        }

        public string LocalDownloadPath => Path.Combine(ApplicationData.Current.LocalFolder.Path,
            ResourceService.AppResources.GetString("AR_DownloadsDirectory"), this.Name);

        public string OfflinePath => OfflineService.GetOfflineNodePath(this.OriginalMNode);

        #endregion

        #region UiResources

        public string AddedLabelText => ResourceService.UiResources.GetString("UI_Added");
        public string AudioLabelText => ResourceService.UiResources.GetString("UI_Audio");
        public string DetailsText => ResourceService.UiResources.GetString("UI_Details");
        public string DownloadText => ResourceService.UiResources.GetString("UI_Download");
        public string EnableOfflineViewText => ResourceService.UiResources.GetString("UI_EnableOfflineVIew");
        public string EnableLinkText => ResourceService.UiResources.GetString("UI_EnableLink");
        public string CancelText => ResourceService.UiResources.GetString("UI_Cancel");
        public string ClosePanelText => ResourceService.UiResources.GetString("UI_ClosePanel");
        public string CopyOrMoveText => CopyText + "/" + MoveText;
        public string CopyText => ResourceService.UiResources.GetString("UI_Copy");
        public string FileLabelText => ResourceService.UiResources.GetString("UI_File");
        public string FilesLabelText => ResourceService.UiResources.GetString("UI_Files");
        public string FolderLabelText => ResourceService.UiResources.GetString("UI_Folder");
        public string FoldersLabelText => ResourceService.UiResources.GetString("UI_Folders");
        public string GetLinkText => this.IsExported ? ResourceService.UiResources.GetString("UI_ManageLink") : 
            ResourceService.UiResources.GetString("UI_GetLink");
        public string ImageLabelText => ResourceService.UiResources.GetString("UI_Image");
        public string ImportText => ResourceService.UiResources.GetString("UI_Import");
        public string InformationText => ResourceService.UiResources.GetString("UI_Information");
        public string LinkText => ResourceService.UiResources.GetString("UI_Link");
        public string ModifiedLabelText => ResourceService.UiResources.GetString("UI_Modified");
        public string MoveText => ResourceService.UiResources.GetString("UI_Move");
        public string PreviewText => ResourceService.UiResources.GetString("UI_Preview");
        public string RemoveText => ResourceService.UiResources.GetString("UI_Remove");
        public string RemoveLinkText => ResourceService.UiResources.GetString("UI_RemoveLink");
        public string RenameText => ResourceService.UiResources.GetString("UI_Rename");
        public string RestoreText => ResourceService.UiResources.GetString("UI_Restore");
        public string ShareText => ResourceService.UiResources.GetString("UI_Share");
        public string SizeLabelText => ResourceService.UiResources.GetString("UI_Size");
        public string TypeLabelText => ResourceService.UiResources.GetString("UI_Type");
        public string UnknownLabelText => ResourceService.UiResources.GetString("UI_Unknown");
        public string VideoLabelText => ResourceService.UiResources.GetString("UI_Video");

        public string SetLinkExpirationDateText => string.Format("{0} {1}",
            ResourceService.UiResources.GetString("UI_SetExpirationDate"),
            ResourceService.UiResources.GetString("UI_ProOnly"));

        #endregion

        #region VisualResources

        public string CopyOrMovePathData => ResourceService.VisualResources.GetString("VR_CopyOrMovePathData");
        public string DownloadPathData => ResourceService.VisualResources.GetString("VR_DownloadPathData");
        public string LinkPathData => ResourceService.VisualResources.GetString("VR_LinkPathData");
        public string InformationPathData => ResourceService.VisualResources.GetString("VR_InformationPathData");
        public string PreviewImagePathData => ResourceService.VisualResources.GetString("VR_PreviewImagePathData");
        public string RenamePathData => ResourceService.VisualResources.GetString("VR_RenamePathData");
        public string RubbishBinPathData => ResourceService.VisualResources.GetString("VR_RubbishBinPathData");        

        #endregion
    }
}
