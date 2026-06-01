[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$NodeSource,
    [switch]$SkipInstaller,
    [switch]$Clean
)

$supportedRuntimes = @("win-x64", "win-arm64")
if ($Runtime -notin $supportedRuntimes) {
    Write-Error "Unsupported runtime '$Runtime'. Supported: $($supportedRuntimes -join ', ')"
    exit 1
}

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "src\YPM.UI\YPM.UI.csproj"
$manifestFile = Join-Path $projectRoot "src\YPM.UI\Package.appxmanifest"
$installerScript = Join-Path $projectRoot "installer\Fusion.iss"
$backendDir = Join-Path $projectRoot "backend"
$artifactRoot = Join-Path $projectRoot "artifacts"
$publishDir = Join-Path $artifactRoot ("publish\" + $Runtime)
$installerOutputDir = Join-Path $artifactRoot "installer"
$dotnetHome = Join-Path $projectRoot ".dotnet"

function Resolve-AppVersion {
    param(
        [string]$ExplicitVersion,
        [string]$ManifestPath
    )

    if ($ExplicitVersion) {
        return $ExplicitVersion
    }

    [xml]$manifest = Get-Content -Path $ManifestPath
    $version = $manifest.Package.Identity.Version
    if (-not $version) {
        throw "Unable to resolve app version from $ManifestPath."
    }

    return $version
}

function Resolve-InnoSetupCompiler {
    $candidates = @(
        $env:INNO_SETUP_COMPILER,
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Ensure-BackendDependencies {
    param(
        [string]$BackendPath
    )

    $packageLock = Join-Path $BackendPath "package-lock.json"
    if (-not (Test-Path -LiteralPath $packageLock)) {
        throw "Missing backend lockfile: $packageLock"
    }

    Write-Host "Ensuring backend production dependencies..."
    Push-Location $BackendPath
    try {
        & npm install --include=prod
        if ($LASTEXITCODE -ne 0) {
            throw "npm install failed."
        }
    }
    finally {
        Pop-Location
    }
}

function Reset-ProjectOutputs {
    param(
        [string]$ProjectRoot
    )

    $paths = @(
        (Join-Path $ProjectRoot "src\YPM.UI\bin"),
        (Join-Path $ProjectRoot "src\YPM.UI\obj")
    )

    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

function Optimize-PublishOutput {
    param(
        [string]$PublishPath
    )

    $preservedTopLevelDirs = @(
        "Assets",
        "backend",
        "Controls",
        "en-US",
        "en-us",
        "Microsoft.UI.Xaml",
        "node-runtime",
        "PackageImages",
        "Pages",
        "Themes",
        "zh-CN",
        "zh-TW"
    )

    Write-Host "Removing unused release artifacts..."

    Get-ChildItem -Path $PublishPath -Filter *.pdb -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Get-ChildItem -Path $PublishPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $preservedTopLevelDirs } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Get-ChildItem -Path (Join-Path $PublishPath "backend") -Recurse -File -Include *.map -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Sync-AppPriFiles {
    param(
        [string]$ProjectRoot,
        [string]$PublishPath
    )

    $priNames = @("resources.pri", "Fusion.pri")
    $binRoot = Join-Path $ProjectRoot "src\YPM.UI\bin"

    foreach ($priName in $priNames) {
        $source = Get-ChildItem -Path $binRoot -Filter $priName -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -eq $source) {
            continue
        }

        $destination = Join-Path $PublishPath $priName
        Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
        Write-Host "Copied $priName from $($source.FullName)"
    }
}

function Sync-AppRuntimeContent {
    param(
        [string]$ProjectRoot,
        [string]$PublishPath
    )

    $binRoot = Join-Path $ProjectRoot "src\YPM.UI\bin"
    $runtimeOutput = Get-ChildItem -Path $binRoot -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "Fusion.exe") } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $runtimeOutput) {
        throw "Unable to locate the latest Fusion runtime output under $binRoot."
    }

    $directoriesToCopy = @(
        "Assets",
        "Controls",
        "Pages",
        "Themes"
    )

    foreach ($directoryName in $directoriesToCopy) {
        $sourceDirectory = Join-Path $runtimeOutput.FullName $directoryName
        if (-not (Test-Path -LiteralPath $sourceDirectory)) {
            continue
        }

        $destinationDirectory = Join-Path $PublishPath $directoryName
        Copy-Item -LiteralPath $sourceDirectory -Destination $destinationDirectory -Recurse -Force
        Write-Host "Copied $directoryName from $($runtimeOutput.FullName)"
    }

    $filesToCopy = @(
        "App.xbf",
        "MainWindow.xbf"
    )

    foreach ($fileName in $filesToCopy) {
        $sourceFile = Join-Path $runtimeOutput.FullName $fileName
        if (-not (Test-Path -LiteralPath $sourceFile)) {
            continue
        }

        $destinationFile = Join-Path $PublishPath $fileName
        Copy-Item -LiteralPath $sourceFile -Destination $destinationFile -Force
        Write-Host "Copied $fileName from $($runtimeOutput.FullName)"
    }
}

if ($Clean -and (Test-Path -LiteralPath $artifactRoot)) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null

$resolvedVersion = Resolve-AppVersion -ExplicitVersion $Version -ManifestPath $manifestFile

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Ensure-BackendDependencies -BackendPath $backendDir
Reset-ProjectOutputs -ProjectRoot $projectRoot

$extraArgs = @()
if ($NodeSource) {
    $extraArgs += "-p:BundledNodeSource=$NodeSource"
}

Write-Host "Publishing $Configuration $Runtime ($resolvedVersion)..."
& dotnet publish $projectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishDir="$publishDir\" `
    --configfile (Join-Path $projectRoot "NuGet.Config") `
    $extraArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Sync-AppPriFiles -ProjectRoot $projectRoot -PublishPath $publishDir
Sync-AppRuntimeContent -ProjectRoot $projectRoot -PublishPath $publishDir

Optimize-PublishOutput -PublishPath $publishDir

Write-Host "Publish output: $publishDir"

if ($SkipInstaller) {
    Write-Host "Installer step skipped."
    exit 0
}

$iscc = Resolve-InnoSetupCompiler
if (-not $iscc) {
    Write-Warning "Inno Setup 6 not found. Publish output is ready, but setup.exe was not generated."
    Write-Host "Set INNO_SETUP_COMPILER or install Inno Setup 6, then rerun this script."
    exit 0
}

$architecture = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64compatible" }
$archSuffix = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }

Write-Host "Building installer with Inno Setup ($archSuffix)..."
& $iscc `
    "/DMyAppVersion=$resolvedVersion" `
    "/DSourceDir=$publishDir" `
    "/DInstallerOutputDir=$installerOutputDir" `
    "/DArchSuffix=$archSuffix" `
    "/DArchitecture=$architecture" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup build failed."
}

Write-Host "Installer output: $installerOutputDir"
