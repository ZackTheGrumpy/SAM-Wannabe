using System.Windows;
using System.Threading.Tasks;
using DevExpress.Mvvm.CodeGenerators;
using log4net;
using SAM.Behaviors;
using SAM.SplashScreen;
using SAM.Managers;
using SAM.Extensions;

namespace SAM.ViewModels;

[GenerateViewModel]
public partial class MainWindowViewModel
{
    private const string TITLE_BASE = "SAM | Wannabe Edition";

    private readonly ILog log = LogManager.GetLogger(typeof(MainWindowViewModel));

    [GenerateProperty] private string title = TITLE_BASE;
    [GenerateProperty] private string subTitle;
    [GenerateProperty] private bool _isManager;
    [GenerateProperty] private bool _isLibrary;
    [GenerateProperty] private ApplicationMode _mode;
    [GenerateProperty] private SteamUser _user;
    [GenerateProperty] private HomeViewModel _homeVm;
    [GenerateProperty] private SteamGameViewModel gameVm;
    [GenerateProperty] private MenuViewModel _menuVm;
    [GenerateProperty] private WindowSettings config;
    [GenerateProperty] private object _currentVm;

    public MainWindowViewModel()
    {
        //User = new (SteamClientManager.Default);
        HomeVm = new ();

        CurrentVm = HomeVm;
        MenuVm = new (HomeVm);

        Init();
    }
    
    public MainWindowViewModel(HomeSettings settings)
    {
        //User = new (SteamClientManager.Default);
        HomeVm = new (settings);

        CurrentVm = HomeVm;
        MenuVm = new (HomeVm);

        Init();
    }

    public MainWindowViewModel(SteamGameViewModel gameVm)
    {
        //User = new (SteamClientManager.Default);
        GameVm = gameVm;

        CurrentVm = GameVm;
        MenuVm = new (GameVm);

        Init();
    }
    
    [GenerateCommand]
    protected async Task OnLoaded()
    {
        await SteamLibraryManager.Default.InitAsync().ConfigureAwait(false);
        
        // Trigger library refresh to populate items
        SteamLibraryManager.DefaultLibrary?.RefreshAsync().SafeFireAndForget(e => log.Error("Failed to auto-refresh library on startup", e));

        SplashScreenHelper.Close();

        // activate the main window after closing the splash screen and shutting its dispatcher down
        // activate the main window after closing the splash screen and shutting its dispatcher down
        Application.Current.Dispatcher.Invoke(() => Application.Current.MainWindow?.Activate());
    }

    private void Init()
    {
        Mode = GameVm != null ? ApplicationMode.Manager : ApplicationMode.Default;
        IsManager = Mode == ApplicationMode.Manager;
        IsLibrary = Mode == ApplicationMode.Default;
    }

    private void OnSubTitleChanged()
    {
        if (string.IsNullOrWhiteSpace(SubTitle))
        {
            Title = TITLE_BASE;
            return;
        }

        Title = $"{TITLE_BASE} | {SubTitle}";
    }
}
