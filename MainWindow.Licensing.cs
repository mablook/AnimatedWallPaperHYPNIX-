using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow
{
    private readonly AppLicenseService _license;
    private readonly DispatcherTimer _licenseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _licenseInitialized;
    private DateTimeOffset _lastLicenseCheck;
    private AppLicenseKind _shownLicenseKind;
    private bool LicenseGateVisible => _license.Snapshot.Kind is AppLicenseKind.Expired or AppLicenseKind.NotOwned or AppLicenseKind.Unavailable;

    private async Task InitializeLicenseAsync()
    {
        if (_licenseInitialized || _isQuitting) return;
        _licenseInitialized = true;
        _license.Initialize(new WindowInteropHelper(this).Handle);
        if (!_license.IsStoreManaged) return;
        _licenseTimer.Tick += LicenseTimer_Tick;
        _licenseTimer.Start();
        _lastLicenseCheck = DateTimeOffset.UtcNow;
        await Task.WhenAll(_license.RefreshAsync(), _license.RefreshPriceAsync());
    }
    private void LicenseTimer_Tick(object? sender, EventArgs e)
    {
        // Runs while the main window is hidden in the tray, and after resume from sleep.
        _license.EvaluateTime();
        if (DateTimeOffset.UtcNow - _lastLicenseCheck >= TimeSpan.FromMinutes(5))
        {
            _lastLicenseCheck = DateTimeOffset.UtcNow;
            _ = _license.RefreshAsync();
        }
    }
    private void OnStoreLicenseChanged()
    {
        if (_isQuitting || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(async () => {
            if (!_isQuitting) await _license.RefreshAsync(force: true);
        }));
    }
    private async void RefreshLicenseOnActivate(object? sender, EventArgs e)
    {
        if (!_licenseInitialized || _isQuitting || DateTimeOffset.UtcNow - _lastLicenseCheck < TimeSpan.FromSeconds(10)) return;
        _license.EvaluateTime();
        _lastLicenseCheck = DateTimeOffset.UtcNow;
        await _license.RefreshAsync();
    }
    private void UpdateLicenseUi()
    {
        if (!_isUiInitialized || _isQuitting) return;
        var kind = _license.Snapshot.Kind;
        var previous = _shownLicenseKind;
        _shownLicenseKind = kind;
        if (!_license.CanPlay)
        {
            if (_playRequested || _changingWallpaper || _wallpaperController.IsRunning) StopWallpapers(persist: false);
            _settingsWindow?.Close();
        }
        if (kind == AppLicenseKind.Expired && previous == AppLicenseKind.Trial && !IsVisible)
            _trayIcon?.ShowBalloonTip(4000, "Your HYPNIX trial has ended", "Your settings are saved. Open HYPNIX to purchase the full app.", System.Windows.Forms.ToolTipIcon.Info);

        var title = kind switch {
            AppLicenseKind.Trial => _license.DaysRemaining == 1 ? "1 day left in your free trial" : $"{_license.DaysRemaining} days left in your free trial",
            AppLicenseKind.Owned => "HYPNIX is yours",
            AppLicenseKind.Expired => "Your trial has ended",
            AppLicenseKind.NotOwned => "Unlock HYPNIX",
            AppLicenseKind.Unavailable => "We couldn't verify your license",
            _ => "Checking your license…"
        };
        var detail = kind switch {
            AppLicenseKind.Trial => "All wallpapers and features included. No subscription.",
            AppLicenseKind.Owned => "Full version · One-time purchase · No subscription",
            AppLicenseKind.Expired => "Your animated wallpaper has stopped. Buy HYPNIX to keep using every feature. Your wallpapers, presets and settings are saved.",
            AppLicenseKind.NotOwned => "Purchase the full app, or check your license if you already own HYPNIX. Use the Microsoft Store account you purchased with.",
            AppLicenseKind.Unavailable => "Connect to the internet and sign in to Microsoft Store, then check your license again. Your settings are safe.",
            _ => "Connecting to Microsoft Store. Your settings are safe."
        };
        var canOfferPurchase = kind is AppLicenseKind.Trial or AppLicenseKind.Expired or AppLicenseKind.NotOwned;
        var buyLabel = _license.IsPurchasing ? "Opening Store…" : _license.FormattedPrice is { } price ? $"Buy HYPNIX · {price}" : "Buy HYPNIX";
        foreach (var button in new[] { LicenseBuyButton, LicenseSettingsBuyButton, LicenseGateBuyButton })
        {
            button.Content = buyLabel;
            button.Visibility = canOfferPurchase ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = !_license.IsPurchasing && !_license.IsRefreshing;
        }
        foreach (var button in new[] { LicenseCheckButton, LicenseGateCheckButton })
        {
            button.Content = _license.IsRefreshing ? "Checking…" : "Check license";
            button.IsEnabled = !_license.IsPurchasing && !_license.IsRefreshing;
        }
        LicenseBanner.Visibility = kind is AppLicenseKind.Trial or AppLicenseKind.Checking ? Visibility.Visible : Visibility.Collapsed;
        LicenseBannerTitle.Text = LicenseSettingsTitle.Text = LicenseGateTitle.Text = title;
        LicenseBannerDetail.Text = LicenseSettingsDetail.Text = LicenseGateDetail.Text = detail;
        if (!string.IsNullOrEmpty(_license.Notice)) LicenseBannerDetail.Text = _license.Notice;
        if (kind == AppLicenseKind.Trial && _license.Snapshot.ExpiresAt is { } expiry)
            LicenseSettingsDetail.Text += $"\nTrial ends {expiry.ToLocalTime():d}.";
        LicenseNoticeText.Text = LicenseGateNotice.Text = _license.Notice ?? "";
        LicenseGateBenefits.Visibility = canOfferPurchase ? Visibility.Visible : Visibility.Collapsed;
        LicenseSettingsCard.Visibility = _license.IsStoreManaged ? Visibility.Visible : Visibility.Collapsed;
        if (previous != kind && kind == AppLicenseKind.Owned && previous is AppLicenseKind.Trial or AppLicenseKind.Expired or AppLicenseKind.NotOwned)
            _appSettingsOpen = true;
        UpdateLayoutMode(); UpdateStatus();
    }
    private async void BuyLicense_Click(object sender, RoutedEventArgs e)
    {
        await _license.PurchaseAsync();
    }
    private async void CheckLicense_Click(object sender, RoutedEventArgs e)
    {
        _lastLicenseCheck = DateTimeOffset.UtcNow;
        _license.Initialize(new WindowInteropHelper(this).Handle);
        await Task.WhenAll(_license.RefreshAsync(force: true), _license.RefreshPriceAsync());
    }
}
