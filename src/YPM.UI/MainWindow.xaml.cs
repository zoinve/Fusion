using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;
using YPM.Core.Services;
using YPM.UI.Pages;
using YPM.UI.Services;
using Microsoft.UI;

namespace YPM.UI;

public sealed partial class MainWindow : Window
{
    private WindowsSystemDispatcherQueueHelper? _wsdqHelper;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;
    private GlobalHotkeyService? _hotkeyService;
    private SMTCService? _smtcService;

    public MainWindow()
    {
        // Initialize audio player BEFORE InitializeComponent so PlayerBarControl can use it
        App.AudioPlayer ??= new AudioPlayerService(App.ApiClient);

        // Initialize SMTC for system media controls integration
        _smtcService = new SMTCService(App.AudioPlayer);
        _smtcService.Initialize();

        InitializeComponent();

        // Wire up navigation
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
        // Wire up login-required guard so Library and Settings redirect
        // unauthenticated users to Settings (where the login UI lives).
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
        // Navigate to configured start page
        var startTag = App.Settings.StartPage ?? "home";
        var startPageType = PageTypeForTag(startTag) ?? typeof(HomePage);

        // Select the corresponding NavigationView item
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

        // Wire up PlayerBar click to show/hide NowPlayingPage.
        PlayerBarControl.BarTapped += OnPlayerBarTapped;

        // Apply acrylic if enabled in settings.
        if (App.Settings.EnableAcrylic)
        {
            ApplyAcrylic(true);
        }

        // Global hotkeys
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
            this.SystemBackdrop = null;
            RootGrid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
            TitleBarHost.Background = RootGrid.Background;
            return;
        }

        if (DesktopAcrylicController.IsSupported())
        {
            // Use the modern high-level SystemBackdrop property available in Windows App SDK 1.2+
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            TitleBarHost.Background = RootGrid.Background;
        }
    }

    private void ConfigureTitleBar()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        UpdateTitleBarButtonColors();
    }

    private void UpdateTitleBarButtonColors()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var titleBar = appWindow.TitleBar;
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
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        // Persist playback state before disposing services
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
        switch (((FrameworkElement)this.Content).ActualTheme)
        {
            case ElementTheme.Dark: _configurationSource!.Theme = SystemBackdropTheme.Dark; break;
            case ElementTheme.Light: _configurationSource!.Theme = SystemBackdropTheme.Light; break;
            case ElementTheme.Default: _configurationSource!.Theme = SystemBackdropTheme.Default; break;
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

        // Already on the root page of this section — nothing to do.
        if (RootFrame.CurrentSourcePageType == rootPageType)
        {
            return;
        }

        // If the target root page is already in the back stack (we're in a sub-page
        // of this section), navigate back to it instead of creating a new instance.
        if (IsPageInBackStack(rootPageType))
        {
            while (RootFrame.CanGoBack && RootFrame.CurrentSourcePageType != rootPageType)
            {
                RootFrame.GoBack();
            }
            return;
        }

        // Switching to a different section — clear old pages to avoid memory leaks.
        RootFrame.BackStack.Clear();
        RootFrame.Navigate(rootPageType);
    }

    private bool IsPageInBackStack(Type pageType)
    {
        for (var i = 0; i < RootFrame.BackStack.Count; i++)
        {
            if (RootFrame.BackStack[i].SourcePageType == pageType)
                return true;
        }
        return false;
    }

    private void OnPlayerBarTapped(object? sender, EventArgs e)
    {
        // Only respond when a track is loaded.
        if (App.AudioPlayer?.CurrentTrack is null)
            return;

        if (NowPlayingPage.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            _ = NowPlayingPage.HideAsync();
        }
        else
        {
            _ = NowPlayingPage.ShowAsync();
        }
    }

    // ── Global hotkey handlers ──────────────────────────────────

    private async void OnHotkeyPlayPause()
    {
        var player = App.AudioPlayer;
        if (player is null) return;

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
                    await player.PlayAsync(player.CurrentTrack);
                break;
        }
    }

    private async void OnHotkeyNext()
    {
        if (App.AudioPlayer is { } player)
            await player.NextAsync();
    }

    private async void OnHotkeyPrevious()
    {
        if (App.AudioPlayer is { } player)
            await player.PreviousAsync();
    }

    private void OnHotkeyVolumeUp()
    {
        if (App.AudioPlayer is { } player)
            player.Volume = Math.Min(1.0, player.Volume + 0.05);
    }

    private void OnHotkeyVolumeDown()
    {
        if (App.AudioPlayer is { } player)
            player.Volume = Math.Max(0.0, player.Volume - 0.05);
    }
}
