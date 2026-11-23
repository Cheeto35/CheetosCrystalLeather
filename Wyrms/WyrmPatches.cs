#if QUEST
using Il2CppAlta.Intelligence;
#else
using Alta.Impact;
using Alta.Intelligence;
using Alta.Intelligence.Wyrms;
using Alta.Networking;
#endif
using HarmonyLib;
using UnityEngine;

namespace CrystalLeather
{
    [HarmonyPatch(typeof(Wyrm), "Initialize")]
    static class WyrmInitializePatch
    {
        static void Postfix(Wyrm __instance) { WyrmVariants.Track(__instance); }
    }

#if !QUEST
    // These all change how wyrms fight. That runs on the server, so they aren't in the Quest build.

    // Attack speed: every move a wyrm makes (bite, spit, spin, dig, coming up...) gets shorter the faster it is.
    // Other creatures can run through this same code, so only touch wyrms.
    [HarmonyPatch(typeof(BasicActiveAction<Wyrm>), "SetDuration", new[] { typeof(float) })]
    static class WyrmActionDurationPatch
    {
        static int _logged;

        static void Prefix(object __instance, ref float duration)
        {
            IActiveAction a = __instance as IActiveAction;
            Wyrm w = a != null ? a.GeneralTopic as Wyrm : null;
            WyrmVariants.State s = WyrmVariants.Get(w);
            if (s == null || s.M.Speed == 1f) return;
            float before = duration;
            duration /= s.M.Speed;
            if (WyrmCommands.Verbose || _logged < 10)
            {
                _logged++;
                Main.Log.Msg("wyrm " + s.Id + " " + __instance.GetType().Name + " " + before.ToString("0.00") + "s -> " + duration.ToString("0.00") + "s");
            }
        }
    }

    // Damage: bites and scythe hits from a wyrm get its damage boost.
    [HarmonyPatch(typeof(HealthObject), "DetectImpact")]
    static class WyrmDamagePatch
    {
        static int _logged;

        static void Prefix(HealthObject __instance, ImpactHit impact)
        {
            if (!Main.IsServer || impact == null || !impact.IsValid) return;
            ImpactHitWithTool hit = impact as ImpactHitWithTool;
            if (WyrmCommands.Verbose && __instance is PlayerHealth)
                Main.Log.Msg("player hit on '" + __instance.name + "': " + impact.GetType().Name + " " + impact.ImpactType + " dmg " + impact.HealthDamage.ToString("0.00")
                    + " tool=" + (hit != null && hit.Tool != null ? hit.Tool.name : "none")
                    + " projectile=" + (hit != null && hit.Tool != null && hit.Tool.GetComponentInParent<Projectile>() != null));
            if (hit == null || hit.Tool == null) return;
            if (hit.Tool.GetComponentInParent<Projectile>() != null) return; // spit is handled when it's fired, below
            NetworkEntity root = WyrmVariants.RootOf(hit.Tool.transform);
            WyrmVariants.State s = WyrmVariants.GetByRoot(root);
            if (s == null || s.M.Damage == 1f) return;
            if (WyrmVariants.RootOf(__instance.transform) == root) return; // it hit itself
            float before = impact.HealthDamage;
            impact.Multipliers *= s.M.Damage;
            if (WyrmCommands.Verbose || _logged < 20)
            {
                _logged++;
                Main.Log.Msg("wyrm hit: " + s.Id + (s.Crystal ? " " + s.Tint : " normal") + " L" + s.Level + " x" + s.M.Damage.ToString("0.00")
                    + " on '" + __instance.name + "' dmg " + before.ToString("0.0") + " -> " + impact.HealthDamage.ToString("0.0"));
            }
        }
    }

    // Spit damage: the spit has usually disappeared by the time the hit lands, so give it the boost when it's fired.
    [HarmonyPatch]
    static class WyrmSpitDamagePatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(Projectile)))
                if (m.Name == "Shoot" && m.GetParameters().Length == 2) return m;
            return null;
        }

        static int _logged;

        static void Prefix(Projectile __instance)
        {
            if (!Main.IsServer || __instance == null) return;
            WyrmVariants.State s = WyrmVariants.Get(__instance.Shooter as Wyrm);
            if (s == null || s.M.Damage == 1f) return;
            __instance.SetupDamageMultiplier(s.M.Damage);
            if (WyrmCommands.Verbose || _logged < 10)
            {
                _logged++;
                Main.Log.Msg("wyrm spit: " + s.Id + (s.Crystal ? " " + s.Tint : " normal") + " L" + s.Level + " '" + __instance.name + "' damage x" + s.M.Damage.ToString("0.00"));
            }
        }
    }

    // Green's behavior is normally set up on the creature itself (WyrmPrefabs.Tune). These two are a backup in case
    // that didn't work, so green still bites more often and stays up longer.
    [HarmonyPatch(typeof(ActiveMeleeAttack), "OnStart")]
    static class GreenFlurryPatch
    {
        static void Postfix(ActiveMeleeAttack __instance)
        {
            WyrmVariants.State s = WyrmVariants.Get(__instance.Topic);
            if (s == null || !s.Crystal || s.Tint != Tint.Green || WyrmPrefabs.OwnActions(Tint.Green)) return;
            __instance.Topic.FlurryChance += WyrmTiers.GreenFlurry - WyrmTiers.VanillaFlurry;
        }
    }

    [HarmonyPatch(typeof(Wyrm), "IdleStart", MethodType.Setter)]
    static class GreenStayOutPatch
    {
        static void Prefix(Wyrm __instance, ref float value)
        {
            WyrmVariants.State s = WyrmVariants.Get(__instance);
            if (s != null && s.Crystal && s.Tint == Tint.Green && !WyrmPrefabs.OwnActions(Tint.Green)) value += WyrmTiers.GreenStayOut;
        }
    }

    // Purple rage: when a raging purple wyrm picks its next move, spinning and spitting count double.
    // Every creature picks its moves through this same code, so only look at wyrms.
    [HarmonyPatch(typeof(ActionDecider<Wyrm>), "GetBestAction")]
    static class WyrmDecidingPatch
    {
        static readonly System.Reflection.FieldInfo AgentField = AccessTools.Field(typeof(ActionDecider<Wyrm>), "agent");
        public static WyrmVariants.State Raging;
        static int _logged;

        static void Prefix(object __instance)
        {
            Raging = null;
            if (!(__instance is ActionDecider<Wyrm>)) return;
            WyrmVariants.State s = WyrmVariants.Get(AgentField.GetValue(__instance) as Wyrm);
            if (s != null && s.Raging) Raging = s;
        }

        static void Postfix(object __result)
        {
            if (Raging == null) return;
            if (WyrmCommands.Verbose || _logged < 10)
            {
                _logged++;
                Main.Log.Msg("raging wyrm " + Raging.Id + " picked " + Traverse.Create(__result).Field("Action").Property("Name").GetValue<string>());
            }
        }

        static void Finalizer() { Raging = null; }
    }

    [HarmonyPatch(typeof(ActionComponent), "Multiplier", MethodType.Getter)]
    static class RageWeightPatch
    {
        static void Postfix(ActionComponent __instance, ref float __result)
        {
            if (WyrmDecidingPatch.Raging == null) return;
            if (__instance is WyrmSpinSettings || __instance is WyrmSpitSettings) __result *= WyrmTiers.RageAttackWeight;
        }
    }
#endif
}
