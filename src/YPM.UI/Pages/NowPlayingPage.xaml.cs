using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;
using YPM.UI.Helpers;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class NowPlayingPage : Page
{
    private NowPlayingViewModel? _viewModel;
    private bool _isSeeking;
    private bool _isAdjustingVolume;
    private bool _isFullscreen;
    private int _lastScaledLyricIndex = -1;

    public NowPlayingViewModel ViewModel => _viewModel!;

    public string FullscreenEnterGlyph => "\uE740";
    public string FullscreenExitGlyph => "\uE73F";
    public string PrevGlyph => IconGlyph.Previous;
    public string NextGlyph => IconGlyph.Next;
    public string CollapsePageGlyph => "\uE70D";

    public NowPlayingPage()
    {
        InitializeViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public async Task ShowAsync()
    {
        InitializeViewModel();
        Visibility = Visibility.Visible;
        
        if (Resources["ShowStoryboard"] is Storyboard showStoryboard)
        {
            showStoryboard.Begin();
        }

        UpdateProgressVisual();
        Bindings.Update();
        Focus(FocusState.Programmatic);
        await Task.CompletedTask;
    }

    public async Task HideAsync()
    {
        if (Resources["HideStoryboard"] is Storyboard hideStoryboard)
        {
            var tcs = new TaskCompletionSource();
            
            void OnCompleted(object? sender, object e)
            {
                hideStoryboard.Completed -= OnCompleted;
                Visibility = Visibility.Collapsed;
                tcs.SetResult();
            }

            hideStoryboard.Completed += OnCompleted;
            hideStoryboard.Begin();
            await tcs.Task;
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void InitializeViewModel()
    {
        if (_viewModel is not null || App.AudioPlayer is null)
        {
            return;
        }

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            return;
        }

        _viewModel = new NowPlayingViewModel(App.AudioPlayer, App.LikedSongsService, dispatcher);
        _viewModel.RefreshFromPlayer();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeViewModel();
        UpdateProgressVisual();
        CenterCurrentLyric();
        Bindings.Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingViewModel.ProgressValue))
        {
            UpdateProgressVisual();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.Lyrics) or nameof(NowPlayingViewModel.CurrentLyricIndex))
        {
            _ = DispatcherQueue.TryEnqueue(CenterCurrentLyric);
        }
    }

    private void OnProgressBarHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressVisual();
    }

    private void UpdateProgressVisual()
    {
        if (_viewModel is null)
        {
            return;
        }

        var width = ProgressBarHost.ActualWidth;
        var fillWidth = width * Math.Clamp(_viewModel.ProgressValue / 100d, 0, 1);
        ProgressFill.Width = Math.Clamp(fillWidth, 0, width);
    }

    private void OnProgressBarPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
        ProgressBarHost.CapturePointer(e.Pointer);
        SeekFromPoint(e.GetCurrentPoint(ProgressBarHost).Position.X);
    }

    private void OnProgressBarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isSeeking)
        {
            SeekFromPoint(e.GetCurrentPoint(ProgressBarHost).Position.X);
        }
    }

    private void OnProgressBarPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSeeking)
        {
            return;
        }

        SeekFromPoint(e.GetCurrentPoint(ProgressBarHost).Position.X);
        _isSeeking = false;
        ProgressBarHost.ReleasePointerCapture(e.Pointer);
    }

    private void OnProgressBarPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
    }

    private void SeekFromPoint(double x)
    {
        if (_viewModel is null || ProgressBarHost.ActualWidth <= 0)
        {
            return;
        }

        var percent = Math.Clamp(x / ProgressBarHost.ActualWidth * 100d, 0, 100);
        _viewModel.SeekTo(percent);
    }

    private void OnRightPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLyricsPadding();
    }

    private void UpdateLyricsPadding()
    {
        var halfHeight = RightPanel.ActualHeight / 2;
        if (halfHeight > 0)
        {
            // We want the middle of the first/last lyric line to be at the center.
            // A typical lyric line with padding is about 60-80px.
            // For now, we use halfHeight as a base.
            LyricsHeader.Height = halfHeight - 40; 
            LyricsFooter.Height = halfHeight;
        }
    }

    private void CenterCurrentLyric()
    {
        if (_viewModel?.Lyrics is not { Count: > 0 })
        {
            return;
        }

        int targetIndex = Math.Max(0, _viewModel.CurrentLyricIndex);
        LyricsListView.UpdateLayout();

        var container = LyricsListView.ContainerFromIndex(targetIndex) as FrameworkElement;
        var scrollViewer = FindScrollViewer(LyricsListView);

        if (container != null && scrollViewer != null)
        {
            var transform = container.TransformToVisual(LyricsListView);
            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double itemHeight = container.ActualHeight;
            double viewportHeight = scrollViewer.ViewportHeight;
            double currentOffset = scrollViewer.VerticalOffset;

            double targetVerticalOffset = currentOffset + position.Y - (viewportHeight / 2) + (itemHeight / 2);

            scrollViewer.ChangeView(null, targetVerticalOffset, null);
        }
        else
        {
            LyricsListView.ScrollIntoView(_viewModel.Lyrics[targetIndex], ScrollIntoViewAlignment.Default);
        }

        // Scale animation: needs realized containers, retry if not ready
        TryAnimateScale(targetIndex);
    }

    private void TryAnimateScale(int targetIndex, int retryCount = 0)
    {
        var prevChanged = _lastScaledLyricIndex >= 0 && _lastScaledLyricIndex != targetIndex;
        var currChanged = _lastScaledLyricIndex != targetIndex;

        if (!prevChanged && !currChanged)
            return;

        bool animated = false;

        if (prevChanged)
        {
            var prevContainer = LyricsListView.ContainerFromIndex(_lastScaledLyricIndex) as FrameworkElement;
            if (prevContainer != null)
            {
                var prevScale = FindScaleTransform(prevContainer);
                if (prevScale != null)
                {
                    AnimateScale(prevScale, 1.0);
                    animated = true;
                }
            }
        }

        if (currChanged)
        {
            var currContainer = LyricsListView.ContainerFromIndex(targetIndex) as FrameworkElement;
            if (currContainer != null)
            {
                var currScale = FindScaleTransform(currContainer);
                if (currScale != null)
                {
                    AnimateScale(currScale, 1.3);
                }
            }
        }

        _lastScaledLyricIndex = targetIndex;

        // If containers weren't realized yet (virtualization), retry once after layout
        if (!animated && retryCount < 3)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                LyricsListView.UpdateLayout();
                TryAnimateScale(targetIndex, retryCount + 1);
            });
        }
    }

    private ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private async void OnDismissClick(object sender, RoutedEventArgs e)
    {
        await HideAsync();
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        if (_isFullscreen)
        {
            ToggleFullscreen();
        }
        else
        {
            _ = HideAsync();
        }

        e.Handled = true;
    }

    private void ToggleFullscreen()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (_isFullscreen)
            {
                appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                _isFullscreen = false;
                ExpandIcon.Visibility = Visibility.Visible;
                CollapseIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                _isFullscreen = true;
                ExpandIcon.Visibility = Visibility.Collapsed;
                CollapseIcon.Visibility = Visibility.Visible;
            }
        }
        catch
        {
        }
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.PreviousAsync();
        }
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.PlayPauseAsync();
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.NextAsync();
        }
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleMode();
    }

    private void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleMute();
    }

    private async void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.ToggleLikeAsync();
        }
    }

    private async void OnArtistTapped(object sender, TappedRoutedEventArgs e)
    {
        var artistId = _viewModel?.CurrentTrack?.Artists?.FirstOrDefault()?.Id;
        if (artistId is > 0)
        {
            await HideAsync();
            App.NavigationService?.Navigate(Core.Services.PageRoute.ArtistDetail, artistId.Value);
        }
    }

    private async void OnAlbumTapped(object sender, TappedRoutedEventArgs e)
    {
        var albumId = _viewModel?.CurrentTrack?.Album?.Id;
        if (albumId is > 0)
        {
            await HideAsync();
            App.NavigationService?.Navigate(Core.Services.PageRoute.AlbumDetail, albumId.Value);
        }
    }

    private static ScaleTransform? FindScaleTransform(DependencyObject element)
    {
        if (element is ScaleTransform st) return st;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = FindScaleTransform(child);
            if (result != null) return result;
        }
        return null;
    }

    private static void AnimateScale(ScaleTransform scale, double to)
    {
        var storyboard = new Storyboard();
        var animX = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var animY = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(animX, scale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, scale);
        Storyboard.SetTargetProperty(animY, "ScaleY");

        storyboard.Children.Add(animX);
        storyboard.Children.Add(animY);
        storyboard.Begin();
    }
}
