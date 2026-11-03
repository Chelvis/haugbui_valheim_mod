# Builds a Release Thunderstore zip. Stops if any of the four look textures is missing.
# Does not commit art/reference. Output: artifacts/Haugbui-<version>.zip
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$props = Get-Content "Directory.Build.props" -Raw
if ($props -notmatch "<HaugbuiVersion>([^<]+)</HaugbuiVersion>") {
    throw "HaugbuiVersion was not found in Directory.Build.props"
}
$version = $Matches[1]

$textures = @(
    @{ Source = "art\reference\grausten-chest\graustenchest_veins.png"; Name = "graustenchest_veins.png" },
    @{ Source = "art\reference\rune-table\Runetable_d.png"; Name = "Runetable_d.png" },
    @{ Source = "art\reference\collection.png"; Name = "collection.png" },
    @{ Source = "art\reference\delivery.png"; Name = "delivery.png" }
)
foreach ($texture in $textures) {
    if (-not (Test-Path (Join-Path $root $texture.Source))) {
        throw "Missing texture $($texture.Source). The package would ship the vanilla look."
    }
}

dotnet build src/Haugbui/Haugbui.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$stage = Join-Path $root "artifacts\thunderstore-stage"
$plugin = Join-Path $stage "plugins\Haugbui"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $plugin "textures") | Out-Null

$out = Join-Path $root "src\Haugbui\bin\Release"
Copy-Item (Join-Path $out "Haugbui.dll") $plugin
Copy-Item (Join-Path $out "Haugbui.Core.dll") $plugin
Copy-Item (Join-Path $out "Translations") (Join-Path $plugin "Translations") -Recurse
Copy-Item (Join-Path $out "icons") (Join-Path $plugin "icons") -Recurse
foreach ($texture in $textures) {
    Copy-Item (Join-Path $root $texture.Source) (Join-Path $plugin "textures\$($texture.Name)")
}

$manifestPath = Join-Path $root "thunderstore\manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version_number = $version
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage "manifest.json") -Encoding utf8
Copy-Item (Join-Path $root "README.md") (Join-Path $stage "README.md")
Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $stage "CHANGELOG.md")
Copy-Item (Join-Path $root "thunderstore\icon.png") (Join-Path $stage "icon.png")

$zip = Join-Path $root "artifacts\Haugbui-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Packed $zip"
