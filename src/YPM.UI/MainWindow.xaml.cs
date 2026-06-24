using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using YPM.Core.Services;
using YPM.UI.Pages;
using YPM.UI.Services;

namespace YPM.UI;

public sealed partial class MainWindow : Window
{
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;
    private GlobalHotkeyService? _hotkeyService;
    private SMTCService? _smtcService;
    private AppWindow? _appWindow;
    private TrayIconService? _trayIconService;
    private IntPtr _hwnd;
    private bool _isExitRequested;

    public MainWindow()
    {
        App.AudioPlayer ??= new AudioPlayerService(App.ApiClient);

        _smtcService = new SMTCService(App.AudioPlayer);
        _smtcService.Initialize();

        InitializeComponent();
        InitializeWindowing();
        InitializeTrayIcon();

        var nav = new FrameNavigationService();
        nav.Frame = RootFrame;
        nav.RegisterRoute(PageRoute.Home, typeof(HomePage));
        nav.RegisterRoute(PageRoute.Search, typeof(SearchPage));
        nav.RegisterRoute(PageRoute.Library, typeof(LibraryPage));
        nav.RegisterRoute(PageRoute.AlbumDetail, typeof(AlbumPage));
        nav.RegisterRoute(PageRoute.ArtistDetail, typeof(ArtistPage));
        nav.RegisterRoute(PageRoute.PlaylistDetail, typeof(PlaylistPage));
        nav.RegisterRoute(PageRoute.SearchType, typeof(SearchTypePage));
        nav.RegisterRoute(PageRoute.Settings, typeof(SettingsPage));
        nav.SetLoginRequiredGuard(
            () => App.Settings.CurrentUser is not null,
            () => { nav.Navigate(PageRoute.Settings, clearBackStack: true); return Task.CompletedTask; });

        App.NavigationService = nav;

        Title = "Fusion";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        Activated += Window_Activated;
        Closed += Window_Closed;
        ((FrameworkElement)Content).ActualThemeChanged += Window_ThemeChanged;
        ConfigureTitleBar();

        var startTag = App.Settings.StartPage ?? "home";
        var startPageType = PageTypeForTag(startTag) ?? typeof(HomePage);

        NavigationViewItem? selectedItem = null;
        foreach (var item in RootNavigationView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == startTag)
            {
                selectedItem = nvi;
                break;
            }
        }

        RootNavigationView.SelectedItem = selectedItem ?? RootNavigationView.MenuItems[0];
        RootFrame.Navigate(startPageType);

        PlayerBarControl.BarTapped += OnPlayerBarTapped;

        if (App.Settings.EnableAcrylic)
        {
            ApplyAcrylic(true);
        }

