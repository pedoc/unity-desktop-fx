#include <windows.h>

#include "interactive_wallpaper/native_api.h"
#include "interactive_wallpaper/version.h"

#include "desktop_actions_internal.h"
#include "desktop_snapshot_internal.h"
#include "desktop_window_internal.h"

#include <algorithm>
#include <exception>
#include <new>
#include <string>

namespace
{
void WriteError(wchar_t* buffer, std::uint32_t capacity, const std::string& message) noexcept
{
    if (buffer == nullptr || capacity == 0)
    {
        return;
    }

    const int required = MultiByteToWideChar(
        CP_UTF8,
        0,
        message.data(),
        static_cast<int>(message.size()),
        nullptr,
        0);
    if (required <= 0)
    {
        buffer[0] = L'\0';
        return;
    }

    const int writable = std::min(required, static_cast<int>(capacity - 1));
    MultiByteToWideChar(
        CP_UTF8,
        0,
        message.data(),
        static_cast<int>(message.size()),
        buffer,
        writable);
    buffer[writable] = L'\0';
}

void ClearError(wchar_t* buffer, std::uint32_t capacity) noexcept
{
    if (buffer != nullptr && capacity > 0)
    {
        buffer[0] = L'\0';
    }
}

RECT PrimaryMonitorBounds()
{
    const POINT primaryOrigin{0, 0};
    const HMONITOR monitor = MonitorFromPoint(primaryOrigin, MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO info{};
    info.cbSize = sizeof(info);
    if (monitor == nullptr || !GetMonitorInfoW(monitor, &info))
    {
        throw std::runtime_error("GetMonitorInfoW failed for the primary monitor");
    }
    return info.rcMonitor;
}

void RestrictToPrimaryMonitor(interactive_wallpaper::shell::DesktopSnapshot& snapshot)
{
    const RECT bounds = PrimaryMonitorBounds();
    std::erase_if(snapshot.items, [&](const auto& item)
    {
        return item.x < bounds.left || item.x >= bounds.right ||
            item.y < bounds.top || item.y >= bounds.bottom;
    });
    snapshot.virtualDesktopX = bounds.left;
    snapshot.virtualDesktopY = bounds.top;
    snapshot.virtualDesktopWidth = bounds.right - bounds.left;
    snapshot.virtualDesktopHeight = bounds.bottom - bounds.top;
}
} // namespace

InteractiveWallpaperNativeVersion InteractiveWallpaperNative_GetVersion() noexcept
{
    return {
        INTERACTIVE_WALLPAPER_VERSION_MAJOR,
        INTERACTIVE_WALLPAPER_VERSION_MINOR,
        INTERACTIVE_WALLPAPER_VERSION_PATCH,
    };
}

InteractiveWallpaperNativeStatus InteractiveWallpaperNative_CreateDesktopSnapshotJson(
    InteractiveWallpaperUtf8Buffer* output,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept
{
    return InteractiveWallpaperNative_CreateDesktopSnapshotJsonEx(
        nullptr,
        output,
        errorBuffer,
        errorBufferCapacity);
}

InteractiveWallpaperNativeStatus InteractiveWallpaperNative_CreateDesktopSnapshotJsonEx(
    const InteractiveWallpaperDesktopSnapshotOptions* options,
    InteractiveWallpaperUtf8Buffer* output,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept
{
    if (output == nullptr)
    {
        WriteError(errorBuffer, errorBufferCapacity, "output buffer is null");
        return InteractiveWallpaperNativeStatus::invalid_argument;
    }

    *output = {};
    ClearError(errorBuffer, errorBufferCapacity);

    try
    {
        interactive_wallpaper::shell::DesktopSnapshotOptions internalOptions;
        if (options != nullptr)
        {
            if (options->structSize < sizeof(InteractiveWallpaperDesktopSnapshotOptions))
            {
                WriteError(errorBuffer, errorBufferCapacity, "options structSize is invalid");
                return InteractiveWallpaperNativeStatus::invalid_argument;
            }
            if (options->iconDirectory != nullptr)
            {
                internalOptions.iconDirectory = options->iconDirectory;
            }
            if (options->iconSizeCount > 0 && options->iconSizes == nullptr)
            {
                WriteError(errorBuffer, errorBufferCapacity, "iconSizes is null while iconSizeCount is non-zero");
                return InteractiveWallpaperNativeStatus::invalid_argument;
            }
            for (std::uint32_t index = 0; index < options->iconSizeCount; ++index)
            {
                const std::uint32_t size = options->iconSizes[index];
                if (size < 16 || size > 512)
                {
                    WriteError(errorBuffer, errorBufferCapacity, "icon size must be between 16 and 512 pixels");
                    return InteractiveWallpaperNativeStatus::invalid_argument;
                }
                internalOptions.iconSizes.push_back(size);
            }
        }

        auto snapshot = interactive_wallpaper::shell::CaptureDesktopSnapshot(internalOptions);
        if (options != nullptr && options->primaryMonitorOnly != 0)
        {
            RestrictToPrimaryMonitor(snapshot);
        }
        auto json = interactive_wallpaper::shell::SerializeDesktopSnapshot(snapshot);
        auto* owner = new std::string(std::move(json));
        output->data = owner->data();
        output->size = static_cast<std::uint64_t>(owner->size());
        output->owner = owner;
        return InteractiveWallpaperNativeStatus::ok;
    }
    catch (const std::bad_alloc&)
    {
        WriteError(errorBuffer, errorBufferCapacity, "memory allocation failed");
        return InteractiveWallpaperNativeStatus::allocation_failed;
    }
    catch (const std::exception& error)
    {
        WriteError(errorBuffer, errorBufferCapacity, error.what());
        return InteractiveWallpaperNativeStatus::snapshot_failed;
    }
    catch (...)
    {
        WriteError(errorBuffer, errorBufferCapacity, "unexpected desktop snapshot error");
        return InteractiveWallpaperNativeStatus::unexpected_error;
    }
}

InteractiveWallpaperNativeStatus InteractiveWallpaperNative_OpenDesktopItemByStableId(
    const char* stableId,
    std::uint32_t* launched,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept
{
    if (stableId == nullptr || launched == nullptr)
    {
        WriteError(errorBuffer, errorBufferCapacity, "invalid desktop item launch argument");
        return InteractiveWallpaperNativeStatus::invalid_argument;
    }

    *launched = 0;
    ClearError(errorBuffer, errorBufferCapacity);
    try
    {
        const bool found = interactive_wallpaper::shell::OpenDesktopItemByStableId(stableId);
        if (!found)
        {
            WriteError(errorBuffer, errorBufferCapacity, "desktop item was not found");
            return InteractiveWallpaperNativeStatus::item_not_found;
        }
        *launched = 1;
        return InteractiveWallpaperNativeStatus::ok;
    }
    catch (const std::exception& error)
    {
        WriteError(errorBuffer, errorBufferCapacity, error.what());
        return InteractiveWallpaperNativeStatus::launch_failed;
    }
    catch (...)
    {
        WriteError(errorBuffer, errorBufferCapacity, "unexpected desktop item launch error");
        return InteractiveWallpaperNativeStatus::unexpected_error;
    }
}

InteractiveWallpaperNativeStatus InteractiveWallpaperNative_AttachCurrentProcessWindow(
    InteractiveWallpaperDesktopWindowAttachment* result,
    wchar_t* errorBuffer,
    std::uint32_t errorBufferCapacity) noexcept
{
    if (result == nullptr)
    {
        WriteError(errorBuffer, errorBufferCapacity, "window attachment result is null");
        return InteractiveWallpaperNativeStatus::invalid_argument;
    }

    *result = {};
    ClearError(errorBuffer, errorBufferCapacity);
    try
    {
        const auto attachment = interactive_wallpaper::desktop::AttachCurrentProcessWindow();
        result->windowFound = attachment.windowFound ? 1U : 0U;
        result->explorerDesktopFound = attachment.explorerDesktopFound ? 1U : 0U;
        result->attached = attachment.attached ? 1U : 0U;
        result->width = attachment.width;
        result->height = attachment.height;

        if (!attachment.windowFound)
        {
            WriteError(errorBuffer, errorBufferCapacity, "Unity top-level window was not found");
            return InteractiveWallpaperNativeStatus::window_not_found;
        }
        if (!attachment.explorerDesktopFound)
        {
            WriteError(errorBuffer, errorBufferCapacity, "Explorer desktop container was not found");
            return InteractiveWallpaperNativeStatus::explorer_desktop_unavailable;
        }
        if (!attachment.attached)
        {
            WriteError(errorBuffer, errorBufferCapacity, "Unity window was not attached to the desktop container");
            return InteractiveWallpaperNativeStatus::window_attachment_failed;
        }
        return InteractiveWallpaperNativeStatus::ok;
    }
    catch (const std::exception& error)
    {
        WriteError(errorBuffer, errorBufferCapacity, error.what());
        return InteractiveWallpaperNativeStatus::window_attachment_failed;
    }
    catch (...)
    {
        WriteError(errorBuffer, errorBufferCapacity, "unexpected desktop window attachment error");
        return InteractiveWallpaperNativeStatus::unexpected_error;
    }
}

void InteractiveWallpaperNative_ReleaseUtf8Buffer(InteractiveWallpaperUtf8Buffer* buffer) noexcept
{
    if (buffer == nullptr)
    {
        return;
    }

    delete static_cast<std::string*>(buffer->owner);
    *buffer = {};
}