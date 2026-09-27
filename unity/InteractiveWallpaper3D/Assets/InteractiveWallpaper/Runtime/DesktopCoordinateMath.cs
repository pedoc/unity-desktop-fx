#nullable enable

using System;

namespace InteractiveWallpaper
{
    public readonly struct RuntimeWorldPoint
    {
        public RuntimeWorldPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    public static class DesktopCoordinateMath
    {
        public static RuntimeWorldPoint PixelToWorld(
            double pixelX,
            double pixelY,
            VirtualDesktopBounds bounds,
            double worldHeight)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }
            if (bounds.width <= 0 || bounds.height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds), "Desktop bounds must be positive.");
            }
            if (worldHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldHeight));
            }

            var normalizedX = (pixelX - bounds.x) / bounds.width;
            var normalizedY = (pixelY - bounds.y) / bounds.height;
            var worldWidth = worldHeight * bounds.width / bounds.height;
            return new RuntimeWorldPoint(
                (normalizedX - 0.5) * worldWidth,
                (0.5 - normalizedY) * worldHeight);
        }
    }
}
