#requires -Version 5.1
# Validate a signed copy of a Store candidate. Never modify the upload package.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$BaselinePackagePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ExpectedUserSid,
    [switch]$RunWack
)
$ErrorActionPreference = 'Stop'
Start-Transcript -LiteralPath ([IO.Path]::GetFullPath($OutputDirectory) + '.bootstrap.log') -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($identity.User.Value -ne $ExpectedUserSid) { throw 'Run as the same Windows user, not another administrator account.' }
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an elevated Windows PowerShell session.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$baseline = (Resolve-Path -LiteralPath $BaselinePackagePath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory for each validation run.' }
function Read-Identity([string]$Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$xml = $reader.ReadToEnd(); return $xml.Package.Identity }
        finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
$targetIdentity = Read-Identity $package
$baselineIdentity = Read-Identity $baseline
$name = 'Mablook.HYPNIX'
$publisher = 'CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72'
$family = 'Mablook.HYPNIX_pc2mes91x5ay8'
foreach ($item in @($targetIdentity, $baselineIdentity)) {
    if ($item.Name -ne $name -or $item.Publisher -ne $publisher -or $item.ProcessorArchitecture -ne 'x64') {
        throw 'Unexpected package identity; refusing to sign or install.'
    }
}
if ([version]$baselineIdentity.Version -ge [version]$targetIdentity.Version) { throw 'Baseline must be older than the candidate.' }
if (Get-AppxPackage -Name $name) { throw 'HYPNIX is already installed as MSIX; refusing to replace an existing installation.' }
if (Get-Process HYPNIX -ErrorAction SilentlyContinue) { throw 'Quit the running HYPNIX application before this test.' }
$packageData = Join-Path $env:LOCALAPPDATA "Packages/$family"
if (Test-Path -LiteralPath $packageData) { throw 'Existing HYPNIX package data found; use a clean test account to preserve it.' }
$sdk = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin' -Directory |
    Where-Object Name -Match '^10\.' | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK SignTool is required.' }
$appcert = 'C:/Program Files (x86)/Windows Kits/10/App Certification Kit/appcert.exe'
if ($RunWack -and !(Test-Path -LiteralPath $appcert)) { throw 'Windows App Certification Kit is required.' }
New-Item -ItemType Directory -Path $output | Out-Null
$results = New-Object 'Collections.Generic.List[object]'
$state = [ordered]@{ Started = [DateTimeOffset]::UtcNow.ToString('o'); Completed = $null; Passed = $false;
    Package = $package; OriginalSha256 = (Get-FileHash -LiteralPath $package).Hash;
    Steps = $results; Failure = $null; CleanupErrors = @(); WackRequested = [bool]$RunWack;
    StoreCommerceValidated = $false; CleanMachineValidated = $false }
function Save-State { $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8 }
function Passed([string]$Step, $Details) {
    $results.Add([ordered]@{ Check = $Step; Passed = $true; Details = $Details; Time = [DateTimeOffset]::UtcNow.ToString('o') })
    Save-State
}
$data = Join-Path $env:LOCALAPPDATA 'HYPNIX'
$before = @()
$cert = $null
$trust = $null
$installedByTest = $false
$appProcessId = 0
$wackProcess = $null
Save-State
try {
    if (Test-Path -LiteralPath $data) {
        Copy-Item -LiteralPath $data -Destination (Join-Path $output 'user-data-backup') -Recurse
        $before = @(Get-ChildItem -LiteralPath $data -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($data.Length + 1)
            $hash = (Get-FileHash -LiteralPath $_.FullName).Hash
            if ((Get-FileHash -LiteralPath (Join-Path $output "user-data-backup/$relative")).Hash -ne $hash) { throw "Backup mismatch: $relative" }
            [pscustomobject]@{ Relative = $relative; Hash = $hash }
        })
    }
    Passed 'Verified backup of existing HYPNIX user data' $before.Count
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -KeyUsage DigitalSignature `
        -CertStoreLocation 'Cert:/CurrentUser/My' -NotAfter (Get-Date).AddDays(2) `
        -FriendlyName 'HYPNIX temporary lifecycle validation' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    $publicCert = Join-Path $output 'temporary-test.cer'
    Export-Certificate -Cert $cert -FilePath $publicCert | Out-Null
    $trust = Import-Certificate -FilePath $publicCert -CertStoreLocation 'Cert:/LocalMachine/TrustedPeople'
    foreach ($pair in @(@($baseline, 'baseline.msix'), @($package, 'candidate.msix'))) {
        $copy = Join-Path $output $pair[1]
        Copy-Item -LiteralPath $pair[0] -Destination $copy
        & $sdk sign /fd SHA256 /sha1 $cert.Thumbprint $copy *> (Join-Path $output ($pair[1] + '.sign.log'))
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $($pair[1])" }
        & $sdk verify /pa /v $copy *> (Join-Path $output ($pair[1] + '.verify.log'))
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $($pair[1])" }
    }
    Passed 'Signed and verified test copies' $cert.Thumbprint
    Add-AppxPackage -Path (Join-Path $output 'baseline.msix')
    $installedByTest = $true
    $installed = Get-AppxPackage -Name $name
    if (!$installed -or $installed.Version -ne $baselineIdentity.Version) { throw 'Baseline installation version mismatch.' }
    Passed 'Baseline MSIX installation' $installed.PackageFullName
    $canaryDirectory = Join-Path $packageData 'LocalState'
    New-Item -ItemType Directory -Path $canaryDirectory -Force | Out-Null
    $canary = Join-Path $canaryDirectory 'hypnix-upgrade-validation.txt'
    [Guid]::NewGuid().ToString() | Set-Content -LiteralPath $canary
    $canaryHash = (Get-FileHash -LiteralPath $canary).Hash
    Add-AppxPackage -Path (Join-Path $output 'candidate.msix')
    $installed = Get-AppxPackage -Name $name
    if (!$installed -or $installed.Version -ne $targetIdentity.Version -or $installed.Status -ne 'Ok') { throw 'Candidate installation is not healthy.' }
    if ((Get-FileHash -LiteralPath $canary).Hash -ne $canaryHash) { throw 'MSIX update changed LocalState data.' }
    Passed 'MSIX upgrade preserves LocalState' $installed.PackageFullName

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class HypnixPackageLaunch {
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivation {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint processId);
    }
    public static uint Launch(string id) {
        object manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        try { uint pid; Marshal.ThrowExceptionForHR(((IActivation)manager).ActivateApplication(id, null, 0, out pid)); return pid; }
        finally { Marshal.ReleaseComObject(manager); }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
    public static string PackageName(uint pid) {
        IntPtr process = OpenProcess(0x1000, false, pid);
        if(process == IntPtr.Zero) throw new Exception("Cannot inspect launched process.");
        try { uint size=0; int error=GetPackageFullName(process,ref size,null);
            if(error!=122) throw new Exception("Launched process has no package identity: " + error);
            var name=new StringBuilder((int)size); error=GetPackageFullName(process,ref size,name);
            if(error!=0) throw new Exception("Package identity read failed: " + error); return name.ToString(); }
        finally { CloseHandle(process); }
    }
}
'@
    $appProcessId = [HypnixPackageLaunch]::Launch($family + '!HYPNIX')
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $app = Get-Process -Id $appProcessId -ErrorAction Stop
        if ($app.MainWindowHandle -ne 0 -and $app.Responding) { break }
    } while ((Get-Date) -lt $deadline)
    if ($app.MainWindowHandle -eq 0 -or !$app.Responding) { throw 'Installed MSIX did not open a responsive window.' }
    $actualPackage = [HypnixPackageLaunch]::PackageName($appProcessId)
    if ($actualPackage -ne $installed.PackageFullName) { throw 'Launched process has the wrong package identity.' }
    Start-Sleep -Seconds 10
    $app.Refresh()
    if ($app.HasExited -or !$app.Responding) { throw 'MSIX startup did not remain healthy.' }
    Passed 'MSIX activation with real package identity and responsive window' $actualPackage
    Stop-Process -Id $appProcessId -Force
    $appProcessId = 0

    if ($RunWack) {
        $wackReport = Join-Path $output 'wack.xml'
        # Reset an earlier certification job and explicitly use the signed test copy.
        # Process.Start retains its handle, unlike Start-Process in Windows PowerShell
        # where ExitCode can remain null after WaitForExit.
        foreach ($job in @('reset','test')) {
            $start = New-Object Diagnostics.ProcessStartInfo
            $start.FileName = $appcert
            $start.WorkingDirectory = Split-Path $appcert -Parent
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.Arguments = if ($job -eq 'reset') { 'reset' } else {
                'test -appxpackagepath "' + (Join-Path $output 'candidate.msix') + '" -reportoutputpath "' + $wackReport + '"'
            }
            $wackProcess = [Diagnostics.Process]::Start($start)
            $stdout = $wackProcess.StandardOutput.ReadToEndAsync()
            $stderr = $wackProcess.StandardError.ReadToEndAsync()
            if (!$wackProcess.WaitForExit(1800000)) { throw 'WACK exceeded 30 minutes; report is incomplete.' }
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "wack-$job.stdout.log")
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "wack-$job.stderr.log")
            $state.WackExitCode = $wackProcess.ExitCode
            Save-State
            if ($wackProcess.ExitCode -ne 0) { throw "WACK $job failed with exit code $($wackProcess.ExitCode)." }
        }
        $state.WackReport = $wackReport
        Save-State
        if ($wackProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $wackReport)) { throw 'WACK did not complete successfully; inspect its logs.' }
        [xml]$certification = Get-Content -LiteralPath $wackReport -Raw
        $state.WackOverallResult = $certification.REPORT.OVERALL_RESULT
        $state.WackPartialRun = $certification.REPORT.PARTIAL_RUN
        $state.WackNonPassingTests = @($certification.SelectNodes('//TEST') | Where-Object { $_.RESULT.InnerText -ne 'PASS' } |
            ForEach-Object { [ordered]@{ Name = $_.NAME; Optional = $_.OPTIONAL; Result = $_.RESULT.InnerText } })
        Save-State
        if ($certification.REPORT.APP_NAME -ne $name -or $certification.REPORT.APP_VERSION -ne $targetIdentity.Version `
            -or $certification.REPORT.OVERALL_RESULT -ne 'PASS' -or $certification.REPORT.PARTIAL_RUN -ne 'FALSE') {
            throw 'WACK report is partial, belongs to another package, or did not pass.'
        }
        Passed 'WACK overall result passed; inspect individual optional findings' $wackReport
    }
    if ((Get-FileHash -LiteralPath $package).Hash -ne $state.OriginalSha256) { throw 'Original upload package changed.' }
} catch {
    $state.Failure = $_.Exception.ToString()
} finally {
    try {
        if ($wackProcess -and !$wackProcess.HasExited) { Stop-Process -Id $wackProcess.Id -Force }
        if ($appProcessId -gt 0) {
            $owned = Get-Process -Id $appProcessId -ErrorAction SilentlyContinue
            if ($owned -and [HypnixPackageLaunch]::PackageName($appProcessId) -like ($name + '_*')) { Stop-Process -Id $appProcessId -Force }
        }
        if ($installedByTest) {
            # WACK may activate the package again. Stop only processes carrying this exact identity.
            foreach ($process in @(Get-Process HYPNIX -ErrorAction SilentlyContinue)) {
                if ([HypnixPackageLaunch]::PackageName($process.Id) -eq $installed.PackageFullName) { Stop-Process -Id $process.Id -Force }
            }
            Get-AppxPackage -Name $name | Where-Object Publisher -EQ $publisher | Remove-AppxPackage
            if (Get-AppxPackage -Name $name) { throw 'Test MSIX uninstall left an installed package.' }
            Passed 'MSIX uninstall' $name
        }
    } catch { $state.CleanupErrors += $_.Exception.Message }
    foreach ($item in $before) {
        # App logs can grow on launch; all other pre-existing files must survive unchanged.
        if ($item.Relative -like 'logs\*') { continue }
        $path = Join-Path $data $item.Relative
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $item.Hash) {
            $state.CleanupErrors += "Existing user data changed: $($item.Relative); verified backup retained."
        }
    }
    try { if ($trust) { Remove-Item -LiteralPath $trust.PSPath } } catch { $state.CleanupErrors += $_.Exception.Message }
    try { if ($cert) { Remove-Item -LiteralPath $cert.PSPath -DeleteKey } } catch { $state.CleanupErrors += $_.Exception.Message }
    $state.Completed = [DateTimeOffset]::UtcNow.ToString('o')
    $state.Passed = !$state.Failure -and $state.CleanupErrors.Count -eq 0
    Save-State
}
if (!$state.Passed) { Write-Error "MSIX validation incomplete or failed. See $output/result.json"; exit 1 }
Write-Output "PASS: MSIX lifecycle and requested WACK checks. Real Store commerce testing remains separate: $output"
