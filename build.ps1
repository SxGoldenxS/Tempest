param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [ValidatePattern('^0\.3\.\d+$')][string]$Version = '0.3.0')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/Tempest.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish src/Tempest/Tempest.csproj -c Release -r $Runtime --self-contained true -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "dist/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host "Built dist/$Runtime/Tempest.exe. Use --demo to try it without changing Spotify."
} finally { Pop-Location }
