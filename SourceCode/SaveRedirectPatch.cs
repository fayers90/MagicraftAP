
using System;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MagicraftAP
{
    [HarmonyPatch]
    public static class SaveRedirectPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource(
                "MagicraftAP.SaveRedirect"
            );

        private const string ProfileName = "TestProfile";

        private static string profilePath;

        public static string ProfilePath
        {
            get
            {
                if (profilePath == null)
                {
                    profilePath = Path.Combine(
                        Application.persistentDataPath,
                        "ArchipelagoProfiles",
                        ProfileName
                    );
                }

                return profilePath;
            }
        }

        [HarmonyPatch(typeof(DataMgr), "SetAllFileAttributes")]
        [HarmonyPrefix]
        private static void RedirectBeforeSaveAccess()
        {
            try
            {
                string destination = ProfilePath;

                Directory.CreateDirectory(destination);

                string normalPath = Path.GetFullPath(
                    Application.persistentDataPath
                );

                string apPath = Path.GetFullPath(destination);

                if (string.Equals(
                    normalPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    ),
                    apPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    ),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Archipelago save path matches normal save path."
                    );
                }

                DataMgr.dataPath =
                    destination.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    ) + Path.DirectorySeparatorChar;

                Log.LogWarning(
                    "[MagicraftAP] SAVE REDIRECT ACTIVE"
                );

                Log.LogWarning(
                    "[MagicraftAP] AP profile: " + ProfileName
                );

                Log.LogWarning(
                    "[MagicraftAP] Save directory: " +
                    DataMgr.dataPath
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] SAVE REDIRECT FAILED: " + ex
                );

                throw;
            }
        }
    }
}
