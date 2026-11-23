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
            if (tint.HasValue) GlowManager.Parts[__instance.GetInstanceID()] = __instance;
            else GlowManager.Parts.Remove(__instance.GetInstanceID());
        }
    }

    // Players' side: twice a second, group the crystal leather pieces by the item they're part of and give each
    // item one light. The more pieces it has, the brighter the light. Only the 8 brightest, closest ones are lit so
    // a pile of leather doesn't slow the game down.
    public static class GlowManager
    {
        // Keyed by instance id, so the same piece is never counted twice.
        public static readonly Dictionary<int, PhysicalMaterialPart> Parts = new Dictionary<int, PhysicalMaterialPart>();
        public static int NFull = Brightness.DefaultNFull;

        class Glow { public GameObject Go; public Light Light; public float Intensity; }
        class Group { public Transform Root; public Tint Tint; public readonly List<PhysicalMaterialPart> Pieces = new List<PhysicalMaterialPart>(); }
        // One light per item and color, so an item made from more than one color glows in each of them. Keyed by the
        // item's instance id and the color.
        static readonly Dictionary<KeyValuePair<int, Tint>, Glow> Glows = new Dictionary<KeyValuePair<int, Tint>, Glow>();
        static float _next;

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + Brightness.Rescan;

            foreach (int gone in Parts.Where(kv => kv.Value == null || !CrystalMaterial.Is(kv.Value.PhysicalMaterial)).Select(kv => kv.Key).ToList())
                Parts.Remove(gone);

            var groups = new Dictionary<KeyValuePair<int, Tint>, Group>();
            foreach (PhysicalMaterialPart p in Parts.Values)
            {
                if (!p.gameObject.activeInHierarchy) continue;
                Transform root = GroupOf(p.transform);
                Tint tint = CrystalMaterial.TintOf(p.PhysicalMaterial) ?? Tint.Blue;
                var key = new KeyValuePair<int, Tint>(root.GetInstanceID(), tint);
                Group grp;
                if (!groups.TryGetValue(key, out grp)) groups[key] = grp = new Group { Root = root, Tint = tint };
                grp.Pieces.Add(p);
            }

            // Remove lights for items that are gone.
            foreach (var dead in Glows.Keys.Where(k => !groups.ContainsKey(k)).ToList())
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
                Brightness.For(kv.Value.Pieces.Count, NFull, out range, out intensity);
                Transform anchor = kv.Value.Pieces[0].transform;
                Glow g;
                if (!Glows.TryGetValue(kv.Key, out g) || g.Go == null)
                {
                    g = new Glow { Go = new GameObject("Crystal Leather Glow") };
                    g.Light = g.Go.AddComponent<Light>();
                    g.Light.type = LightType.Point;
                    g.Light.color = CrystalMaterial.LightColor(kv.Value.Tint);
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
