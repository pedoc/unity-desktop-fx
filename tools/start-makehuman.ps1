param(
    [switch] $Console,
    [switch] $NoShaders,
    [switch] $Wait,
    [switch] $AutoExport,
    [string] $ExportDirectory
)

$ErrorActionPreference = 'Stop'
$prefix = (& scoop prefix makehuman 2>$null | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($prefix) -or -not (Test-Path -LiteralPath $prefix)) {
    throw '未找到 Scoop MakeHuman 安装目录。请先确认 scoop list makehuman 能正常显示。'
}

$pythonRelative = if ($Console) { 'Python\python.exe' } else { 'Python\pythonw.exe' }
$python = Join-Path $prefix $pythonRelative
$script = Join-Path $prefix 'makehuman\makehuman.py'
$workingDirectory = Join-Path $prefix 'makehuman'
foreach ($required in @($python, $script, $workingDirectory)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "MakeHuman 文件缺失：$required"
    }
}

function Install-AutoExportPlugin {
    $pluginName = '9_zz_interactive_wallpaper_export'
    $pluginSource = Join-Path $PSScriptRoot "makehuman\$pluginName.py"
    if (-not (Test-Path -LiteralPath $pluginSource)) {
        throw "自动导出插件文件缺失：$pluginSource"
    }

    $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    $userRoot = Join-Path $documents 'makehuman\v1py3'
    $pluginDirectory = Join-Path $userRoot 'plugins'
    $pluginTarget = Join-Path $pluginDirectory "$pluginName.py"
    $settingsPath = Join-Path $userRoot 'settings.ini'

    New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
    Copy-Item -LiteralPath $pluginSource -Destination $pluginTarget -Force

    if (-not (Test-Path -LiteralPath $settingsPath)) {
        throw "MakeHuman 用户配置不存在：$settingsPath。请先正常启动一次 MakeHuman。"
    }

    $settingsText = Get-Content -LiteralPath $settingsPath -Raw
    $settings = $settingsText | ConvertFrom-Json
    $activePlugins = @($settings.activeUserPlugins)
    if ($activePlugins -notcontains $pluginName) {
        $settings.activeUserPlugins = @($activePlugins + $pluginName)
        $updatedSettings = $settings | ConvertTo-Json -Depth 20
        [IO.File]::WriteAllText($settingsPath, $updatedSettings + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }

    Write-Host "MakeHuman 自动导出插件已就绪：$pluginTarget"
}

$arguments = @('"' + $script + '"')
if ($NoShaders) {
    $arguments += '--noshaders'
}

# NetSarang Xmanager ships a 32-bit opengl32.dll and places its directory before
# Windows System32 on PATH. The bundled 64-bit MakeHuman Python then loads the
# wrong DLL and exits with WinError 193. Only change PATH for the child process.
$previousAutoExport = $env:INTERACTIVE_WALLPAPER_MH_AUTO_EXPORT
$previousExportDirectory = $env:INTERACTIVE_WALLPAPER_MH_EXPORT_DIR
if ($AutoExport) {
    Install-AutoExportPlugin
    if ([string]::IsNullOrWhiteSpace($ExportDirectory)) {
        $ExportDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\out\makehuman\DefaultHuman'))
    }
    New-Item -ItemType Directory -Force -Path $ExportDirectory | Out-Null
    $env:INTERACTIVE_WALLPAPER_MH_AUTO_EXPORT = '1'
    $env:INTERACTIVE_WALLPAPER_MH_EXPORT_DIR = [IO.Path]::GetFullPath($ExportDirectory)
}
$originalPath = $env:PATH
$env:PATH = "$env:WINDIR\System32;$originalPath"
try {
    $process = Start-Process `
        -FilePath $python `
        -ArgumentList $arguments `
        -WorkingDirectory $workingDirectory `
        -PassThru
    if ($Wait) {
        $process.WaitForExit()
        exit $process.ExitCode
    }
    Write-Host "MakeHuman 已启动，PID=$($process.Id)"
}
finally {
    $env:PATH = $originalPath
    $env:INTERACTIVE_WALLPAPER_MH_AUTO_EXPORT = $previousAutoExport
    $env:INTERACTIVE_WALLPAPER_MH_EXPORT_DIR = $previousExportDirectory
}
