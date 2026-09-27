#nullable enable

using System;

namespace InteractiveWallpaper
{
    [Serializable]
    public sealed class DesktopSnapshot
    {
        public int schemaVersion;
        public string capturedAtUtc = string.Empty;
        public VirtualDesktopBounds virtualDesktop = new VirtualDesktopBounds();
        public DesktopViewSnapshot view = new DesktopViewSnapshot();
        public int itemCount;
        public DesktopItemSnapshot[] items = Array.Empty<DesktopItemSnapshot>();
        public DesktopSnapshotWarning[] warnings = Array.Empty<DesktopSnapshotWarning>();
    }

    [Serializable]
    public sealed class VirtualDesktopBounds
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class DesktopViewSnapshot
    {
        public int mode;
        public int iconSize;
        public DesktopPoint spacing = new DesktopPoint();
        public bool autoArrange;
    }
    [Serializable]
    public sealed class DesktopItemSnapshot
    {
        public string stableId = string.Empty;
        public string displayName = string.Empty;
        public string parsingName = string.Empty;
        public string typeName = string.Empty;
        public DesktopPoint position = new DesktopPoint();
        public int systemIconIndex;
        public DesktopItemCapabilities capabilities = new DesktopItemCapabilities();
        public DesktopIconAsset[] icons = Array.Empty<DesktopIconAsset>();
    }

    [Serializable]
    public sealed class DesktopPoint
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class DesktopItemCapabilities
    {
        public bool fileSystem;
        public bool folder;
        public bool link;
        public bool rename;
        public bool delete;
    }

    [Serializable]
    public sealed class DesktopIconAsset
    {
        public int size;
        public string path = string.Empty;
    }

    [Serializable]
    public sealed class DesktopSnapshotWarning
    {
        public int itemIndex;
        public int hresult;
        public string operation = string.Empty;
    }
}
