using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevExpress.Mvvm;
using System.Windows.Data;
using DevExpress.Mvvm.CodeGenerators;
using log4net;
using SAM.Core.Extensions;
using SAM.Core.Messages;
using SAM.Core;
using SAM.Managers;
using SAM.Services;
using SAM.Extensions;

namespace SAM.ViewModels;

[GenerateViewModel(ImplementISupportServices = true)]
public partial class LibraryViewModel
{
    protected readonly ILog log = LogManager.GetLogger(nameof(HomeViewModel));
    protected IGroupViewService groupViewService => GetService<IGroupViewService>();

    private readonly ObservableHandler<ILibrarySettings> _settingsHandler;

    protected CollectionViewSource _itemsViewSource;
    protected bool _loading = true;
    private ILibrarySettings _settings;
    
    [GenerateProperty] protected string _filterText;
    [GenerateProperty] protected bool _filterNormal;
    [GenerateProperty] protected bool _filterDemos;
    [GenerateProperty] protected bool _filterMods;
    [GenerateProperty] protected bool _filterJunk;
    [GenerateProperty] protected string _filterTool;
    [GenerateProperty] protected ICollectionView _itemsView;
    [GenerateProperty] protected List<string> _suggestions;
    [GenerateProperty] protected SteamApp _selectedItem;
    [GenerateProperty] protected SteamLibrary _library;
    
    // Pagination
    [GenerateProperty] protected int _currentPage = 1;
    [GenerateProperty] protected int _totalPages = 1;
    [GenerateProperty] protected System.Collections.IEnumerable _pagedView;
    protected const int PageSize = 16;
    
    // Skeleton Loading
    public List<int> SkeletonItems { get; } = Enumerable.Range(0, 16).ToList();

    protected LibraryViewModel(ILibrarySettings settings)
    {
        _settings = settings;

        _settingsHandler = new ObservableHandler<ILibrarySettings>(settings)
            .Add(s => s.EnableGrouping, OnEnableGroupingChanged)
            .Add(s => s.ShowHidden, OnShowHiddenChanged)
            .Add(s => s.ShowFavoritesOnly, OnFilterFavoritesChanged);

        Messenger.Default.Register<ActionMessage>(this, OnActionMessage);
    }

    [GenerateCommand]
    public void ExpandAll()
    {
        groupViewService.ExpandAll();
    }

    [GenerateCommand]
    public void CollapseAll()
    {
        groupViewService.CollapseAll();
    }

    [GenerateCommand]
    public void ToggleShowHidden()
    {
        if (_settings == null) return;

        _settings.ShowHidden = !_settings.ShowHidden;
    }
    
    [GenerateCommand]
    public void ToggleEnableGrouping()
    {
        if (_settings == null) return;

        _settings.EnableGrouping = !_settings.EnableGrouping;
    }

    [GenerateCommand]
    public void UnHideAll()
    {
        // TODO: consider adding confirmation before clearing the user's hidden apps
        var hidden = _library!.Items.Where(a => a.IsHidden).ToList();

        hidden.ForEach(a => a.ToggleVisibility());
    }

    [GenerateCommand]
    public void ManageApp()
    {
        if (SelectedItem == null) return;

        SAMHelper.OpenManager(SelectedItem.Id);
    }

