#if QUEST
using System.Collections.Generic;
using Il2Cpp;
using Il2CppAlta;
using Il2CppAlta.Intelligence;
using Il2CppAlta.Networking;
using UnityEngine;

namespace CrystalLeather
{
    // The Quest version of Game.cs. The Quest game keeps the same lists as the PC one, but its private fields are
    // reached as properties, its lists are its own kind of dictionary, and leathers keep their looks in
    // androidMaterialChannels instead of materialChannels.
    public static class Game
    {
        // Creatures and objects.
        public static IEnumerable<NetworkPrefab> AllPrefabs()
        {
            var map = PrefabManager.prefabMap;
            if (map == null) yield break;
            foreach (NetworkPrefab p in map.Values) yield return p;
        }

        public static bool PrefabMapReady() { return PrefabManager.prefabMap != null; }

        public static NetworkPrefab PrefabAt(uint hash)
        {
            var map = PrefabManager.prefabMap;
            NetworkPrefab p;
            return map != null && map.TryGetValue(hash, out p) ? p : null;
        }

        public static void PutPrefab(uint hash, NetworkPrefab p) { PrefabManager.prefabMap[hash] = p; }
        public static void SetPrefabHash(NetworkPrefab p, uint hash) { p.hash = unchecked((int)hash); }

        public static Projectile ShotPrefab(ProjectileShooter s) { return s.projectilePrefab; }
        public static void SetShotPrefab(ProjectileShooter s, Projectile p) { s.projectilePrefab = p; }

        // Materials.
        public static IEnumerable<PhysicalMaterial> AllMaterials()
        {
            var items = HashedGeneralValue<PhysicalMaterial>.items;
            if (items == null) yield break;
            foreach (PhysicalMaterial m in items.Values) yield return m;
        }

        public static bool MaterialTableReady()
        {
            HashedGeneralValue<PhysicalMaterial>.CheckItems();
            return HashedGeneralValue<PhysicalMaterial>.items != null;
        }

        public static PhysicalMaterial MaterialAt(uint hash)
        {
            var items = HashedGeneralValue<PhysicalMaterial>.items;
            PhysicalMaterial m;
            return items != null && items.TryGetValue(hash, out m) ? m : null;
        }

        public static void PutMaterial(uint hash, PhysicalMaterial m) { HashedGeneralValue<PhysicalMaterial>.items[hash] = m; }
        public static void ResetMaterialNames() { HashedGeneralValue<PhysicalMaterial>.itemsByName = null; }
        public static void SetMaterialHash(PhysicalMaterial m, uint hash) { m.hash = unchecked((int)hash); }

        static IEnumerable<PhysicalMaterial.MaterialChannel> Channels(PhysicalMaterial m)
        {
            var list = m.androidMaterialChannels;
            if (list == null) yield break;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) yield return list[i];
        }

        // The material a leather is drawn with.
        public static Material ChannelBaseMaterial(PhysicalMaterial m)
        {
            foreach (PhysicalMaterial.MaterialChannel ch in Channels(m))
            {
                Material mat = ch.nonDeformingMaterial ?? ch.material;
                if (mat != null) return mat;
            }
            return null;
        }

        // Points a leather at our own material everywhere the game might look.
        public static void SetAllChannels(PhysicalMaterial m, Material mine)
        {
            foreach (PhysicalMaterial.MaterialChannel ch in Channels(m))
            {
                ch.material = mine;
                ch.nonDeformingMaterial = mine;
                ch.atlasedMaterial = mine;
                ch.needsFixing = false;
                ch.offsetScale = new Vector4(1f, 1f, 0f, 0f);
            }
        }

        public static T Cast<T>(Object o) where T : Object { return o != null ? o.TryCast<T>() : null; }
    }

    // The console commands are server-only and aren't in the Quest build. This keeps the shared code's
    // "log more when verbose" checks compiling.
    public static class WyrmCommands
    {
        public const bool Verbose = false;
    }
}
#endif
