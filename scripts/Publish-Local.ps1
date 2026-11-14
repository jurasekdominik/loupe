$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    $packageCache = Join-Path $env:USERPROFILE '.nuget/packages'
    dotnet restore src/Loupe/Loupe.csproj --source $packageCache --locked-mode -p:NuGetAudit=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed. The required packages must already be cached.' }
    dotnet publish src/Loupe/Loupe.csproj -c Release --no-restore --self-contained false -o artifacts/Loupe --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.txt -Destination artifacts/Loupe
    Copy-Item -LiteralPath 'src/Loupe/loupe.ico' -Destination artifacts/Loupe
    # Remove obsolete icon extras left by publishing over an earlier build.
    foreach ($obsolete in @('icon-concepts.png', 'Icons/Focus.ico', 'Icons/PixelLens.ico', 'Icons/PixelStack.ico')) {
        $obsoletePath = Join-Path 'artifacts/Loupe' $obsolete
        if (Test-Path -LiteralPath $obsoletePath -PathType Leaf) { Remove-Item -LiteralPath $obsoletePath }
    }
    $oldIconDirectory = Join-Path $workspace 'artifacts/Loupe/Icons'
    if ((Test-Path -LiteralPath $oldIconDirectory -PathType Container) -and @(Get-ChildItem -LiteralPath $oldIconDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $oldIconDirectory
    }
    Compress-Archive -Path 'artifacts/Loupe/*' -DestinationPath artifacts/Loupe-win-x64.zip -Force
    Write-Output 'Ready: artifacts/Loupe/Loupe.exe'
    Write-Output 'Portable archive: artifacts/Loupe-win-x64.zip (.NET 10 Desktop Runtime required)'
}
finally { Pop-Location }
