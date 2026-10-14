using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Haugbui.Core.Recipes;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>
    /// Registers each structure as a Jötunn piece cloned from its vanilla host. On placement the ZDO is switched to the
    /// host prefab (HaugbuiStructure.OnPlaced); on load, hosts carrying haugbui_kind get the behaviour and look back.
    /// </summary>
    internal static class HostPrefabSystem
    {
        public const string Category = "Haugbui";

        private static readonly Dictionary<StructureKind, GameObject> Placeholders = new Dictionary<StructureKind, GameObject>();

        /// <summary>Self-test switch: hosts load as plain vanilla objects, as if Haugbui were not installed.</summary>
        internal static bool SuppressAugmentation { get; set; }

        internal static bool IsRegistered(StructureKind kind) => Placeholders.ContainsKey(kind);

        public static void RegisterPieces()
        {
            foreach (StructureDefinition definition in StructureDefinitions.All)
            {
                try
                {
                    Register(definition);
                }
                catch (Exception e)
                {
                    Log.Error($"Could not register {definition.PieceId}: {e}");
                }
            }
        }

        private static void Register(StructureDefinition definition)
        {
            if (!StartupValidation.IsPieceEnabled(definition.PieceId))
            {
                Log.Warning($"{definition.PieceId} disabled by recipe validation.");
                return;
            }

            definition.Host = definition.HostCandidates.FirstOrDefault(h => PrefabManager.Cache.GetPrefab<GameObject>(h) != null);
            if (definition.Host == null)
            {
                Log.Error($"{definition.PieceId} disabled: none of its host prefabs exist ({string.Join(", ", definition.HostCandidates)}).");
                return;
            }

            PieceRecipe recipe = StartupValidation.Results[definition.PieceId].Recipe;
            var config = new PieceConfig
            {
                Name = definition.NameToken,
                Description = definition.DescriptionToken,
                PieceTable = PieceTables.Hammer,
                Category = Category,
                CraftingStation = recipe.Station,
                Icon = ItemIcon(definition.IconItem),
                Requirements = recipe.Ingredients
                    .Select(i => new RequirementConfig { Item = i.Item, Amount = i.Amount, Recover = true })
                    .ToArray(),
            };

            var piece = new CustomPiece(definition.PieceId, definition.Host, config);
            if (piece.PiecePrefab == null)
            {
                Log.Error($"{definition.PieceId}: cloning {definition.Host} failed.");
                return;
            }

            GameObject prefab = piece.PiecePrefab;
            prefab.AddComponent<HaugbuiStructure>().m_kind = (int)definition.Kind;
            ConfigureContainer(prefab.GetComponent<Container>(), definition.Kind);
            StructureVisuals.Apply(prefab, definition);
            PieceManager.Instance.AddPiece(piece);
            Placeholders[definition.Kind] = prefab;
            Log.Debug($"Registered {definition.PieceId} on host {definition.Host}");
        }

        private static void ConfigureContainer(Container container, StructureKind kind)
        {
            string name = StructureDefinitions.ContainerName(kind);
            if (container == null || name == null) return;
            container.m_name = name;
            if (container.m_inventory != null) container.m_inventory.m_name = name;
            if (!StructureDefinitions.IsChest(kind)) return;
            // The host already has 5x2; set it anyway so a changed host prefab cannot change HB-070/080.
            container.m_width = StructureDefinitions.ChestWidth;
            container.m_height = StructureDefinitions.ChestHeight;
        }

        private static Sprite ItemIcon(string item)
        {
            ItemDrop drop = PrefabManager.Cache.GetPrefab<ItemDrop>(item);
            Sprite[] icons = drop != null ? drop.m_itemData.m_shared.m_icons : null;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        /// <summary>Gives a loaded vanilla host the Haugbui name, refund, behaviour and look.</summary>
        internal static void Augment(Piece piece)
        {
            ZNetView view = piece.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || SuppressAugmentation || piece.GetComponent<HaugbuiStructure>() != null) return;

            var kind = (StructureKind)zdo.GetInt(ZdoKeys.Kind);
            StructureDefinition definition = StructureDefinitions.For(kind);
            if (definition == null) return;

            if (Placeholders.TryGetValue(kind, out GameObject placeholder))
            {
                Piece template = placeholder.GetComponent<Piece>();
                piece.m_name = template.m_name;
                piece.m_description = template.m_description;
                piece.m_resources = template.m_resources;
            }
            else
            {
                Log.ErrorRateLimited("augment:" + kind, $"{definition.PieceId} is not registered; the host stays vanilla.");
                return;
            }

            ConfigureContainer(piece.GetComponent<Container>(), kind);

            StructureVisuals.Apply(piece.gameObject, definition);
            piece.gameObject.AddComponent<HaugbuiStructure>().m_kind = (int)kind;
        }
    }

    [HarmonyPatch]
    internal static class HostPrefabPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Piece), nameof(Piece.Awake))]
        private static void AugmentHost(Piece __instance)
        {
            try
            {
                HostPrefabSystem.Augment(__instance);
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("augment", $"Haugbui host augmentation failed: {e}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static void HaugbuiHover(Container __instance, ref string __result)
        {
            HaugbuiStructure structure = __instance.GetComponent<HaugbuiStructure>();
            if (structure == null || string.IsNullOrEmpty(__result)) return;
            if (structure.Kind == StructureKind.Tomb) TombHover(__instance, structure, ref __result);
            else if (structure.Kind == StructureKind.DeliveryChest) DeliveryHover(__instance, ref __result);
        }

        private static void TombHover(Container container, HaugbuiStructure structure, ref string result)
        {
            int firstBreak = result.IndexOf('\n');
            string actions = firstBreak >= 0 ? result.Substring(firstBreak) : string.Empty;
            var controller = container.GetComponent<Creature.TombController>();
            string status = controller != null ? "\n" + controller.StatusLine() : "";
            result = Localization.instance.Localize($"$haugbui_piece_tomb\n{structure.LevelLine()}{status}") + actions +
                     Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $haugbui_panel_open");
        }

        private static void DeliveryHover(Container container, ref string result)
        {
            DeliveryChest chest = container.GetComponent<DeliveryChest>();
            if (chest == null) return;
            int open = 0, remaining = 0;
            foreach (Core.Persistence.DeliveryRequest line in chest.Ledger.OpenLines)
            {
                open++;
                remaining += line.Remaining;
            }
            result += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $haugbui_delivery_edit") +
                      (open > 0 ? "\n" + Localization.instance.Localize("$haugbui_delivery_hover_open", open.ToString(), remaining.ToString()) : "");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static bool PanelOnAltInteract(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (!alt || hold) return true;
            var controller = __instance.GetComponent<Creature.TombController>();
            if (controller != null)
            {
                UI.TombPanel.Open(controller);
                __result = true;
                return false;
            }
            DeliveryChest chest = __instance.GetComponent<DeliveryChest>();
            if (chest == null) return true;
            // Same access rule as opening the chest; otherwise vanilla refuses with its own message.
            if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return true;
            if (!__instance.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID())) return true;
            UI.DeliveryPanel.Open(chest);
            __result = true;
            return false;
        }
    }
}
