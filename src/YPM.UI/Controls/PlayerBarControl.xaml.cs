using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using YPM.Core.Models;
using YPM.UI.Helpers;
using YPM.UI.ViewModels;

namespace YPM.UI.Controls;

public sealed partial class PlayerBarControl : UserControl
{
    private PlayerBarViewModel? _viewModel;
    private bool _isSeeking;
    private bool _isAdjustingVolume;
    private double _queueFlyoutMaxHeight = 420;

    public event EventHandler? BarTapped;

    public PlayerBarViewModel ViewModel => _viewModel!;
    public double QueueFlyoutMaxHeight => _queueFlyoutMaxHeight;

    public string PrevGlyph => IconGlyph.Previous;
    public string NextGlyph => IconGlyph.Next;
    public string QueueGlyph => IconGlyph.List;

    public PlayerBarControl()
    {
        EnsureViewModelInitialized();
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnControlSizeChanged;
        Unloaded += OnUnloaded;
    }

    private void EnsureViewModelInitialized()
    {
        if (_viewModel is not null || App.AudioPlayer is null)
        {
            return;
        }

        var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
        {
            return;
        }

        _viewModel = new PlayerBarViewModel(App.AudioPlayer, App.LikedSongsService, dispatcherQueue);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureViewModelInitialized();

        UpdateQueueFlyoutMaxHeight();
        Bindings.Update();
        UpdateProgressVisual();
        UpdateVolumeVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateQueueFlyoutMaxHeight();
        Bindings.Update();
        UpdateProgressVisual();
        UpdateVolumeVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerBarViewModel.ProgressValue) or nameof(PlayerBarViewModel.ShowProgressBar))
        {
            UpdateProgressVisual();
        }

        if (e.PropertyName is nameof(PlayerBarViewModel.Volume))
        {
            UpdateVolumeVisual();
        }
    }

    private void UpdateQueueFlyoutMaxHeight()
    {
        var rootHeight = XamlRoot?.Size.Height ?? App.MainWindow.Bounds.Height;
        var availableHeight = rootHeight - ActualHeight - 24;
        _queueFlyoutMaxHeight = Math.Max(160, availableHeight);
    }

    private void UpdateProgressVisual()
    {
        if (_viewModel is null) return;

        var width = ProgressBarHost.ActualWidth;
        var fillWidth = width * Math.Clamp(_viewModel.ProgressValue / 100d, 0, 1);
        ProgressFill.Width = Math.Clamp(fillWidth, 0, width);
    }

    private void UpdateVolumeVisual()
    {
        if (_viewModel is null) return;

        var height = VolumeBarHost.ActualHeight;
        var fillHeight = height * Math.Clamp(_viewModel.Volume, 0, 1);
        VolumeFill.Height = Math.Clamp(fillHeight, 0, height);
    }

    private void OnVolumeFlyoutOpened(object sender, object e)
    {
        UpdateVolumeVisual();
    }

    private void OnVolumeBarHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVolumeVisual();
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
        if (_isSeeking)
        {
            SeekFromPoint(e.GetCurrentPoint(ProgressBarHost).Position.X);
            _isSeeking = false;
            ProgressBarHost.ReleasePointerCapture(e.Pointer);
        }
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

    private void OnVolumeBarPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isAdjustingVolume = true;
        VolumeBarHost.CapturePointer(e.Pointer);
        SetVolumeFromPoint(e.GetCurrentPoint(VolumeBarHost).Position.Y);
    }

    private void OnVolumeBarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isAdjustingVolume)
        {
            SetVolumeFromPoint(e.GetCurrentPoint(VolumeBarHost).Position.Y);
        }
    }

    private void OnVolumeBarPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isAdjustingVolume)
        {
            SetVolumeFromPoint(e.GetCurrentPoint(VolumeBarHost).Position.Y);
            _isAdjustingVolume = false;
            VolumeBarHost.ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnVolumeBarPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isAdjustingVolume = false;
    }

    private void SetVolumeFromPoint(double y)
    {
        if (_viewModel is null || VolumeBarHost.ActualHeight <= 0)
        {
            return;
        }

        var normalized = 1d - Math.Clamp(y / VolumeBarHost.ActualHeight, 0, 1);
        _viewModel.SetVolumeLevel(normalized);
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.PreviousAsync();
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.PlayPauseAsync();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.NextAsync();
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleMode();
    }

    private void OnVolumeButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (VolumeButton.Flyout is FlyoutBase flyout)
        {
            flyout.ShowAt(VolumeButton);
        }
    }

    private void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleMute();
    }

    private async void OnQueueItemClick(object sender, ItemClickEventArgs e)
    {
        if (_viewModel is not null && e.ClickedItem is TrackInfo track)
        {
            await _viewModel.PlayQueueItemAsync(track);
            if (QueueButton.Flyout is Flyout flyout)
            {
                flyout.Hide();
            }
        }
    }

    private async void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.ToggleLikeAsync();
    }

    private void OnInteractiveElementTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnPlayerBarTapped(object sender, TappedRoutedEventArgs e)
    {
        BarTapped?.Invoke(this, EventArgs.Empty);
    }
}
