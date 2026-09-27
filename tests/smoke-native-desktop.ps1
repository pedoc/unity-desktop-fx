param(
    [Parameter(Mandatory = $true)] [string] $NativeLibrary,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$library = [IO.Path]::GetFullPath($NativeLibrary)
if (-not (Test-Path -LiteralPath $library)) {
    throw "Native library does not exist: $library"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path $OutputDirectory 'desktop-snapshot.json'
$literal = $library.Replace('"', '""')

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class InteractiveWallpaperNativeSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Utf8Buffer
    {
        public IntPtr Data;
        public UInt64 Size;
        public IntPtr Owner;
    }

    [DllImport(@"$literal", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int InteractiveWallpaperNative_CreateDesktopSnapshotJson(
        out Utf8Buffer output,
        StringBuilder errorBuffer,
        UInt32 errorBufferCapacity);

    [DllImport(@"$literal", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void InteractiveWallpaperNative_ReleaseUtf8Buffer(ref Utf8Buffer buffer);
}
"@

$errorBuffer = [Text.StringBuilder]::new(1024)
$buffer = [InteractiveWallpaperNativeSmoke+Utf8Buffer]::new()
try {
    $status = [InteractiveWallpaperNativeSmoke]::InteractiveWallpaperNative_CreateDesktopSnapshotJson(
        [ref] $buffer,
        $errorBuffer,
        [uint32] $errorBuffer.Capacity)
    if ($status -ne 0) {
        throw "Native desktop snapshot failed with status ${status}: $errorBuffer"
    }
    if ($buffer.Data -eq [IntPtr]::Zero -or $buffer.Size -eq 0 -or $buffer.Size -gt [int]::MaxValue) {
        throw 'Native desktop snapshot returned an invalid buffer.'
    }
    $bytes = [byte[]]::new([int] $buffer.Size)
    [Runtime.InteropServices.Marshal]::Copy($buffer.Data, $bytes, 0, $bytes.Length)
    $json = [Text.Encoding]::UTF8.GetString($bytes)
    Set-Content -LiteralPath $output -Value $json -Encoding utf8 -NoNewline
}
finally {
    if ($buffer.Owner -ne [IntPtr]::Zero) {
        [InteractiveWallpaperNativeSmoke]::InteractiveWallpaperNative_ReleaseUtf8Buffer([ref] $buffer)
    }
}

$snapshot = Get-Content -LiteralPath $output -Raw -Encoding utf8 | ConvertFrom-Json
if ($snapshot.schemaVersion -ne 1 -or $snapshot.itemCount -ne $snapshot.items.Count) {
    throw 'Native desktop snapshot contract is invalid.'
}
Write-Host "Native desktop snapshot verified: $($snapshot.itemCount) items."