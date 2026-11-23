using System;
using System.Collections.Generic;
using System.Reflection;
using Alta;
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    // Makes the three new leathers ("Crystal Leather", "Green Crystal Leather" and "Purple Crystal Leather") by copying
    // the normal wyrm leather. Each gets its own fixed id and goes into the game's list of materials, so items made
    // from it save and show up for everyone.
    public static class CrystalMaterial
    {
        static readonly Tint[] Tints = { Tint.Blue, Tint.Green, Tint.Purple };
        static readonly PhysicalMaterial[] _instances = new PhysicalMaterial[3];
        // The normal wyrm leather the new ones are copied from.
        public static PhysicalMaterial Source { get; private set; }
        static readonly bool[] _crystalLook = new bool[3];

        // The blue one.
        public static PhysicalMaterial Instance { get { return _instances[0]; } }
        public static PhysicalMaterial For(Tint t) { return _instances[(int)t]; }

        public static Tint? TintOf(PhysicalMaterial m)
        {
            if (m == null) return null;
            foreach (Tint t in Tints)
                if (_instances[(int)t] != null && m.Hash == Ids.Leather(t).Hash) return t;
            return null;
        }

        public static bool Is(PhysicalMaterial m) { return TintOf(m).HasValue; }

        // Safe to call more than once. Makes the leathers the first time, and puts them back in the game's list if
        // they've gone missing from it.
        public static bool Register()
        {
            try
            {
                if (!Game.MaterialTableReady()) { Main.Log.Error("material table not loaded yet"); return false; }

                PhysicalMaterial src = null;
                bool changed = false;
                foreach (Tint t in Tints)
                {
                    Ids.LeatherInfo info = Ids.Leather(t);
                    if (_instances[(int)t] == null)
                    {
                        if (src == null) src = NameLookup.Find(Game.AllMaterials(), m => m.name, Ids.SourceName);
                        if (src != null) Source = src;
                        if (src == null) { Main.Log.Error("source leather '" + Ids.SourceName + "' not found"); return false; }
                        PhysicalMaterial clone = UnityEngine.Object.Instantiate(src);
                        clone.name = info.Name;
                        clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        Game.SetMaterialHash(clone, info.Hash);
                        if (!Main.IsServer) Recolor(clone, info);
                        _instances[(int)t] = clone;
                        Main.Log.Msg("registered " + info.Name + " hash=" + info.Hash + " from " + src.name);
                    }
                    if (Game.MaterialAt(info.Hash) != _instances[(int)t])
                    {
                        Game.PutMaterial(info.Hash, _instances[(int)t]);
                        changed = true;
                    }
                }
                if (changed) Game.ResetMaterialNames();
                return true;
            }
            catch (Exception e)
            {
                Main.Log.Error("Register failed: " + e);
                return false;
            }
        }

        const float EmissionBoost = 1.6f;
        static float _nextLookTry;

        // Finds the game's crystal material for a color.
        public static Material FindCrystal(Ids.LeatherInfo info)
        {
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.name == null) continue;
                if (info.CrystalMat != null) { if (m.name == info.CrystalMat) return m; continue; }
                bool all = true;
                foreach (string part in info.CrystalMatParts)
                    if (m.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) { all = false; break; }
                if (all && m.name.IndexOf("Leather", StringComparison.OrdinalIgnoreCase) < 0) return m;
            }
            return null;
        }

        static readonly Dictionary<Tint, Material> _crystalCopies = new Dictionary<Tint, Material>();

        // A glowing copy of a color's crystal material, used for the wyrms' crystals and their spit. Null until the
        // game has loaded that crystal.
        public static Material CrystalCopy(Tint t)
        {
            Material mine;
            if (_crystalCopies.TryGetValue(t, out mine) && mine != null) return mine;
            Ids.LeatherInfo info = Ids.Leather(t);
            Material crystal = FindCrystal(info);
            bool shifted = false;
            if (crystal == null && t == Tint.Purple)
            {
                // The pink crystal only loads near pink crystal clusters, so recolor the blue one instead.
                crystal = FindCrystal(Ids.Leather(Tint.Blue));
                shifted = crystal != null;
            }
            if (crystal == null) return null;
            mine = new Material(crystal) { name = info.Name + "_m" };
            if (shifted)
            {
                HueShift(mine, LightColor(t));
                Main.Log.Msg(info.Name + " hue-shifted from " + crystal.name + ": " + ColorProps(mine) + " | before: " + ColorProps(crystal));
            }
            if (mine.HasProperty("_EmissionColor"))
            {
                mine.EnableKeyword("_EMISSION");
                mine.SetColor("_EmissionColor", mine.GetColor("_EmissionColor") * EmissionBoost);
            }
            mine.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _crystalCopies[t] = mine;
            Main.Log.Msg(info.Name + " material: " + ColorProps(mine));
            Main.Log.Msg(info.Name + " look: " + crystal.name + " (" + crystal.shader.name + ") color="
                + (crystal.HasProperty("_Color") ? "#" + ColorUtility.ToHtmlStringRGB(crystal.GetColor("_Color")) : "?")
                + " emission=" + (crystal.HasProperty("_EmissionColor") ? "#" + ColorUtility.ToHtmlStringRGB(crystal.GetColor("_EmissionColor")) : "?"));
            return mine;
        }

        // The leather's look: the same crystal, with a softer glow than the wyrms. This only changes how bright the
        // leather itself looks. The light it gives off is in GlowManager.
        const float LeatherEmission = 0.65f;
        static readonly Dictionary<Tint, Material> _leatherCopies = new Dictionary<Tint, Material>();

        public static Material LeatherCopy(Tint t)
        {
            Material mine;
            if (_leatherCopies.TryGetValue(t, out mine) && mine != null) return mine;
            Material crystal = CrystalCopy(t);
            if (crystal == null) return null;
            mine = new Material(crystal) { name = Ids.Leather(t).Name + "_leather_m" };
            if (mine.HasProperty("_EmissionColor"))
                mine.SetColor("_EmissionColor", mine.GetColor("_EmissionColor") * LeatherEmission);
            mine.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _leatherCopies[t] = mine;
            return mine;
        }

        // Changes every colored setting on a material to the target color, keeping how bright each one is.
        public static void HueShift(Material m, Color target)
        {
            float th, ts, tv;
            Color.RGBToHSV(target, out th, out ts, out tv);
            for (int i = 0; i < m.shader.GetPropertyCount(); i++)
            {
                if (m.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string prop = m.shader.GetPropertyName(i);
                Color c = m.GetColor(prop);
                float h, sat, v;
                Color.RGBToHSV(c, out h, out sat, out v);
                if (sat < 0.05f) continue;
                // Strong colors get the target's strength, pale ones stay pale.
                Color n = Color.HSVToRGB(th, sat >= 0.5f ? Math.Max(sat, ts * 0.8f) : sat, v, true);
                n.a = c.a;
                m.SetColor(prop, n);
            }
        }

        public static string ColorProps(Material m)
        {
            var sb = new System.Text.StringBuilder(m.shader.name + " ");
            for (int i = 0; i < m.shader.GetPropertyCount(); i++)
            {
                var type = m.shader.GetPropertyType(i);
                string prop = m.shader.GetPropertyName(i);
                if (type == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append(prop).Append("=#").Append(ColorUtility.ToHtmlStringRGB(m.GetColor(prop))).Append(' ');
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture) sb.Append(prop).Append("=tex:").Append(m.GetTexture(prop) != null ? m.GetTexture(prop).name : "none").Append(' ');
            }
            return sb.ToString();
        }

        public static bool AllLooksApplied { get { return _crystalLook[0] && _crystalLook[1] && _crystalLook[2]; } }

        // Players' side, every frame: the crystal materials only load once you're in the world (the pink one only near
        // pink crystals), so keep trying every 2 seconds until each leather has its crystal look, then update any
        // leather that's already out.
        public static void TryUpgradeLook()
        {
            if (Main.IsServer || AllLooksApplied || Time.time < _nextLookTry) return;
            _nextLookTry = Time.time + 2f;
            foreach (Tint t in Tints)
            {
                PhysicalMaterial inst = _instances[(int)t];
                if (inst == null || _crystalLook[(int)t]) continue;
                Material mine = LeatherCopy(t);
                if (mine == null) continue;
                Game.SetAllChannels(inst, mine);
                _crystalLook[(int)t] = true;
                foreach (PhysicalMaterialPart p in new List<PhysicalMaterialPart>(GlowManager.Parts.Values))
                    if (p != null && p.PhysicalMaterial == inst) p.SetMaterial(inst);
            }
        }

        static Color Hex(string hex)
        {
            float r, g, b;
            Ids.TryParseHex(hex, out r, out g, out b);
            return new Color(r, g, b, 1f);
        }

        public static Color LightColor(Tint t) { return Hex(Ids.Leather(t).AlbedoHex); }

        // Players' side: plain tinted leather to use until the crystal material is loaded.
        static void Recolor(PhysicalMaterial clone, Ids.LeatherInfo info)
        {
            Material baseMat = Game.ChannelBaseMaterial(clone);
            if (baseMat == null) return;
            Material mine = new Material(baseMat) { name = info.Name + "_m" };
            Color albedo = Hex(info.AlbedoHex);
            for (int i = 0; i < mine.shader.GetPropertyCount(); i++)
            {
                if (mine.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string prop = mine.shader.GetPropertyName(i);
                if (prop.StartsWith("_Color")) mine.SetColor(prop, albedo);
            }
            if (mine.HasProperty("_EmissionColor"))
            {
                mine.EnableKeyword("_EMISSION");
                mine.SetColor("_EmissionColor", Hex(info.EmissionHex) * 0.95f);
            }
            mine.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Game.SetAllChannels(clone, mine);
        }
    }
}
