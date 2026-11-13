[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$CoreBundlePath,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ReleaseNotesPath,

    [switch]$AllowNonStandardReleaseNotes,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PreviousReleaseDirectory,

    [string]$ReleaseRepositoryUrl = 'https://github.com/S-Hov/Trisko-connect',

    [switch]$SkipPreviousReleaseDownload,

    [switch]$AllowDirtySourceArchive,

    [string]$CertificateThumbprint,

    [switch]$SkipPortable
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $repositoryRoot 'eng\core-lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$stageRoot = Join-Path $repositoryRoot 'artifacts\release-stage'
$publishDirectory = Join-Path $stageRoot 'app'
$releaseDirectory = Join-Path $repositoryRoot "artifacts\releases\$Version"
$velopackDirectory = Join-Path $releaseDirectory 'velopack'
$portableDirectory = Join-Path $releaseDirectory 'portable'
$licenseCacheDirectory = Join-Path $repositoryRoot 'artifacts\license-cache'
$projectPath = Join-Path $repositoryRoot 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj'
$iconPath = Join-Path $repositoryRoot 'v2rayN\v2rayN.Desktop\Assets\Geksa\Brand\trisko-connect.ico'
$packageId = 'TriskoConnect-Desktop'

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = Join-Path $repositoryRoot "docs\releases\$Version.md"
}
if (-not (Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf)) {
    throw "Создайте описание релиза: $ReleaseNotesPath"
}
$canonicalReleaseNotesPath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "docs\releases\$Version.md"))
$actualReleaseNotesPath = [IO.Path]::GetFullPath($ReleaseNotesPath)
$usesCanonicalReleaseNotes = $actualReleaseNotesPath.Equals(
    $canonicalReleaseNotesPath,
    [StringComparison]::OrdinalIgnoreCase)
if (-not $usesCanonicalReleaseNotes -and -not $AllowNonStandardReleaseNotes) {
    throw "Клиентский релиз можно собирать только с docs\releases\$Version.md. Для локальной проверки добавьте -AllowNonStandardReleaseNotes; такой пакет нельзя будет опубликовать."
}
$releaseNotesContent = Get-Content -LiteralPath $ReleaseNotesPath -Raw
if ($releaseNotesContent -match '(?i)(не\s+публиков|do\s+not\s+publish|локальн\w*\s+(провер|тест)|test[- ]only)') {
    throw 'Описание содержит признак локальной тестовой сборки. Такой пакет нельзя собирать как клиентский релиз.'
}

function Remove-ArtifactDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $artifacts = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot 'artifacts')).Path
    if (-not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Отказ очищать каталог за пределами artifacts: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Get-VerifiedReleaseFile([object]$ReleaseFile, [string]$Destination, [string]$CacheDirectory = $licenseCacheDirectory) {
    $cached = Join-Path $CacheDirectory $ReleaseFile.fileName
    if ((Test-Path -LiteralPath $cached) -and
        (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash -eq $ReleaseFile.sha256) {
        Copy-Item -LiteralPath $cached -Destination $Destination -Force
        return
    }

    $lastFailure = $null
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            Invoke-WebRequest -Uri $ReleaseFile.url -OutFile $Destination
            if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $ReleaseFile.sha256) {
                throw "Файл $($ReleaseFile.fileName) не прошёл SHA-256."
            }
            New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
            Copy-Item -LiteralPath $Destination -Destination $cached -Force
            return
        }
        catch {
            $lastFailure = $_
            if ($attempt -lt 4) { Start-Sleep -Seconds $attempt }
        }
    }
    throw "Не удалось получить проверенный файл $($ReleaseFile.fileName): $lastFailure"
}

if ([string]::IsNullOrWhiteSpace($CoreBundlePath)) {
    $CoreBundlePath = & (Join-Path $PSScriptRoot 'get-core-bundle.ps1') | Select-Object -Last 1
}
$coreRoot = (Resolve-Path -LiteralPath $CoreBundlePath).Path
foreach ($entry in $lock.files) {
    $relative = $entry.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $source = Join-Path $coreRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Комплект ядра не прошёл проверку: $relative"
    }
}

Remove-ArtifactDirectory $stageRoot
Remove-ArtifactDirectory $releaseDirectory
New-Item -ItemType Directory -Path $publishDirectory, $velopackDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить Velopack CLI.' }

    dotnet test '.\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Автоматические тесты завершились ошибкой.' }

    dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:SelfContained=true -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Публикация приложения завершилась ошибкой.' }
}
finally {
    Pop-Location
}

$publishedBin = Join-Path $publishDirectory 'bin'
New-Item -ItemType Directory -Path $publishedBin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $coreRoot 'xray') -Destination $publishedBin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $coreRoot 'sing_box') -Destination $publishedBin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $coreRoot 'geoip.dat') -Destination $publishedBin -Force
Copy-Item -LiteralPath (Join-Path $coreRoot 'geosite.dat') -Destination $publishedBin -Force

$licenses = Join-Path $publishDirectory 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $licenses 'v2rayN-GPL-3.0.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $licenses
foreach ($license in $lock.licenses) {
    Get-VerifiedReleaseFile $license (Join-Path $licenses $license.fileName)
}

