using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Intelligence;
using Alta.Intelligence.Wyrms;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Green and purple crystal wyrms are their own creatures. They're copies of the normal 'Crystal Wyrm' (and its
    // spit), added to the game's list of creatures with their own ids, on the server and in every player's game.
    // That way they save, load and show up for players like any other creature. It also means they need the mod.
    // Each copy gets its own set of behavior settings, which is where green's behavior is changed.
    //
    // The copies are kept on a switched-off object so they don't come alive by themselves.
    public static class WyrmPrefabs
    {
        static readonly FieldInfo MapField = AccessTools.Field(typeof(PrefabManager), "prefabMap");
        static readonly FieldInfo HashField = AccessTools.Field(typeof(NetworkPrefab), "hash");
        static readonly FieldInfo ShotPrefabField = AccessTools.Field(typeof(ProjectileShooter), "projectilePrefab");

        static GameObject _holder;
        static readonly Dictionary<uint, NetworkPrefab> Templates = new Dictionary<uint, NetworkPrefab>();
        static readonly bool[] OwnActionSets = new bool[3];
        public static readonly List<string> Report = new List<string>();

        public static bool Ready { get { return Templates.ContainsKey(WyrmTiers.GreenWyrmHash) && Templates.ContainsKey(WyrmTiers.PurpleWyrmHash); } }

        // True when this color got its own behavior settings.
        public static bool OwnActions(Tint t) { return OwnActionSets[(int)t]; }

        public static NetworkPrefab Get(Tint t)
        {
            NetworkPrefab p;
            if (t == Tint.Blue) return PrefabManager.GetPrefab(WyrmTiers.CrystalWyrmHash);
            return Templates.TryGetValue(WyrmTiers.PrefabHash(t), out p) ? p : null;
        }

        // Safe to call more than once. Runs after the game has loaded its list of creatures.
        public static void Register()
        {
            var map = MapField.GetValue(null) as Dictionary<uint, NetworkPrefab>;
            if (map == null || Ready) return;
            try
            {
                NetworkPrefab wyrm, spit = null;
                map.TryGetValue(WyrmTiers.CrystalWyrmHash, out wyrm);
                if (wyrm == null || wyrm.name != "Crystal Wyrm") { Main.Log.Error("prefab " + WyrmTiers.CrystalWyrmHash + " is not 'Crystal Wyrm' (" + (wyrm != null ? wyrm.name : "missing") + ")"); return; }
                foreach (ProjectileShooter ps in wyrm.GetComponentsInChildren<ProjectileShooter>(true))
                {
                    // GetComponentInParent doesn't work on these templates, so go to the top object instead.
                    Projectile pr = ShotPrefabField.GetValue(ps) as Projectile;
                    NetworkPrefab np = pr != null ? pr.transform.root.GetComponent<NetworkPrefab>() : null;
                    if (np != null) { spit = np; break; }
                }
                if (spit == null) Main.Log.Warning("couldn't find the crystal wyrm's spit, so green and purple spit will stay blue");

                if (_holder == null)
                {
                    _holder = new GameObject("CrystalLeather prefab templates");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                }
                Report.Clear();
                foreach (Tint t in new[] { Tint.Green, Tint.Purple })
                {
                    uint wh = WyrmTiers.PrefabHash(t), sh = WyrmTiers.SpitHash(t);
                    if (Taken(map, wh) || (spit != null && Taken(map, sh))) continue;

                    NetworkPrefab spitT = null;
                    if (spit != null)
                    {
                        spitT = Clone(spit, WyrmTiers.SpitName(t), sh);
                        if (!Main.IsServer) RecolorSpit(spitT, t);
                    }
                    NetworkPrefab wyrmT = Clone(wyrm, WyrmTiers.PrefabName(t), wh);
                    wyrmT.transform.localScale = wyrmT.transform.localScale * WyrmTiers.TintMults(t).Scale;
                    if (spitT != null)
                        foreach (ProjectileShooter ps in wyrmT.GetComponentsInChildren<ProjectileShooter>(true))
                            ShotPrefabField.SetValue(ps, spitT.GetComponentInChildren<Projectile>(true));
                    OwnActionSets[(int)t] = Tune(wyrmT, t);

                    if (spitT != null) { map[sh] = spitT; Templates[sh] = spitT; }
                    map[wh] = wyrmT; Templates[wh] = wyrmT;
                }
                foreach (string line in Report) Main.Log.Msg(line);
            }
            catch (Exception e) { Main.Log.Error("prefab registration failed: " + e); }
        }

        static bool Taken(Dictionary<uint, NetworkPrefab> map, uint hash)
        {
            NetworkPrefab existing;
            if (!map.TryGetValue(hash, out existing) || existing == null) return false;
            if (!Templates.ContainsKey(hash)) Main.Log.Error("id " + hash + " is already taken by '" + existing.name + "' (another mod?), so that color is turned off");
            return true;
        }

        static NetworkPrefab Clone(NetworkPrefab src, string name, uint hash)
        {
            NetworkPrefab copy = UnityEngine.Object.Instantiate(src, _holder.transform, false);
            copy.name = name;
            HashField.SetValue(copy, unchecked((int)hash));
            Report.Add("registered prefab '" + name + "' hash " + hash + " (from '" + src.name + "')");
            return copy;
        }

        static void Set(object o, string field, Func<float, float> f, string label)
        {
            Traverse tr = Traverse.Create(o).Field(field);
            float before = tr.GetValue<float>();
            tr.SetValue(f(before));
            Report.Add("  " + label + ": " + before.ToString("0.##") + " -> " + f(before).ToString("0.##"));
        }

        // A crystal wyrm's behavior settings (how often it bites twice, how far it spits and so on) aren't part of the
        // wyrm itself. They're in a separate object that every crystal wyrm shares. Give each color its own copy so
        // green's settings can be different. The copy has to stay switched on or the game won't find the settings.
        // Returns false if things aren't laid out the way we expect, and then the backup patches take over.
        static bool Tune(NetworkPrefab wyrmT, Tint t)
        {
            Wyrm w = wyrmT.GetComponentInChildren<Wyrm>(true);
            if (w == null) { Report.Add("  no Wyrm component on " + wyrmT.name); return false; }
            Component burrowed = Traverse.Create(w).Field("burrowedActions").GetValue<Component>();
            Component emerged = Traverse.Create(w).Field("emergedActions").GetValue<Component>();
            if (burrowed == null || emerged == null) { Report.Add("  " + wyrmT.name + ": no action sets"); return false; }
            Transform root = burrowed.transform.root;
            if (emerged.transform.root != root || root.IsChildOf(wyrmT.transform) || root.GetComponentsInChildren<Component>(true).Any(c => c is NetworkEntity || c is Agent))
            { Report.Add("  " + wyrmT.name + ": behavior settings aren't laid out as expected ('" + root.name + "'), using the backup patches"); return false; }

            GameObject copy = UnityEngine.Object.Instantiate(root.gameObject);
            copy.name = root.name + " (" + t + ")";
            UnityEngine.Object.DontDestroyOnLoad(copy);
            foreach (string f in new[] { "burrowedActions", "emergedActions" })
            {
                Component set = f == "burrowedActions" ? burrowed : emerged;
                string rel = set.transform == root ? "" : RelPath(set.transform, root);
                Transform mine = rel == "" ? copy.transform : copy.transform.Find(rel);
                Component c = mine != null ? mine.GetComponent(set.GetType()) : null;
                if (c == null) { Report.Add("  " + wyrmT.name + ": copy has no '" + rel + "'"); UnityEngine.Object.Destroy(copy); return false; }
                Traverse.Create(w).Field(f).SetValue(c);
            }
            Report.Add("  " + wyrmT.name + " has its own action sets ('" + copy.name + "')");
            if (t != Tint.Green) return true;
            foreach (WyrmMeleeSettings m in copy.GetComponentsInChildren<WyrmMeleeSettings>(true))
                Set(m, "flurryChancePerAttack", v => WyrmTiers.GreenFlurry, "green bite combo chance per bite");
            foreach (WyrmEmergeSettings m in copy.GetComponentsInChildren<WyrmEmergeSettings>(true))
                Set(m, "radius", v => v * WyrmTiers.GreenEmergeRange, "green emerge radius");
            foreach (WyrmSpitSettings m in copy.GetComponentsInChildren<WyrmSpitSettings>(true))
                Set(m, "maximumRange", v => v * WyrmTiers.GreenSpitRange, "green spit range");
            foreach (WyrmDigSettings m in copy.GetComponentsInChildren<WyrmDigSettings>(true))
                Set(m, "maxStrengthDuration", v => v * WyrmTiers.GreenDigPatience, "green time before digging in");
            return true;
        }

        static string RelPath(Transform t, Transform root)
        {
            string p = t.name;
            for (Transform c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
            return p;
        }

        // Players' side: the spit takes the wyrm's color. The crystal ball gets that color's glowing crystal look, and
        // the sparks, trail and light get recolored.
        static void RecolorSpit(NetworkPrefab spitT, Tint t)
        {
            Color target = CrystalMaterial.LightColor(t);
            Material crystal = CrystalMaterial.CrystalCopy(t);
            var copies = new Dictionary<Material, Material>();
            foreach (Renderer r in spitT.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    Material c;
                    if (!copies.TryGetValue(m, out c))
                    {
                        bool ball = m.name.IndexOf("Crystal", StringComparison.OrdinalIgnoreCase) >= 0 && m.shader.name.IndexOf("Atlas", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (ball && crystal != null) c = crystal;
                        else
                        {
                            c = new Material(m) { name = m.name + " (" + t + ")" };
                            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
                            CrystalMaterial.HueShift(c, target);
                        }
                        copies[m] = c;
                        Report.Add("  spit " + t + " '" + r.name + "' " + m.name + " -> " + c.name + ": " + CrystalMaterial.ColorProps(c));
                    }
                    mats[i] = c;
                }
                r.sharedMaterials = mats;
            }
            foreach (ParticleSystem p in spitT.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main; main.startColor = Shift(main.startColor, target);
                var col = p.colorOverLifetime; if (col.enabled) col.color = Shift(col.color, target);
                var spd = p.colorBySpeed; if (spd.enabled) spd.color = Shift(spd.color, target);
                var tr = p.trails; if (tr.enabled) { tr.colorOverLifetime = Shift(tr.colorOverLifetime, target); tr.colorOverTrail = Shift(tr.colorOverTrail, target); }
            }
            foreach (Light l in spitT.GetComponentsInChildren<Light>(true)) l.color = Shift(l.color, target);
            foreach (TrailRenderer tr in spitT.GetComponentsInChildren<TrailRenderer>(true)) tr.colorGradient = Shift(tr.colorGradient, target);
            foreach (LineRenderer lr in spitT.GetComponentsInChildren<LineRenderer>(true)) lr.colorGradient = Shift(lr.colorGradient, target);
        }

        static ParticleSystem.MinMaxGradient Shift(ParticleSystem.MinMaxGradient g, Color target)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Shift(g.color, target));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Shift(g.colorMin, target), Shift(g.colorMax, target));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Shift(g.gradient, target));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Shift(g.gradientMin, target), Shift(g.gradientMax, target));
                default:
                    var r = new ParticleSystem.MinMaxGradient(Shift(g.gradient, target));
                    r.mode = g.mode;
                    return r;
            }
        }

        static Gradient Shift(Gradient g, Color target)
        {
            if (g == null) return null;
            var n = new Gradient { mode = g.mode };
            n.SetKeys(g.colorKeys.Select(k => new GradientColorKey(Shift(k.color, target), k.time)).ToArray(), g.alphaKeys);
            return n;
        }

        static Color Shift(Color c, Color target)
        {
            float h, s, v, th, ts, tv;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.05f) return c;
            Color.RGBToHSV(target, out th, out ts, out tv);
            Color n = Color.HSVToRGB(th, Math.Max(s, ts * 0.8f), v, true);
            n.a = c.a;
            return n;
        }
    }

    [HarmonyPatch(typeof(PrefabManager), "PrepareSpawnSetups")]
    static class PrefabTablePatch
    {
        static void Postfix() { WyrmPrefabs.Register(); }
    }

    // Natural spawns: when the game is about to spawn a crystal wyrm, pick its color based on how deep the level is.
    [HarmonyPatch(typeof(Alta.Populations.PopulationSample), "SpawnRandom")]
    static class PopulationSpawnPatch
    {
        public static Alta.Populations.PopulationSample Current;
        static void Prefix(Alta.Populations.PopulationSample __instance) { Current = __instance; }
        static void Finalizer() { Current = null; }
    }

    [HarmonyPatch(typeof(Alta.Populations.PopulationDefinition), "GetRandom")]
    static class PopulationPickPatch
    {
        static void Postfix(ref uint __result)
        {
            var sample = PopulationSpawnPatch.Current;
            if (!Main.IsServer || __result != WyrmTiers.CrystalWyrmHash || sample == null || !WyrmPrefabs.Ready) return;
            int level = -1;
            var chunk = sample.Chunk;
            if (chunk != null)
            {
                var st = chunk.GetComponent<Alta.Chunks.CaveLayerStreamer>();
                level = st != null ? st.LayerIdentifier : WyrmVariants.LevelAtPos(chunk.transform.position);
            }
            if (level < 0) return;
            Tint t = WyrmTiers.Pick(UnityEngine.Random.value, level);
            if (t != Tint.Blue) __result = WyrmTiers.PrefabHash(t);
            Main.Log.Msg("population spawn on L" + level + ": " + WyrmTiers.PrefabName(t));
        }
    }
}
