[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$DestinationPath,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$stageDirectory = Join-Path $artifactsRoot "source-stage\TriskoConnect-$Version-source"

if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path $artifactsRoot "releases\$Version\TriskoConnect-$Version-source.zip"
}

function Assert-ArtifactPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullArtifacts = [IO.Path]::GetFullPath($artifactsRoot)
    if (-not $fullPath.StartsWith(
        $fullArtifacts + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Отказ работать с путём за пределами artifacts: $fullPath"
    }
}

Assert-ArtifactPath $stageDirectory
Assert-ArtifactPath $DestinationPath

if (Test-Path -LiteralPath $stageDirectory) {
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $DestinationPath) {
    Remove-Item -LiteralPath $DestinationPath -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationPath) -Force | Out-Null

Push-Location $repositoryRoot
try {
    $workingTreeStatus = @(git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Не удалось проверить состояние Git.'
    }
    if ($workingTreeStatus.Count -gt 0 -and -not $AllowDirty) {
        throw 'Исходники содержат незакоммиченные изменения. Сначала создайте commit или используйте -AllowDirty только для локальной проверки.'
    }

    $sourceFiles = @(
        git ls-files --cached
        if ($AllowDirty) { git ls-files --others --exclude-standard }
    ) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $sourceFiles.Count -eq 0) {
        throw 'Git не вернул список файлов исходного кода.'
    }

    foreach ($relativePath in $sourceFiles) {
        $sourcePath = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }

        $destination = Join-Path $stageDirectory $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
    }

    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw 'Не удалось определить commit исходного кода.'
    }

    $forbiddenSourceFiles = @($sourceFiles | Where-Object {
        $_ -match '(?i)\.(pfx|p12|pem|key|gac3)$' -and
        $_ -notmatch '(?i)^examples/.+\.example\.gac3$'
    })
    if ($forbiddenSourceFiles.Count -gt 0) {
        throw "В архив исходников могли попасть секреты:`n$($forbiddenSourceFiles -join "`n")"
    }
}
finally {
    Pop-Location
}

@(
    "Trisko connect $Version"
    "Source commit: $commit$(if ($workingTreeStatus.Count -gt 0) { ' (dirty local test)' })"
    'Includes the modified v2rayN source and the vendored GlobalHotKeys source.'
    'Build instructions: docs/release-and-update.md'
) | Set-Content -LiteralPath (Join-Path $stageDirectory 'SOURCE-COMMIT.txt') -Encoding utf8NoBOM

# Editors can restore projects inside the staging folder while it exists.
# Archive only the explicit Git snapshot, never incidental obj/bin files.
$archive = [IO.Compression.ZipFile]::Open(
    [IO.Path]::GetFullPath($DestinationPath),
    [IO.Compression.ZipArchiveMode]::Create)
try {
    $archiveRoot = Split-Path -Leaf $stageDirectory
    foreach ($relativePath in @($sourceFiles) + @('SOURCE-COMMIT.txt')) {
        $stagedFile = Join-Path $stageDirectory $relativePath
        if (-not (Test-Path -LiteralPath $stagedFile -PathType Leaf)) { continue }
        $entryName = "$archiveRoot/$($relativePath.Replace('\', '/'))"
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $stagedFile, $entryName,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

Write-Output ([IO.Path]::GetFullPath($DestinationPath))
