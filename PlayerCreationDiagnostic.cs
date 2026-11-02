
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch(typeof(PlayerMgr), nameof(PlayerMgr.CreatePlayer))]
    public static class PlayerCreationDiagnostic
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource(
                "MagicraftAP.PlayerCreation"
            );

        [HarmonyPrefix]
        public static void Prefix()
        {
            Log.LogWarning(
                "[MagicraftAP] PlayerMgr.CreatePlayer ENTERED"
            );
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            Log.LogWarning(
                "[MagicraftAP] PlayerMgr.CreatePlayer COMPLETED"
            );
        }
    }
}
