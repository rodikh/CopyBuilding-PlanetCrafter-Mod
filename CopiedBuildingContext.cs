using System.Collections.Generic;
using System.Linq;
using SpaceCraft;
using UnityEngine;

namespace CopyBuildingMod
{
    public sealed partial class Plugin
    {
        private sealed class CopiedBuildingContext
        {
            public int GroupHash { get; private set; }
            public int Setting { get; private set; }
            public string Text { get; private set; }
            public Color Color { get; private set; }
            public int LinkedPlanetHash { get; private set; }
            public List<Group> LinkedGroups { get; private set; }
            public List<Group> LogisticDemandGroups { get; private set; }
            public List<Group> LogisticSupplyGroups { get; private set; }
            public int LogisticPriority { get; private set; }
            public bool HasLogisticData { get; private set; }

            public static CopiedBuildingContext From(WorldObject sourceWorldObject, GroupConstructible group, bool includeSettings)
            {
                var context = new CopiedBuildingContext
                {
                    GroupHash = group.stableHashCode,
                    Setting = 0,
                    Text = string.Empty,
                    Color = default,
                    LinkedPlanetHash = 0,
                    LinkedGroups = null,
                    LogisticDemandGroups = null,
                    LogisticSupplyGroups = null,
                    LogisticPriority = 0,
                    HasLogisticData = false
                };

                if (!includeSettings || sourceWorldObject == null)
                {
                    return context;
                }

                context.Setting = sourceWorldObject.GetSetting();
                context.Text = sourceWorldObject.GetText() ?? string.Empty;
                context.Color = sourceWorldObject.GetColor();
                context.LinkedPlanetHash = sourceWorldObject.GetPlanetLinkedHash();
                var sourceGroups = sourceWorldObject.GetLinkedGroups();
                if (sourceGroups != null && sourceGroups.Count > 0)
                {
                    context.LinkedGroups = new List<Group>(sourceGroups);
                }

                int sourceInventoryId = sourceWorldObject.GetLinkedInventoryId();
                if (sourceInventoryId > 0 && InventoriesHandler.Instance != null)
                {
                    var sourceInventory = InventoriesHandler.Instance.GetInventoryById(sourceInventoryId);
                    var sourceLogistic = sourceInventory?.GetLogisticEntity();
                    if (sourceLogistic != null)
                    {
                        context.LogisticDemandGroups = sourceLogistic.GetDemandGroups()?.ToList();
                        context.LogisticSupplyGroups = sourceLogistic.GetSupplyGroups()?.ToList();
                        context.LogisticPriority = sourceLogistic.GetPriority();
                        context.HasLogisticData = true;
                    }
                }

                return context;
            }

            public void ApplyTo(GameObject targetGo, WorldObject targetWo)
            {
                if (targetGo == null || targetWo == null)
                {
                    return;
                }

                targetWo.SetSetting(Setting);
                targetWo.SetText(Text);
                targetWo.SetColor(Color);
                targetWo.SetPlanetLinkedHash(LinkedPlanetHash);
                targetWo.SetLinkedGroups(LinkedGroups);

                var settingProxy = targetGo.GetComponentInChildren<SettingProxy>(true);
                if (settingProxy != null)
                {
                    settingProxy.SetSetting(Setting);
                }

                var textProxy = targetGo.GetComponentInChildren<TextProxy>(true);
                if (textProxy != null)
                {
                    textProxy.SetText(Text);
                }

                var colorProxy = targetGo.GetComponentInChildren<ColorProxy>(true);
                if (colorProxy != null)
                {
                    colorProxy.SetColor(Color);
                }

                var linkedPlanetProxy = targetGo.GetComponentInChildren<LinkedPlanetProxy>(true);
                if (linkedPlanetProxy != null)
                {
                    linkedPlanetProxy.SetLinkedPlanet(LinkedPlanetHash);
                }

                var linkedGroupsProxy = targetGo.GetComponentInChildren<LinkedGroupsProxy>(true);
                if (linkedGroupsProxy != null)
                {
                    linkedGroupsProxy.SetLinkedGroups(LinkedGroups);
                }

                if (HasLogisticData && InventoriesHandler.Instance != null)
                {
                    int targetInventoryId = targetWo.GetLinkedInventoryId();
                    if (targetInventoryId > 0)
                    {
                        var targetInventory = InventoriesHandler.Instance.GetInventoryById(targetInventoryId);
                        var targetLogistic = targetInventory?.GetLogisticEntity();
                        if (targetLogistic != null)
                        {
                            targetLogistic.SetDemandGroups(LogisticDemandGroups != null
                                ? new HashSet<Group>(LogisticDemandGroups)
                                : new HashSet<Group>());
                            targetLogistic.SetSupplyGroups(LogisticSupplyGroups != null
                                ? new HashSet<Group>(LogisticSupplyGroups)
                                : new HashSet<Group>());
                            targetLogistic.SetPriority(LogisticPriority);
                            InventoriesHandler.Instance.UpdateLogisticEntity(targetInventory);
                        }
                    }
                }
            }
        }
    }
}
