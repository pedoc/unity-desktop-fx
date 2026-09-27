#nullable enable

using UnityEngine;

namespace InteractiveWallpaper
{
    public static class CharacterImpactMath
    {
        public static Vector3 ComputeImpulse(
            float targetMass,
            Vector3 limbVelocity,
            Vector3 targetVelocity,
            float strength,
            float maximumImpulse)
        {
            var safeMass = Mathf.Max(0.01f, targetMass);
            var relativeVelocity = limbVelocity - targetVelocity;
            var impulse = relativeVelocity * (safeMass * Mathf.Max(0f, strength));
            return Vector3.ClampMagnitude(impulse, Mathf.Max(0f, maximumImpulse));
        }

        public static float SmoothActionProgress(float elapsed, float duration)
        {
            var value = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
            return value * value * (3f - 2f * value);
        }
    }
}