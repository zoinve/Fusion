using Microsoft.UI.Xaml.Controls;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class FrameNavigationService : INavigationService
{
    private Func<bool>? _isLoggedIn;
    private Func<Task>? _redirectToLogin;
    private readonly Dictionary<PageRoute, Type> _routeMap = new();
    private readonly HashSet<PageRoute> _loginRequiredRoutes = new()
    {
        PageRoute.Library, PageRoute.Settings,
    };

    public event EventHandler<PageRoute>? Navigated;

    public Frame? Frame { get; set; }

    object? INavigationService.Frame
    {
        get => Frame;
        set => Frame = value as Frame;
    }

    public bool CanGoBack => Frame is { CanGoBack: true };

    public void RegisterRoute(PageRoute route, Type pageType)
    {
        _routeMap[route] = pageType;
    }

    public void Navigate(PageRoute route, object? parameter = null, bool clearBackStack = false)
    {
        if (Frame is null) return;

        if (_isLoggedIn is not null && IsLoggedInRequired(route) && !_isLoggedIn())
        {
            _redirectToLogin?.Invoke();
            return;
        }

        if (!_routeMap.TryGetValue(route, out var pageType)) return;

        if (clearBackStack)
        {
            Frame.BackStack.Clear();
        }

        Frame.Navigate(pageType, parameter);
        Navigated?.Invoke(this, route);
    }

    public void GoBack()
    {
        if (Frame is { CanGoBack: true })
        {
            Frame.GoBack();
        }
    }

    public bool IsLoggedInRequired(PageRoute route) => _loginRequiredRoutes.Contains(route);

    public void SetLoginRequiredGuard(Func<bool> isLoggedIn, Func<Task> redirectToLogin)
    {
        _isLoggedIn = isLoggedIn;
        _redirectToLogin = redirectToLogin;
    }
}
