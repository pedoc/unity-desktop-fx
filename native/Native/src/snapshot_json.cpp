#include "desktop_snapshot_internal.h"

#include <iomanip>
#include <sstream>
#include <string_view>

namespace interactive_wallpaper::shell
{
namespace
{
std::string EscapeJson(std::string_view value)
{
    std::ostringstream output;
    for (const unsigned char character : value)
    {
        switch (character)
        {
        case '"': output << "\\\""; break;
        case '\\': output << "\\\\"; break;
        case '\b': output << "\\b"; break;
        case '\f': output << "\\f"; break;
        case '\n': output << "\\n"; break;
        case '\r': output << "\\r"; break;
        case '\t': output << "\\t"; break;
        default:
            if (character < 0x20)
            {
                output << "\\u"
                       << std::hex << std::uppercase << std::setfill('0')
                       << std::setw(4) << static_cast<unsigned int>(character)
                       << std::dec;
            }
            else
            {
                output << static_cast<char>(character);
            }
            break;
        }
    }
    return output.str();
}

void WriteString(std::ostringstream& output, std::string_view value)
{
    output << '"' << EscapeJson(value) << '"';
}

void WriteBool(std::ostringstream& output, bool value)
{
    output << (value ? "true" : "false");
}
} // namespace

std::string SerializeDesktopSnapshot(const DesktopSnapshot& snapshot)
{
    std::ostringstream output;
    output << "{\n";
    output << "  \"schemaVersion\": " << snapshot.schemaVersion << ",\n";
    output << "  \"capturedAtUtc\": "; WriteString(output, snapshot.capturedAtUtc); output << ",\n";
    output << "  \"source\": \"explorer-desktop-folder-view\",\n";
    output << "  \"explorerWindowHandle\": "; WriteString(output, snapshot.explorerWindowHandle); output << ",\n";
    output << "  \"virtualDesktop\": { \"x\": " << snapshot.virtualDesktopX
           << ", \"y\": " << snapshot.virtualDesktopY
           << ", \"width\": " << snapshot.virtualDesktopWidth
           << ", \"height\": " << snapshot.virtualDesktopHeight << " },\n";
    output << "  \"view\": {\n";
    output << "    \"mode\": " << snapshot.viewMode << ",\n";
    output << "    \"iconSize\": " << snapshot.iconSize << ",\n";
    output << "    \"spacing\": { \"x\": " << snapshot.spacingX << ", \"y\": " << snapshot.spacingY << " },\n";
    output << "    \"autoArrange\": "; WriteBool(output, snapshot.autoArrange); output << "\n";
    output << "  },\n";
    output << "  \"itemCount\": " << snapshot.items.size() << ",\n";
    output << "  \"items\": [\n";

    for (std::size_t index = 0; index < snapshot.items.size(); ++index)
    {
        const auto& item = snapshot.items[index];
        output << "    {\n";
        output << "      \"stableId\": "; WriteString(output, item.stableId); output << ",\n";
        output << "      \"displayName\": "; WriteString(output, item.displayName); output << ",\n";
        output << "      \"parsingName\": "; WriteString(output, item.parsingName); output << ",\n";
        output << "      \"typeName\": "; WriteString(output, item.typeName); output << ",\n";
        output << "      \"position\": { \"x\": " << item.x << ", \"y\": " << item.y << " },\n";
        output << "      \"systemIconIndex\": " << item.systemIconIndex << ",\n";
        output << "      \"attributes\": " << item.attributes << ",\n";
        output << "      \"capabilities\": {\n";
        output << "        \"fileSystem\": "; WriteBool(output, item.isFileSystem); output << ",\n";
        output << "        \"folder\": "; WriteBool(output, item.isFolder); output << ",\n";
        output << "        \"link\": "; WriteBool(output, item.isLink); output << ",\n";
        output << "        \"rename\": "; WriteBool(output, item.canRename); output << ",\n";
        output << "        \"delete\": "; WriteBool(output, item.canDelete); output << "\n";
        output << "      },\n";
        output << "      \"icons\": [";
        for (std::size_t iconIndex = 0; iconIndex < item.icons.size(); ++iconIndex)
        {
            const auto& icon = item.icons[iconIndex];
            if (iconIndex != 0)
            {
                output << ", ";
            }
            output << "{ \"size\": " << icon.size << ", \"path\": ";
            WriteString(output, icon.path);
            output << " }";
        }
        output << "],\n";
        output << "      \"pidlHex\": "; WriteString(output, item.pidlHex); output << "\n";
        output << "    }";
        if (index + 1 != snapshot.items.size())
        {
            output << ',';
        }
        output << "\n";
    }

    output << "  ],\n";
    output << "  \"warnings\": [\n";
    for (std::size_t index = 0; index < snapshot.warnings.size(); ++index)
    {
        const auto& warning = snapshot.warnings[index];
        output << "    { \"itemIndex\": " << warning.itemIndex
               << ", \"hresult\": " << warning.hresult
               << ", \"operation\": ";
        WriteString(output, warning.operation);
        output << " }";
        if (index + 1 != snapshot.warnings.size())
        {
            output << ',';
        }
        output << "\n";
    }
    output << "  ]\n";
    output << "}\n";
    return output.str();
}
} // namespace interactive_wallpaper::shell
