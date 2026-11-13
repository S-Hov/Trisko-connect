[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$CoreBundlePath,

    [string]$DataDirectory,

    [switch]$UseInstalledUserData,

    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj'
$submoduleProject = Join-Path $repositoryRoot 'v2rayN\GlobalHotKeys\src\GlobalHotKeys\GlobalHotKeys.csproj'
$outputRoot = Join-Path $repositoryRoot "v2rayN\v2rayN.Desktop\bin\$Configuration\net10.0"
$outputBin = Join-Path $outputRoot 'bin'
$dataDirectoryVariable = 'TRISKO_CONNECT_DATA_DIRECTORY'

if (-not $IsWindows) {
    throw 'Запуск корпоративной версии из исходников сейчас поддерживается только в Windows.'
}

if (-not $PrepareOnly) {
    $runningApplication = Get-Process -Name 'Trisko.Connect' -ErrorAction SilentlyContinue
    if ($null -ne $runningApplication) {
        throw 'Закройте уже запущенную Trisko connect и повторите команду. Другие VPN-клиенты закрывать не нужно.'
    }
}

$dotnetVersion = (& dotnet --version 2>$null)
if ($LASTEXITCODE -ne 0 -or $dotnetVersion -notmatch '^10\.') {
    throw "Требуется .NET 10 SDK. Обнаруженная версия: $dotnetVersion"
}

if (-not (Test-Path -LiteralPath $submoduleProject -PathType Leaf)) {
    throw 'Отсутствует исходный код GlobalHotKeys. Повторно получите полный репозиторий.'
}

if ([string]::IsNullOrWhiteSpace($CoreBundlePath)) {
    $CoreBundlePath = & (Join-Path $PSScriptRoot 'get-core-bundle.ps1') | Select-Object -Last 1
}
$coreRoot = (Resolve-Path -LiteralPath $CoreBundlePath).Path

Write-Host "Собирается Trisko connect ($Configuration)..."
dotnet build $projectPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Сборка приложения завершилась ошибкой.'
}

New-Item -ItemType Directory -Path $outputBin -Force | Out-Null
foreach ($directoryName in @('xray', 'sing_box')) {
    $sourceDirectory = Join-Path $coreRoot $directoryName
    $targetDirectory = Join-Path $outputBin $directoryName
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceDirectory -Force |
        Copy-Item -Destination $targetDirectory -Recurse -Force
}
foreach ($fileName in @('geoip.dat', 'geosite.dat')) {
    Copy-Item -LiteralPath (Join-Path $coreRoot $fileName) -Destination (Join-Path $outputBin $fileName) -Force
}

Write-Host "Ядра подготовлены: $outputBin"
if ($PrepareOnly) {
    Write-Host 'Подготовка завершена. Приложение не запускалось.'
    return
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
$isAdministrator = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    throw 'Для запуска Trisko connect необходимы права администратора. Откройте PowerShell от имени Администратора (нажмите правой кнопкой мыши по PowerShell -> Запуск от имени администратора) и повторите команду.'
}

$oldDataDirectory = [Environment]::GetEnvironmentVariable($dataDirectoryVariable, 'Process')
try {
    if (-not $UseInstalledUserData) {
        if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
            $DataDirectory = Join-Path $repositoryRoot 'artifacts\dev-user-data'
        }
        $resolvedDataDirectory = [IO.Path]::GetFullPath($DataDirectory)
        $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
        if (-not $resolvedDataDirectory.StartsWith(
                $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Тестовый каталог данных должен находиться внутри artifacts: $resolvedDataDirectory"
        }
        New-Item -ItemType Directory -Path $resolvedDataDirectory -Force | Out-Null
        [Environment]::SetEnvironmentVariable($dataDirectoryVariable, $resolvedDataDirectory, 'Process')
        Write-Host "Тестовые данные: $resolvedDataDirectory"
    }
    else {
        [Environment]::SetEnvironmentVariable($dataDirectoryVariable, $null, 'Process')
        Write-Warning 'Используются данные установленной версии из LocalAppData.'
    }

    Write-Host 'Запускается Trisko connect...'
    dotnet run --project $projectPath -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Приложение завершилось с кодом $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable($dataDirectoryVariable, $oldDataDirectory, 'Process')
}
