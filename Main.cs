using System;
using MelonLoader;

[assembly: MelonInfo(typeof(CrystalLeather.Main), "CrystalLeather", "1.0.0", "Cheeto35")]
#if QUEST
[assembly: MelonGame("Alta", "Att_Quest")]
#else
[assembly: MelonGame("Alta", "A Township Tale")]
#endif

namespace CrystalLeather
{
    public class Main : MelonMod
    {
        public static MelonLogger.Instance Log;
        public static bool IsServer;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
#if QUEST
            // The Quest game is only ever a player's game. Servers run the PC version.
            IsServer = false;
            Log.Msg("CrystalLeather 1.0.0 starting (quest)");
#else
            IsServer = ApplicationManager.IsHeadless
                || CommandLineArguments.Contains(CommandLineArguments.StartServerArgument);
            Alta.Console.CommandService.CommandCollection.Collect(typeof(Main).Assembly);
            Log.Msg("CrystalLeather 1.0.0 starting (" + (IsServer ? "server" : "client") + ")");
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            CrystalMaterial.Register();
            WyrmPrefabs.Register();
        }

        public override void OnUpdate()
        {
            try { WyrmVariants.Tick(); } catch (Exception e) { Log.Error("wyrms: " + e); }
            if (IsServer) return;
            try { CrystalMaterial.TryUpgradeLook(); } catch (Exception e) { Log.Error("look: " + e); }
            try { GlowManager.Tick(); } catch (Exception e) { Log.Error("glow: " + e); }
        }
    }
}
