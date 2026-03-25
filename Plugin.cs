using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CopyBuildingMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "rodikh.planetcrafter.copybuilding";
        public const string PluginName = "Copy Building";
        public const string PluginVersion = "1.0.0";

        private static Plugin _instance;
        private static readonly Harmony Harmony = new Harmony(PluginGuid);

        private ConfigEntry<bool> _copySettings;
        private ConfigEntry<float> _maxCopyDistance;

        private InputAction _copyAction;
        private CopiedBuildingContext _copiedContext;
        private bool _copySessionActive;
        private int _activePrepareRequestId;
        private int _nextPrepareRequestId;
        private bool _applyingCopySelection;

        private static bool _checkedNearbyPrepareApi;
        private static Action<MonoBehaviour, Vector3, Action> _nearbyPrepareSetNewGhostApi;

        private void Awake()
        {
            _instance = this;

            _copySettings = Config.Bind(
                "General",
                "CopyBuildingSettings",
                true,
                "If true, copied buildings also try to copy configurable settings (recipe/filter/miner setting/text/color/linked planet).");

            _maxCopyDistance = Config.Bind(
                "General",
                "MaxCopyDistance",
                60f,
                "Maximum raycast distance used to detect a building to copy.");

            _copyAction = new InputAction("CopyBuilding", InputActionType.Button, "<Mouse>/middleButton");
            _copyAction.Enable();

            Harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _copyAction?.Disable();
            _copyAction?.Dispose();
            Harmony.UnpatchSelf();
            _instance = null;
        }

        private void Update()
        {
            if (_activePrepareRequestId != 0 && Managers.GetManager<PlayersManager>()?.GetActivePlayerController() == null)
            {
                ResetPreparationState();
            }

            if (_copyAction == null || !_copyAction.WasPressedThisFrame())
            {
                return;
            }

            if (_activePrepareRequestId != 0)
            {
                // User pressed copy again while waiting for an async nearby-container prepare callback.
                // Cancel the stale pending operation and retarget immediately.
                Logger.LogInfo("Copy retarget requested during prepare; cancelling stale pending prepare.");
                ResetPreparationState();
            }

            if (Managers.GetManager<WindowsHandler>()?.GetHasUiOpen() == true)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            if (player == null)
            {
                return;
            }

            if (!TryGetTargetConstructible(player, Mathf.Max(1f, _maxCopyDistance.Value), out var constructibleGroup, out var sourceWorldObject))
            {
                DisplayCursorText("CopyBuilding: aim at a constructible building first.", 2f);
                return;
            }

            var builder = player.GetPlayerBuilder();
            if (builder == null)
            {
                return;
            }

            if (builder.GetIsGhostExisting())
            {
                builder.InputOnCancelAction();
            }

            _copiedContext = CopiedBuildingContext.From(sourceWorldObject, constructibleGroup, _copySettings.Value);

            int prepareRequestId = BeginPrepareRequest();
            if (TryPrepareSetNewGhostWithNearbyContainers(player, () =>
            {
                // Ignore stale callback after timeout/cancel/re-target.
                if (_activePrepareRequestId != prepareRequestId)
                {
                    return;
                }
                ResetPreparationState();
                TrySetGhostAndReport(builder, constructibleGroup);
            }))
            {
                return;
            }

            ResetPreparationState();
            TrySetGhostAndReport(builder, constructibleGroup);
        }

        private void TrySetGhostAndReport(PlayerBuilder builder, GroupConstructible constructibleGroup)
        {
            _applyingCopySelection = true;
            bool enteredBuildMode;
            try
            {
                enteredBuildMode = builder.SetNewGhost(constructibleGroup, null);
            }
            finally
            {
                _applyingCopySelection = false;
            }
            _copySessionActive = enteredBuildMode;
            if (enteredBuildMode)
            {
                DisplayCursorText("CopyBuilding: copy mode ready.", 1.25f);
            }
            else
            {
                DisplayCursorText("CopyBuilding: not enough resources.", 2f);
            }
        }

        private bool TryPrepareSetNewGhostWithNearbyContainers(PlayerMainController player, Action onReady)
        {
            if (!TryResolveNearbyPrepareApi())
            {
                return false;
            }

            try
            {
                _nearbyPrepareSetNewGhostApi?.Invoke(this, player.transform.position, onReady);
                return _nearbyPrepareSetNewGhostApi != null;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Nearby container prepare hook failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryResolveNearbyPrepareApi()
        {
            if (_checkedNearbyPrepareApi)
            {
                return _nearbyPrepareSetNewGhostApi != null;
            }

            _checkedNearbyPrepareApi = true;
            var pluginType = AccessTools.TypeByName("CheatCraftFromNearbyContainers.Plugin");
            if (pluginType == null)
            {
                return false;
            }

            var field = AccessTools.Field(pluginType, "apiPrepareSetNewGhost");
            if (field?.GetValue(null) is Action<MonoBehaviour, Vector3, Action> prepareAction)
            {
                _nearbyPrepareSetNewGhostApi = prepareAction;
                return true;
            }

            return false;
        }

        private static bool TryGetTargetConstructible(PlayerMainController player, float maxDistance, out GroupConstructible constructibleGroup, out WorldObject sourceWorldObject)
        {
            constructibleGroup = null;
            sourceWorldObject = null;

            var aimController = player.GetComponent<PlayerAimController>();
            if (aimController == null)
            {
                return false;
            }

            return TryResolveConstructibleFromRaycast(aimController, maxDistance, out constructibleGroup, out sourceWorldObject);
        }

        private static bool TryResolveConstructibleFromRaycast(PlayerAimController aimController, float maxDistance, out GroupConstructible constructibleGroup, out WorldObject sourceWorldObject)
        {
            constructibleGroup = null;
            sourceWorldObject = null;

            var ray = aimController.GetAimRay();

            int commonMask = ~LayerMask.GetMask(GameConfig.commonIgnoredLayers);
            if (UnityEngine.Physics.Raycast(ray, out var hit, maxDistance, commonMask) &&
                TryResolveConstructibleFromGameObject(hit.collider.gameObject, out constructibleGroup, out sourceWorldObject))
            {
                return true;
            }

            int waterMask = ~LayerMask.GetMask(GameConfig.commonIgnoredAndWater);
            if (UnityEngine.Physics.Raycast(ray, out hit, maxDistance, waterMask) &&
                TryResolveConstructibleFromGameObject(hit.collider.gameObject, out constructibleGroup, out sourceWorldObject))
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveConstructibleFromGameObject(GameObject gameObject, out GroupConstructible constructibleGroup, out WorldObject sourceWorldObject)
        {
            constructibleGroup = null;
            sourceWorldObject = null;

            if (gameObject == null)
            {
                return false;
            }

            // Prefer the nearest owner on the direct hit hierarchy first.
            // This helps choose window/door-specific constructibles instead of their parent compartment.
            if (TryResolveConstructibleFromTransformChain(gameObject.transform, out constructibleGroup, out sourceWorldObject))
            {
                return true;
            }

            // Only if direct chain doesn't resolve, try the game's aim proxy indirection.
            var aimProxy = gameObject.GetComponent<AimProxy>();
            if (aimProxy != null && aimProxy.GetAim() != null)
            {
                if (TryResolveConstructibleFromTransformChain(aimProxy.GetAim().transform, out constructibleGroup, out sourceWorldObject))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveConstructibleFromTransformChain(Transform start, out GroupConstructible constructibleGroup, out WorldObject sourceWorldObject)
        {
            constructibleGroup = null;
            sourceWorldObject = null;

            for (var transform = start; transform != null; transform = transform.parent)
            {
                // Panels (doors/windows/etc.) are often variants on a compartment world object.
                // Prefer the panel's own constructible group when available.
                var panel = transform.GetComponent<Panel>();
                if (panel != null)
                {
                    var panelGroup = panel.GetPanelGroupConstructible();
                    if (panelGroup != null)
                    {
                        constructibleGroup = panelGroup;
                        sourceWorldObject = panel.GetWorldObjectAssociated()?.GetWorldObject();
                        return true;
                    }
                }

                var woAssociated = transform.GetComponent<WorldObjectAssociated>();
                var worldObject = woAssociated?.GetWorldObject();
                if (worldObject?.GetGroup() is GroupConstructible byWorldObject)
                {
                    constructibleGroup = byWorldObject;
                    sourceWorldObject = worldObject;
                    return true;
                }

                var groupNetwork = transform.GetComponent<GroupNetworkBase>();
                if (groupNetwork?.GetGroup() is GroupConstructible byGroupNetwork)
                {
                    constructibleGroup = byGroupNetwork;
                    sourceWorldObject = groupNetwork.GetComponent<WorldObjectAssociated>()?.GetWorldObject();
                    return true;
                }
            }

            return false;
        }

        private static void DisplayCursorText(string message, float seconds)
        {
            Managers.GetManager<BaseHudHandler>()?.DisplayCursorText(message, seconds, string.Empty, string.Empty);
        }

        private void ResetPreparationState()
        {
            _activePrepareRequestId = 0;
        }

        private int BeginPrepareRequest()
        {
            _nextPrepareRequestId = (_nextPrepareRequestId == int.MaxValue) ? 1 : (_nextPrepareRequestId + 1);
            _activePrepareRequestId = _nextPrepareRequestId;
            return _activePrepareRequestId;
        }

        private void ResetCopySession()
        {
            _copySessionActive = false;
            ResetPreparationState();
        }

        private void ApplyCopiedSettingsIfNeeded(GameObject result)
        {
            if (!_copySettings.Value || !_copySessionActive || _copiedContext == null)
            {
                return;
            }

            if (result == null)
            {
                return;
            }

            var targetWo = result.GetComponentInChildren<WorldObjectAssociated>(true)?.GetWorldObject();
            if (targetWo == null || targetWo.GetGroup() == null)
            {
                return;
            }

            if (targetWo.GetGroup().stableHashCode != _copiedContext.GroupHash)
            {
                return;
            }

            _copiedContext.ApplyTo(result, targetWo);
        }

        private void HandleSetNewGhostCalled(Group groupConstructible)
        {
            if (_applyingCopySelection || !_copySessionActive || _copiedContext == null)
            {
                return;
            }

            if (groupConstructible is GroupConstructible targetGroup &&
                targetGroup.stableHashCode == _copiedContext.GroupHash)
            {
                return;
            }

            ResetCopySession();
        }

    }
}
