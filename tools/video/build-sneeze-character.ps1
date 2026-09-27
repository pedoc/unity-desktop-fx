param(
    [Parameter(Mandatory = $true)] [string] $InputVideo,
    [string] $OutputDirectory = "",
    [double] $StartSeconds = 0.0,
    [double] $DurationSeconds = 5.5
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'out\video-matting\sneeze'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$venvPython = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'InteractiveWallpaper\video-matting-venv\Scripts\python.exe'
$script = Join-Path $PSScriptRoot 'create-character-matte.py'
foreach ($required in @($InputVideo, $venvPython, $script)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少视频抠像所需文件：$required"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& $venvPython $script `
    --input ([IO.Path]::GetFullPath($InputVideo)) `
    --output $OutputDirectory `
    --start $StartSeconds `
    --duration $DurationSeconds
if ($LASTEXITCODE -ne 0) {
    throw "人物遮罩生成失败，退出码：$LASTEXITCODE"
}

$maskPattern = Join-Path $OutputDirectory 'masks\%06d.png'
$sideBySide = Join-Path $OutputDirectory 'SneezeRgbMask.mp4'
$filter = '[0:v]scale=1280:720,setsar=1[color];[1:v]format=gray,scale=1280:720,setsar=1[mask];[color][mask]hstack=inputs=2[out]'
& ffmpeg -y -v error `
    -ss $StartSeconds -t $DurationSeconds -i ([IO.Path]::GetFullPath($InputVideo)) `
    -framerate 30 -i $maskPattern `
    -filter_complex $filter `
    -map '[out]' -an -c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p `
    -profile:v baseline -level:v 5.0 -bf 0 -refs 1 -g 30 -r 30 -fps_mode cfr `
    -color_primaries bt709 -color_trc bt709 -colorspace bt709 -movflags +faststart `
    $sideBySide
if ($LASTEXITCODE -ne 0) {
    throw "RGB+遮罩视频编码失败，退出码：$LASTEXITCODE"
}

$unityVideoDirectory = Join-Path $repositoryRoot 'unity\InteractiveWallpaper3D\Assets\StreamingAssets\CharacterVideo'
New-Item -ItemType Directory -Force -Path $unityVideoDirectory | Out-Null
$unityVideo = Join-Path $unityVideoDirectory 'SneezeRgbMask.mp4'
Copy-Item -LiteralPath $sideBySide -Destination $unityVideo -Force

Write-Host "透明人物原型素材已生成：$sideBySide"
Write-Host "已安装到 Unity StreamingAssets：$unityVideo"
