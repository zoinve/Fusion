using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using YPM.UI.Helpers;
using YPM.UI.ViewModels;

namespace YPM.UI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();
    public LoginViewModel LoginVM { get; } = new();

    public string SettingsGlyph => IconGlyph.Settings;
    public string UserGlyph => IconGlyph.User;
    public string GlobeGlyph => IconGlyph.Globe;
    public string PaletteGlyph => IconGlyph.Palette;
    public string HomeGlyph => IconGlyph.Home;
    public string SpeakerGlyph => IconGlyph.Speaker;
    public string WifiGlyph => IconGlyph.Wifi;
    public string InfoGlyph => IconGlyph.Info;
    public string StorageGlyph => IconGlyph.Storage;
    public string FolderGlyph => IconGlyph.Folder;
    public string SizeGlyph => IconGlyph.Edit;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadFromSettings();
        await LoginVM.InitializeAsync();
        await ViewModel.RefreshCacheSizeAsync();
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除音乐缓存",
            Content = "确定要清除所有已缓存的音乐文件吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ClearCacheAsync();
        }
    }

    private async void OnPickCacheFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.UpdateCacheLocationAsync(folder.Path);
        }
    }

    private void OnCacheLimitChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (CacheLimitLabel is not null)
        {
            CacheLimitLabel.Text = $"{(int)e.NewValue} MB";
        }
    }
}
