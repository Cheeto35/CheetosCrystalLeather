using System;

namespace CrystalLeather
{
    // How bright the glow is for a number of leather pieces. One piece gives a faint glow, a full bag's worth
    // lights up the area around you, and it stops getting brighter after that.
    public static class Brightness
    {
        public const float MinRange = 2.2f, MinIntensity = 0.65f;
        public const float MaxRange = 11f, MaxIntensity = 2.4f;
        public const int MaxLights = 8;
        public const float Rescan = 0.5f;
        // A normal Bag has 9 leather parts.
        public const int DefaultNFull = 9;

        public static void For(int count, int nFull, out float range, out float intensity)
        {
            if (count <= 0) { range = 0f; intensity = 0f; return; }
            float t = nFull <= 1 ? 1f : (count - 1f) / (nFull - 1f);
            t = (float)Math.Sqrt(Math.Max(0f, Math.Min(1f, t)));
            range = MinRange + (MaxRange - MinRange) * t;
            intensity = MinIntensity + (MaxIntensity - MinIntensity) * t;
        }
    }
}
