
using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace MagicraftAP
{
    public static class RelicExporter
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("MagicraftAP.RelicExporter");

        private static bool exported = false;

        public static void TryExport()
        {
            if (exported)
                return;

            if (RelicConfig.list == null || RelicConfig.list.Count == 0)
                return;

            try
            {
                string path = Path.Combine(
                    Paths.PluginPath,
                    "MagicraftAP_Relics.csv"
                );

                var lines = new[]
                {
                    "ID,Name,DropType,MaxCount,NeedActivate,AbilityType,EndlessReplaceID"
                }
                .Concat(
                    RelicConfig.list
                        .OrderBy(r => r.id)
                        .Select(r => string.Join(",", new[]
                        {
                            r.id.ToString(),
                            EscapeCsv(r.GetName(false)),
                            r.dropType.ToString(),
                            r.maxCount.ToString(),
                            r.needActivate.ToString(),
                            r.abilityType.ToString(),
                            r.EndlessReplaceID.ToString()
                        }))
                );

                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));

                exported = true;

                Log.LogInfo(
                    "[MagicraftAP] Exported relic catalogue with names: " +
                    path
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Relic export failed: " + ex
                );
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
                return "";

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
