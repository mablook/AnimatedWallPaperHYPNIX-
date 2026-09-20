using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Services.Store;

namespace AnimatedWallPaper.Services;

internal sealed class MicrosoftStoreLicenseProvider : IAppLicenseProvider
{
    internal const string StoreId = "9MTRP976K91M";
    private StoreContext? _context;
    public bool IsStoreManaged { get; } = HasPackageIdentity();
    public event Action? LicenseChanged;
    public void Initialize(IntPtr owner)
    {
        if (!IsStoreManaged || _context is not null) return;
        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, owner);
        context.OfflineLicensesChanged += OnLicenseChanged;
        _context = context;
    }
    private StoreContext Context => _context ?? throw new InvalidOperationException("The Store context is not initialized.");
    private void OnLicenseChanged(StoreContext sender, object args) => LicenseChanged?.Invoke();
    public async Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken cancellationToken)
    {
        var license = await Context.GetAppLicenseAsync().AsTask(cancellationToken);
        return AppLicenseSnapshot.FromStore(license.IsActive, license.IsTrial, license.ExpirationDate, DateTimeOffset.UtcNow);
    }
    public async Task<string?> GetPriceAsync(CancellationToken cancellationToken)
    {
        var result = await Context.GetStoreProductForCurrentAppAsync().AsTask(cancellationToken);
        if (result.ExtendedError is not null) throw result.ExtendedError;
        return result.Product?.Price.FormattedPrice;
    }
    public async Task<AppPurchaseResult> PurchaseAsync(CancellationToken cancellationToken)
    {
        var result = await Context.RequestPurchaseAsync(StoreId).AsTask(cancellationToken);
        if (result.ExtendedError is not null) AppLog.WriteException("Microsoft Store purchase result", result.ExtendedError);
        return result.Status switch {
            StorePurchaseStatus.Succeeded => AppPurchaseResult.Purchased,
            StorePurchaseStatus.AlreadyPurchased => AppPurchaseResult.AlreadyOwned,
            StorePurchaseStatus.NotPurchased => AppPurchaseResult.Cancelled,
            StorePurchaseStatus.NetworkError => AppPurchaseResult.NetworkError,
            _ => AppPurchaseResult.Error
        };
    }
    public void Dispose()
    {
        if (_context is not null) _context.OfflineLicensesChanged -= OnLicenseChanged;
        _context = null;
    }
    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        if (result == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE: existing direct-install channel.
        if (result is 0 or 122) return true;
        // Never treat a failed identity lookup as an unlicensed direct installation.
        AppLog.WriteException("Package identity lookup", new Win32Exception(result));
        return true;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
