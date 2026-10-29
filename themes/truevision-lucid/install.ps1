param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$cli = (Get-Command spicetify -ErrorAction Stop).Source
$configPath = (& $cli -c | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $configPath)) { throw 'Cannot locate Spicetify configuration.' }
$root = Split-Path $configPath
$backup = Join-Path $root ('TempestBackups/truevision-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $configPath -Destination (Join-Path $backup 'config-xpui.ini')
$extension = 'tempest-truevision.js'
$target = Join-Path $root ('Extensions/' + $extension)
if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $extension) }
if ($Uninstall) {
    & $cli config extensions ($extension + '-')
} else {
    $css = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'truevision.css'))
    $encoded = ConvertTo-Json -InputObject $css -Compress
    $js = '(() => { const style = document.createElement("style"); style.id = "tempest-truevision"; style.textContent = ' + $encoded + '; document.head.appendChild(style); })();'
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    [IO.File]::WriteAllText($target, $js)
    & $cli config extensions $extension
}
if ($LASTEXITCODE -ne 0) { throw 'Could not update extension configuration.' }
& $cli apply
if ($LASTEXITCODE -ne 0) { throw "Apply failed. Configuration backup: $backup" }
& $cli restart
if ($LASTEXITCODE -ne 0) { throw 'Theme applied, but Spotify restart failed.' }
