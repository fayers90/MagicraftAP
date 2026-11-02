
using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MagicraftAP
{
    [BepInPlugin(
        "gatorbloom.magicraft.ap",
        "Magicraft Archipelago",
        "0.1.0"
    )]
    public class Plugin : BaseUnityPlugin
    {
        private bool showMessage = true;

        private string hostName = "archipelago.gg";
        private string port = "";
        private string slotName = "";
        private string password = "";

        private string connectionStatus = "Disconnected";
        private bool isConnected = false;

        private ArchipelagoClient apClient;

        private void HandleChestOpened()
        {
            if (apClient != null)
                apClient.SubmitNextChestCheck();
        }

        private Rect windowRect = new Rect(20, 20, 400, 350);

        private GUIStyle windowStyle;
        private Texture2D windowBackground;

        private void Awake()
        {
            Logger.LogInfo("MagicraftAP loaded");

            ChestDiagnostic.OnChestOpened = HandleChestOpened;

            // ---------------------------------------
            // Initialise Archipelago client
            // ---------------------------------------

            Logger.LogInfo(
                "[MagicraftAP] Initialising Archipelago client..."
            );

            try
            {
                apClient = new ArchipelagoClient(Logger);

                ArchipelagoShop.Initialize(apClient, Logger);

                Logger.LogInfo(
                    "[MagicraftAP] Archipelago client initialised."
                );
            }
            catch (Exception ex)
            {
                connectionStatus = "Client initialisation failed";

                Logger.LogError(
                    "[MagicraftAP] Client initialisation failed: " +
                    ex
                );
            }

            // ---------------------------------------
            // Load saved connection details
            // ---------------------------------------

            hostName = Config.Bind(
                "Archipelago",
                "Host",
                "archipelago.gg",
                "Archipelago server hostname"
            ).Value;

            port = Config.Bind(
                "Archipelago",
                "Port",
                "",
                "Archipelago server port"
            ).Value;

            slotName = Config.Bind(
                "Archipelago",
                "SlotName",
                "",
                "Archipelago player slot name"
            ).Value;

            // ---------------------------------------
            // Register existing Harmony patches
            // ---------------------------------------

            var harmony = new Harmony("gatorbloom.magicraft.ap");

            Logger.LogInfo(
                "RelicPoolPatch compiled: " +
                (typeof(RelicPoolPatch) != null)
            );

            Logger.LogInfo(
                "LiveRelicExporter compiled: " +
                (typeof(LiveRelicExporter) != null)
            );

            Logger.LogInfo(
                "PlayerCreationDiagnostic compiled: " +
                (typeof(PlayerCreationDiagnostic) != null)
            );

            harmony.PatchAll();

            // Verify that the native Magicraft shop update has our Harmony prefix.
            // This runs at startup even if no shop has been entered yet.
            try
            {
                MethodInfo shopMethod = AccessTools.Method(typeof(ItemSystem), "OnUpdate");
                var shopPatches = shopMethod != null ? Harmony.GetPatchInfo(shopMethod) : null;
                int prefixCount = shopPatches != null ? shopPatches.Prefixes.Count : 0;

                Logger.LogWarning(
                    "[MagicraftAP] SHOP PATCH CHECK | Target found: " +
                    (shopMethod != null) + " | Prefixes: " + prefixCount
                );

                bool ourShopPrefixFound = false;
                if (shopPatches != null)
                {
                    foreach (var patch in shopPatches.Prefixes)
                    {
                        string typeName = patch.PatchMethod.DeclaringType != null
                            ? patch.PatchMethod.DeclaringType.FullName
                            : "<unknown>";
                        Logger.LogWarning(
                            "[MagicraftAP] SHOP PREFIX | " + typeName + "." +
                            patch.PatchMethod.Name + " | Owner: " + patch.owner
                        );
                        if (patch.owner == "gatorbloom.magicraft.ap" &&
                            typeName.Contains("NativeShopItemSystemPatch"))
                            ourShopPrefixFound = true;
                    }
                }

                if (!ourShopPrefixFound)
                    Logger.LogError(
                        "[MagicraftAP] SHOP PREFIX MISSING | Check that " +
                        "NativeShopItemSystemPatch uses TargetMethod() (not Target()) " +
                        "and that ArchipelagoShop.cs is included in the build."
                    );
            }
            catch (Exception ex)
            {
                Logger.LogError("[MagicraftAP] SHOP PATCH DIAGNOSTIC FAILED: " + ex);
            }

            Logger.LogInfo(
                "MagicraftAP Harmony patches registered"
            );

            Logger.LogWarning(
                "[MagicraftAP] BEPINEX LOGGING TEST"
            );

            // ---------------------------------------
            // BattleData.Initialize diagnostics
            // ---------------------------------------

            MethodInfo battleDataMethod = AccessTools.Method(
                typeof(BattleData),
                nameof(BattleData.Initialize)
            );

            if (battleDataMethod == null)
            {
                Logger.LogError(
                    "BattleData.Initialize could not be found!"
                );
            }
            else
            {
                var battleDataPatchInfo =
                    Harmony.GetPatchInfo(battleDataMethod);

                if (battleDataPatchInfo == null)
                {
                    Logger.LogError(
                        "No Harmony patches found on BattleData.Initialize!"
                    );
                }
                else
                {
                    Logger.LogInfo(
                        "BattleData.Initialize postfix count: " +
                        battleDataPatchInfo.Postfixes.Count
                    );

                    foreach (var patch in battleDataPatchInfo.Postfixes)
                    {
                        Logger.LogInfo(
                            "Registered postfix: " +
                            patch.PatchMethod.DeclaringType.FullName +
                            "." +
                            patch.PatchMethod.Name +
                            " | Owner: " +
                            patch.owner
                        );
                    }
                }
            }

            // ---------------------------------------
            // PlayerMgr.CreatePlayer diagnostics
            // ---------------------------------------

            MethodInfo createPlayerMethod = AccessTools.Method(
                typeof(PlayerMgr),
                nameof(PlayerMgr.CreatePlayer)
            );

            if (createPlayerMethod == null)
            {
                Logger.LogError(
                    "PlayerMgr.CreatePlayer could not be found!"
                );
            }
            else
            {
                var createPlayerPatchInfo =
                    Harmony.GetPatchInfo(createPlayerMethod);

                if (createPlayerPatchInfo == null)
                {
                    Logger.LogError(
                        "No Harmony patches found on PlayerMgr.CreatePlayer!"
                    );
                }
                else
                {
                    Logger.LogInfo(
                        "CreatePlayer prefix count: " +
                        createPlayerPatchInfo.Prefixes.Count
                    );

                    foreach (var patch in createPlayerPatchInfo.Prefixes)
                    {
                        Logger.LogInfo(
                            "Registered CreatePlayer prefix: " +
                            patch.PatchMethod.DeclaringType.FullName +
                            "." +
                            patch.PatchMethod.Name +
                            " | Owner: " +
                            patch.owner
                        );
                    }

                    Logger.LogInfo(
                        "CreatePlayer postfix count: " +
                        createPlayerPatchInfo.Postfixes.Count
                    );

                    foreach (var patch in createPlayerPatchInfo.Postfixes)
                    {
                        Logger.LogInfo(
                            "Registered CreatePlayer postfix: " +
                            patch.PatchMethod.DeclaringType.FullName +
                            "." +
                            patch.PatchMethod.Name +
                            " | Owner: " +
                            patch.owner
                        );
                    }
                }
            }

            Logger.LogInfo(
                "MagicraftAP startup diagnostics complete"
            );
        }

        private void Update()
        {
            // Preserve relic catalogue export.
            RelicExporter.TryExport();

            // F8 toggles the connection window.
            if (Input.GetKeyDown(KeyCode.F8))
            {
                showMessage = !showMessage;
            }

            // Monitor connection status.
            if (apClient != null)
            {
                apClient.RefreshConnectionStatus();

                connectionStatus = apClient.Status;
                isConnected = apClient.IsConnected;
            }
        }

        private void CreateWindowStyle()
        {
            if (windowStyle != null)
                return;

            windowBackground = new Texture2D(1, 1);

            windowBackground.SetPixel(
                0,
                0,
                new Color(0.06f, 0.10f, 0.17f, 1f)
            );

            windowBackground.Apply();

            windowStyle = new GUIStyle(GUI.skin.window);

            windowStyle.normal.background = windowBackground;
            windowStyle.onNormal.background = windowBackground;

            windowStyle.normal.textColor = Color.white;
            windowStyle.fontSize = 16;
            windowStyle.fontStyle = FontStyle.Bold;
            windowStyle.alignment = TextAnchor.UpperCenter;
        }

        private void OnGUI()
        {
            if (!showMessage)
                return;

            CreateWindowStyle();

            windowRect = GUI.Window(
                87421,
                windowRect,
                DrawArchipelagoWindow,
                "Magicraft Archipelago",
                windowStyle
            );
        }

        private void DrawArchipelagoWindow(int windowId)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontSize = 13;

            GUIStyle statusStyle = new GUIStyle(labelStyle);
            statusStyle.fontStyle = FontStyle.Bold;

            GUI.Label(
                new Rect(20, 38, 350, 25),
                "Archipelago Server Connection",
                labelStyle
            );

            GUI.Label(
                new Rect(20, 75, 100, 25),
                "Host Name",
                labelStyle
            );

            hostName = GUI.TextField(
                new Rect(125, 75, 250, 25),
                hostName
            );

            GUI.Label(
                new Rect(20, 112, 100, 25),
                "Port",
                labelStyle
            );

            port = GUI.TextField(
                new Rect(125, 112, 250, 25),
                port
            );

            GUI.Label(
                new Rect(20, 149, 100, 25),
                "Name",
                labelStyle
            );

            slotName = GUI.TextField(
                new Rect(125, 149, 250, 25),
                slotName
            );

            GUI.Label(
                new Rect(20, 186, 100, 25),
                "Password",
                labelStyle
            );

            password = GUI.PasswordField(
                new Rect(125, 186, 250, 25),
                password,
                '*'
            );

            GUI.Label(
                new Rect(125, 213, 250, 20),
                "(Optional)",
                labelStyle
            );

            statusStyle.normal.textColor =
                isConnected ? Color.green : Color.yellow;

            GUI.Label(
                new Rect(20, 245, 355, 30),
                "Status: " + connectionStatus,
                statusStyle
            );

            if (GUI.Button(
                new Rect(20, 285, 170, 35),
                "Connect"
            ))
            {
                AttemptConnection();
            }

            if (GUI.Button(
                new Rect(205, 285, 170, 35),
                "Disconnect"
            ))
            {
                Disconnect();
            }

            GUIStyle shortcutStyle = new GUIStyle(labelStyle);
            shortcutStyle.alignment = TextAnchor.MiddleCenter;
            shortcutStyle.fontSize = 12;
            shortcutStyle.normal.textColor = new Color(0.75f, 0.82f, 0.92f, 1f);

            GUI.Label(
                new Rect(20, 324, 355, 20),
                "Press F8 to show/hide the Archipelago menu",
                shortcutStyle
            );

            GUI.DragWindow(
                new Rect(0, 0, 400, 30)
            );
        }

        private void AttemptConnection()
        {
            if (apClient == null)
            {
                connectionStatus = "Client unavailable";
                return;
            }

            if (string.IsNullOrWhiteSpace(hostName) ||
                string.IsNullOrWhiteSpace(port) ||
                string.IsNullOrWhiteSpace(slotName))
            {
                connectionStatus = "Missing connection details";
                return;
            }

            int parsedPort;

            if (!int.TryParse(port, out parsedPort) ||
                parsedPort < 1 ||
                parsedPort > 65535)
            {
                connectionStatus = "Invalid port";
                return;
            }

            // Save connection settings.
            Config.Bind(
                "Archipelago",
                "Host",
                "archipelago.gg"
            ).Value = hostName;

            Config.Bind(
                "Archipelago",
                "Port",
                ""
            ).Value = port;

            Config.Bind(
                "Archipelago",
                "SlotName",
                ""
            ).Value = slotName;

            Logger.LogInfo(
                "[MagicraftAP] Connect requested."
            );

            apClient.Connect(
                hostName,
                parsedPort,
                slotName,
                password
            );

            connectionStatus = apClient.Status;
            isConnected = apClient.IsConnected;
        }

        private void Disconnect()
        {
            if (apClient != null)
            {
                apClient.Disconnect();

                connectionStatus = apClient.Status;
                isConnected = apClient.IsConnected;
            }
            else
            {
                connectionStatus = "Disconnected";
                isConnected = false;
            }
        }

        private void OnDestroy()
        {
            if (apClient != null)
            {
                apClient.Disconnect();
            }

            ChestDiagnostic.OnChestOpened = null;

            if (windowBackground != null)
            {
                Destroy(windowBackground);
                windowBackground = null;
            }
        }
    }
}
