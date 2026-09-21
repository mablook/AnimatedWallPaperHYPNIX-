using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AnimatedWallPaper.Services;

internal sealed record LicenseState(DateTimeOffset TrialStartedAt, DateTimeOffset LastSeenAt, string InstallationId)
{
    public string? Key { get; init; }
    public string? InstanceId { get; init; }
    public string? Scope { get; init; }
    public DateTimeOffset? VerifiedUntil { get; init; }
}
internal interface ILicenseStateStore
{
    LicenseState? Load();
    void Save(LicenseState state);
}
internal sealed class LicenseStateStore(string path) : ILicenseStateStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HYPNIX/licensing/v1");
    public LicenseState? Load()
    {
        if (!File.Exists(path)) return null;
        // Corrupt or copied state must not silently become a fresh trial.
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Invalid license state.");
        var data = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<LicenseState>(data) ?? throw new InvalidDataException("Invalid license state."); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public void Save(LicenseState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
}
