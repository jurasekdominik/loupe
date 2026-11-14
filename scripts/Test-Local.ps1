param([switch]$Render, [switch]$Ui)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    # Only the existing NuGet cache is available to restore; no network or hosted CI.
    $packageCache = Join-Path $env:USERPROFILE '.nuget/packages'
    dotnet restore Loupe.slnx --source $packageCache --locked-mode -p:NuGetAudit=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed. The required packages must already be cached.' }
    dotnet build Loupe.slnx -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/Loupe.Tests -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Local checks failed.' }
    if ($Render) {
        $smoke = Start-Process -FilePath 'src/Loupe/bin/Release/net10.0-windows/Loupe.exe' -ArgumentList '--smoke-test','--output','artifacts/smoke' -WindowStyle Hidden -PassThru
        if (-not $smoke.WaitForExit(20000)) { Stop-Process -Id $smoke.Id; throw 'Local rendering check timed out.' }
        Get-Content -LiteralPath 'artifacts/smoke/result.json'
        if ($smoke.ExitCode -ne 0) { throw 'Local rendering check failed.' }
    }
    if ($Ui) {
        $uiCheck = Start-Process -FilePath 'src/Loupe/bin/Release/net10.0-windows/Loupe.exe' -ArgumentList '--ui-test','--output','artifacts/ui' -WindowStyle Hidden -PassThru
        if (-not $uiCheck.WaitForExit(20000)) { Stop-Process -Id $uiCheck.Id; throw 'Local UI check timed out.' }
        Get-Content -LiteralPath 'artifacts/ui/result.json'
        if ($uiCheck.ExitCode -ne 0) { throw 'Local UI check failed.' }
    }
}
finally { Pop-Location }
