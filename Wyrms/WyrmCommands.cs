using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Alta.Chunks;
using Alta.Console;
using Alta.Intelligence;
using Alta.Networking;
using UnityEngine;

namespace CrystalLeather
{
    // Server console commands, like "wyrmvar list" or "wyrmvar spawn purple Cheeto35".
    [Module("wyrmvar", "CrystalLeather: crystal wyrm colors and depth scaling")]
    public static class WyrmCommands
    {
        public static bool Verbose;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static string Line(WyrmVariants.State s)
        {
            HealthObject h = s.Wyrm.HealthObject;
            Vector3 p = s.Root.transform.position;
            return string.Format(Inv, "{0} '{1}' L{2} {3}: {4} | hp {5:0}/{6:0} ({7:0}%){8} at ({9:0},{10:0},{11:0})",
                s.Id, s.Name, s.Level, s.Crystal ? s.Tint.ToString() : "normal", s.M,
                h != null ? h.Health : 0f, h != null ? h.MaxHealth : 0f, WyrmVariants.HealthFraction(s) * 100f,
                s.Raging ? " RAGING" : "", p.x, p.y, p.z);
        }

        [Command("list", "List the wyrms that are loaded, on every level or just one: list [level]")]
        public static string List(int level = -1)
        {
            var rows = WyrmVariants.All.Where(s => level < 0 || s.Level == level).OrderBy(s => s.Level).ThenBy(s => s.Id).ToList();
            if (rows.Count == 0) return "no wyrms loaded" + (level >= 0 ? " on level " + level : "");
            var sb = new StringBuilder();
            sb.Append(rows.Count + " wyrms: blue " + rows.Count(r => r.Crystal && r.Tint == Tint.Blue) + ", green " + rows.Count(r => r.Crystal && r.Tint == Tint.Green)
                + ", purple " + rows.Count(r => r.Crystal && r.Tint == Tint.Purple) + ", normal " + rows.Count(r => !r.Crystal));
            foreach (var s in rows) sb.Append("\n  ").Append(Line(s));
            return sb.ToString();
        }

        [Command("odds", "Show the color chances and how tough each wyrm is on a level: odds <level>")]
        public static string Odds(int level)
        {
            float b, g, p;
            WyrmTiers.Shares(level, out b, out g, out p);
            return string.Format(Inv, "L{0}: blue {1:0.#}%, green {2:0.#}%, purple {3:0.#}%\n  normal: {4}\n  blue: {5}\n  green: {6}\n  purple: {7} (raging: {8})",
                level, b * 100f, g * 100f, p * 100f,
                WyrmTiers.For(false, Tint.Blue, level, false), WyrmTiers.For(true, Tint.Blue, level, false),
                WyrmTiers.For(true, Tint.Green, level, false), WyrmTiers.For(true, Tint.Purple, level, false), WyrmTiers.For(true, Tint.Purple, level, true));
        }

        [Command("verbose", "Log extra detail about every wyrm attack and hit: verbose <true|false>")]
        public static string SetVerbose(bool on) { Verbose = on; return "verbose " + on; }

        static Player FindPlayer(string name)
        {
            foreach (Alta.Networking.Scripts.Player.IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p == null || p.UserInfo == null || p.PlayerController == null || p.PlayerController.Head == null) continue;
                if (string.Equals(p.UserInfo.Username, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        internal static Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out hit, 20f)) return hit.point + Vector3.up * 0.2f;
            return p;
        }

        [Command("spawn", "Spawn a wyrm 6 m in front of a player: spawn <blue|green|purple|normal> <player>")]
        public static string Spawn(string kind, string player)
        {
            Player pl = FindPlayer(player);
            if (pl == null) return "no player named '" + player + "'";
            bool normal = string.Equals(kind, "normal", StringComparison.OrdinalIgnoreCase);
            Tint want = Tint.Blue;
            if (!normal && !WyrmTiers.TryParse(kind, out want)) return "kind has to be blue, green, purple or normal";
            int level = WyrmVariants.LevelAtPos(pl.PlayerController.Head.position);
            if (level < 0) return player + " isn't in the mines";
            float b, g, p;
            WyrmTiers.Shares(level, out b, out g, out p);
            float share = normal ? 1f : want == Tint.Blue ? b : want == Tint.Green ? g : p;
            string note = share <= 0f ? " (it never shows up on this level by itself)" : "";

            NetworkPrefab prefab = normal ? PrefabManager.AllPrefabs.FirstOrDefault(x => x != null && x.name == "Wyrm") : WyrmPrefabs.Get(want);
            if (prefab == null) return "couldn't find the " + (normal ? "normal" : want.ToString().ToLowerInvariant()) + " wyrm";
            Transform head = pl.PlayerController.Head;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 pos = Ground(head.position + fwd.normalized * 6f);
            Chunk chunk = LocationChunkHelper.GetBestContaining(pos);
            NetworkEntity e = SpawnHelper.Spawn(prefab, SpawnData.Default, chunk, pos, Quaternion.identity);
            if (e == null) return "spawn failed";
            return "spawned '" + prefab.name + "' id " + e.Identifier + " on level " + level + note + ": " + WyrmTiers.For(!normal, want, level, false);
        }

        [Command("clear", "Remove every wyrm within 30 m of a player: clear <player>")]
        public static string Clear(string player)
        {
            Player pl = FindPlayer(player);
            if (pl == null) return "no player named '" + player + "'";
            Vector3 at = pl.PlayerController.Head.position;
            int n = 0;
            foreach (var s in WyrmVariants.All.ToList())
                if (Vector3.Distance(s.Root.transform.position, at) < 30f) { s.Root.SceneDestroy(); n++; }
            return "removed " + n + " wyrms";
        }
    }
}
