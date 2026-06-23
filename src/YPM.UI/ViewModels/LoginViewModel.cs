using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Extensions;
using YPM.UI.Services;

namespace YPM.UI.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private PeriodicTimer? _timer;
    private int _qrRetryCount;
    private int _expiryAutoRefreshCount;
    private const int MaxQrRetries = 5;
    private const int MaxExpiryAutoRefreshes = 3;
    private string _status = "准备获取二维码";
    private string _qrImageBase64 = string.Empty;
    private string _welcomeText = "使用网易云音乐 App 扫码登录";
    private string _descriptionText = "首批迁移优先接入二维码登录，手机号和邮箱登录后续补齐。";
    private UserProfile? _currentUser;

    // ── Phone / Captcha login ──────────────────────────────────
    private bool _isPhoneLoginMode;
    private string _phoneNumber = string.Empty;
    private string _countryCode = "86";
    private string _captchaCode = string.Empty;
    private string _captchaStatus = string.Empty;
    private string _loginStatus = string.Empty;
    private bool _isSendingCaptcha;
    private int _captchaCountdown;
    private bool _isLoggingIn;

    public LoginViewModel()
    {
        RefreshQrCodeCommand = new AsyncRelayCommand(RefreshQrCodeAsync, () => CurrentUser is null);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => CurrentUser is not null);
        SwitchToPhoneLoginCommand = new RelayCommand(SwitchToPhoneLogin);
        SwitchToQrLoginCommand = new RelayCommand(SwitchToQrLogin);
        SendCaptchaCommand = new AsyncRelayCommand(SendCaptchaAsync, () => CanSendCaptcha);
        PhoneLoginCommand = new AsyncRelayCommand(PhoneLoginAsync, () => CanPhoneLogin);
    }

    public AsyncRelayCommand RefreshQrCodeCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public RelayCommand SwitchToPhoneLoginCommand { get; }
    public RelayCommand SwitchToQrLoginCommand { get; }
    public AsyncRelayCommand SendCaptchaCommand { get; }
    public AsyncRelayCommand PhoneLoginCommand { get; }

    // ── Shared state ───────────────────────────────────────────

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(ShowStatusVisibility));
            }
        }
    }

    public string QrImageBase64
    {
        get => _qrImageBase64;
        set => SetProperty(ref _qrImageBase64, value);
    }

    public string WelcomeText
    {
        get => _welcomeText;
        set => SetProperty(ref _welcomeText, value);
    }

    public string DescriptionText
    {
        get => _descriptionText;
        set => SetProperty(ref _descriptionText, value);
    }

    public UserProfile? CurrentUser
    {
        get => _currentUser;
        set
        {
            if (SetProperty(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(CurrentUserName));
                OnPropertyChanged(nameof(ShowQrVisibility));
                OnPropertyChanged(nameof(ShowQrActionsVisibility));
                OnPropertyChanged(nameof(ShowLogoutVisibility));
                OnPropertyChanged(nameof(ShowStatusVisibility));
                OnPropertyChanged(nameof(ShowPhoneLoginVisibility));
                OnPropertyChanged(nameof(ShowPhoneLoginActionsVisibility));
                OnPropertyChanged(nameof(ShowLoginModeSwitchVisibility));
                RefreshQrCodeCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CurrentUserName => CurrentUser?.Nickname ?? "未登录";

    // ── QR visibility ──────────────────────────────────────────

    public Visibility ShowQrVisibility =>
        CurrentUser is null && !_isPhoneLoginMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShowQrActionsVisibility =>
        CurrentUser is null && !_isPhoneLoginMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShowLogoutVisibility =>
        CurrentUser is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ShowStatusVisibility =>
        CurrentUser is null && !string.IsNullOrWhiteSpace(Status) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShowLoginModeSwitchVisibility =>
        CurrentUser is null ? Visibility.Visible : Visibility.Collapsed;

    // ── Phone login state ──────────────────────────────────────

    public bool IsPhoneLoginMode
    {
        get => _isPhoneLoginMode;
        set
        {
            if (SetProperty(ref _isPhoneLoginMode, value))
            {
                OnPropertyChanged(nameof(ShowQrVisibility));
                OnPropertyChanged(nameof(ShowQrActionsVisibility));
                OnPropertyChanged(nameof(ShowPhoneLoginVisibility));
                OnPropertyChanged(nameof(ShowPhoneLoginActionsVisibility));
            }
        }
    }

    public string PhoneNumber
    {
        get => _phoneNumber;
        set
        {
            if (SetProperty(ref _phoneNumber, value))
            {
                SendCaptchaCommand.NotifyCanExecuteChanged();
                PhoneLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CountryCode
    {
        get => _countryCode;
        set => SetProperty(ref _countryCode, value);
    }

    public string CaptchaCode
    {
        get => _captchaCode;
        set
        {
            if (SetProperty(ref _captchaCode, value))
            {
                PhoneLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CaptchaStatus
    {
        get => _captchaStatus;
        set => SetProperty(ref _captchaStatus, value);
    }

    public string LoginStatus
    {
        get => _loginStatus;
        set => SetProperty(ref _loginStatus, value);
    }

    public bool IsSendingCaptcha
    {
        get => _isSendingCaptcha;
        set
        {
            if (SetProperty(ref _isSendingCaptcha, value))
            {
                SendCaptchaCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int CaptchaCountdown
    {
        get => _captchaCountdown;
        set
        {
            if (SetProperty(ref _captchaCountdown, value))
            {
                OnPropertyChanged(nameof(CaptchaCountdownText));
                SendCaptchaCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CaptchaCountdownText =>
        _captchaCountdown > 0 ? $"{_captchaCountdown}s 后重发" : "发送验证码";

    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set
        {
            if (SetProperty(ref _isLoggingIn, value))
            {
                PhoneLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility ShowPhoneLoginVisibility =>
        CurrentUser is null && _isPhoneLoginMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShowPhoneLoginActionsVisibility =>
        CurrentUser is null && _isPhoneLoginMode ? Visibility.Visible : Visibility.Collapsed;

    private bool CanSendCaptcha =>
        !_isSendingCaptcha && _captchaCountdown <= 0 && !string.IsNullOrWhiteSpace(_phoneNumber);

    private bool CanPhoneLogin =>
        !_isLoggingIn && !string.IsNullOrWhiteSpace(_phoneNumber) && !string.IsNullOrWhiteSpace(_captchaCode);

    // ── Initialization ─────────────────────────────────────────

    public async Task InitializeAsync()
    {
        StopPolling();

        if (App.Settings.CurrentUser is not null && !string.IsNullOrWhiteSpace(App.Settings.SessionCookie))
        {
            CurrentUser = App.Settings.CurrentUser;
            WelcomeText = $"欢迎回来，{CurrentUser.Nickname}";
            DescriptionText = "当前登录状态已从本地会话恢复。";
            Status = string.Empty;
            QrImageBase64 = string.Empty;

            try
            {
                var currentUser = await App.ApiClient.GetCurrentUserAsync();
                if (currentUser is not null)
                {
                    CurrentUser = currentUser;
                    App.Settings.CurrentUser = currentUser;
                    await App.SettingsService.SaveAsync(App.Settings);
                    await App.SaveAuthStateToCacheAsync();
                    return;
                }
            }
            catch
            {
                // Can't reach the backend — keep the existing session rather than
                // clearing it due to a transient error.
                return;
            }

            // API returned null user — session is genuinely invalid.
            await ClearLoginStateAsync(showLoggedOutMessage: false);
        }

        await RefreshQrCodeAsync();
    }

    // ── Mode switching ─────────────────────────────────────────

    private void SwitchToPhoneLogin()
    {
        StopPolling();
        IsPhoneLoginMode = true;
        Status = string.Empty;
        QrImageBase64 = string.Empty;
        CaptchaStatus = string.Empty;
        LoginStatus = string.Empty;
    }

    private void SwitchToQrLogin()
    {
        IsPhoneLoginMode = false;
        CaptchaStatus = string.Empty;
        LoginStatus = string.Empty;
        _ = RefreshQrCodeAsync();
    }

    // ── Captcha flow ───────────────────────────────────────────

    public async Task SendCaptchaAsync()
    {
        if (_isSendingCaptcha || string.IsNullOrWhiteSpace(_phoneNumber))
        {
            return;
        }

        IsSendingCaptcha = true;
        CaptchaStatus = "正在发送验证码...";
        LoginStatus = string.Empty;

        try
        {
            var result = await App.ApiClient.SendCaptchaAsync(_phoneNumber, _countryCode);
            if (result.Data)
            {
                CaptchaStatus = "验证码已发送";
                StartCaptchaCountdown();
            }
            else
            {
                CaptchaStatus = result.Message ?? "发送失败";
            }
        }
        catch (Exception ex)
        {
            CaptchaStatus = $"发送失败: {ex.Message}";
        }
        finally
        {
            IsSendingCaptcha = false;
        }
    }

    private void StartCaptchaCountdown()
    {
        CaptchaCountdown = 60;
        _ = RunCountdownAsync();
    }

    private async Task RunCountdownAsync()
    {
        while (_captchaCountdown > 0)
        {
            await Task.Delay(1000);
            CaptchaCountdown--;
        }
    }

    public async Task PhoneLoginAsync()
    {
        if (_isLoggingIn || string.IsNullOrWhiteSpace(_phoneNumber) || string.IsNullOrWhiteSpace(_captchaCode))
        {
            return;
        }

        IsLoggingIn = true;
        LoginStatus = "正在登录...";

        try
        {
            var loginResult = await App.ApiClient.LoginCellphoneAsync(
                _phoneNumber, captcha: _captchaCode, countryCode: _countryCode);

            if (loginResult.Code == 200)
            {
                if (!string.IsNullOrWhiteSpace(loginResult.Cookie))
                {
                    App.ApiClient.SetSessionCookie(loginResult.Cookie);
                }
                App.Settings.SessionCookie = App.ApiClient.ExportSessionCookie();
                CurrentUser = await LoadCurrentUserAfterLoginAsync();
                App.Settings.CurrentUser = CurrentUser;
                await App.SettingsService.SaveAsync(App.Settings);
                await App.SaveAuthStateToCacheAsync();

                CaptchaStatus = string.Empty;
                LoginStatus = string.Empty;
                WelcomeText = CurrentUser is null ? "登录成功" : $"欢迎回来，{CurrentUser.Nickname}";
                DescriptionText = CurrentUser is null ? "当前账号信息暂不可用。" : "当前账号已登录，可在此页退出登录。";
                IsPhoneLoginMode = false;
            }
            else
            {
                LoginStatus = loginResult.Message ?? $"登录失败 (code: {loginResult.Code})";
            }
        }
        catch (Exception ex)
        {
            LoginStatus = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    // ── QR code flow ───────────────────────────────────────────

    public async Task RefreshQrCodeAsync()
    {
        StopPolling();
        _qrRetryCount = 0;
        _expiryAutoRefreshCount = 0;
        await GenerateQrCodeAsync();
    }

    private async Task GenerateQrCodeAsync()
    {
        try
        {
            CurrentUser = null;
            WelcomeText = "使用网易云音乐 App 扫码登录";
            DescriptionText = "手机号登录请点击下方\"手机号登录\"。";

            if (_qrRetryCount == 0)
                Status = "正在生成二维码";

            var session = await App.ApiClient.CreateQrLoginSessionAsync();

            if (string.IsNullOrWhiteSpace(session.QrImageBase64))
            {
                await RetryQrAfterDelayAsync("服务端返回空二维码");
                return;
            }

            QrImageBase64 = session.QrImageBase64;
            Status = "请扫码";
            _qrRetryCount = 0;
            StartPolling(session.Key);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            await RetryQrAfterDelayAsync("二维码接口 404");
        }
        catch (Exception ex)
        {
            await RetryQrAfterDelayAsync($"二维码生成失败: {ex.Message}");
        }
    }

    private async Task RetryQrAfterDelayAsync(string reason)
    {
        _qrRetryCount++;
        if (_qrRetryCount >= MaxQrRetries)
        {
            Status = $"{reason}（已重试 {_qrRetryCount} 次，请手动刷新）";
            QrImageBase64 = string.Empty;
            return;
        }

        var delaySeconds = 3 * _qrRetryCount;
        Status = $"{reason}，{delaySeconds}s 后自动重试（{_qrRetryCount}/{MaxQrRetries}）";
        QrImageBase64 = string.Empty;
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        await GenerateQrCodeAsync();
    }

    public async Task LogoutAsync()
    {
        try
        {
            await App.ApiClient.LogoutAsync();
        }
        catch
        {
        }

        await ClearLoginStateAsync(showLoggedOutMessage: true);
        await RefreshQrCodeAsync();
    }

    private void StartPolling(string key)
    {
        StopPolling();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _ = PollAsync(key);
    }

    private async Task PollAsync(string key)
    {
        var timer = _timer;
        if (timer is null)
        {
            return;
        }

        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                var result = await App.ApiClient.CheckQrLoginStatusAsync(key);
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    Status = result.Code switch
                    {
                        800 => "二维码已过期，请刷新",
                        801 => "等待扫码",
                        802 => "已扫码，请在手机确认",
                        803 => "登录成功",
                        _ => string.IsNullOrWhiteSpace(result.Message) ? $"状态码 {result.Code}" : result.Message,
                    };

                    if (result.IsCompleted)
                    {
                        if (!string.IsNullOrWhiteSpace(result.Cookie))
                        {
                            App.ApiClient.SetSessionCookie(result.Cookie.Replace(" HTTPOnly", string.Empty, StringComparison.OrdinalIgnoreCase));
                        }
                        App.Settings.SessionCookie = App.ApiClient.ExportSessionCookie();
                        CurrentUser = await LoadCurrentUserAfterLoginAsync();
                        App.Settings.CurrentUser = CurrentUser;
                        await App.SettingsService.SaveAsync(App.Settings);
                        await App.SaveAuthStateToCacheAsync();
                        QrImageBase64 = string.Empty;
                        Status = string.Empty;
                        WelcomeText = CurrentUser is null ? "登录成功" : $"欢迎回来，{CurrentUser.Nickname}";
                        DescriptionText = CurrentUser is null ? "当前账号信息暂不可用。" : "当前账号已登录，可在此页退出登录。";
                        StopPolling();
                    }
                    else if (result.Code == 800)
                    {
                        StopPolling();
                        if (_expiryAutoRefreshCount < MaxExpiryAutoRefreshes)
                        {
                            _expiryAutoRefreshCount++;
                            Status = $"二维码已过期，即将自动刷新（{_expiryAutoRefreshCount}/{MaxExpiryAutoRefreshes}）";
                            await Task.Delay(2000);
                            await _dispatcherQueue.EnqueueAsync(async () => await GenerateQrCodeAsync());
                        }
                        else
                        {
                            Status = "二维码已多次过期，请检查网络后手动刷新";
                        }
                    }
                });
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            await _dispatcherQueue.EnqueueAsync(() => Status = $"轮询失败: {ex.Message}");
        }
    }

    private void StopPolling()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task<UserProfile?> LoadCurrentUserAfterLoginAsync()
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var currentUser = await App.ApiClient.GetCurrentUserAsync();
                if (currentUser is not null)
                {
                    return currentUser;
                }
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && ex.StatusCode is System.Net.HttpStatusCode.NotFound)
            {
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
            }
        }

        try
        {
            return await App.ApiClient.GetCurrentUserAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task ClearLoginStateAsync(bool showLoggedOutMessage)
    {
        StopPolling();
        App.Settings.CurrentUser = null;
        App.Settings.SessionCookie = string.Empty;
        App.Settings.LastCookieRefreshDay = 0;

        // Stop playback and clear the audio queue.
        if (App.AudioPlayer is not null)
        {
            await App.AudioPlayer.StopAsync();
            App.AudioPlayer.ClearQueue();
        }

        // Clear persisted playback state so the next login doesn't resume an old session.
        App.Settings.LastPlayback = null;

        if (App.LikedSongsService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        App.LikedSongsService = null;

        // Remove cached liked track IDs so they aren't visible to the next user.
        if (App.CacheService is not null)
        {
            await App.CacheService.RemoveAsync(LikedSongsService.LikedIdsCacheKey);
        }

        // Overwrite cache with empty data first so that even if the subsequent
        // removal fails, RestoreAuthStateFromCacheAsync won't resurrect the old session.
        await App.SaveAuthStateToCacheAsync();
        await App.SettingsService.SaveAsync(App.Settings);
        await App.ClearAuthStateCacheAsync();
        App.RecreateApiClient();

        // Navigate to Home page and clear back stack so stale user data is unreachable.
        App.NavigationService?.Navigate(PageRoute.Home, clearBackStack: true);

        CurrentUser = null;
        QrImageBase64 = string.Empty;
        IsPhoneLoginMode = false;
        PhoneNumber = string.Empty;
        CaptchaCode = string.Empty;
        CaptchaStatus = string.Empty;
        LoginStatus = string.Empty;
        WelcomeText = "使用网易云音乐 App 扫码登录";
        DescriptionText = "手机号登录请点击下方\"手机号登录\"。";
        Status = showLoggedOutMessage ? "已退出登录" : "登录状态已失效，请重新扫码";
    }
}
