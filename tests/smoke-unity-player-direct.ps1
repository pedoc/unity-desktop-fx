param(
    [Parameter(Mandatory = $true)] [string] $PlayerExecutable,
    [Parameter(Mandatory = $true)] [string] $NativeLibrary,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$player = [IO.Path]::GetFullPath($PlayerExecutable)
$native = [IO.Path]::GetFullPath($NativeLibrary)
if (-not (Test-Path -LiteralPath $player)) { throw "Player does not exist: $player" }
if (-not (Test-Path -LiteralPath $native)) { throw "Native library does not exist: $native" }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class InteractiveWallpaperShortcutSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    private static void PressShortcut(byte key, int holdMilliseconds = 750)
    {
        const uint KeyUp = 0x0002;
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        Thread.Sleep(holdMilliseconds);
        keybd_event(key, 0, KeyUp, UIntPtr.Zero);
        keybd_event(0x12, 0, KeyUp, UIntPtr.Zero);
        keybd_event(0x11, 0, KeyUp, UIntPtr.Zero);
    }

    public static void PressGravityShortcut() { PressShortcut(0x47); }
    public static void PressKickShortcut() { PressShortcut(0x4B); }
    public static void PressSweepShortcut() { PressShortcut(0x57); }
    public static void PressShockwaveShortcut() { PressShortcut(0x42); }
    public static void PressShockwaveShortcutFast() { PressShortcut(0x42, 120); }
    public static void PressResetShortcut() { PressShortcut(0x52); }
    public static void PressSneezeShortcut() { PressShortcut(0x53); }
    public static void PressOverlayShortcut() { PressShortcut(0x48); }
    public static void PressQuitShortcut() { PressShortcut(0x51); }
    public static int GetCursorX() { Point point; return GetCursorPos(out point) ? point.X : -1; }
    public static int GetCursorY() { Point point; return GetCursorPos(out point) ? point.Y : -1; }
}
"@

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$playerDirectory = Split-Path -Parent $player
$deployedNative = Join-Path $playerDirectory 'InteractiveWallpaper.Native.dll'
Copy-Item -LiteralPath $native -Destination $deployedNative -Force
$log = Join-Path $OutputDirectory 'player.log'
if (Test-Path -LiteralPath $log) { Clear-Content -LiteralPath $log }
$playerPid = 0
$syncFile = $null

