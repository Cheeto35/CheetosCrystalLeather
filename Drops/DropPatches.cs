using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Server: when a wyrm dies, the game gives each leather drop the wyrm's own leather. Right after that, a crystal
    // wyrm's drop gets switched to the crystal leather of its color. The game saves and shares it from there.
    [HarmonyPatch(typeof(SpawnerBase), "PreChunk")]
    static class SpawnerPreChunkPatch
    {
        static void Postfix(SpawnerBase __instance, NetworkEntity entity)
        {
            if (!Main.IsServer || CrystalMaterial.Instance == null || entity == null) return;
            PhysicalMaterialPart part = entity.GetComponent<PhysicalMaterialPart>();
            if (part == null || part.PhysicalMaterial == null) return;
            Transform owner = TopEntity(__instance.transform);
            if (!DropRule.ShouldConvert(owner.name, part.PhysicalMaterial.name)) return;
            Tint tint = WyrmVariants.TintOf(owner);
            PhysicalMaterial leather = CrystalMaterial.For(tint) ?? CrystalMaterial.Instance;
            part.SetMaterial(leather);
            Main.Log.Msg("crystal drop -> " + leather.name + ": '" + entity.name + "' from '" + owner.name + "'");
        }

        public static Transform TopEntity(Transform t)
        {
            Transform top = t;
            for (Transform c = t; c != null; c = c.parent)
                if (c.GetComponent<NetworkEntity>() != null) top = c;
            return top;
        }
    }
}
