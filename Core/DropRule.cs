namespace CrystalLeather
{
    // A drop turns into crystal leather when it came from a crystal wyrm and the game just gave it
    // the wyrm's own leather.
    public static class DropRule
    {
        public static bool ShouldConvert(string ownerEntityName, string copiedMaterialName)
        {
            if (ownerEntityName == null || copiedMaterialName == null) return false;
            return copiedMaterialName == Ids.SourceName
                && ownerEntityName.IndexOf(Ids.CrystalWyrmPrefab, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
