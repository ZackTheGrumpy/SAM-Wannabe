using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using log4net;
using SAM.Core;

namespace SAM.Managers;

public static class WannabeManager
{
    private static readonly ILog log = LogManager.GetLogger(typeof(WannabeManager));
    private static readonly HttpClient _httpClient = new HttpClient();

    private const string LAUNCHER_URL = "https://raw.githubusercontent.com/ZackTheGrumpy/SAM-Wannabe/refs/heads/WannabeLauncher/WannabeLauncher.exe";
    private const string LIST_URL = "https://raw.githubusercontent.com/ZackTheGrumpy/SAM-Wannabe/refs/heads/WannabeLauncher/ListEXE.json";

    private const string LAUNCHER_FILENAME = "WannabeLauncher.exe";
    private const string LIST_FILENAME = "ListEXE.json";

    public static async Task CheckAndRunUpdatesAsync()
    {
        try
        {
            log.Info("Starting WannabeManager updates...");

            // 1. Download/Update files in SAM directory
            await DownloadFileAsync(LAUNCHER_URL, LAUNCHER_FILENAME).ConfigureAwait(false);
            await DownloadFileAsync(LIST_URL, LIST_FILENAME).ConfigureAwait(false);

            // 2. Distribute to all installed games
            await DistributeFilesToGamesAsync().ConfigureAwait(false);

            log.Info("WannabeManager updates completed successfully.");
        }
        catch (Exception ex)
        {
            log.Error($"WannabeManager update failed: {ex.Message}", ex);
        }
    }

    private static async Task DownloadFileAsync(string url, string fileName)
    {
        try
        {
            var destinationPath = Path.Combine(AppContext.BaseDirectory, fileName);
            log.Info($"Downloading {fileName} from {url}...");

            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await File.WriteAllBytesAsync(destinationPath, data).ConfigureAwait(false);

            log.Info($"Successfully downloaded {fileName} to {destinationPath}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to download {fileName}: {ex.Message}", ex);
        }
    }

    private static async Task DistributeFilesToGamesAsync()
    {
        await Task.Run(() =>
        {
            var samPath = AppContext.BaseDirectory;
            var launcherSource = Path.Combine(samPath, LAUNCHER_FILENAME);
            var listSource = Path.Combine(samPath, LIST_FILENAME);

            if (!File.Exists(launcherSource) || !File.Exists(listSource))
            {
                log.Warn("Source files for distribution are missing. Skipping distribution.");
                return;
            }

            // We need to iterate over installed games.
            // Assuming SteamClientManager gives us access to installed apps via SteamApps008
            var client = SteamClientManager.Default;
            if (client == null)
            {
                log.Warn("SteamClientManager not initialized. Cannot distribute files.");
                return;
            }

            // We can get a list of all apps from the Library manager if initialized, 
            // or check SteamApps directly if we have a list of IDs.
            // However, SteamLibraryManager.Default.Library might be empty if not refreshed.
            // A safer bet is to iterate the "Apps" from the manager which includes all supported games,
            // and check if they are installed.

            var apps = SteamLibraryManager.Default.Apps;
            log.Info($"Found {apps.Count} supported apps in SteamLibraryManager.");
            
            int count = 0;
            int installedCount = 0;

            foreach (var app in apps.Values)
            {
                try
                {
                    if (client.SteamApps008.IsAppInstalled(app.Id))
                    {
                        installedCount++;
                        var installDir = client.SteamApps008.GetAppInstallDir(app.Id);
                        
                        if (string.IsNullOrEmpty(installDir)) {
                             log.Debug($"App {app.Id} is installed but has empty install directory.");
                             continue;
                        }

                        if (!Directory.Exists(installDir)) {
                             log.Debug($"App {app.Id} directory does not exist: {installDir}");
                             continue;
                        }

                        var launcherDest = Path.Combine(installDir, LAUNCHER_FILENAME);
                        var listDest = Path.Combine(installDir, LIST_FILENAME);

                        log.Debug($"Copying files to {installDir} for App {app.Id}...");

                        File.Copy(launcherSource, launcherDest, overwrite: true);
                        File.Copy(listSource, listDest, overwrite: true);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    // specific game failure shouldn't stop others
                    log.Error($"Failed to copy files to game {app.Id}: {ex.Message}", ex);
                }
            }

            log.Info($"Found {installedCount} installed games. Distributed WannabeLauncher files to {count} game folders.");
        });
    }
}
