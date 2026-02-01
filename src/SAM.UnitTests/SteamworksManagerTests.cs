using SAM.Core;
using Xunit;
using Xunit.Abstractions;

namespace SAM.UnitTests
{
    public class SteamworksManagerTests
    {
        private readonly ITestOutputHelper _output;

        public SteamworksManagerTests(ITestOutputHelper testOutputHelper)
        {
            _output = testOutputHelper;
        }

        [Theory(DisplayName = "Steamworks App")]
        [InlineData(287290)]
        [InlineData(254700)]
        [InlineData(952060)]
        public void SteamworksManager_GetAppInfo_Succeeds(uint appId)
        {
            var appData = SteamworksManager.GetAppInfo(appId);

            Assert.NotNull(appData.StoreApp);
            Assert.NotEmpty(appData.StoreApp.Name);

            _output.WriteLine($"App {appId} is '{appData.StoreApp.Name}'.");
        }

        [Theory(DisplayName = "Steamworks App (w/ DLC)")]
        [InlineData(287290)]
        [InlineData(952060)]
        public void SteamworksManager_GetAppInfo_WithDLC_Succeeds(uint appId)
        {
            var appData = SteamworksManager.GetAppInfo(appId, true);

            Assert.NotNull(appData.StoreApp);
            Assert.NotEmpty(appData.StoreApp.Name);
            Assert.NotEmpty(appData.StoreApp.DlcInfo);
        }

        [Theory(DisplayName = "Steamworks App (w/o DLC)")]
        [InlineData(254700)]
        public void SteamworksManager_GetAppInfo_NoDLC_Succeeds(uint appId)
        {
            var appData = SteamworksManager.GetAppInfo(appId, true);

            Assert.NotNull(appData.StoreApp);
            Assert.NotEmpty(appData.StoreApp.Name);
            Assert.Empty(appData.StoreApp.DlcInfo);
        }

        [Fact(DisplayName = "Steamworks App List")]
        public void SteamworksManager_GetAppList_NotEmpty()
        {
            var apps = SteamworksManager.GetAppList();

            Assert.NotEmpty(apps);
        }
    }
}