        _hotkeyService = new GlobalHotkeyService(this, DispatcherQueue);
        _hotkeyService.PlayPause += OnHotkeyPlayPause;
        _hotkeyService.NextTrack += OnHotkeyNext;
        _hotkeyService.PreviousTrack += OnHotkeyPrevious;
        _hotkeyService.VolumeUp += OnHotkeyVolumeUp;
        _hotkeyService.VolumeDown += OnHotkeyVolumeDown;
        _hotkeyService.RegisterAll();
    }

    public void ApplyAcrylic(bool enable)
    {
        if (!enable)
        {
            SystemBackdrop = null;
            RootGrid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
            TitleBarHost.Background = RootGrid.Background;
            return;
        }

        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            TitleBarHost.Background = RootGrid.Background;
        }
    }

    public void ShowMainWindow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(_hwnd, SW_RESTORE);
        Activate();
        SetForegroundWindow(_hwnd);
    }

    private void InitializeWindowing()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnAppWindowClosing;
    }

    private void InitializeTrayIcon()
    {
        _trayIconService = new TrayIconService(ShowMainWindow, ExitApplication, "Fusion");
    }

    private void ConfigureTitleBar()
    {
        if (_appWindow is null)
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        UpdateTitleBarButtonColors();
    }

    private void UpdateTitleBarButtonColors()
    {
        if (_appWindow is null)
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        var darkTheme = ((FrameworkElement)Content).ActualTheme == ElementTheme.Dark;

        titleBar.ButtonForegroundColor = darkTheme ? Colors.White : Colors.Black;
        titleBar.ButtonHoverForegroundColor = darkTheme ? Colors.White : Colors.Black;
        titleBar.ButtonPressedForegroundColor = darkTheme ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = darkTheme
            ? ColorHelper.FromArgb(160, 255, 255, 255)
            : ColorHelper.FromArgb(160, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = darkTheme
            ? ColorHelper.FromArgb(32, 255, 255, 255)
            : ColorHelper.FromArgb(16, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = darkTheme
            ? ColorHelper.FromArgb(56, 255, 255, 255)
            : ColorHelper.FromArgb(28, 0, 0, 0);
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExitRequested || !App.Settings.CloseToBackground)
        {
            return;
        }

        args.Cancel = true;
        HideToBackground();
    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        await App.PersistPlaybackStateAsync(force: true);

        _hotkeyService?.Dispose();
        _hotkeyService = null;

        _smtcService?.Dispose();
        _smtcService = null;

        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }

        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        _trayIconService?.Dispose();
        _trayIconService = null;

        _configurationSource = null;
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTitleBarButtonColors();
        if (_configurationSource != null)
        {
            SetConfigurationSourceTheme();
        }
    }

    private void SetConfigurationSourceTheme()
    {
        switch (((FrameworkElement)Content).ActualTheme)
        {
            case ElementTheme.Dark:
                _configurationSource!.Theme = SystemBackdropTheme.Dark;
                break;
            case ElementTheme.Light:
                _configurationSource!.Theme = SystemBackdropTheme.Light;
                break;
            default:
                _configurationSource!.Theme = SystemBackdropTheme.Default;
                break;
        }
    }

    private Type? PageTypeForTag(string tag) => tag switch
    {
        "settings" => typeof(SettingsPage),
        "library" => typeof(LibraryPage),
        "search" => typeof(SearchPage),
        _ => typeof(HomePage),
    };

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        var rootPageType = PageTypeForTag(tag);
        if (rootPageType is null)
        {
            return;
        }

        if (RootFrame.CurrentSourcePageType == rootPageType)
        {
            return;
        }

        if (IsPageInBackStack(rootPageType))
        {
            while (RootFrame.CanGoBack && RootFrame.CurrentSourcePageType != rootPageType)
            {
                RootFrame.GoBack();
            }

            return;
        }

        RootFrame.BackStack.Clear();
        RootFrame.Navigate(rootPageType);
    }

    private bool IsPageInBackStack(Type pageType)
    {
        for (var i = 0; i < RootFrame.BackStack.Count; i++)
        {
            if (RootFrame.BackStack[i].SourcePageType == pageType)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPlayerBarTapped(object? sender, EventArgs e)
    {
        if (App.AudioPlayer?.CurrentTrack is null)
        {
            return;
        }

        if (NowPlayingPage.Visibility == Visibility.Visible)
        {
            _ = NowPlayingPage.HideAsync();
        }
        else
        {
            _ = NowPlayingPage.ShowAsync();
        }
    }

    private async void OnHotkeyPlayPause()
    {
        var player = App.AudioPlayer;
        if (player is null)
        {
            return;
        }

        switch (player.State)
        {
            case PlayerState.Playing:
                await player.PauseAsync();
                break;
            case PlayerState.Paused:
                await player.ResumeAsync();
                break;
            default:
                if (player.CurrentTrack is not null)
                {
                    await player.PlayAsync(player.CurrentTrack);
                }
                break;
        }
    }

    private async void OnHotkeyNext()
    {
        if (App.AudioPlayer is { } player)
        {
            await player.NextAsync();
        }
    }

    private async void OnHotkeyPrevious()
    {
        if (App.AudioPlayer is { } player)
        {
            await player.PreviousAsync();
        }
    }

    private void OnHotkeyVolumeUp()
    {
        if (App.AudioPlayer is { } player)
        {
            player.Volume = Math.Min(1.0, player.Volume + 0.05);
        }
    }

    private void OnHotkeyVolumeDown()
    {
        if (App.AudioPlayer is { } player)
        {
            player.Volume = Math.Max(0.0, player.Volume - 0.05);
        }
    }

    private void HideToBackground()
    {
        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, SW_HIDE);
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        Close();
    }

    private const int SW_HIDE = 0;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
