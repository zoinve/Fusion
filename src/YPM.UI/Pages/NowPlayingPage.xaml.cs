using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
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
    private string? _lastBackgroundCoverUrl;
    private long _backgroundUpdateVersion;

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
        TranslationToggleButton.Opacity = _viewModel?.ShowLyricsTranslation == true ? 0.55 : 0.3;
        Bindings.Update();
        _ = UpdateBackgroundFromCoverAsync();
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

        if (e.PropertyName is nameof(NowPlayingViewModel.CurrentTrackCoverUrl))
        {
            _ = UpdateBackgroundFromCoverAsync();
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

    private void OnTranslationToggleClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.ShowLyricsTranslation = !_viewModel.ShowLyricsTranslation;
        TranslationToggleButton.Opacity = _viewModel.ShowLyricsTranslation ? 0.55 : 0.3;
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

    private async Task UpdateBackgroundFromCoverAsync()
    {
        if (_viewModel is null)
        {
            return;
        }

        var coverUrl = NormalizeCoverUrl(_viewModel.CurrentTrackCoverUrl);
        if (string.Equals(_lastBackgroundCoverUrl, coverUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastBackgroundCoverUrl = coverUrl;
        var version = Interlocked.Increment(ref _backgroundUpdateVersion);

        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            await DispatcherQueue.EnqueueAsync(() => ApplyNowPlayingPalette(Colors.Black));
            return;
        }

        try
        {
            var imagePath = await ResolveCoverPathAsync(coverUrl);
            var color = imagePath is not null
                ? await ExtractMonetColorAsync(imagePath)
                : Colors.Black;

            if (version != _backgroundUpdateVersion)
            {
                return;
            }

            await DispatcherQueue.EnqueueAsync(() => ApplyNowPlayingPalette(color));
        }
        catch
        {
            if (version != _backgroundUpdateVersion)
            {
                return;
            }

            await DispatcherQueue.EnqueueAsync(() => ApplyNowPlayingPalette(Colors.Black));
        }
    }

    private static string NormalizeCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var normalized = url.Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = $"https:{normalized}";
        }
        else if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"https://{normalized["http://".Length..]}";
        }

        return normalized;
    }

    private static async Task<string?> ResolveCoverPathAsync(string coverUrl)
    {
        var cache = App.ImageCacheService;
        if (cache is null)
        {
            return null;
        }

        var cachedPath = cache.GetCachedFilePath(coverUrl);
        if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
        {
            return cachedPath;
        }

        await cache.CacheImageAsync(coverUrl);
        cachedPath = cache.GetCachedFilePath(coverUrl);
        return !string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath) ? cachedPath : null;
    }

    private static async Task<Color> ExtractMonetColorAsync(string imagePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1u, Math.Min(96u, decoder.PixelWidth)),
            ScaledHeight = Math.Max(1u, Math.Min(96u, decoder.PixelHeight)),
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var pixels = pixelData.DetachPixelData();
        if (pixels.Length < 4)
        {
            return Colors.Black;
        }

        double sumR = 0;
        double sumG = 0;
        double sumB = 0;
        double totalWeight = 0;

        for (int i = 0; i <= pixels.Length - 4; i += 16)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];

            if (a < 32)
            {
                continue;
            }

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = max == 0 ? 0 : (max - min) / (double)max;
            var luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255d;

            if (luminance < 0.08 || luminance > 0.92)
            {
                continue;
            }

            var saturationBoost = 0.35 + saturation * 0.65;
            var luminanceBias = 1.0 - Math.Abs(luminance - 0.55) * 1.4;
            var weight = Math.Max(0.05, saturationBoost * luminanceBias);

            sumR += r * weight;
            sumG += g * weight;
            sumB += b * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0.001)
        {
            return Color.FromArgb(255, 28, 28, 28);
        }

        var avgR = sumR / totalWeight;
        var avgG = sumG / totalWeight;
        var avgB = sumB / totalWeight;
        return ToMonetTone(avgR, avgG, avgB);
    }

    private static Color ToMonetTone(double red, double green, double blue)
    {
        var gray = (red + green + blue) / 3d;
        red = gray * 0.28 + red * 0.72;
        green = gray * 0.28 + green * 0.72;
        blue = gray * 0.28 + blue * 0.72;

        red = 18 + red * 0.62;
        green = 18 + green * 0.62;
        blue = 18 + blue * 0.62;

        var max = Math.Max(red, Math.Max(green, blue));
        if (max < 72)
        {
            var scale = 72 / Math.Max(1, max);
            red *= scale;
            green *= scale;
            blue *= scale;
        }

        return Color.FromArgb(
            255,
            (byte)Math.Clamp(Math.Round(red), 0, 255),
            (byte)Math.Clamp(Math.Round(green), 0, 255),
            (byte)Math.Clamp(Math.Round(blue), 0, 255));
    }

    private void ApplyNowPlayingPalette(Color backgroundColor)
    {
        AnimateBrushColor(NowPlayingBackgroundBrush, backgroundColor);
        AnimateBrushColor(NowPlayingAccentBrush, CreateProgressAccentColor(backgroundColor));
        AnimateBrushColor(NowPlayingTrackBrush, CreateProgressTrackColor(backgroundColor));
    }

    private static Color CreateProgressAccentColor(Color backgroundColor)
    {
        var lift = 0.42;
        var saturationBoost = 1.2;
        var red = LiftChannel(backgroundColor.R, lift);
        var green = LiftChannel(backgroundColor.G, lift);
        var blue = LiftChannel(backgroundColor.B, lift);
        return ApplySaturation(red, green, blue, saturationBoost, alpha: 255);
    }

    private static Color CreateProgressTrackColor(Color backgroundColor)
    {
        var lift = 0.18;
        var saturationBoost = 0.82;
        var red = LiftChannel(backgroundColor.R, lift);
        var green = LiftChannel(backgroundColor.G, lift);
        var blue = LiftChannel(backgroundColor.B, lift);
        return ApplySaturation(red, green, blue, saturationBoost, alpha: 150);
    }

    private static double LiftChannel(byte channel, double amount)
    {
        return channel + (255 - channel) * amount;
    }

    private static Color ApplySaturation(double red, double green, double blue, double factor, byte alpha)
    {
        var gray = (red + green + blue) / 3d;
        red = gray + (red - gray) * factor;
        green = gray + (green - gray) * factor;
        blue = gray + (blue - gray) * factor;

        return Color.FromArgb(
            alpha,
            (byte)Math.Clamp(Math.Round(red), 0, 255),
            (byte)Math.Clamp(Math.Round(green), 0, 255),
            (byte)Math.Clamp(Math.Round(blue), 0, 255));
    }

    private static void AnimateBrushColor(SolidColorBrush? brush, Color color)
    {
        if (brush is null)
        {
            return;
        }

        if (brush.Color == color)
        {
            return;
        }

        var animation = new ColorAnimation
        {
            To = color,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, brush);
        Storyboard.SetTargetProperty(animation, "Color");
        storyboard.Children.Add(animation);
        storyboard.Begin();
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

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
    {
        var tcs = new TaskCompletionSource();
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetCanceled();
        }

        return tcs.Task;
    }
}
