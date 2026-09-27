#pragma once

#include <cstdint>

namespace interactive_wallpaper::desktop
{
struct WindowAttachmentResult
{
    bool windowFound = false;
    bool explorerDesktopFound = false;
    bool attached = false;
    std::int32_t width = 0;
    std::int32_t height = 0;
};

WindowAttachmentResult AttachCurrentProcessWindow();
} // namespace interactive_wallpaper::desktop