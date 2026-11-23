using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Chunks;
using Alta.Impact;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Keeps track of every wyrm (normal, trial and crystal). As soon as we know which mine level a wyrm is on, it
    // gets its boosts, once: attack speed on both the server and the players' games, health, blunt resistance and
    // reaction time on the server, and the new colors on the players' side. Purple wyrms go into a rage below half
    // health.
    public static class WyrmVariants
    {
        public sealed class State
        {
            public Wyrm Wyrm;
            public NetworkEntity Root;
            public uint Id;
            public string Name;
            public int Level = -1;
            public bool Crystal;
            public Tint Tint;
            public WyrmTiers.Mults M;
            public bool Raging;
            public float BaseMax;
            public float FirstSeen;
            public bool Applied;
        }

        public const float LevelTimeout = 10f;
        static readonly System.Reflection.FieldInfo ImpactMultsField = AccessTools.Field(typeof(HealthObject), "impactTypeMultipliers");
        static readonly System.Reflection.FieldInfo UpdateIntervalField = AccessTools.Field(typeof(Agent), "updateInterval");
        // Everything below is keyed by instance id, so the same object is never tracked twice.
        static readonly Dictionary<int, State> States = new Dictionary<int, State>();
        static float _next;

        public static IEnumerable<State> All { get { return States.Values.Where(s => s.Wyrm != null && s.Root != null && s.Applied); } }

        public static State Get(Wyrm w)
        {
            State s;
            return w != null && States.TryGetValue(w.GetInstanceID(), out s) && s.Applied ? s : null;
        }

        // Players' side: colors read off a wyrm's leather before we got to it (see Convert).
        static readonly Dictionary<int, KeyValuePair<NetworkEntity, Tint>> Noted = new Dictionary<int, KeyValuePair<NetworkEntity, Tint>>();

        public static void NoteTint(NetworkEntity root, Tint t)
        {
            if (root == null) return;
            Noted[root.GetInstanceID()] = new KeyValuePair<NetworkEntity, Tint>(root, t);
            foreach (State s in States.Values)
                if (s.Applied && s.Root == root && s.Crystal && s.Tint != t)
                    Main.Log.Msg("wyrm " + s.Id + ": synced color " + t + " differs from applied " + s.Tint);
        }

        public static void Track(Wyrm w)
        {
            if (w == null || States.ContainsKey(w.GetInstanceID())) return;
            States[w.GetInstanceID()] = new State { Wyrm = w, FirstSeen = Time.time };
        }

        public static NetworkEntity RootOf(Transform t)
        {
            NetworkEntity e = t != null ? t.GetComponentInParent<NetworkEntity>() : null;
            return e != null ? (e.PrefabRoot ?? e) : null;
        }

        public static State GetByRoot(NetworkEntity root)
        {
            if (root == null) return null;
            foreach (State s in States.Values)
                if (s.Applied && s.Root == root) return s;
            return null;
        }

        // The color of the wyrm this object belongs to. Blue if we don't know.
        public static Tint TintOf(Transform t)
        {
            if (t == null) return Tint.Blue;
            Wyrm w = t.GetComponentInChildren<Wyrm>(true) ?? t.GetComponentInParent<Wyrm>();
            State s = Get(w);
            return s != null && s.Crystal ? s.Tint : Tint.Blue;
        }

        public static int LevelAt(NetworkEntity e)
        {
            Chunk c = e != null ? e.Chunk : null;
            CaveLayerStreamer st = c != null ? c.GetComponent<CaveLayerStreamer>() : null;
            if (st != null) return st.LayerIdentifier;
            return e != null ? LevelAtPos(e.transform.position) : -1;
        }

        public static int LevelAtPos(Vector3 pos)
        {
            LocationChunk lc = LocationChunkHelper.GetBestContaining(pos);
            CaveLayerStreamer st = lc != null ? lc.GetComponent<CaveLayerStreamer>() : null;
            return st != null ? st.LayerIdentifier : -1;
        }

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            List<int> dead = null;
            // Loop over a copy, because replacing an old wyrm spawns a new one while we're going through the list.
            foreach (var kv in States.ToList())
            {
                State s = kv.Value;
                if (s.Wyrm == null) { (dead ?? (dead = new List<int>())).Add(kv.Key); continue; }
                try
                {
                    if (!s.Applied) TryApply(s);
                    else if (s.Crystal && s.Tint == Tint.Purple && !s.Raging) CheckRage(s);
                }
                catch (Exception e)
                {
                    Main.Log.Error("wyrm " + s.Id + ": " + e);
                    s.Applied = true;
                }
            }
            if (dead != null) foreach (int id in dead) States.Remove(id);
            if (Noted.Count > 0) foreach (int n in Noted.Where(kv => kv.Value.Key == null).Select(kv => kv.Key).ToList()) Noted.Remove(n);
        }

        static void TryApply(State s)
        {
            Wyrm w = s.Wyrm;
            NetworkEntity ai = w.Entity;
            // The Wyrm script sits on a part inside the wyrm, so go up to the whole creature.
            NetworkEntity e = ai != null ? (ai.PrefabRoot ?? ai) : null;
            if (e == null || e.Identifier == 0) return;
            int level = LevelAt(e);
            if (level < 0)
            {
                if (Time.time - s.FirstSeen < LevelTimeout) return;
                level = 0;
            }
            s.Root = e;
            s.Id = e.Identifier;
            s.Name = e.OriginalName ?? w.name;
            s.Level = level;
            // The color comes from which creature it is: 'Crystal Wyrm', 'Green Crystal Wyrm' or 'Purple Crystal Wyrm'.
            // Normal and trial wyrms don't have one.
            Tint tint;
            s.Crystal = WyrmTiers.TintFromName(s.Name, out tint);
            s.Tint = tint;
            if (s.Crystal && tint == Tint.Blue)
            {
                // Early test versions of the mod marked green and purple wyrms by changing a normal crystal wyrm's
                // leather. If one of those is still around in a save, the server swaps it for the real creature.
                PhysicalMaterialPart part = e.GetComponent<PhysicalMaterialPart>();
                Tint? legacy = part != null ? CrystalMaterial.TintOf(part.PhysicalMaterial) : null;
                KeyValuePair<NetworkEntity, Tint> noted;
                if (!legacy.HasValue && Noted.TryGetValue(e.GetInstanceID(), out noted)) legacy = noted.Value;
                if (legacy.HasValue && legacy.Value != Tint.Blue)
                {
                    if (Main.IsServer && Convert(s, legacy.Value)) return;
                    if (!Main.IsServer) return; // the server will replace it
                }
            }
            s.M = WyrmTiers.For(s.Crystal, s.Tint, level, false);
            SetAnimSpeed(s);

            HealthObject h = w.HealthObject;
            if (h != null) s.BaseMax = h.MaxHealth;
            if (Main.IsServer)
            {
                if (h != null && s.M.Health != 1f)
                {
                    bool full = h.Health >= s.BaseMax - 0.01f;
                    h.MaxHealth = s.BaseMax * s.M.Health;
                    if (full) h.Health = h.MaxHealth;
                    float share;
                    if (PendingHealth.TryGetValue(e.GetInstanceID(), out share)) { PendingHealth.Remove(e.GetInstanceID()); h.Health = h.MaxHealth * share; }
                    if (WyrmCommands.Verbose || Math.Abs(h.MaxHealth - s.BaseMax * s.M.Health) > 0.05f)
                        Main.Log.Msg("wyrm " + s.Id + " health: base " + s.BaseMax + " wanted " + (s.BaseMax * s.M.Health) + " got max " + h.MaxHealth
                            + " health " + h.Health + " on '" + h.name + "'; health objects: "
                            + string.Join(", ", e.GetComponentsInChildren<HealthObject>(true).Select(x => x.name + "=" + x.MaxHealth).ToArray()));
                }
                if (s.Crystal && s.Tint == Tint.Purple && h != null)
                {
                    var mults = ImpactMultsField.GetValue(h) as Dictionary<ImpactType, float>;
                    float m;
                    if (mults != null) mults[ImpactType.Blunt] = (mults.TryGetValue(ImpactType.Blunt, out m) ? m : 1f) * WyrmTiers.PurpleBlunt;
                    else Main.Log.Error("wyrm " + s.Id + ": no impact multipliers");
                }
                if (s.Crystal && s.Tint == Tint.Green) UpdateIntervalField.SetValue(w, (float)UpdateIntervalField.GetValue(w) * WyrmTiers.GreenThink);
            }
            else if (s.Crystal && s.Tint != Tint.Blue) Recolor(s);

            s.Applied = true;
            if (s.Crystal || level >= 50 || WyrmCommands.Verbose)
                Main.Log.Msg("wyrm " + s.Id + " '" + s.Name + "' L" + level + (s.Crystal ? " " + s.Tint : "") + ": " + s.M
                    + (h != null ? " hp " + h.Health.ToString("0") + "/" + h.MaxHealth.ToString("0") : ""));
        }

        // Server: swap one of those old wyrms for the real green or purple one, in the same spot with the same health left.
        static bool Convert(State s, Tint t)
        {
            NetworkPrefab prefab = WyrmPrefabs.Get(t);
            if (prefab == null) return false;
            NetworkEntity old = s.Root;
            float share = HealthFraction(s);
            Transform tr = old.transform;
            NetworkEntity e = SpawnHelper.Spawn(prefab, SpawnData.Default, old.Chunk, tr.position, tr.rotation);
            if (e == null) { Main.Log.Error("wyrm " + s.Id + ": could not spawn '" + prefab.name + "' to replace it"); return false; }
            Main.Log.Msg("wyrm " + s.Id + " (old " + t + ") replaced by '" + prefab.name + "' id " + e.Identifier + " at " + (share * 100f).ToString("0") + "% health");
            if (share < 0.999f) PendingHealth[e.GetInstanceID()] = share;
            old.SceneDestroy();
            s.Applied = true;
            return true;
        }

        static readonly Dictionary<int, float> PendingHealth = new Dictionary<int, float>();

        static void SetAnimSpeed(State s)
        {
            Alta.Networking.Animation.NetworkAnimator na = s.Wyrm.NetworkAnimator;
            Animator a = na != null ? na.Animator : null;
            if (a != null) a.speed = s.M.Speed;
        }

        // Uses the game's own synced health numbers, so the server and the players agree on it.
        public static float HealthFraction(State s)
        {
            HealthObject h = s.Wyrm.HealthObject;
            if (h == null || h.MaxHealth <= 0f) return 1f;
            return h.Health / h.MaxHealth;
        }

        static void CheckRage(State s)
        {
            HealthObject h = s.Wyrm.HealthObject;
            if (h == null || h.Health <= 0f || HealthFraction(s) >= WyrmTiers.RageAt) return;
            s.Raging = true;
            s.M = WyrmTiers.For(true, s.Tint, s.Level, true);
            SetAnimSpeed(s);
            Main.Log.Msg("wyrm " + s.Id + " enraged at " + (HealthFraction(s) * 100f).ToString("0") + "%: " + s.M);
        }

        // Recolored materials, made once per color and shared by every wyrm of that color.
        static readonly Dictionary<KeyValuePair<int, Tint>, Material> Recolored = new Dictionary<KeyValuePair<int, Tint>, Material>();

        // Backup for when the game hasn't loaded that color's crystal yet: the same material, just tinted.
        static Material TintCopy(Material src, Tint t)
        {
            var key = new KeyValuePair<int, Tint>(src.GetInstanceID(), t);
            Material mine;
            if (Recolored.TryGetValue(key, out mine) && mine != null) return mine;
            mine = new Material(src) { name = src.name + " (" + t + ")" };
            mine.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Color c = CrystalMaterial.LightColor(t);
            if (mine.HasProperty("_Color")) mine.SetColor("_Color", c);
            if (mine.HasProperty("_EmissionColor")) { mine.EnableKeyword("_EMISSION"); mine.SetColor("_EmissionColor", c); }
            Recolored[key] = mine;
            return mine;
        }

        // The crystal wyrm's body is one texture: grey skin with blue crystal bits painted on. Copy it and change the
        // color of the blue parts, so green and purple look just like the blue one, only in their own color.
        static readonly Dictionary<KeyValuePair<int, Tint>, Material> Shifted = new Dictionary<KeyValuePair<int, Tint>, Material>();

        static Material TextureShiftCopy(Material src, Tint t)
        {
            var key = new KeyValuePair<int, Tint>(src.GetInstanceID(), t);
            Material mine;
            if (Shifted.TryGetValue(key, out mine)) return mine;
            mine = null;
            try
            {
                Color target = CrystalMaterial.LightColor(t);
                Material m = new Material(src) { name = src.name + " (" + t + ")" };
                m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                int done = 0;
                foreach (string prop in m.GetTexturePropertyNames())
                {
                    if (prop.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || prop.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    Texture tex = m.GetTexture(prop);
                    if (tex == null) continue;
                    m.SetTexture(prop, HueShiftTexture(tex, target));
                    done++;
                }
                CrystalMaterial.HueShift(m, target);
                if (done > 0) mine = m;
                Main.Log.Msg("wyrm look " + t + ": " + done + " textures shifted on " + src.name + " -> " + CrystalMaterial.ColorProps(m));
            }
            catch (Exception e) { Main.Log.Error("texture shift failed (" + src.name + ", " + t + "): " + e.Message); }
            Shifted[key] = mine;
            return mine;
        }

        static Texture2D HueShiftTexture(Texture src, Color target)
        {
            float th, ts, tv;
            Color.RGBToHSV(target, out th, out ts, out tv);
            int w = src.width, h = src.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = src.name + " shifted", wrapMode = src.wrapMode, filterMode = src.filterMode };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                if (ss < 0.12f) continue; // grey skin stays grey
                // Only the color changes, not how strong it is, so the dull blue-grey skin turns dull green or purple
                // like on the blue wyrm. The bright crystal bits can get a little more vivid.
                Color n = Color.HSVToRGB(th, ss >= 0.6f ? Math.Max(ss, ts * 0.8f) : ss, vv);
                n.a = c.a;
                px[i] = n;
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        static void Recolor(State s)
        {
            Material crystal = CrystalMaterial.CrystalCopy(s.Tint);
            int swapped = 0;
            var seen = new HashSet<string>();
            foreach (Renderer r in s.Root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    seen.Add(m.name);
                    // Only the crystal parts change color.
                    bool crystalPart = m.name.StartsWith("Blue_Crystal", StringComparison.OrdinalIgnoreCase)
                        || m.name.StartsWith("Crystal Wyrm", StringComparison.OrdinalIgnoreCase);
                    if (!crystalPart) continue;
                    Material textured = m.name.StartsWith("Crystal Wyrm", StringComparison.OrdinalIgnoreCase) ? TextureShiftCopy(m, s.Tint) : null;
                    mats[i] = textured ?? crystal ?? TintCopy(m, s.Tint);
                    any = true; swapped++;
                }
                if (any) r.sharedMaterials = mats;
            }
            Main.Log.Msg("wyrm " + s.Id + " recolor " + s.Tint + ": " + swapped + " slots; materials [" + string.Join(", ", seen.ToArray()) + "]");
        }
    }
}
