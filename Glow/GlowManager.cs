using System.Collections.Generic;
using System.Linq;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Players' side: keep a list of every piece of crystal leather that's out in the world.
    [HarmonyPatch(typeof(PhysicalMaterialPart), "SetMaterial")]
    static class SetMaterialPatch
    {
        static void Postfix(PhysicalMaterialPart __instance, PhysicalMaterial physicalMaterial)
        {
            if (Main.IsServer || __instance == null) return;
            Tint? tint = CrystalMaterial.TintOf(physicalMaterial);
            if (tint.HasValue && CrystalMaterial.Source != null && __instance.GetComponentInChildren<Alta.Intelligence.Wyrm>(true) != null)
            {
                // An old test wyrm marked with crystal leather (see WyrmVariants): note its color and give it its
                // normal skin back.
                WyrmVariants.NoteTint(__instance.GetComponent<NetworkEntity>(), tint.Value);
                __instance.SetMaterial(CrystalMaterial.Source);
                return;
            }
            if (tint.HasValue) GlowManager.Parts.Add(__instance);
            else GlowManager.Parts.Remove(__instance);
        }
    }

    // Players' side: twice a second, group the crystal leather pieces by the item they're part of and give each
    // item one light. The more pieces it has, the brighter the light. Only the 8 brightest, closest ones are lit so
    // a pile of leather doesn't slow the game down.
    public static class GlowManager
    {
        public static readonly HashSet<PhysicalMaterialPart> Parts = new HashSet<PhysicalMaterialPart>();
        public static int NFull = Brightness.DefaultNFull;

        class Glow { public GameObject Go; public Light Light; public float Intensity; }
        // One light per item and color, so an item made from more than one color glows in each of them.
        static readonly Dictionary<KeyValuePair<Transform, Tint>, Glow> Glows = new Dictionary<KeyValuePair<Transform, Tint>, Glow>();
        static float _next;

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + Brightness.Rescan;

            Parts.RemoveWhere(p => p == null || !CrystalMaterial.Is(p.PhysicalMaterial));

            var groups = new Dictionary<KeyValuePair<Transform, Tint>, List<PhysicalMaterialPart>>();
            foreach (PhysicalMaterialPart p in Parts)
            {
                if (!p.gameObject.activeInHierarchy) continue;
                var root = new KeyValuePair<Transform, Tint>(GroupOf(p.transform), CrystalMaterial.TintOf(p.PhysicalMaterial) ?? Tint.Blue);
                List<PhysicalMaterialPart> list;
                if (!groups.TryGetValue(root, out list)) groups[root] = list = new List<PhysicalMaterialPart>();
                list.Add(p);
            }

            // Remove lights for items that are gone.
            foreach (var dead in Glows.Keys.Where(k => k.Key == null || !groups.ContainsKey(k)).ToList())
            {
                Glow g;
                if (Glows.TryGetValue(dead, out g) && g.Go != null) Object.Destroy(g.Go);
                Glows.Remove(dead);
            }

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            var scored = new List<KeyValuePair<float, Glow>>();
            foreach (var kv in groups)
            {
                float range, intensity;
                Brightness.For(kv.Value.Count, NFull, out range, out intensity);
                Transform anchor = kv.Value[0].transform;
                Glow g;
                if (!Glows.TryGetValue(kv.Key, out g) || g.Go == null)
                {
                    g = new Glow { Go = new GameObject("Crystal Leather Glow") };
                    g.Light = g.Go.AddComponent<Light>();
                    g.Light.type = LightType.Point;
                    g.Light.color = CrystalMaterial.LightColor(kv.Key.Value);
                    g.Light.shadows = LightShadows.None;
                    g.Light.renderMode = LightRenderMode.Auto;
                    Glows[kv.Key] = g;
                }
                if (g.Go.transform.parent != anchor)
                {
                    g.Go.transform.SetParent(anchor, false);
                    g.Go.transform.localPosition = Vector3.zero;
                }
                g.Light.range = range;
                g.Light.intensity = intensity;
                g.Intensity = intensity;
                float score = intensity / (1f + Vector3.Distance(anchor.position, camPos));
                scored.Add(new KeyValuePair<float, Glow>(score, g));
            }

            int lit = 0;
            foreach (var s in scored.OrderByDescending(s => s.Key))
            {
                bool on = lit < Brightness.MaxLights;
                if (s.Value.Light.enabled != on) s.Value.Light.enabled = on;
                if (on) lit++;
            }
        }

        static Transform GroupOf(Transform part)
        {
            var chain = new List<Transform>();
            var nodes = new List<GroupRoot.Node>();
            for (Transform c = part; c != null; c = c.parent)
            {
                chain.Add(c);
                nodes.Add(new GroupRoot.Node(c.GetComponent<NetworkEntity>() != null,
                    c.GetComponent<Alta.Chunks.Chunk>() != null, c.GetComponent<Player>() != null));
            }
            return chain[GroupRoot.Pick(nodes.ToArray())];
        }
    }
}
