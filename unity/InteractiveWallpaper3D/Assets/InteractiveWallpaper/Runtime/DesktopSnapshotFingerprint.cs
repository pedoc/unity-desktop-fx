#nullable enable

using System;

namespace InteractiveWallpaper
{
    public static class DesktopSnapshotFingerprint
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static string Compute(DesktopSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var hash = OffsetBasis;
            Add(ref hash, snapshot.virtualDesktop?.x ?? 0);
            Add(ref hash, snapshot.virtualDesktop?.y ?? 0);
            Add(ref hash, snapshot.virtualDesktop?.width ?? 0);
            Add(ref hash, snapshot.virtualDesktop?.height ?? 0);
            Add(ref hash, snapshot.view?.mode ?? 0);
            Add(ref hash, snapshot.view?.iconSize ?? 0);

            var sourceItems = snapshot.items ?? Array.Empty<DesktopItemSnapshot>();
            var items = new DesktopItemSnapshot[sourceItems.Length];
            Array.Copy(sourceItems, items, sourceItems.Length);
            Array.Sort(items, CompareItems);
            Add(ref hash, items.Length);
            foreach (var item in items)
            {
                Add(ref hash, item.stableId);
                Add(ref hash, item.displayName);
                Add(ref hash, item.parsingName);
                Add(ref hash, item.position?.x ?? 0);
                Add(ref hash, item.position?.y ?? 0);
            }
            return hash.ToString("X16");
        }

        private static int CompareItems(DesktopItemSnapshot? left, DesktopItemSnapshot? right)
        {
            return string.CompareOrdinal(left?.stableId, right?.stableId);
        }

        private static void Add(ref ulong hash, int value)
        {
            unchecked
            {
                Add(ref hash, (char)(value & 0xFFFF));
                Add(ref hash, (char)((value >> 16) & 0xFFFF));
            }
        }

        private static void Add(ref ulong hash, string? value)
        {
            foreach (var character in value ?? string.Empty)
            {
                Add(ref hash, character);
            }
            Add(ref hash, '\0');
        }

        private static void Add(ref ulong hash, char value)
        {
            unchecked
            {
                hash ^= value;
                hash *= Prime;
            }
        }
    }
}