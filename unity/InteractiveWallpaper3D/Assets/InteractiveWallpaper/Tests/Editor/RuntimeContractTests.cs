#nullable enable

using NUnit.Framework;

namespace InteractiveWallpaper.Tests
{
    public sealed class RuntimeContractTests
    {
        [Test]
        public void DesktopCenterMapsToWorldOrigin()
        {
            var bounds = new VirtualDesktopBounds { x = -1920, y = 0, width = 4480, height = 1440 };
            var point = DesktopCoordinateMath.PixelToWorld(320, 720, bounds, 10);
            Assert.That(point.X, Is.EqualTo(0).Within(0.0001));
            Assert.That(point.Y, Is.EqualTo(0).Within(0.0001));
        }

        [Test]
        public void DesktopSnapshotFingerprintIgnoresCaptureTimeButDetectsPositionChanges()
        {
            var snapshot = new DesktopSnapshot
            {
                capturedAtUtc = "first",
                itemCount = 1,
                items = new[]
                {
                    new DesktopItemSnapshot
                    {
                        stableId = "desktop-test",
                        displayName = "Test",
                        parsingName = "Test",
                        position = new DesktopPoint { x = 10, y = 20 },
                    },
                },
            };
            var first = DesktopSnapshotFingerprint.Compute(snapshot);
            snapshot.capturedAtUtc = "second";
            Assert.That(DesktopSnapshotFingerprint.Compute(snapshot), Is.EqualTo(first));
            snapshot.items[0].position.x = 11;
            Assert.That(DesktopSnapshotFingerprint.Compute(snapshot), Is.Not.EqualTo(first));
        }
        [Test]
        public void CharacterImpactUsesRelativeVelocityAndClampsMagnitude()
        {
            var impulse = CharacterImpactMath.ComputeImpulse(
                0.2f,
                new UnityEngine.Vector3(10f, 4f, 0f),
                new UnityEngine.Vector3(1f, 0f, 0f),
                3f,
                2.5f);
            Assert.That(impulse.magnitude, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(impulse.x, Is.GreaterThan(0f));
            Assert.That(impulse.y, Is.GreaterThan(0f));
        }

        [Test]
        public void CharacterActionProgressIsSmoothAndBounded()
        {
            Assert.That(CharacterImpactMath.SmoothActionProgress(-1f, 1f), Is.EqualTo(0f));
            Assert.That(CharacterImpactMath.SmoothActionProgress(0.5f, 1f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(CharacterImpactMath.SmoothActionProgress(2f, 1f), Is.EqualTo(1f));
        }
        [Test]
        public void RuntimeStatusOverlayDoesNotUseImmediateModeGui()
        {
            var onGui = typeof(RuntimeStatusOverlay).GetMethod(
                "OnGUI",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(onGui, Is.Null, "Production status overlay must not use IMGUI OnGUI.");
        }
        [Test]
        public void DesktopWindowAttachmentPreservesNativeResult()
        {
            var attachment = new DesktopWindowAttachment(true, true, true, 4480, 1440);
            Assert.That(attachment.WindowFound, Is.True);
            Assert.That(attachment.ExplorerDesktopFound, Is.True);
            Assert.That(attachment.Attached, Is.True);
            Assert.That(attachment.Width, Is.EqualTo(4480));
            Assert.That(attachment.Height, Is.EqualTo(1440));
        }
    }
}