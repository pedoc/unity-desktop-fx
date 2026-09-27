#pragma once

#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

namespace interactive_wallpaper::shell
{
struct IconAssetSnapshot
{
    std::uint32_t size = 0;
    std::string path;
};

struct DesktopItemSnapshot
{
    std::string stableId;
    std::string displayName;
    std::string parsingName;
    std::string typeName;
    std::string pidlHex;
    std::int32_t x = 0;
    std::int32_t y = 0;
    std::int32_t systemIconIndex = -1;
    std::uint32_t attributes = 0;
    bool isFileSystem = false;
    bool isFolder = false;
    bool isLink = false;
    bool canRename = false;
    bool canDelete = false;
    std::vector<IconAssetSnapshot> icons;
};

struct SnapshotWarning
{
    std::int32_t itemIndex = -1;
    std::int32_t hresult = 0;
    std::string operation;
};

struct DesktopSnapshotOptions
{
    std::filesystem::path iconDirectory;
    std::vector<std::uint32_t> iconSizes;
};

struct DesktopSnapshot
{
    std::uint32_t schemaVersion = 1;
    std::string capturedAtUtc;
    std::string explorerWindowHandle;
    std::int32_t viewMode = 0;
    std::int32_t iconSize = 0;
    std::int32_t virtualDesktopX = 0;
    std::int32_t virtualDesktopY = 0;
    std::int32_t virtualDesktopWidth = 0;
    std::int32_t virtualDesktopHeight = 0;
    std::int32_t spacingX = 0;
    std::int32_t spacingY = 0;
    bool autoArrange = false;
    std::vector<DesktopItemSnapshot> items;
    std::vector<SnapshotWarning> warnings;
};

DesktopSnapshot CaptureDesktopSnapshot(const DesktopSnapshotOptions& options = {});
std::string SerializeDesktopSnapshot(const DesktopSnapshot& snapshot);
std::string HResultMessage(long hresult);
} // namespace interactive_wallpaper::shell
