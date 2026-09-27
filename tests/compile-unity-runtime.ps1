param(
    [Parameter(Mandatory = $true)] [string] $EditorExecutable,
    [Parameter(Mandatory = $true)] [string] $ProjectPath,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$editorDirectory = Split-Path -Parent $EditorExecutable
$data = Join-Path $editorDirectory 'Data'
$legacyRunner = Join-Path $data 'netcorerun\netcorerun.exe'
$legacyCompiler = Join-Path $data 'DotNetSdkRoslyn\csc.dll'
if ((Test-Path -LiteralPath $legacyRunner) -and (Test-Path -LiteralPath $legacyCompiler)) {
    $runner = $legacyRunner
    $compiler = $legacyCompiler
}
else {
    $runner = Join-Path $data 'DotNetSdk\dotnet.exe'
    $compiler = Get-ChildItem -LiteralPath (Join-Path $data 'DotNetSdk\sdk') -Filter 'csc.dll' -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
$netstandard = Join-Path $data 'NetStandard\ref\2.1.0\netstandard.dll'
$engine = Join-Path $data 'Managed\UnityEngine'

foreach ($required in @($EditorExecutable, $runner, $compiler, $netstandard)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Unity compiler component is missing: $required"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$runtimeOutput = Join-Path $OutputDirectory 'InteractiveWallpaper.Runtime.dll'
$editorOutput = Join-Path $OutputDirectory 'InteractiveWallpaper.Editor.dll'
$runtimeSources = @(Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets\InteractiveWallpaper\Runtime') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$editorSources = @(Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Assets\InteractiveWallpaper\Editor') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)

$runtimeReferences = @(
    $netstandard,
    (Join-Path $engine 'UnityEngine.CoreModule.dll'),
    (Join-Path $engine 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $engine 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $engine 'UnityEngine.PhysicsModule.dll'),
    (Join-Path $engine 'UnityEngine.AnimationModule.dll'),
    (Join-Path $engine 'UnityEngine.VideoModule.dll'),
    (Join-Path $engine 'UnityEngine.JSONSerializeModule.dll'),
    (Join-Path $engine 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $engine 'UnityEngine.ScreenCaptureModule.dll'),
    (Join-Path $engine 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $engine 'UnityEngine.ScriptingModule.dll')
)
$runtimeArguments = @(
    $compiler,
    '/nologo', '/noconfig', '/nostdlib+', '/target:library',
    '/langversion:latest', '/nullable:enable', '/warnaserror+',
    "/out:$runtimeOutput"
)
foreach ($reference in $runtimeReferences) { $runtimeArguments += "/reference:$reference" }
$runtimeArguments += $runtimeSources
& $runner @runtimeArguments
if ($LASTEXITCODE -ne 0) { throw "Unity Runtime static compilation failed: $LASTEXITCODE" }

$editorReferences = @(
    $netstandard,
    $runtimeOutput,
    (Join-Path $engine 'UnityEngine.CoreModule.dll'),
    (Join-Path $engine 'UnityEngine.AnimationModule.dll'),
    (Join-Path $engine 'UnityEngine.VideoModule.dll'),
    (Join-Path $engine 'UnityEditor.CoreModule.dll'),
    (Join-Path $engine 'UnityEditor.BuildProfileModule.dll')
)
$editorArguments = @(
    $compiler,
    '/nologo', '/noconfig', '/nostdlib+', '/target:library',
    '/langversion:latest', '/nullable:enable', '/warnaserror+',
    "/out:$editorOutput"
)
foreach ($reference in $editorReferences) { $editorArguments += "/reference:$reference" }
$editorArguments += $editorSources
& $runner @editorArguments
if ($LASTEXITCODE -ne 0) { throw "Unity Editor static compilation failed: $LASTEXITCODE" }

Write-Host "Unity Runtime and Editor assemblies compiled successfully without launching the licensed Editor."
