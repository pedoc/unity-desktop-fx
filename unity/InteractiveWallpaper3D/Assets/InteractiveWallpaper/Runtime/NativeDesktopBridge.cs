#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InteractiveWallpaper
{
    internal enum NativeStatus
    {
        Ok = 0,
        InvalidArgument = 1,
        ComInitializationFailed = 2,
        DesktopViewUnavailable = 3,
        SnapshotFailed = 4,
        AllocationFailed = 5,
        UnexpectedError = 6,
        ItemNotFound = 7,
        LaunchFailed = 8,
        WindowNotFound = 9,
        ExplorerDesktopUnavailable = 10,
        WindowAttachmentFailed = 11,
    }

    public readonly struct DesktopWindowAttachment
    {
        public DesktopWindowAttachment(bool windowFound, bool explorerDesktopFound, bool attached, int width, int height)
        {
            WindowFound = windowFound;
            ExplorerDesktopFound = explorerDesktopFound;
            Attached = attached;
            Width = width;
            Height = height;
        }

        public bool WindowFound { get; }
        public bool ExplorerDesktopFound { get; }
        public bool Attached { get; }
        public int Width { get; }
        public int Height { get; }
    }

    public static class NativeDesktopBridge
    {
        private const string LibraryName = "InteractiveWallpaper.Native";
        private const int ErrorCapacity = 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeUtf8Buffer
        {
            public IntPtr Data;
            public ulong Size;
            public IntPtr Owner;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSnapshotOptions
        {
            public uint StructSize;
            public IntPtr IconDirectory;
            public IntPtr IconSizes;
            public uint IconSizeCount;
            public uint PrimaryMonitorOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeWindowAttachment
        {
            public uint WindowFound;
            public uint ExplorerDesktopFound;
            public uint Attached;
            public int Width;
            public int Height;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern NativeStatus InteractiveWallpaperNative_CreateDesktopSnapshotJsonEx(
            ref NativeSnapshotOptions options,
            out NativeUtf8Buffer output,
            [Out] StringBuilder errorBuffer,
            uint errorBufferCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern NativeStatus InteractiveWallpaperNative_OpenDesktopItemByStableId(
            IntPtr stableId,
            out uint launched,
            [Out] StringBuilder errorBuffer,
            uint errorBufferCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern NativeStatus InteractiveWallpaperNative_AttachCurrentProcessWindow(
            out NativeWindowAttachment result,
            [Out] StringBuilder errorBuffer,
            uint errorBufferCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void InteractiveWallpaperNative_ReleaseUtf8Buffer(ref NativeUtf8Buffer buffer);

        public static string CaptureDesktopSnapshotJson(string iconDirectory, bool primaryMonitorOnly, params uint[] iconSizes)
        {
            var directoryPointer = Marshal.StringToHGlobalUni(iconDirectory);
            var sizes = iconSizes ?? Array.Empty<uint>();
            var sizesHandle = default(GCHandle);
            var output = default(NativeUtf8Buffer);
            try
            {
                var sizesPointer = IntPtr.Zero;
                if (sizes.Length > 0)
                {
                    sizesHandle = GCHandle.Alloc(sizes, GCHandleType.Pinned);
                    sizesPointer = sizesHandle.AddrOfPinnedObject();
                }

                var options = new NativeSnapshotOptions
                {
                    StructSize = (uint)Marshal.SizeOf<NativeSnapshotOptions>(),
                    IconDirectory = directoryPointer,
                    IconSizes = sizesPointer,
                    IconSizeCount = (uint)sizes.Length,
                    PrimaryMonitorOnly = primaryMonitorOnly ? 1U : 0U,
                };
                var error = new StringBuilder(ErrorCapacity);
                var status = InteractiveWallpaperNative_CreateDesktopSnapshotJsonEx(
                    ref options,
                    out output,
                    error,
                    ErrorCapacity);
                ThrowIfFailed(status, error);
                if (output.Data == IntPtr.Zero || output.Size == 0 || output.Size > int.MaxValue)
                {
                    throw new InvalidOperationException("Native desktop snapshot returned an invalid UTF-8 buffer.");
                }

                var bytes = new byte[(int)output.Size];
                Marshal.Copy(output.Data, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                if (output.Owner != IntPtr.Zero)
                {
                    InteractiveWallpaperNative_ReleaseUtf8Buffer(ref output);
                }
                if (sizesHandle.IsAllocated)
                {
                    sizesHandle.Free();
                }
                Marshal.FreeHGlobal(directoryPointer);
            }
        }

        public static bool OpenDesktopItem(string stableId, out string errorMessage)
        {
            var stableIdPointer = Marshal.StringToCoTaskMemUTF8(stableId);
            try
            {
                var error = new StringBuilder(ErrorCapacity);
                var status = InteractiveWallpaperNative_OpenDesktopItemByStableId(
                    stableIdPointer,
                    out var launched,
                    error,
                    ErrorCapacity);
                errorMessage = error.ToString();
                return status == NativeStatus.Ok && launched != 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(stableIdPointer);
            }
        }

        public static bool TryAttachCurrentProcessWindow(
            out DesktopWindowAttachment attachment,
            out string errorMessage)
        {
            var error = new StringBuilder(ErrorCapacity);
            var status = InteractiveWallpaperNative_AttachCurrentProcessWindow(
                out var native,
                error,
                ErrorCapacity);
            attachment = new DesktopWindowAttachment(
                native.WindowFound != 0,
                native.ExplorerDesktopFound != 0,
                native.Attached != 0,
                native.Width,
                native.Height);
            errorMessage = error.ToString();
            return status == NativeStatus.Ok && attachment.Attached;
        }

        private static void ThrowIfFailed(NativeStatus status, StringBuilder error)
        {
            if (status == NativeStatus.Ok)
            {
                return;
            }
            var message = error.Length > 0 ? error.ToString() : status.ToString();
            throw new InvalidOperationException($"Native desktop operation failed ({status}): {message}");
        }
    }
}