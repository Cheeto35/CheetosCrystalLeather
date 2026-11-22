using System;

namespace CrystalLeather
{
    public enum Tint { Blue, Green, Purple }

    // The numbers: which color a crystal wyrm is on each mine level, and how much tougher every wyrm gets
    // the deeper you go.
    public static class WyrmTiers
    {
        public const int DepthCap = 150;
        public const float SpeedCap = 2f;
        public const float RageAt = 0.5f, RageSpeed = 1.2f;
        public const float PurpleBlunt = 0.75f;
        public const float GreenFlurry = 0.20f, VanillaFlurry = 0.05f;
        public const float GreenThink = 0.6f;
        public const float GreenStayOut = 15f;
        public const float BlueFloor = 0.15f;

        // (level, chance) points. The chance goes in a straight line between them, is 0 before the first one
        // and stays flat after the last one.
        static readonly float[,] GreenPts = { { 50, 0.05f }, { 100, 0.45f }, { 125, 0.40f }, { 150, 0.40f } };
        static readonly float[,] PurplePts = { { 100, 0.05f }, { 150, 0.45f } };

        static float Curve(float[,] pts, int level)
        {
            int n = pts.GetLength(0);
            if (level < pts[0, 0]) return 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = pts[i, 0], x1 = pts[i + 1, 0];
                if (level <= x1)
                {
                    float t = (level - x0) / (x1 - x0);
                    return pts[i, 1] + (pts[i + 1, 1] - pts[i, 1]) * t;
                }
            }
            return pts[n - 1, 1];
        }

        public static void Shares(int level, out float blue, out float green, out float purple)
        {
            green = Curve(GreenPts, level);
            purple = Curve(PurplePts, level);
            blue = 1f - green - purple;
            if (blue < BlueFloor)
            {
                float scale = (1f - BlueFloor) / (green + purple);
                green *= scale; purple *= scale; blue = BlueFloor;
            }
        }

        // Turns an id into a number from 0 to 1 that's always the same for that id.
        public static float Hash01(uint id)
        {
            uint x = id ^ 0x9E3779B9u;
            x ^= x >> 16; x = unchecked(x * 0x7FEB352Du);
            x ^= x >> 15; x = unchecked(x * 0x846CA68Bu);
            x ^= x >> 16;
            return (x >> 8) / 16777216f;
        }

        public static Tint Roll(uint id, int level) { return Pick(Hash01(id), level); }

        // Picks a color from a random number between 0 and 1, using that level's chances.
        public static Tint Pick(float u, int level)
        {
            float b, g, p;
            Shares(level, out b, out g, out p);
            if (u < b) return Tint.Blue;
            if (u < b + g) return Tint.Green;
            return Tint.Purple;
        }

        // Creature ids. The game sends these as 16-bit numbers, so they have to stay under 65536.
        // Green and purple are this mod's own creatures.
        public const uint CrystalWyrmHash = 37392;
        public const uint GreenWyrmHash = 52392, PurpleWyrmHash = 52393, GreenSpitHash = 52394, PurpleSpitHash = 52395;

        public static uint PrefabHash(Tint t) { return t == Tint.Green ? GreenWyrmHash : t == Tint.Purple ? PurpleWyrmHash : CrystalWyrmHash; }
        public static uint SpitHash(Tint t) { return t == Tint.Green ? GreenSpitHash : t == Tint.Purple ? PurpleSpitHash : 0u; }
        public static string PrefabName(Tint t) { return t == Tint.Green ? "Green Crystal Wyrm" : t == Tint.Purple ? "Purple Crystal Wyrm" : "Crystal Wyrm"; }
        public static string SpitName(Tint t) { return t == Tint.Green ? "Green Wyrm Crystal Spit" : t == Tint.Purple ? "Purple Wyrm Crystal Spit" : "Wyrm Crystal Spit"; }

        // A crystal wyrm's color from its name. Returns false for normal and trial wyrms.
        public static bool TintFromName(string name, out Tint t)
        {
            t = Tint.Blue;
            if (!IsCrystal(name)) return false;
            if (name.IndexOf("Green", StringComparison.OrdinalIgnoreCase) >= 0) t = Tint.Green;
            else if (name.IndexOf("Purple", StringComparison.OrdinalIgnoreCase) >= 0) t = Tint.Purple;
            return true;
        }

        // How green and purple wyrms behave differently.
        public const float GreenEmergeRange = 1.5f;   // comes up for players 1.5x farther away (15 m -> 22.5 m)
        public const float GreenSpitRange = 1.4f;     // spits from 1.4x farther (20 m -> 28 m)
        public const float GreenDigPatience = 1.6f;   // waits 1.6x longer before digging back in
        public const float RageAttackWeight = 2f;     // raging purple picks spin and spit twice as often

        public static void Depth(int level, out float health, out float damage, out float speed)
        {
            int l = Math.Max(0, Math.Min(DepthCap, level));
            health = 1f + 0.015f * l;
            damage = 1f + 0.008f * l;
            speed = 1f + 0.003f * l;
        }

        public struct Mults
        {
            public float Scale, Health, Damage, Speed;
            public override string ToString()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "size x{0:0.##} hp x{1:0.##} dmg x{2:0.##} speed x{3:0.##}", Scale, Health, Damage, Speed);
            }
        }

        public static Mults TintMults(Tint t)
        {
            switch (t)
            {
                case Tint.Green: return new Mults { Scale = 1.15f, Health = 1.75f, Damage = 1.4f, Speed = 1.2f };
                case Tint.Purple: return new Mults { Scale = 1.3f, Health = 2.5f, Damage = 1.8f, Speed = 1.35f };
                default: return new Mults { Scale = 1f, Health = 1f, Damage = 1f, Speed = 1f };
            }
        }

        // Normal and trial wyrms (crystal = false) only get the depth boost, not the color one.
        public static Mults For(bool crystal, Tint t, int level, bool raging)
        {
            float h, d, s;
            Depth(level, out h, out d, out s);
            Mults c = TintMults(crystal ? t : Tint.Blue);
            float speed = s * c.Speed * (raging && crystal && t == Tint.Purple ? RageSpeed : 1f);
            return new Mults { Scale = c.Scale, Health = h * c.Health, Damage = d * c.Damage, Speed = Math.Min(SpeedCap, speed) };
        }

        public static bool IsCrystal(string originalName)
        {
            return originalName != null
                && originalName.IndexOf("Crystal Wyrm", StringComparison.OrdinalIgnoreCase) >= 0
                && originalName.IndexOf("Trial", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public static bool TryParse(string s, out Tint t)
        {
            t = Tint.Blue;
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "blue": t = Tint.Blue; return true;
                case "green": t = Tint.Green; return true;
                case "purple": case "pink": t = Tint.Purple; return true;
                default: return false;
            }
        }
    }
}
