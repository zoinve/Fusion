using YPM.Core.Mvvm;

namespace YPM.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private string _title = "Fusion";
    private string _status = "WinUI 3 migration shell";

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
