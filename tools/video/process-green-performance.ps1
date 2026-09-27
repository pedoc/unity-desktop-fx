param(
    [string] $InputDirectory = "",
    [string] $OutputDirectory = ""
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($InputDirectory)) {
    throw '请用 -InputDirectory 指定外部原始绿幕视频目录；本仓库不再跟踪原始素材。'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'unity\InteractiveWallpaper3D\Assets\StreamingAssets\CharacterVideo\Performances\SneezePickupRestore'
}
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$venvPython = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'InteractiveWallpaper\video-matting-venv\Scripts\python.exe'
$script = Join-Path $PSScriptRoot 'key-green-video.py'
$handScript = Join-Path $PSScriptRoot 'track-character-hands.py'
foreach ($required in @($InputDirectory, $venvPython, $script, $handScript)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少绿幕处理所需文件：$required"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$tempRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('interactive-wallpaper-green-' + [Guid]::NewGuid().ToString('N'))))
$tempRootBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $tempRoot.StartsWith($tempRootBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to use a temp path outside the system temp directory.'
}
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
try {
    foreach ($input in Get-ChildItem -LiteralPath $InputDirectory -File -Filter '*.mp4' | Sort-Object Name) {
        $base = [IO.Path]::GetFileNameWithoutExtension($input.Name)
        $normalized = Join-Path $tempRoot ($base + '-normalized.mp4')
        $masks = Join-Path $tempRoot ($base + '-masks')
        $frames = Join-Path $tempRoot ($base + '-frames')
        New-Item -ItemType Directory -Force -Path $masks, $frames | Out-Null

        & ffmpeg -y -v error -i $input.FullName -vf fps=30 -an `
            -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -r 30 -video_track_timescale 30000 `
            $normalized
        if ($LASTEXITCODE -ne 0) {
            throw "视频帧率归一化失败：$($input.Name)"
        }

        $output = Join-Path $OutputDirectory ($base + '.mp4')
        $trackOutput = Join-Path $OutputDirectory ($base + '.handtrack.json')
        & $venvPython $script --input $normalized --output $output --masks $masks --frames $frames
        if ($LASTEXITCODE -ne 0) {
            throw "绿幕遮罩生成失败：$($input.Name)"
        }
        & $venvPython $handScript --input $normalized --output $trackOutput
        if ($LASTEXITCODE -ne 0) {
            throw "手部轨迹生成失败：$($input.Name)"
        }

        $maskPattern = Join-Path $masks '%06d.png'
        $framePattern = Join-Path $frames '%06d.jpg'
        $filter = '[0:v]scale=1280:720,setsar=1[color];[1:v]format=gray,scale=1280:720,setsar=1[mask];[color][mask]hstack=inputs=2[out]'
        & ffmpeg -y -v error `
            -framerate 30 -i $framePattern `
            -framerate 30 -i $maskPattern `
            -filter_complex $filter `
            -map '[out]' -an -c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p `
            -profile:v baseline -level:v 5.0 -bf 0 -refs 1 -g 30 -r 30 -fps_mode cfr `
            -color_primaries bt709 -color_trc bt709 -colorspace bt709 -movflags +faststart `
            $output
        if ($LASTEXITCODE -ne 0) {
            throw "绿幕视频编码失败：$($input.Name)"
        }
        Write-Host "已生成 Unity RGB+Alpha 视频：$output"
        Write-Host "已生成手部轨迹：$trackOutput"
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
