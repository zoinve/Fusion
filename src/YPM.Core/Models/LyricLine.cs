using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YPM.Core.Models;

public sealed class LyricLine : INotifyPropertyChanged
{
    private bool _isHighlighted;

    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
