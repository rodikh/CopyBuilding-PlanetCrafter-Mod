using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace CopyBuildingMod
{
    public sealed partial class Plugin
    {
        [HarmonyPatch(typeof(PlayerBuilder), "OnConstructed")]
        private static class PlayerBuilder_OnConstructed_Patch
        {
            private static void Postfix(GameObject result)
            {
                _instance?.ApplyCopiedSettingsIfNeeded(result);
            }
        }

        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.SetNewGhost))]
        private static class PlayerBuilder_SetNewGhost_Patch
        {
            private static void Prefix(Group groupConstructible)
            {
                _instance?.HandleSetNewGhostCalled(groupConstructible);
            }
        }

        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.InputOnCancelAction))]
        private static class PlayerBuilder_InputOnCancelAction_Patch
        {
            private static void Postfix()
            {
                _instance?.ResetCopySession();
            }
        }

        [HarmonyPatch(typeof(PlayerInputDispatcher), nameof(PlayerInputDispatcher.OnOpenConstructionDispatcher))]
        private static class PlayerInputDispatcher_OnOpenConstructionDispatcher_Patch
        {
            private static void Prefix()
            {
                _instance?.ResetCopySession();
            }
        }
    }
}
