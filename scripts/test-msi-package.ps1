#requires -Version 7.0
# Read-only gate. Run before installation and after all MSI modifications/signing.
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    $database = $installer.OpenDatabase((Resolve-Path -LiteralPath $Path).Path, 0)
    function Read-MsiRow([string]$Sql, [int]$Columns = 1) {
        $view = $database.OpenView($Sql)
        $record = $null
        try {
            [void]$view.Execute()
            $record = $view.Fetch()
            if ($record) { for ($column = 1; $column -le $Columns; $column++) { $record.StringData($column) } }
        } finally {
            if ($record) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
            [void]$view.Close()
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
    }
    foreach ($property in @('RustAppId', 'ApplicationFolderName')) {
        if ((Read-MsiRow "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$property'") -ne 'HypnixWallpaper') {
            throw "Unsafe MSI: $property must be HypnixWallpaper, separate from HYPNIX user data."
        }
    }
    $directory = @(Read-MsiRow "SELECT ``Directory_Parent``, ``DefaultDir`` FROM ``Directory`` WHERE ``Directory`` = 'INSTALLFOLDER'" 2)
    if ($directory.Count -ne 2 -or $directory[0] -ne 'LocalAppDataFolder' -or $directory[1] -ne 'HypnixWallpaper') {
        throw 'Unsafe MSI: silent install must default to LocalAppDataFolder\HypnixWallpaper.'
    }
    if ((Read-MsiRow "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ALLUSERS'")) {
        throw 'Expected a per-user MSI, but ALLUSERS is set.'
    }
    # The existing custom destination must still be applied before directory costing.
    $override = @(Read-MsiRow "SELECT ``Condition``, ``Sequence`` FROM ``InstallExecuteSequence`` WHERE ``Action`` = 'SetINSTALLFOLDER'" 2)
    if ($override.Count -ne 2 -or $override[0] -ne 'VELOPACK_INSTALLDIR' -or [int]$override[1] -ge 1000) {
        throw 'MSI custom install directory override is missing or runs too late.'
    }
    Write-Output 'PASS: MSI per-user destination, data isolation and directory override.'
} finally {
    if ($database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
