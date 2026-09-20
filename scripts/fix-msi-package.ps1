#requires -Version 7.0
# Velopack 1.2.0 leaves INSTALLFOLDER under TARGETDIR. Its UI supplies a path, but /qn skips
# that initialization and installs to C:\HYPNIX. Give Windows Installer a real per-user default.
# It also uses packTitle as RustAppId, making uninstall delete %LocalAppData%\HYPNIX user data.
# Correct both defects BEFORE signing. The existing VELOPACK_INSTALLDIR override remains supported.
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $Path).Path
if ((Get-AuthenticodeSignature -LiteralPath $msi).Status -ne 'NotSigned') {
    throw 'Set the MSI install directory before signing the package.'
}
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    $database = $installer.OpenDatabase($msi, 1)
    function Invoke-MsiStatement([string]$Sql) {
        $view = $database.OpenView($Sql)
        try { $view.Execute() } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    }
    $identityView = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ApplicationFolderName'")
    try {
        $identityView.Execute()
        $identity = $identityView.Fetch()
        if (!$identity -or $identity.StringData(1) -ne 'HypnixWallpaper') { throw 'Not a HYPNIX Velopack package.' }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($identity)
    } finally { $identityView.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($identityView) }
    $view = $database.OpenView("SELECT ``Directory_Parent``, ``DefaultDir`` FROM ``Directory`` WHERE ``Directory`` = 'INSTALLFOLDER'")
    try {
        $view.Execute()
        $record = $view.Fetch()
        if (!$record -or $record.StringData(1) -ne 'TARGETDIR' -or $record.StringData(2) -ne 'HYPNIX') {
            throw 'Unexpected MSI layout; review the directory workaround for this Velopack version.'
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
    } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    Invoke-MsiStatement "INSERT INTO ``Directory`` (``Directory``, ``Directory_Parent``, ``DefaultDir``) VALUES ('LocalAppDataFolder', 'TARGETDIR', '.')"
    Invoke-MsiStatement "UPDATE ``Directory`` SET ``Directory_Parent`` = 'LocalAppDataFolder', ``DefaultDir`` = 'HypnixWallpaper' WHERE ``Directory`` = 'INSTALLFOLDER'"
    Invoke-MsiStatement "UPDATE ``Property`` SET ``Value`` = 'HypnixWallpaper' WHERE ``Property`` = 'RustAppId'"
    $database.Commit()
} finally {
    if ($database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
& (Join-Path $PSScriptRoot 'test-msi-package.ps1') -Path $msi