function Read-SharedText([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    catch { return '' }
}

try {
    $process = Start-Process `
        -FilePath $player `
        -ArgumentList @('-screen-fullscreen', '0', '-logFile', $log) `
        -PassThru
    if ($null -eq $process) { throw 'Unity runtime process was not created.' }
    $playerPid = $process.Id

    $deadline = (Get-Date).AddSeconds(90)
    $ready = $false
    do {
        Start-Sleep -Milliseconds 250
        $content = Read-SharedText $log
        $ready = $content -like '*Desktop window attached:*'
        if ($content -like '*DllNotFoundException*' -or $content -like '*Interactive Wallpaper startup failed*') {
            $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 120 -Encoding utf8) -join "`n" } else { '<no log>' }
            throw "Direct Unity runtime failed to initialize.`n$tail"
        }
        $running = Get-Process -Id $playerPid -ErrorAction SilentlyContinue
        if ($null -eq $running -and -not $ready) {
            throw 'Direct Unity runtime exited before attaching to the desktop.'
        }
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) {
        $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 120 -Encoding utf8) -join "`n" } else { '<no log>' }
        throw "Direct Unity runtime did not attach to the desktop.`n$tail"
    }

    $snapshotDeadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 250
        $content = Read-SharedText $log
        $snapshotReady = $content -like '*Primary desktop snapshot:*' -or $content -like '*Cached desktop snapshot:*'
    } while (-not $snapshotReady -and (Get-Date) -lt $snapshotDeadline)

    $attachmentLogIndex = $content.IndexOf('Desktop window attached:')
    $snapshotLogIndex = $content.IndexOf('Primary desktop snapshot:')
    if ($snapshotLogIndex -lt 0) { $snapshotLogIndex = $content.IndexOf('Cached desktop snapshot:') }
    if ($attachmentLogIndex -lt 0 -or $snapshotLogIndex -lt 0 -or $attachmentLogIndex -gt $snapshotLogIndex) {
        throw 'Desktop attachment did not complete before the expensive icon snapshot.'
    }
    if ($content -like '*InvalidOperationException*' -or
        $content -like '*NotSupportedException*' -or
        $content -like '*Setting linear velocity of a kinematic body is not supported*' -or
        $content -like '*Setting angular velocity of a kinematic body is not supported*') {
        $tail = (Get-Content -LiteralPath $log -Tail 160 -Encoding utf8) -join "`n"
        throw "Direct Unity runtime produced unexpected errors after attaching.`n$tail"
    }

    $attachment = [regex]::Match($content, 'Desktop window attached: (\d+)x(\d+)')
    $snapshot = [regex]::Match($content, '(?:Primary|Cached) desktop snapshot: (\d+)x(\d+); .*items=(\d+)')
    if (-not $attachment.Success -or -not $snapshot.Success) {
        throw 'Primary-display attachment diagnostics are missing from the Player log.'
    }

    $iconReadyDeadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $iconsReady = $content -like '*Desktop proxy icons ready:*'
    } while (-not $iconsReady -and (Get-Date) -lt $iconReadyDeadline)
    if (-not $iconsReady) {
        throw 'Desktop proxy icons did not finish building before shortcut effects were tested.'
    }
    $primary = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    foreach ($match in @($attachment, $snapshot)) {
        if ([int] $match.Groups[1].Value -ne $primary.Width -or
            [int] $match.Groups[2].Value -ne $primary.Height) {
            throw "Runtime size $($match.Groups[1].Value)x$($match.Groups[2].Value) does not match primary display $($primary.Width)x$($primary.Height)."
        }
    }

    $content = Read-SharedText $log
    $hiddenCountBefore = [regex]::Matches($content, 'Runtime status overlay hidden').Count
    [InteractiveWallpaperShortcutSmoke]::PressOverlayShortcut()
    $overlayHideDeadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $overlayHidden = [regex]::Matches($content, 'Runtime status overlay hidden').Count -gt $hiddenCountBefore
    } while (-not $overlayHidden -and (Get-Date) -lt $overlayHideDeadline)
    if (-not $overlayHidden) { throw 'Simplified Ctrl+Alt+H shortcut did not hide the runtime status overlay.' }

    Start-Sleep -Milliseconds 250
    $visibleCountBefore = [regex]::Matches($content, 'Runtime status overlay visible').Count
    [InteractiveWallpaperShortcutSmoke]::PressOverlayShortcut()
    $overlayShowDeadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $overlayVisible = [regex]::Matches($content, 'Runtime status overlay visible').Count -gt $visibleCountBefore
    } while (-not $overlayVisible -and (Get-Date) -lt $overlayShowDeadline)
    if (-not $overlayVisible) { throw 'Simplified Ctrl+Alt+H shortcut did not restore the runtime status overlay.' }
    $desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $syncFile = Join-Path $desktopDirectory ("InteractiveWallpaper-SyncTest-" + [Guid]::NewGuid().ToString('N') + '.txt')
    Set-Content -LiteralPath $syncFile -Value 'interactive wallpaper synchronization test' -Encoding utf8
    $syncDeadline = (Get-Date).AddSeconds(60)
    $synchronized = $false
    do {
        Start-Sleep -Milliseconds 250
        $content = Read-SharedText $log
        $synchronized = $content -like '*Desktop proxy icons synchronized:*'
    } while (-not $synchronized -and (Get-Date) -lt $syncDeadline)
    if (-not $synchronized) {
        throw 'Desktop proxy synchronization did not detect a newly created desktop item.'
    }
    [InteractiveWallpaperShortcutSmoke]::PressGravityShortcut()
    $effectDeadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $gravityWorked = $content -like '*Desktop effect: gravity drop*'
    } while (-not $gravityWorked -and (Get-Date) -lt $effectDeadline)
    if (-not $gravityWorked) { throw 'Global gravity-drop shortcut did not trigger the desktop effect.' }


    Start-Sleep -Seconds 3
    $content = Read-SharedText $log
    $videoCharacterMode = $content -like '*Video character prepared:*'
    if ($videoCharacterMode) {
        $sneezeCountBefore = [regex]::Matches($content, 'Desktop effect: character sneeze burst').Count
        [InteractiveWallpaperShortcutSmoke]::PressResetShortcut()
        Start-Sleep -Milliseconds 250
        [InteractiveWallpaperShortcutSmoke]::PressSneezeShortcut()
        $sneezeDeadline = (Get-Date).AddSeconds(10)
        $sneezeWorked = $false
        do {
            Start-Sleep -Milliseconds 100
            $content = Read-SharedText $log
            $sneezeCountAfter = [regex]::Matches($content, 'Desktop effect: character sneeze burst').Count
            $sneezeWorked = $sneezeCountAfter -gt $sneezeCountBefore
        } while (-not $sneezeWorked -and (Get-Date) -lt $sneezeDeadline)
        if (-not $sneezeWorked) {
            throw 'Global sneeze shortcut did not trigger the video-character icon effect.'
        }
    }
    else {
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(420, ($primary.Height - 120))
        Start-Sleep -Milliseconds 200
        [InteractiveWallpaperShortcutSmoke]::PressKickShortcut()
        $kickDeadline = (Get-Date).AddSeconds(25)
        $kickContactWorked = $false
        do {
            Start-Sleep -Milliseconds 150
            $content = Read-SharedText $log
            $kickContactWorked = $content -like '*Character kick contact:*'
            $running = Get-Process -Id $playerPid -ErrorAction SilentlyContinue
            if ($null -eq $running) { break }
        } while (-not $kickContactWorked -and (Get-Date) -lt $kickDeadline)
        if (-not $kickContactWorked) {
            throw 'Character kick action did not reach its physical contact event.'
        }
    }


    Start-Sleep -Seconds 1
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(400, 300)
    Start-Sleep -Milliseconds 200
    $sweepCursorX = [InteractiveWallpaperShortcutSmoke]::GetCursorX()
    $sweepCursorY = [InteractiveWallpaperShortcutSmoke]::GetCursorY()
    [InteractiveWallpaperShortcutSmoke]::PressSweepShortcut()
    $sweepDeadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $sweepWorked = $content -like '*Desktop effect: sweep all*'
    } while (-not $sweepWorked -and (Get-Date) -lt $sweepDeadline)
    if (-not $sweepWorked) { throw 'Global sweep shortcut did not trigger the desktop effect.' }

    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(800, 600)
    Start-Sleep -Milliseconds 200
    $shockCursorX = [InteractiveWallpaperShortcutSmoke]::GetCursorX()
    $shockCursorY = [InteractiveWallpaperShortcutSmoke]::GetCursorY()
    [InteractiveWallpaperShortcutSmoke]::PressShockwaveShortcut()
    $shockwaveDeadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $shockwaveWorked = $content -like '*Desktop effect: shockwave*'
    } while (-not $shockwaveWorked -and (Get-Date) -lt $shockwaveDeadline)
    if (-not $shockwaveWorked) { throw 'Global shockwave shortcut did not trigger the desktop effect.' }

    $sweepMatch = [regex]::Matches($content, 'Desktop effect: sweep all \(\d+ icons from ([-\d.]+), ([-\d.]+)\)') | Select-Object -Last 1
    $shockwaveMatch = [regex]::Matches($content, 'Desktop effect: shockwave \(\d+ icons from ([-\d.]+), ([-\d.]+)\)') | Select-Object -Last 1
    if (-not $sweepMatch.Success -or -not $shockwaveMatch.Success) {
        throw 'Mouse-position effect diagnostics are missing from the Player log.'
    }
    $sweepX = [double]$sweepMatch.Groups[1].Value
    $sweepY = [double]$sweepMatch.Groups[2].Value
    $shockX = [double]$shockwaveMatch.Groups[1].Value
    $shockY = [double]$shockwaveMatch.Groups[2].Value
    $worldWidth = 10.0 * [int]$attachment.Groups[1].Value / [int]$attachment.Groups[2].Value
    if ([Math]::Abs($sweepX) -gt $worldWidth * 0.5 -or
        [Math]::Abs($shockX) -gt $worldWidth * 0.5 -or
        [Math]::Abs($sweepY) -gt 5.0 -or
        [Math]::Abs($shockY) -gt 5.0 -or
        ([Math]::Abs($sweepX - $shockX) -lt 0.1 -and [Math]::Abs($sweepY - $shockY) -lt 0.1)) {
        throw 'Sweep or shockwave produced an invalid mouse-derived world position.'
    }
    [InteractiveWallpaperShortcutSmoke]::PressResetShortcut()
    $resetDeadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $content = Read-SharedText $log
        $resetWorked = $content -like '*Desktop effect: reset all*'
    } while (-not $resetWorked -and (Get-Date) -lt $resetDeadline)
    if (-not $resetWorked) { throw 'Global reset shortcut did not reset the desktop icons.' }


    for ($index = 0; $index -lt 12; $index++) {
        [InteractiveWallpaperShortcutSmoke]::PressShockwaveShortcutFast()
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Seconds 3
    if (-not (Get-Process -Id $playerPid -ErrorAction SilentlyContinue)) {
        throw 'Runtime crashed during repeated effect stress testing.'
    }
    $content = Read-SharedText $log
    if ($content -like '*Crash!!!*') {
        throw 'Runtime crash marker was written during repeated effect stress testing.'
    }
    if ($syncFile -and (Test-Path -LiteralPath $syncFile)) {
        Remove-Item -LiteralPath $syncFile -Force
        $syncFile = $null
    }
    [InteractiveWallpaperShortcutSmoke]::PressQuitShortcut()
    $quitDeadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        $running = Get-Process -Id $playerPid -ErrorAction SilentlyContinue
    } while ($null -ne $running -and (Get-Date) -lt $quitDeadline)
    if ($null -ne $running) {
        $content = Read-SharedText $log
        $shutdownLogged = $content -like '*Input Polling Thread exited*' -and
            $content -like '*Input System module state changed to: Shutdown*'
        if (-not $shutdownLogged) {
            throw 'Global Ctrl+Alt+Q shortcut did not exit the runtime.'
        }
    }
    $playerPid = 0
    $syncFile = $null

    Write-Host "Direct Unity runtime matched the primary display ($($primary.Width)x$($primary.Height)) and the global exit shortcut worked."
}
finally {
    if ($syncFile -and (Test-Path -LiteralPath $syncFile)) {
        Remove-Item -LiteralPath $syncFile -Force -ErrorAction SilentlyContinue
    }
    if ($playerPid -gt 0 -and (Get-Process -Id $playerPid -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $playerPid -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $playerPid -Timeout 10 -ErrorAction SilentlyContinue
    }
}
