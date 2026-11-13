[CmdletBinding()]
param(
    [string]$DestinationPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $repositoryRoot 'eng\core-lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path $repositoryRoot 'artifacts\core-bundle'
}
$destination = [IO.Path]::GetFullPath($DestinationPath)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $destination.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Каталог ядра должен находиться внутри artifacts: $destination"
}

$cache = Join-Path $repositoryRoot 'artifacts\downloads'
$archive = Join-Path $cache ([IO.Path]::GetFileName([uri]$lock.source.url))
New-Item -ItemType Directory -Path $cache -Force | Out-Null

function Test-Hash([string]$Path, [string]$Expected) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected)
}

if (-not (Test-Hash $archive $lock.source.sha256)) {
    $temporaryArchive = "$archive.partial"
    Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -Uri $lock.source.url -OutFile $temporaryArchive
    if (-not (Test-Hash $temporaryArchive $lock.source.sha256)) {
        Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
        throw 'SHA-256 загруженного комплекта v2rayN не совпадает с eng/core-lock.json.'
    }
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
}

$temporaryDirectory = Join-Path $repositoryRoot ('artifacts\core-extract-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $temporaryDirectory
    $sourceRoot = Join-Path $temporaryDirectory ($lock.source.archiveRoot.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        throw "В архиве отсутствует $($lock.source.archiveRoot)."
    }

    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    foreach ($entry in $lock.files) {
        $relative = $entry.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $source = Join-Path $sourceRoot $relative
        if (-not (Test-Hash $source $entry.sha256)) {
            throw "Файл $relative отсутствует или не прошёл SHA-256."
        }
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

Write-Host "Закреплённый комплект ядра подготовлен: $destination"
Write-Output $destination
