#if !QUEST
using System.Collections.Generic;
using System.Reflection;
using Alta;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Everything the mod reads or changes inside the game's own lists, in one place. The Quest version of the game
    // keeps these the same way but has to reach them differently (see GameQuest.cs).
    public static class Game
    {
        static readonly FieldInfo PrefabMapField = AccessTools.Field(typeof(PrefabManager), "prefabMap");
        static readonly FieldInfo PrefabHashField = AccessTools.Field(typeof(NetworkPrefab), "hash");
        static readonly FieldInfo ShotPrefabField = AccessTools.Field(typeof(ProjectileShooter), "projectilePrefab");
        static readonly FieldInfo MaterialHashField = AccessTools.Field(typeof(HashedGeneralValue), "hash");
        static readonly FieldInfo MaterialItemsField = AccessTools.Field(typeof(HashedGeneralValue<PhysicalMaterial>), "items");
        static readonly FieldInfo MaterialByNameField = AccessTools.Field(typeof(HashedGeneralValue<PhysicalMaterial>), "itemsByName");

        // Creatures and objects.
        static Dictionary<uint, NetworkPrefab> PrefabMap { get { return PrefabMapField.GetValue(null) as Dictionary<uint, NetworkPrefab>; } }

        public static IEnumerable<NetworkPrefab> AllPrefabs() { return PrefabManager.AllPrefabs; }
        public static bool PrefabMapReady() { return PrefabMap != null; }

        public static NetworkPrefab PrefabAt(uint hash)
        {
            var map = PrefabMap;
            NetworkPrefab p;
            return map != null && map.TryGetValue(hash, out p) ? p : null;
        }

        public static void PutPrefab(uint hash, NetworkPrefab p) { PrefabMap[hash] = p; }
        public static void SetPrefabHash(NetworkPrefab p, uint hash) { PrefabHashField.SetValue(p, unchecked((int)hash)); }

        public static Projectile ShotPrefab(ProjectileShooter s) { return ShotPrefabField.GetValue(s) as Projectile; }
        public static void SetShotPrefab(ProjectileShooter s, Projectile p) { ShotPrefabField.SetValue(s, p); }

        // Materials.
        static Dictionary<uint, PhysicalMaterial> MaterialTable { get { return MaterialItemsField.GetValue(null) as Dictionary<uint, PhysicalMaterial>; } }

        public static IEnumerable<PhysicalMaterial> AllMaterials() { return HashedGeneralValue<PhysicalMaterial>.All; }

        public static bool MaterialTableReady()
        {
            HashedGeneralValue<PhysicalMaterial>.CheckItems();
            return MaterialTable != null;
        }

        public static PhysicalMaterial MaterialAt(uint hash)
        {
            var items = MaterialTable;
            PhysicalMaterial m;
            return items != null && items.TryGetValue(hash, out m) ? m : null;
        }

        public static void PutMaterial(uint hash, PhysicalMaterial m) { MaterialTable[hash] = m; }
        public static void ResetMaterialNames() { MaterialByNameField.SetValue(null, null); }
        public static void SetMaterialHash(PhysicalMaterial m, uint hash) { MaterialHashField.SetValue(m, unchecked((int)hash)); }

        static System.Collections.IEnumerable Channels(PhysicalMaterial m)
        {
            var list = Traverse.Create(m).Field("materialChannels").GetValue<System.Collections.IEnumerable>();
            if (list == null) yield break;
            foreach (object ch in list) if (ch != null) yield return ch;
        }

        // The material a leather is drawn with.
        public static Material ChannelBaseMaterial(PhysicalMaterial m)
        {
            foreach (object ch in Channels(m))
            {
                var t = Traverse.Create(ch);
                Material mat = t.Field("nonDeformingMaterial").GetValue<Material>() ?? t.Field("material").GetValue<Material>();
                if (mat != null) return mat;
            }
            return null;
        }

        // Points a leather at our own material everywhere the game might look.
        public static void SetAllChannels(PhysicalMaterial m, Material mine)
        {
            foreach (object ch in Channels(m))
            {
                var t = Traverse.Create(ch);
                t.Field("material").SetValue(mine);
                t.Field("nonDeformingMaterial").SetValue(mine);
                t.Field("atlasedMaterial").SetValue(mine);
                t.Field("needsFixing").SetValue(false);
                t.Field("offsetScale").SetValue(new Vector4(1f, 1f, 0f, 0f));
            }
        }

        public static T Cast<T>(Object o) where T : Object { return o as T; }
    }
}
#endif
