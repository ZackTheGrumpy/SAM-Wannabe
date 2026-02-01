using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using log4net;
using Microsoft.Win32;
using SAM.Models;

namespace SAM.Managers;

public class GameUpdaterManager
{
    private static readonly ILog log = LogManager.GetLogger(typeof(GameUpdaterManager));
    private readonly HttpClient _httpClient;

    public GameUpdaterManager()
    {
        _httpClient = new HttpClient();
    }

    public async Task<GameUpdateResult> UpdateGameAsync(string appId, bool useMirror)
    {
        log.Info($"Starting Game Updater for AppID: {appId}. Mirror: {useMirror}");

        string steamPath = GetSteamPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            log.Error("Steam path not found.");
            return GameUpdateResult.DownloadFailed;
        }

        string depotCache = Path.Combine(steamPath, "depotcache");
        Directory.CreateDirectory(depotCache);

        log.Info($"Fetching depots for AppID: {appId}");
        List<DepotInfo> depots = await GetDepotsAsync(appId);

        if (depots.Count == 0)
        {
            log.Warn($"No depots found for AppID: {appId}");
            return GameUpdateResult.InvalidAppId;
        }

        bool anySuccess = false;
        bool anyFailure = false;

        foreach (var depot in depots)
        {
            string manifestPath = Path.Combine(
                depotCache,
                $"{depot.DepotId}_{depot.Gid}.manifest"
            );

            if (!File.Exists(manifestPath))
            {
                bool success = await DownloadManifestAsync(
                    depot.DepotId,
                    depot.Gid,
                    manifestPath,
                    appId,
                    useMirror
                );
                
                if (success) anySuccess = true;
                else anyFailure = true;
            }
            else
            {
                log.Info($"Manifest already exists: {manifestPath}");
                anySuccess = true;
            }
        }

        if (anyFailure && !anySuccess) return GameUpdateResult.DownloadFailed;
        if (anyFailure) log.Warn("Some manifests failed to download.");
        
        log.Info($"Update complete for AppID: {appId}");
        return GameUpdateResult.Success;
    }

    private string? GetSteamPath()
    {
        // Try using existing SteamClientManager if possible, otherwise fallback to registry
        try
        {
             // return SteamClientManager.SteamInstallPath; 
             // Logic from provided snippet:
             using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
             return key?.GetValue("SteamPath")?.ToString();
        }
        catch (Exception ex)
        {
            log.Error("Failed to get Steam path from registry.", ex);
            return null;
        }
    }

    private async Task<List<DepotInfo>> GetDepotsAsync(string appId)
    {
        // Use SteamCMD public API instead of authenticated Steam Web API
        string url = $"https://api.steamcmd.net/v1/info/{appId}";

        try
        {
            string json = await _httpClient.GetStringAsync(url);
            return ParseDepots(json, appId);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to fetch depots for AppID {appId}", ex);
            return new List<DepotInfo>();
        }
    }

    private List<DepotInfo> ParseDepots(string json, string appId)
    {
        var depots = new List<DepotInfo>();

        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json);
            var depotData = root?["data"]?[appId]?["depots"];

            if (depotData is System.Text.Json.Nodes.JsonObject depotObj)
            {
                foreach (var depotEntry in depotObj)
                {
                    // key is depotId
                    if (int.TryParse(depotEntry.Key, out int depotId))
                    {
                        var manifests = depotEntry.Value?["manifests"];
                        if (manifests is System.Text.Json.Nodes.JsonObject manifestObj)
                        {
                            foreach (var manifestEntry in manifestObj)
                            {
                                var manifestNode = manifestEntry.Value;
                                var gidStr = manifestNode?["gid"]?.ToString();
                                if (ulong.TryParse(gidStr, out ulong gid))
                                {
                                    depots.Add(new DepotInfo { DepotId = depotId, Gid = gid });
                                }
                            }
                        }
                    }
                }
            }

            return depots;
        }
        catch (Exception ex)
        {
            log.Error("Failed to parse depot JSON from SteamCMD.", ex);
            return new List<DepotInfo>();
        }
    }

    private async Task<bool> DownloadManifestAsync(int depotId, ulong manifestId, string outputPath, string appId, bool useMirror)
    {
        var urlsToTry = new List<string>();

        if (useMirror)
        {
             // Primary Mirror
             urlsToTry.Add($"https://raw.githubusercontent.com/ZackTheGrumpy/ProSubHelper/refs/heads/{appId}/{depotId}_{manifestId}.manifest");
             // Backup Mirror (Fallback)
             urlsToTry.Add($"https://raw.githubusercontent.com/ZackTheGrumpy/nomoreupdate/refs/heads/{appId}/{depotId}_{manifestId}.manifest");
        }
        else
        {
            // Default Source
            urlsToTry.Add($"https://raw.githubusercontent.com/qwe213312/k25FCdfEOoEJ42S6/main/{depotId}_{manifestId}.manifest");
        }

        foreach (var url in urlsToTry)
        {
            log.Info($"Downloading manifest: {url}");

            try
            {
                byte[] data = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(outputPath, data);
                log.Info($"Downloaded to {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to download manifest from {url}: {ex.Message}");
                // Continue loop to try next URL
            }
        }

        return false;
    }
}
