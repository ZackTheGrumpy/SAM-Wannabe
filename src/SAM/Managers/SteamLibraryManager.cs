using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using log4net;
using SAM.Core;
using SAM.Core.Interfaces;
using SAM.Core.Storage;

namespace SAM.Managers;

public class SteamLibraryManager
{
    // TODO: Make this a configurable setting
    private const string SAM_GAME_LIST_URL = @"http://gib.me/sam/games.xml";


    private readonly ILog log = LogManager.GetLogger(nameof(SteamLibraryManager));

    private ConcurrentDictionary<uint, ISupportedApp> _gameList;
    private readonly HttpClient client = new ();
    private static readonly object syncLock = new ();
    private static SteamLibraryManager _instance;

    public IDictionary<uint, ISupportedApp> Apps
    {
        get
        {
            if (_gameList != null) return _gameList;

            // Warn if accessed before initialization
            log.Warn("Apps property accessed before initialization. This may cause synchronous blocking or return empty.");
             _gameList = new ConcurrentDictionary<uint, ISupportedApp>(GetSupportedGamesSync()); // Fallback for now, but ideally should be awaited

            return _gameList;
        }
    }
    public bool IsInitialized { get; private set; }
    public SteamLibrary Library { get; set; }
    
    protected SteamLibraryManager()
    {

    }

    public static SteamLibraryManager Default
    {
        get
        {
            if (_instance != null) return _instance;
            lock (syncLock)
            {
                _instance = new ();
            }
            return _instance;
        }
    }
    public static SteamLibrary DefaultLibrary => Default.Library;

    public async Task InitAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        try
        {
            await GetSupportedGamesAsync().ConfigureAwait(false);

            // Library creation might still need to be mindful of thread affinity if it does UI work, 
            // but SteamLibrary ctor seems mostly data-oriented.
            // We'll init it here, but its Refresh will be called separately or it will start empty.
            var library = new SteamLibrary();
            
            // We won't call Refresh here synchronously. It should be called by the UI/ViewModel when ready.
            Default.Library = library;

            IsInitialized = true;
        }
        catch (Exception e)
        {
            var message = $"An error occurred attempting to initialize the Steam library. {e.Message}";
            log.Error(message, e);

            throw new SAMInitializationException(message, e);
        }
    }
    
    // Kept for backward compatibility but deprecated
    public void Init()
    {
        AsyncHelper.RunSync(InitAsync);
    }

    public bool TryGetApp(uint id, out ISupportedApp app)
    {
        if (_gameList == null)
        {
             _ = Apps; // Force load if null
        }
        return Apps.TryGetValue(id, out app);
    }

    // TODO: give this a different name so that it's distinct from TryGetApp
    public ISupportedApp GetApp(uint id)
    {
        // this method is used when SAM is managing an app so that it loads only the
        // requested app. in every other situation, all Apps are loaded into the list
        // Note: This specific usage might still need attention if it's called on hot paths, 
        // but typically managing an app is a distinct action.
        var apps = Apps; 

        if (apps.TryGetValue(id, out var app)) return app;
        
        // If not found in the full list, try fetching just this one (if logic supported it) 
        // or re-fetch. For now, relying on already loaded list.

        var message = $"App '{id}' is not currently supported.";
        throw new SAMException(message);
    }

    public async Task<IDictionary<uint, ISupportedApp>> GetSupportedGamesAsync(uint? appId = null)
    {
        if (_gameList != null && appId == null) return _gameList;

        try
        {
            // If we are forcing a refresh or loading first time
            if (_gameList == null) _gameList = new ConcurrentDictionary<uint, ISupportedApp>();

            var cacheKey = CacheKeys.Games;
            string gamesXml;

            if (!CacheManager.TryGetTextFile(cacheKey, out gamesXml))
            {
                var response = await client.GetAsync(SAM_GAME_LIST_URL).ConfigureAwait(false);
                gamesXml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                CacheManager.CacheText(cacheKey, gamesXml);
            }

            ParseGamesXml(gamesXml, appId);

            return _gameList;
        }
        catch (Exception e)
        {
            var message = $"An error occurred getting the list of supported apps. {e.Message}";
            log.Error(message, e);
            throw new SAMException(message, e);
        }
    }

    // Synchronous fallback (avoids AsyncHelper if possible, but still blocks)
    private IDictionary<uint, ISupportedApp> GetSupportedGamesSync(uint? appId = null)
    {
         if (_gameList != null && appId == null) return _gameList;
         return AsyncHelper.RunSync(() => GetSupportedGamesAsync(appId));
    }

    private void ParseGamesXml(string gamesXml, uint? appId)
    {
        var doc = new XmlDocument();
        doc.LoadXml(gamesXml);

        var query = appId == null
            ? "/games/game"
            : $"/games/game[text()=\"{appId}\"]";

        var nodes = doc.SelectNodes(query);
         if (nodes == null) return;

        foreach (XmlNode node in nodes)
        {
            Debug.Assert(node != null, $"{nameof(node)} is null");

            var idStr = node.FirstChild?.Value;
             if (string.IsNullOrEmpty(idStr)) 
            {
                // throw new SAMException($"Invalid configuration data..."); 
                // Don't throw loop-breaking exceptions for one bad node
                continue; 
            }
            
            var gameId = uint.Parse(idStr);

            var type = node.Attributes?["type"]?.Value;
            if (string.IsNullOrEmpty(type))
            {
                type = "normal";
            }

            _gameList[gameId] = new SupportedApp(gameId, type);
        }
    }
}
