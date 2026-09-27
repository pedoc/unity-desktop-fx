param()

$ErrorActionPreference = 'Stop'
$venv = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'InteractiveWallpaper\video-matting-venv'
$python = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $venv) | Out-Null
    python -m venv $venv
    if ($LASTEXITCODE -ne 0) {
        throw "创建视频抠像虚拟环境失败，退出码：$LASTEXITCODE"
    }
}

& $python -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) {
    throw "更新虚拟环境 pip 失败，退出码：$LASTEXITCODE"
}
& $python -m pip install --disable-pip-version-check `
    'mediapipe==0.10.21' `
    'numpy<2' `
    'opencv-contrib-python==4.11.0.86'
if ($LASTEXITCODE -ne 0) {
    throw "安装视频抠像依赖失败，退出码：$LASTEXITCODE"
}

& $python -c "import cv2, mediapipe as mp, numpy as np; print('Video matting ready:', 'OpenCV', cv2.__version__, 'MediaPipe', mp.__version__, 'NumPy', np.__version__)"
if ($LASTEXITCODE -ne 0) {
    throw '视频抠像环境验证失败。'
}
Write-Host "视频抠像环境已就绪：$venv"