    [GenerateCommand]
    public async Task Refresh(bool force = false)
    {
        if (_settings == null) return;

        _loading = true;

        if (force)
        {
            await SteamLibraryManager.DefaultLibrary.RefreshAsync().ConfigureAwait(false);
        }

        Library ??= SteamLibraryManager.DefaultLibrary;
        if (Library == null)
        {
            _loading = false;
            return;
        }

        // ReSharper disable once RedundantCheckBeforeAssignment
        _itemsViewSource ??= new ()
        {
            Source = Library.Items
        };

        using (_itemsViewSource.DeferRefresh())
        {
            _itemsViewSource.GroupDescriptions.Clear();
            _itemsViewSource.LiveGroupingProperties.Clear();

            if (_settings.EnableGrouping)
            {
                _itemsViewSource.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SteamApp.Group)));
                _itemsViewSource.LiveGroupingProperties.Add(nameof(SteamApp.Group));
            }

            _itemsViewSource.IsLiveGroupingRequested = _settings.EnableGrouping;

            _itemsViewSource.SortDescriptions.Clear();
            _itemsViewSource.LiveSortingProperties.Clear();

            if (_settings.EnableGrouping)
            {
                _itemsViewSource.SortDescriptions.Add(new (nameof(SteamApp.GroupSortIndex), ListSortDirection.Ascending));
            }

            _itemsViewSource.SortDescriptions.Add(new (nameof(SteamApp.Name), ListSortDirection.Ascending));
            
            _itemsViewSource.IsLiveSortingRequested = true;

            _itemsViewSource.LiveFilteringProperties.Clear();
            _itemsViewSource.LiveFilteringProperties.Add(nameof(SteamApp.IsHidden));
            _itemsViewSource.LiveFilteringProperties.Add(nameof(SteamApp.IsFavorite));
            _itemsViewSource.LiveFilteringProperties.Add(nameof(SteamApp.GameInfoType));
            
            _itemsViewSource.Filter += ItemsViewSourceOnFilter;
            
            _itemsViewSource.IsLiveFilteringRequested = true;
        }

        ItemsView ??= _itemsViewSource.View;
        ItemsView!.Refresh();

        // suggestions are sorted by favorites first, then normal (non-favorite & non-hidden) apps,
        // and then any hidden apps
        Suggestions = Library.Items
            .OrderBy(a => a.GroupSortIndex)
            .ThenBy(a => a.Name)
            .Select(a => a.Name).ToList();

        UpdatePagedView();

        _loading = false;
    }

    protected void OnFilterTextChanged()
    {
        if (_loading) return;

        ItemsView?.Refresh();
        CurrentPage = 1; 
        UpdatePagedView();
    }

    protected void OnShowHiddenChanged()
    {
        if (_loading) return;
        
        ItemsView?.Refresh();
    }

    protected void OnFilterFavoritesChanged()
    {
        if (_loading) return;
        
        ItemsView?.Refresh();
    }

    protected void OnEnableGroupingChanged()
    {
        if (_loading) return;

        Refresh().SafeFireAndForget(e => log.Error("Failed to refresh library", e));
    }

    protected virtual void OnActionMessage(ActionMessage message)
    {
        if (_loading) return;

        // on library refresh completed
        if (message.EntityType == EntityType.Library && message.ActionType == ActionType.Refreshed)
        {
            if (ItemsView == null)
            {
                Refresh().SafeFireAndForget(e => log.Error("Failed to re-initialize library view", e));
            }
            else
            {
                ItemsView.Refresh();
                UpdatePagedView();
            }
        }
    }

    protected virtual void ItemsViewSourceOnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item == null) return;
        if (e.Item is not SteamApp app) throw new ArgumentException(nameof(e.Item));
        if (_settings == null) return;

        var hasNameFilter = !string.IsNullOrWhiteSpace(FilterText);
        var isNameMatch = !hasNameFilter || app.Name.ContainsIgnoreCase(FilterText) || app.Id.ToString().Contains(FilterText);
        var isJunkFiltered = !FilterJunk || app.IsJunk;
        var isHiddenFiltered = _settings.ShowHidden || !app.IsHidden;
        var isNonFavoriteFiltered = !_settings.ShowFavoritesOnly || app.IsFavorite;

        e.Accepted = isNameMatch && isJunkFiltered && isHiddenFiltered && isNonFavoriteFiltered;
    }

    [GenerateCommand]
    public void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            UpdatePagedView();
        }
    }

    [GenerateCommand]
    public void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            UpdatePagedView();
        }
    }

    protected void UpdatePagedView()
    {
        if (ItemsView == null) return;

        // Ensure we are working with the filtered list
        var filteredItems = ItemsView.Cast<SteamApp>().ToList();
        
        int totalItems = filteredItems.Count;
        TotalPages = (int)Math.Ceiling((double)totalItems / PageSize);
        if (TotalPages < 1) TotalPages = 1;

        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        if (CurrentPage < 1) CurrentPage = 1;

        PagedView = filteredItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }
}
