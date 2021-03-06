﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using mega;
using MegaApp.Enums;
using MegaApp.Interfaces;
using MegaApp.Services;

namespace MegaApp.ViewModels.SharedFolders
{
    public class IncomingSharesViewModel : SharedFoldersListViewModel
    {
        public IncomingSharesViewModel(bool isForSelectFolder = false) : 
            base(ContainerType.InShares, isForSelectFolder)
        {

        }

        #region Methods

        public void Initialize(MUser contact = null)
        {
            this.GetIncomingSharedItems(contact);

            this.ItemCollection.ItemCollectionChanged += OnItemCollectionChanged;
            this.ItemCollection.SelectedItemsCollectionChanged += OnSelectedItemsCollectionChanged;

            this.ItemCollection.OrderInverted += OnOrderInverted;

            if (App.GlobalListener == null) return;
            App.GlobalListener.InSharedFolderAdded += this.OnSharedFolderAdded;
            App.GlobalListener.InSharedFolderRemoved += this.OnSharedFolderRemoved;
        }        

        public void Deinitialize()
        {
            this.ItemCollection.ItemCollectionChanged -= OnItemCollectionChanged;
            this.ItemCollection.SelectedItemsCollectionChanged -= OnSelectedItemsCollectionChanged;

            this.ItemCollection.OrderInverted -= OnOrderInverted;

            if (App.GlobalListener == null) return;
            App.GlobalListener.InSharedFolderAdded -= this.OnSharedFolderAdded;
            App.GlobalListener.InSharedFolderRemoved -= this.OnSharedFolderRemoved;
        }

        protected async void GetIncomingSharedItems(MUser contact = null)
        {
            // User must be online to perform this operation
            if (!await IsUserOnlineAsync()) return;

            // First cancel any other loading task that is busy
            CancelLoad();

            // Create the option to cancel
            CreateLoadCancelOption();

            // Process is started so we can set the empty content template to loading already
            SetEmptyContent(true);

            await OnUiThreadAsync(() => this.ItemCollection.Clear());
            MNodeList inSharedItems = (contact != null) ?
                SdkService.MegaSdk.getInShares(contact) : SdkService.MegaSdk.getInShares();

            await Task.Factory.StartNew(() =>
            {
                try
                {
                    var inSharedItemsListSize = inSharedItems.size();
                    for (int i = 0; i < inSharedItemsListSize; i++)
                    {
                        // If the task has been cancelled, stop processing
                        if (LoadingCancelToken.IsCancellationRequested)
                            LoadingCancelToken.ThrowIfCancellationRequested();

                        // To avoid null values
                        if (inSharedItems.get(i) == null) continue;

                        var node = NodeService.CreateNewSharedFolder(SdkService.MegaSdk, App.AppInformation,
                            inSharedItems.get(i), this);

                        // If node creation failed for some reason, continue with the rest and leave this one
                        if (node == null) continue;

                        OnUiThread(() => this.ItemCollection.Items.Add((IMegaSharedFolderNode)node));
                    }
                }
                catch (OperationCanceledException)
                {
                    // Do nothing. Just exit this background process because a cancellation exception has been thrown
                }

            }, LoadingCancelToken, TaskCreationOptions.PreferFairness, TaskScheduler.Current);

            this.SortBy(this.CurrentOrder, this.ItemCollection.CurrentOrderDirection);
            OnItemCollectionChanged(this, EventArgs.Empty);
            SetEmptyContent(false);
        }

