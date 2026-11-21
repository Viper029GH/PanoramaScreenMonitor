[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectDir 'PanoramaScreenMonitor.csproj'
$publishDir = Join-Path $projectDir 'artifacts/publish/win-x64'
$exe = Join-Path $publishDir 'PanoramaScreenMonitor.exe'

dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with code $LASTEXITCODE." }

$assets = Get-Content -LiteralPath (Join-Path $projectDir 'obj/project.assets.json') -Raw | ConvertFrom-Json
$version = $assets.project.version
$publishedFiles = @(Get-ChildItem -LiteralPath $publishDir -Force)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].FullName -ne $exe) {
    throw 'Expected a single published PanoramaScreenMonitor.exe.'
}
$exeVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+', 2
if ($exeVersion[0] -ne $version) { throw 'The EXE version does not match the project version.' }

$releaseDir = Join-Path $projectDir "artifacts/releases/v$version"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$report = Join-Path $projectDir 'artifacts/self-test.txt'
$test = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"{0}"' -f $report)) -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath $report
if ($test.ExitCode -ne 0) { throw "Self-test exited with code $($test.ExitCode)." }

$notices = foreach ($name in 'Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64') {
    $pack = $assets.project.frameworks.'net10.0-windows'.downloadDependencies |
        Where-Object name -EQ $name | Select-Object -First 1
    if ($null -eq $pack -or $pack.version -notmatch '^\[([^,\s]+),\s*\1\]$') {
        throw "Could not determine the restored version of $name."
    }

    $runtimeVersion = $Matches[1]
    $relativePath = Join-Path $name.ToLowerInvariant() $runtimeVersion
    $packDir = $assets.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ $relativePath } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        Select-Object -First 1
    if (-not $packDir) { throw "Runtime pack not found: $name $runtimeVersion." }

    $files = @(Get-ChildItem -LiteralPath $packDir -File |
        Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' } |
        Sort-Object Name)
    if ($files.Count -eq 0) { throw "No redistribution notices found in $name $runtimeVersion." }
    foreach ($file in $files) {
        "$name $runtimeVersion — $($file.Name)"
        ('=' * 72)
        Get-Content -LiteralPath $file.FullName -Raw
        ''
    }
}

$stem = "PanoramaScreenMonitor-v$version-win-x64"
$releaseExe = Join-Path $releaseDir "$stem.exe"
$releaseZip = Join-Path $releaseDir "$stem.zip"
$noticesPath = Join-Path $releaseDir 'THIRD-PARTY-NOTICES.txt'
$checksums = Join-Path $releaseDir 'SHA256SUMS.txt'
$license = Join-Path $projectDir 'LICENSE'

Copy-Item -LiteralPath $exe -Destination $releaseExe -Force
$notices | Set-Content -LiteralPath $noticesPath -Encoding utf8
Compress-Archive -LiteralPath @($exe, $license, $noticesPath) -DestinationPath $releaseZip -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($releaseZip)
try {
    if ($archive.Entries.Count -ne 3) { throw 'Unexpected release ZIP contents.' }
    foreach ($file in @($exe, $license, $noticesPath)) {
        $entry = $archive.GetEntry([IO.Path]::GetFileName($file))
        if ($null -eq $entry) { throw "Missing ZIP entry for $file." }
        $stream = $entry.Open()
        try {
            if ((Get-FileHash -InputStream $stream).Hash -ne (Get-FileHash -LiteralPath $file).Hash) {
                throw "ZIP content does not match $file."
            }
        }
        finally { $stream.Dispose() }
    }
}
finally { $archive.Dispose() }

@($releaseExe, $releaseZip, $noticesPath) |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_.Path) } |
    Set-Content -LiteralPath $checksums -Encoding ascii

Write-Output "Release v${version} in $releaseDir"
