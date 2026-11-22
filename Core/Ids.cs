using System.Globalization;

namespace CrystalLeather
{
    // The leather names, ids and colors.
    public static class Ids
    {
        // Has to fit in a positive int, or saved items made of it won't load back in.
        public const uint Hash = 0x4C157A1E;
        public const string Name = "Crystal Leather";
        public const string SourceName = "Wyrm Face Leather";
        public const string CrystalWyrmPrefab = "Crystal Wyrm";
        public const string AlbedoHex = "#00A0FF";
        public const string EmissionHex = "#24ADFF";

        public sealed class LeatherInfo
        {
            public uint Hash; public string Name, AlbedoHex, EmissionHex;
            // The game's crystal material to copy the look from. When it's null, use the first loaded material
            // whose name has every word in CrystalMatParts.
            public string CrystalMat; public string[] CrystalMatParts;
        }

        static readonly LeatherInfo[] Leathers =
        {
            new LeatherInfo { Hash = Hash, Name = Name, AlbedoHex = AlbedoHex, EmissionHex = EmissionHex, CrystalMat = "Blue_Crystal_m" },
            new LeatherInfo { Hash = 0x4C157A2E, Name = "Green Crystal Leather", AlbedoHex = "#73CA00", EmissionHex = "#7AD600", CrystalMat = "Green_Crystal_m" },
            new LeatherInfo { Hash = 0x4C157A3E, Name = "Purple Crystal Leather", AlbedoHex = "#C040FF", EmissionHex = "#D27CFF", CrystalMatParts = new[] { "Pink", "Crystal" } },
        };

        public static LeatherInfo Leather(Tint t) { return Leathers[(int)t]; }

        public static bool TryParseHex(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (hex == null) return false;
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            int v;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            r = ((v >> 16) & 0xFF) / 255f;
            g = ((v >> 8) & 0xFF) / 255f;
            b = (v & 0xFF) / 255f;
            return true;
        }
    }
}