        public void SortBy(IncomingSharesSortOrderType sortOption, SortOrderDirection sortDirection)
        {
            OnUiThread(() => this.ItemCollection.DisableCollectionChangedDetection());

            switch (sortOption)
            {
                case IncomingSharesSortOrderType.ORDER_NAME:
                    OnUiThread(() =>
                    {
                        this.ItemCollection.Items = new ObservableCollection<IBaseNode>(this.ItemCollection.IsCurrentOrderAscending ? 
                            this.ItemCollection.Items.OrderBy(item => item.Name) : this.ItemCollection.Items.OrderByDescending(item => item.Name));
                    });
                    break;

                case IncomingSharesSortOrderType.ORDER_MODIFICATION:
                    OnUiThread(() =>
                    {
                        this.ItemCollection.Items = new ObservableCollection<IBaseNode>(this.ItemCollection.IsCurrentOrderAscending ? 
                            this.ItemCollection.Items.OrderBy(item => item.ModificationTime) : this.ItemCollection.Items.OrderByDescending(item => item.ModificationTime));
                    });
                    break;

                case IncomingSharesSortOrderType.ORDER_ACCESS:
                    OnUiThread(() =>
                    {
                        this.ItemCollection.Items = new ObservableCollection<IBaseNode>(this.ItemCollection.IsCurrentOrderAscending ?
                            this.ItemCollection.Items.OrderBy(item => (item as IMegaIncomingSharedFolderNode).AccessLevel.AccessType) : 
                            this.ItemCollection.Items.OrderByDescending(item => (item as IMegaIncomingSharedFolderNode).AccessLevel.AccessType));
                    });
                    break;

                case IncomingSharesSortOrderType.ORDER_OWNER:
                    OnUiThread(() =>
                    {
                        this.ItemCollection.Items = new ObservableCollection<IBaseNode>(this.ItemCollection.IsCurrentOrderAscending ?
                            this.ItemCollection.Items.OrderBy(item => (item as IMegaIncomingSharedFolderNode).Owner) : 
                            this.ItemCollection.Items.OrderByDescending(item => (item as IMegaIncomingSharedFolderNode).Owner));
                    });
                    break;
            }

            OnUiThread(() => this.ItemCollection.EnableCollectionChangedDetection());
        }

        protected void OnItemCollectionChanged(object sender, EventArgs args)
        {
            OnUiThread(() =>
            {
                OnPropertyChanged(nameof(this.ItemCollection.Items),
                    nameof(this.NumberOfSharedItems),
                    nameof(this.NumberOfSharedItemsText),
                    nameof(this.OrderTypeAndNumberOfItems),
                    nameof(this.OrderTypeAndNumberOfSelectedItems));
            });
        }

        protected void OnSelectedItemsCollectionChanged(object sender, EventArgs args)
        {
            OnUiThread(() =>
            {
                OnPropertyChanged(nameof(this.OrderTypeAndNumberOfItems),
                    nameof(this.OrderTypeAndNumberOfSelectedItems));
            });
        }

        private void OnOrderInverted(object sender, EventArgs args) =>
            SortBy(this.CurrentOrder, this.ItemCollection.CurrentOrderDirection);

        public override void BrowseToHome()
        {
            this.BreadCrumb.Items.Clear();
            this.GetIncomingSharedItems();
        }

        #endregion

        #region Properties        

        public override string OrderTypeAndNumberOfItems
        {
            get
            {
                switch (this.CurrentOrder)
                {
                    case IncomingSharesSortOrderType.ORDER_NAME:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByName"),
                            this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_MODIFICATION:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByDateModified"),
                            this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_ACCESS:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByAccessLevel"),
                            this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_OWNER:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByOwner"),
                            this.ItemCollection.Items.Count);
                    default:
                        return string.Empty;
                }
            }
        }

        public override string OrderTypeAndNumberOfSelectedItems
        {
            get
            {
                switch (this.CurrentOrder)
                {
                    case IncomingSharesSortOrderType.ORDER_NAME:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByNameMultiSelect"),
                            this.ItemCollection.SelectedItems.Count, this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_MODIFICATION:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByDateModifiedMultiSelect"),
                            this.ItemCollection.SelectedItems.Count, this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_ACCESS:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByAccessLevelMultiSelect"),
                            this.ItemCollection.SelectedItems.Count, this.ItemCollection.Items.Count);
                    case IncomingSharesSortOrderType.ORDER_OWNER:
                        return string.Format(ResourceService.UiResources.GetString("UI_ListSortedByOwnerMultiSelect"),
                            this.ItemCollection.SelectedItems.Count, this.ItemCollection.Items.Count);
                    default:
                        return string.Empty;
                }
            }
        }

        private IncomingSharesSortOrderType _currentOrder;
        public IncomingSharesSortOrderType CurrentOrder
        {
            get { return _currentOrder; }
            set
            {
                SetField(ref _currentOrder, value);

                OnPropertyChanged(nameof(this.OrderTypeAndNumberOfItems),
                    nameof(this.OrderTypeAndNumberOfSelectedItems));
            }
        }

        public ContainerType ContainerType => ContainerType.InShares;

        #endregion
    }
}
