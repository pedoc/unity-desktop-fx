#pragma once

#include <cstdint>

#if defined(INTERACTIVE_WALLPAPER_NATIVE_EXPORTS)
#define INTERACTIVE_WALLPAPER_NATIVE_API __declspec(dllexport)
#else
#define INTERACTIVE_WALLPAPER_NATIVE_API __declspec(dllimport)
#endif

extern "C" {

struct InteractiveWallpaperNativeVersion
{
    std::uint32_t major;
    std::uint32_t minor;
    std::uint32_t patch;
};

enum class InteractiveWallpaperNativeStatus : std::int32_t
{
    ok = 0,
    invalid_argument = 1,
    com_initialization_failed = 2,
    desktop_view_unavailable = 3,
    snapshot_failed = 4,
    allocation_failed = 5,
    unexpected_error = 6,
    item_not_found = 7,
    launch_failed = 8,
    window_not_found = 9,
    explorer_desktop_unavailable = 10,
    window_attachment_failed = 11,
};

struct InteractiveWallpaperUtf8Buffer
{
    const char* data;
    std::uint64_t size;
    void* owner;
};

struct InteractiveWallpaperDesktopSnapshotOptions
{
    std::uint32_t structSize;
    const wchar_t* iconDirectory;
    const std::uint32_t* iconSizes;
    std::uint32_t iconSizeCount;
    std::uint32_t primaryMonitorOnly;
};

struct InteractiveWallpaperDesktopWindowAttachment
{
    std::uint32_t windowFound;
    std::uint32_t explorerDesktopFound;
    std::uint32_t attached;
    std::int32_t width;
    std::int32_t height;
};

INTERACTIVE_WALLPAPER_NATIVE_API InteractiveWallpaperNativeVersion InteractiveWallpaperNative_GetVersion() noexcept;

INTERACTIVE_WALLPAPER_NATIVE_API InteractiveWallpaperNativeStatus InteractiveWallpaperNative_CreateDesktopSnapshotJson(
    InteractiveWallpaperUtf8Buffer* output,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept;

INTERACTIVE_WALLPAPER_NATIVE_API InteractiveWallpaperNativeStatus InteractiveWallpaperNative_CreateDesktopSnapshotJsonEx(
    const InteractiveWallpaperDesktopSnapshotOptions* options,
    InteractiveWallpaperUtf8Buffer* output,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept;

INTERACTIVE_WALLPAPER_NATIVE_API InteractiveWallpaperNativeStatus InteractiveWallpaperNative_OpenDesktopItemByStableId(
    const char* stableId,
    std::uint32_t* launched,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept;

// Places the Unity window in the Explorer desktop container above the native icon view.
// Explorer icons are never hidden, moved, disabled, or otherwise modified.
INTERACTIVE_WALLPAPER_NATIVE_API InteractiveWallpaperNativeStatus InteractiveWallpaperNative_AttachCurrentProcessWindow(
    InteractiveWallpaperDesktopWindowAttachment* result,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept;

INTERACTIVE_WALLPAPER_NATIVE_API void InteractiveWallpaperNative_ReleaseUtf8Buffer(
    InteractiveWallpaperUtf8Buffer* buffer) noexcept;

}