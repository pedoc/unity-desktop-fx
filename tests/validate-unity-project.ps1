param(
    [Parameter(Mandatory = $true)] [string] $EditorExecutable,
    [Parameter(Mandatory = $true)] [string] $ProjectPath
)

$ErrorActionPreference = 'Stop'
$expectedVersion = '6000.6.1f1'
$versionOutput = (& $EditorExecutable -version -batchmode -quit -logFile - 2>&1 | Out-String).Trim()
if ($versionOutput -notmatch [regex]::Escape($expectedVersion)) {
    throw "Unexpected Unity editor version: $versionOutput"
}

$editorDirectory = Split-Path -Parent $EditorExecutable
$il2cpp = Join-Path $editorDirectory 'Data\PlaybackEngines\windowsstandalonesupport\Variations\win64_player_nondevelopment_il2cpp'
if (-not (Test-Path -LiteralPath $il2cpp)) {
    throw "Windows IL2CPP support is missing: $il2cpp"
}

$projectVersionPath = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
$projectVersion = Get-Content -LiteralPath $projectVersionPath -Raw -Encoding utf8
if ($projectVersion -notmatch 'm_EditorVersion: 6000\.6\.1f1' -or
    $projectVersion -notmatch '7efac9f6c10e') {
    throw 'Unity project version is not pinned to the required editor revision.'
}

$manifest = Get-Content -LiteralPath (Join-Path $ProjectPath 'Packages\manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
$expectedPackages = @{
    'com.unity.render-pipelines.universal' = '17.6.0'
    'com.unity.inputsystem' = '1.19.0'
    'com.unity.test-framework' = '1.8.0'
    'com.unity.timeline' = '6.6.0'
}
foreach ($entry in $expectedPackages.GetEnumerator()) {
    $actual = $manifest.dependencies.($entry.Key)
    if ($actual -ne $entry.Value) {
        throw "Package $($entry.Key) expected $($entry.Value), got $actual"
    }
}

$settings = Get-Content -LiteralPath (Join-Path $ProjectPath 'ProjectSettings\ProjectSettings.asset') -Raw -Encoding utf8
foreach ($required in @(
    'companyName: InteractiveWallpaper',
    'runInBackground: 1',
    'resizableWindow: 1',
    'fullscreenMode: 3',
    'activeInputHandler: 2',
    'Standalone: com.interactivewallpaper.runtime'
)) {
    if (-not $settings.Contains($required)) {
        throw "Required player setting is missing: $required"
    }
}

$productLine = ($settings -split "`r?`n" | Where-Object { $_ -match '^  productName:' } | Select-Object -First 1)
if (-not $productLine -or $productLine -notmatch '\\u4EA4\\u4E92\\u5F0F\\u58C1\\u7EB8') {
    throw "Required player product name is missing or invalid: $productLine"
}

$requiredAssets = @(
    'Assets\InteractiveWallpaper\Runtime\InteractiveWallpaperBootstrap.cs',
    'Assets\InteractiveWallpaper\Runtime\NativeDesktopBridge.cs',
    'Assets\InteractiveWallpaper\Runtime\DesktopWallpaperRenderer.cs',
    'Assets\InteractiveWallpaper\Runtime\DesktopEffectFeedback.cs',
    'Assets\InteractiveWallpaper\Runtime\CharacterImpactMath.cs',
    'Assets\InteractiveWallpaper\Runtime\MouseWorldPosition.cs',
    'Assets\InteractiveWallpaper\Runtime\DesktopSnapshotSynchronizer.cs',
    'Assets\InteractiveWallpaper\Runtime\DesktopSnapshotFingerprint.cs',
    'Assets\InteractiveWallpaper\Runtime\ProxyIconSpawner.cs',
    'Assets\InteractiveWallpaper\Runtime\RuntimeStatusOverlay.cs',
    'Assets\InteractiveWallpaper\Runtime\VideoCharacterPerformance.cs',
    'Assets\Resources\Shaders\SideBySideVideoAlpha.shader',
    'Assets\InteractiveWallpaper\Editor\RuntimeBuild.cs',
    'Assets\InteractiveWallpaper\Editor\MakeHumanModelImporter.cs',
    'Assets\Resources\Characters\MakeHumanDefault\DefaultHuman.fbx',
    'Assets\ThirdParty\MakeHuman\LICENSE.md',
    'Assets\InteractiveWallpaper\Tests\Editor\RuntimeContractTests.cs'
)
foreach ($relative in $requiredAssets) {
    $asset = Join-Path $ProjectPath $relative
    if (-not (Test-Path -LiteralPath $asset)) { throw "Missing Unity asset: $relative" }
    if (-not (Test-Path -LiteralPath ($asset + '.meta'))) { throw "Missing Unity meta file: $relative.meta" }
}

Write-Host "Unity $expectedVersion project structure, URP packages, IL2CPP module, and Runtime assets verified."
