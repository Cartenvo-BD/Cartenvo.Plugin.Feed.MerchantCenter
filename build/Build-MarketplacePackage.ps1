# Builds the nopCommerce marketplace package: one zip with the 5.00, 4.90 and 4.80 builds and uploadedItems.json.
# nopCommerce's upload installs only the entry whose SupportedVersion matches the running store.
#
#   ./build/Build-MarketplacePackage.ps1 -Trees @{ '4.80' = 'D:\nop480\src'; '4.90' = 'D:\nop490\src'; '5.00' = 'D:\nop500\src' }
#
# Each tree is a nopCommerce source tree (its src folder). The plugin is built in a scratch copy of each tree that links
# to the tree's libraries, so nothing in the trees changes. The zip goes to dist/.
param([Parameter(Mandatory)][hashtable]$Trees)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$repo = Split-Path $PSScriptRoot -Parent
$name = 'Cartenvo.Plugin.Feed.MerchantCenter'
$systemName = 'Feed.CartenvoMerchantCenter'
$work = Join-Path ([IO.Path]::GetTempPath()) "mc-package-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force $work, $dist | Out-Null
$ownFiles = "$name.dll", 'MerchantCenter.Core.dll', 'plugin.json', 'logo.png'

try {
    foreach ($version in $Trees.Keys) {
        $tree = $Trees[$version]
        $root = Join-Path $work "tree$($version.Replace('.', ''))\src"
        New-Item -ItemType Directory -Force (Join-Path $root 'Plugins'), (Join-Path $root 'Presentation\Nop.Web') | Out-Null
        foreach ($d in 'Libraries', 'Build', 'Presentation\Nop.Web.Framework') {
            New-Item -ItemType Junction -Path (Join-Path $root $d) -Target (Join-Path $tree $d) | Out-Null
        }
        Copy-Item (Join-Path $tree 'Directory.Build.props') $root
        $globalJson = Join-Path (Split-Path $tree -Parent) 'global.json'
        if (Test-Path $globalJson) { Copy-Item $globalJson (Split-Path $root -Parent) }

        $source = Join-Path $root "Plugins\$name"
        robocopy $repo $source /E /XD .git bin obj dist Tests /NFL /NDL /NJH /NJS /NP | Out-Null
        Push-Location (Split-Path $root -Parent)
        try { dotnet build (Join-Path $source "$name.csproj") -c Release -nologo -v:q -p:NopVersion=$version | Out-Host }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "Build for nopCommerce $version failed." }

        $out = Join-Path $root "Presentation\Nop.Web\Plugins\$systemName"
        $stage = Join-Path $work "stage\$version\$systemName"
        New-Item -ItemType Directory -Force $stage | Out-Null
        foreach ($file in $ownFiles) { Copy-Item (Join-Path $out $file) $stage }
        Copy-Item (Join-Path $out 'Views') (Join-Path $stage 'Views') -Recurse
        Copy-Item (Join-Path $repo 'LICENSE') $stage
    }

    $versions = @($Trees.Keys | Sort-Object -Descending)
    $revision = ((Get-Content (Join-Path $work "stage\$($versions[0])\$systemName\plugin.json") -Raw | ConvertFrom-Json).Version -split '\.')[-1]
    $zip = Join-Path $dist "$name-release$revision.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    $items = foreach ($v in $versions) {
        [ordered]@{ Type = 'Plugin'; SupportedVersion = $v; DirectoryPath = "$v/$systemName/"; SystemName = $systemName; SourceDirectoryPath = '' }
    }
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        $entry = $archive.CreateEntry('uploadedItems.json')
        $writer = New-Object IO.StreamWriter($entry.Open(), (New-Object Text.UTF8Encoding($false)))
        $writer.Write((ConvertTo-Json @($items) -Depth 3)); $writer.Dispose()
        $stageRoot = Join-Path $work 'stage'
        Get-ChildItem $stageRoot -Recurse -File | ForEach-Object {
            $entryName = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName, 'Optimal') | Out-Null
        }
    }
    finally { $archive.Dispose() }
    "$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower())  $(Split-Path $zip -Leaf)"
}
finally {
    cmd /c rmdir /s /q "$work" | Out-Null   # rmdir leaves junction targets alone
}
