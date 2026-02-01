using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using DevExpress.Mvvm;
using DevExpress.Mvvm.CodeGenerators;
using log4net;
using SAM.API;
using SAM.Core;
using SAM.Core.Interfaces;
using SAM.Core.Messages;
using SAM.Core.Storage;
using SAM.Extensions;
using SAM.Managers;

namespace SAM;

[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Multiple private readonly fields.")]
[GenerateViewModel]
public partial class SteamLibrary
{
    private const int CACHE_INTERVAL = 25;
    private const int PROGRESS_INTERVAL = 10;

    private readonly ILog log = LogManager.GetLogger(nameof(SteamLibrary));

    private CancellationTokenSource _cts;

    private static readonly object _lock = new ();
    private IDictionary<uint, ISupportedApp> _supportedGames;
    private ConcurrentQueue<ISupportedApp> _refreshQueue;
    private ConcurrentDictionary<uint, ISupportedApp> _addedGames;
    private HashSet<uint> _blacklist = new();

    private void LoadBlacklist()
    {
        try
        {
            string blacklistPath = "blacklist.json";
            if (System.IO.File.Exists(blacklistPath))
            {
                var json = System.IO.File.ReadAllText(blacklistPath);
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<uint>>(json);
                if (ids != null)
                {
                    _blacklist = new HashSet<uint>(ids);
                    log.Info($"Loaded {_blacklist.Count} blacklisted apps.");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Failed to load blacklist.", ex);
        }
    }

    [GenerateProperty] private int _queueCount;
    [GenerateProperty] private int _completedCount;
    [GenerateProperty] private int _supportedGamesCount;
    [GenerateProperty] private int _totalCount;
    [GenerateProperty] private int _gamesCount;
    [GenerateProperty] private int _junkCount;
    [GenerateProperty] private int _toolCount;
    [GenerateProperty] private int _modCount;
    [GenerateProperty] private int _demoCount;
    [GenerateProperty] private int _hiddenCount;
    [GenerateProperty] private int _favoriteCount;
    [GenerateProperty] private decimal _percentComplete;
    [GenerateProperty] private bool _isLoading;

    public ObservableCollection<SteamApp> Items { get; }

    public SteamLibrary()
    {
         // Don't access Apps here to avoid blocking
        Items = [];

        BindingOperations.EnableCollectionSynchronization(Items, _lock);

        Messenger.Default.Register<RequestMessage>(this, OnRequestMessage);
    }

    private void OnRequestMessage(RequestMessage request)
    {
        try
        {
            if (request == null) return;
            if (request.EntityType != EntityType.Library) return;

            // if we received a refresh request refresh the counts
            if (request.RequestType == RequestType.Refresh)
            {
                RefreshCounts();
            }
        }
        catch (Exception e)
        {
            var message = $"An error occurred handling the {nameof(RequestMessage)} for {request}. {e.Message}";

            throw new SAMException(message, e);
        }
    }

    public async Task RefreshAsync(bool loadCache = false)
    {
        // Cancel any existing refresh
        CancelRefresh();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            IsLoading = true;

            LoadBlacklist();

            // Ensure supported games are loaded
            _supportedGames = await SteamLibraryManager.Default.GetSupportedGamesAsync().ConfigureAwait(false);
            SupportedGamesCount = _supportedGames.Count;

            _refreshQueue = new ConcurrentQueue<ISupportedApp>(_supportedGames.Values);
            _addedGames = new ConcurrentDictionary<uint, ISupportedApp>();

            // Clear items on UI thread or via sync context, but Items is thread-safe synchronized
            lock (_lock)
            {
                Items.Clear();
            }

            if (loadCache)
            {
                LoadLibrary();
                //LoadRefreshProgress();
            }

            await Task.Run(async () => await ProcessRefreshQueue(token), token);
        }
        catch (OperationCanceledException)
        {
            log.Info("Refresh cancelled.");
        }
        catch (Exception e)
        {
             var message = $"An error occurred refreshing the Steam library. {e.Message}";
             log.Error(message, e);
        }
        finally
        {
            RefreshCounts();
            Messenger.Default.SendAction(ActionMessage.LibraryRefreshed);
            IsLoading = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
    
    // Kept for compatibility, but acts as fire-and-forget or async void
    public void Refresh(bool loadCache = false)
    {
        RefreshAsync(loadCache).SafeFireAndForget(e => log.Error("Failed to refresh library", e));
    }

    public void CancelRefresh()
    {
        _cts?.Cancel();
    }

    private async Task ProcessRefreshQueue(CancellationToken token)
    {
        var checkedCount = 0;
        var addedCount = 0;

        while (_refreshQueue.TryDequeue(out var game))
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var added = AddGame(game);
            if (added) addedCount++;

            checkedCount++;

            // Update UI less frequently to avoid freezing the Dispatcher
            // Only update if we added games or hit a larger interval
            if (checkedCount % PROGRESS_INTERVAL == 0)
            {
               await Application.Current.Dispatcher.InvokeAsync(RefreshCounts, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        
        // Final update after queue is drained
        await Application.Current.Dispatcher.InvokeAsync(RefreshCounts, System.Windows.Threading.DispatcherPriority.Background);
        
        // Use Parallel.ForEachAsync to limit concurrency and avoid thread pool saturation
        var itemsToLoad = Items.ToList();
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = 20, // Reasonable limit for concurrent I/O or DB ops
            CancellationToken = token 
        };

        await Parallel.ForEachAsync(itemsToLoad, parallelOptions, async (item, ct) =>
        {
            try 
            {
                await item.Load().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                 log.Warn($"Failed to load details for {item.Name} ({item.Id})", ex);
            }
        });
    }

    private bool AddGame(ISupportedApp app)
    {
        try
        {
            var type = app.GameInfoType;

            // TODO: allow users to configure filters for the types
            if (type is not GameInfoType.Normal and not GameInfoType.Mod) return false;
            if (_addedGames.ContainsKey(app.Id)) return false;

            if (_blacklist.Contains(app.Id))
            {
                log.Info($"Skipping blacklisted app '{app.Id}'");
                return false;
            }

            if (!SteamClientManager.Default.OwnsGame(app.Id)) return false;

            var steamGame = new SteamApp(app.Id, type);

            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    Items.Add(steamGame);
                }
            });

            _addedGames[app.Id] = app;

            CacheManager.StorageManager.CreateDirectory($@"apps\{app.Id}");

            return true;
        }
        catch (Exception e)
        {
            var message = $"Error attempting to add app '{app?.Id}'. {e.Message}";
            log.Error(message, e);

            // Don't throw here to avoid stopping the whole loop
            return false;
        }
    }

    private void LoadRefreshProgress()
    {
        try
        {
            if (!CacheManager.TryGetObject<List<SupportedApp>>(CacheKeys.CheckedAppList, out var refreshQueue)) return;

            _refreshQueue = new ConcurrentQueue<ISupportedApp>(refreshQueue);
        }
        catch (Exception e)
        {
            var message = $"An error occurred attempting to load refresh progress. {e.Message}";
            log.Error(message, e);
        }
    }

    private void LoadLibrary()
    {
        try
        {
            if (!CacheManager.TryGetObject<List<SupportedApp>>(CacheKeys.UserLibrary, out var ownedApps)) return;

            foreach (var app in ownedApps)
            {
                if (!SteamLibraryManager.Default.TryGetApp(app.Id, out var appInfo))
                {
                    log.Warn($"App with ID '{app.Id}' was in the local cache but was not found in supported app list.");
                }

                var added = AddGame(appInfo);

                if (!added)
                {
                    log.Warn($"Failed to add app '{appInfo.Id}' from saved library.");
                }
                
                // Note: Removing from _supportedGames map isn't really needed for logic and modifying shared dictionary is risky
                // if we are sharing the instance. Since we copied values to queue, it's fine.
            }

            RefreshCounts();
        }
        catch (Exception e)
        {
            var message = $"An error occurred attempting to load library cache. {e.Message}";
            log.Error(message, e);
        }
    }

    private void RefreshCounts()
    {
        if (_refreshQueue == null) return;
        
        QueueCount = _refreshQueue.Count;
        CompletedCount = SupportedGamesCount - QueueCount;
        
        if (SupportedGamesCount > 0)
             PercentComplete = (decimal) CompletedCount / SupportedGamesCount;

        TotalCount = Items.Count;
        GamesCount = Items.Count(g => g.GameInfoType == GameInfoType.Normal);
        ModCount = Items.Count(g => g.GameInfoType == GameInfoType.Mod);
        ToolCount = Items.Count(g => g.GameInfoType == GameInfoType.Tool);
        JunkCount = Items.Count(g => g.GameInfoType == GameInfoType.Junk);
        DemoCount = Items.Count(g => g.GameInfoType == GameInfoType.Demo);
        HiddenCount = Items.Count(g => g.IsHidden);
        FavoriteCount = Items.Count(g => g.IsFavorite);
    }
}
