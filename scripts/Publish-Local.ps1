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
    Compress-Archive -Path 'artifacts/Loupe/*' -DestinationPath artifacts/Loupe-win-x64.zip -Force
    Write-Output 'Ready: artifacts/Loupe/Loupe.exe'
    Write-Output 'Portable archive: artifacts/Loupe-win-x64.zip (.NET 10 Desktop Runtime required)'
}
finally { Pop-Location }
