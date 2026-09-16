# Builds the release zip:
#   - a single self-contained ZmkOverlay.exe (users do not need to install .NET)
#   - the sample data next to it
#   - README.md, README.ja.md, LICENSE and THIRD-PARTY-NOTICES.md
#   - licenses\ for the .NET runtime that the exe bundles
#   - the images and docs\configuration.md that the READMEs link to
#
#   powershell -File tools\publish.ps1
#
# Output: artifacts\ZmkOverlay-<version>-win-x64.zip
# The version comes from <Version> in src\ZmkOverlay.App\ZmkOverlay.App.csproj.
#
# ASCII only on purpose. PowerShell 5.1 reads a BOM-less .ps1 as ANSI.

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\ZmkOverlay.App\ZmkOverlay.App.csproj'
$out     = Join-Path $root 'artifacts'

# The dev machine keeps the .NET 10 SDK under the user profile; use it if present.
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$version = (& $dotnet msbuild $project -nologo -getProperty:Version | Out-String).Trim()
if (-not $version) { throw 'Could not read <Version> from the project.' }

$name    = "ZmkOverlay-$version-win-x64"
$staging = Join-Path $out $name
$zip     = Join-Path $out "$name.zip"

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# A config.json next to the exe would stop the setup guide from opening on first run.
$stray = Join-Path $staging 'config.json'
if (Test-Path $stray) { Remove-Item $stray -Force }

foreach ($doc in 'README.md', 'README.ja.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item (Join-Path $root $doc) $staging
}

# The READMEs link to these. Without them the images are broken in the unzipped copy.
$docs = Join-Path $staging 'docs'
New-Item -ItemType Directory -Force (Join-Path $docs 'images') | Out-Null
Copy-Item (Join-Path $root 'docs\configuration.md') $docs
Copy-Item (Join-Path $root 'docs\images\*.png') (Join-Path $docs 'images')

# The single exe bundles the .NET runtime (MIT). Ship its license and notices with it.
$runtime = (& $dotnet msbuild $project -nologo -getProperty:BundledNETCoreAppPackageVersion `
    -p:RuntimeIdentifier=win-x64 -p:SelfContained=true | Out-String).Trim()
if (-not $runtime) { throw 'Could not read the bundled .NET runtime version.' }

$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$licenses = Join-Path $staging 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null

$notices = @(
    @{ From = "microsoft.netcore.app.runtime.win-x64\$runtime\LICENSE.TXT";              To = 'dotnet-runtime-LICENSE.txt' },
    @{ From = "microsoft.netcore.app.runtime.win-x64\$runtime\THIRD-PARTY-NOTICES.TXT";  To = 'dotnet-runtime-THIRD-PARTY-NOTICES.txt' },
    @{ From = "microsoft.windowsdesktop.app.runtime.win-x64\$runtime\LICENSE";           To = 'dotnet-windowsdesktop-LICENSE.txt' }
)
foreach ($notice in $notices) {
    $source = Join-Path $packages $notice.From
    if (-not (Test-Path $source)) { throw "License file not found: $source" }
    Copy-Item $source (Join-Path $licenses $notice.To)
}

# Entries are added one by one with '/' separators. Compress-Archive and
# ZipFile.CreateFromDirectory both write backslashes under Windows PowerShell 5.1,
# which the zip format does not allow and some tools mis-read.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $staging -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($staging.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}

$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
"wrote $zip ($size MB)"
Get-ChildItem $staging | Select-Object Name, Length | Format-Table -AutoSize | Out-String
