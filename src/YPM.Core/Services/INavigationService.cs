namespace YPM.Core.Services;

public enum PageRoute
{
    Home,
    Login,
    Library,
    Search,
    Explore,
    DailyTracks,
    NewAlbum,
    Settings,
    PlaylistDetail,
    AlbumDetail,
    ArtistDetail,
    ArtistMv,
    MvPlayer,
    Lyrics,
    Queue,
    UserDetail,
    SearchType,
}

public interface INavigationService
{
    event EventHandler<PageRoute>? Navigated;

    object? Frame { get; set; }

    bool CanGoBack { get; }

    void Navigate(PageRoute route, object? parameter = null, bool clearBackStack = false);

    void GoBack();

    bool IsLoggedInRequired(PageRoute route);

    void SetLoginRequiredGuard(Func<bool> isLoggedIn, Func<Task> redirectToLogin);
}

public sealed class NavigationEventArgs : EventArgs
{
    public PageRoute Route { get; }
    public object? Parameter { get; }
    public NavigationEventArgs(PageRoute route, object? parameter)
    {
        Route = route;
        Parameter = parameter;
    }
}