Push-Location $repositoryRoot
try {
    if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseDirectory)) {
        Get-ChildItem -LiteralPath $PreviousReleaseDirectory -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $velopackDirectory -Force
        }
        Write-Host "Предыдущий Velopack feed скопирован из $PreviousReleaseDirectory"
    }
    elseif (-not $SkipPreviousReleaseDownload) {
        $repositoryUri = [Uri]$ReleaseRepositoryUrl
        $repositoryParts = $repositoryUri.AbsolutePath.Trim('/').Split('/')
        if ($repositoryUri.Host -ne 'github.com' -or $repositoryParts.Count -ne 2) {
            throw "Ожидается адрес GitHub-репозитория вида https://github.com/owner/repo: $ReleaseRepositoryUrl"
        }

        $latestReleaseApi = "https://api.github.com/repos/$($repositoryParts[0])/$($repositoryParts[1])/releases/latest"
        try {
            $latestRelease = Invoke-RestMethod `
                -Uri $latestReleaseApi `
                -Headers @{ 'User-Agent' = 'Trisko-Release-Builder' }
            $velopackFeedAsset = @($latestRelease.assets) |
                Where-Object name -eq 'releases.stable.json' |
                Select-Object -First 1
            $hasCompatibleFeed = $false
            if ($null -ne $velopackFeedAsset) {
                $remoteFeed = Invoke-RestMethod `
                    -Uri $velopackFeedAsset.browser_download_url `
                    -Headers @{ 'User-Agent' = 'Trisko-Release-Builder' }
                $hasCompatibleFeed = @($remoteFeed.Assets) |
                    Where-Object PackageId -eq $packageId |
                    Select-Object -First 1
            }
            if (-not $hasCompatibleFeed) {
                Write-Host "Последний release $($latestRelease.tag_name) не содержит совместимый пакет ${packageId}: создаётся полный пакет."
            }
            else {
                dotnet tool run vpk -- download github `
                    --outputDir $velopackDirectory `
                    --repoUrl $ReleaseRepositoryUrl `
                    --channel stable
                if ($LASTEXITCODE -ne 0) { throw 'Не удалось загрузить предыдущий Velopack release.' }
                Write-Host 'Предыдущий релиз загружен: будет создано delta-обновление.'
            }
        }
        catch [Microsoft.PowerShell.Commands.HttpResponseException] {
            if ($_.Exception.Response.StatusCode -ne [Net.HttpStatusCode]::NotFound) { throw }
            Write-Host 'Публичных релизов пока нет: создаётся первый полный пакет.'
        }
    }

    $packArguments = @(
        'tool', 'run', 'vpk', 'pack',
        '--packId', $packageId,
        '--packVersion', $Version,
        '--packDir', $publishDirectory,
        '--mainExe', 'Trisko.Connect.exe',
        '--packTitle', 'Trisko connect',
        '--packAuthors', 'S-Hov',
        '--icon', $iconPath,
        '--releaseNotes', $ReleaseNotesPath,
        '--channel', 'stable',
        '--shortcuts', 'Desktop,StartMenuRoot',
        '--outputDir', $velopackDirectory
    )
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $packArguments += @(
            '--signParams', "/sha1 $CertificateThumbprint /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com"
        )
    }
    & dotnet @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Velopack не смог создать Setup.exe и пакеты обновления.' }
}
finally {
    Pop-Location
}

$setup = Get-ChildItem -LiteralPath $velopackDirectory -Filter '*-Setup.exe' | Select-Object -First 1
if ($null -eq $setup) { throw 'Velopack не создал Setup.exe.' }
$friendlySetupPath = Join-Path $releaseDirectory 'TriskoConnect-Setup.exe'
Move-Item -LiteralPath $setup.FullName -Destination $friendlySetupPath -Force

$assetsPath = Join-Path $velopackDirectory 'assets.stable.json'
$uploadAssets = @(Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json) |
    Where-Object Type -In @('Full', 'Delta')
ConvertTo-Json -InputObject @($uploadAssets) -Compress |
    Set-Content -LiteralPath $assetsPath -Encoding utf8NoBOM
Get-ChildItem -LiteralPath $velopackDirectory -Filter '*-Portable.zip' |
    Remove-Item -Force

$sourceArchive = & (Join-Path $PSScriptRoot 'build-source-archive.ps1') `
    -Version $Version `
    -DestinationPath (Join-Path $releaseDirectory "TriskoConnect-$Version-source.zip") `
    -AllowDirty:$AllowDirtySourceArchive |
    Select-Object -Last 1

$releaseFiles = @(
    $friendlySetupPath,
    $sourceArchive
)
if (-not $SkipPortable) {
    New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
    $portableZip = Join-Path $portableDirectory "TriskoConnect-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableZip -CompressionLevel Optimal
    $releaseFiles += $portableZip
}

$releaseFiles += Get-ChildItem -LiteralPath $velopackDirectory -File | ForEach-Object FullName
$sourceCommit = (git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Не удалось определить commit релиза.' }
$manifestPath = Join-Path $releaseDirectory 'release-manifest.json'
[ordered]@{
    schemaVersion = 1
    product = 'Trisko connect'
    version = $Version
    packageId = $packageId
    publishable = ($usesCanonicalReleaseNotes -and -not $AllowDirtySourceArchive)
    sourceCommit = $sourceCommit
    releaseNotesSha256 = (Get-FileHash -LiteralPath $ReleaseNotesPath -Algorithm SHA256).Hash.ToLowerInvariant()
    setupFile = (Split-Path -Leaf $friendlySetupPath)
    setupSha256 = (Get-FileHash -LiteralPath $friendlySetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
$releaseFiles += $manifestPath
$checksums = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$releaseFiles | Sort-Object -Unique | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),
        $file.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/')
} | Set-Content -LiteralPath $checksums -Encoding utf8NoBOM

Write-Host "Готов релиз Trisko connect $Version"
Write-Host "Единственный установщик для клиентов: $friendlySetupPath"
Write-Host "Velopack feed: $velopackDirectory"
Write-Host "Исходный код GPL: $sourceArchive"
Write-Host "SHA-256: $checksums"
