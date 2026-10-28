using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Haugbui.Core.Creature;
using Haugbui.Core.Progression;
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
                Icon = StructureIcons.Load(definition),
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
            StructureVisuals.Present(prefab, definition);
            if (definition.Kind == StructureKind.Longsword) WallOnly(prefab.GetComponent<Piece>());
            MarkUpgrade(prefab.GetComponent<Piece>(), definition.Kind);
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

        /// <summary>The hammer draws the small up arrow when <c>Piece.m_isUpgrade</c> is set.</summary>
        private static void MarkUpgrade(Piece piece, StructureKind kind)
        {
            if (piece == null || kind == StructureKind.Tomb || StructureDefinitions.IsChest(kind)) return;
            piece.m_isUpgrade = true;
        }

        /// <summary>Same rule as the forge tool rack: a floor normal is refused (<c>Piece.m_notOnFloor</c>).</summary>
        private static void WallOnly(Piece piece)
        {
            if (piece == null) return;
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_allowAltGroundPlacement = false;
            piece.m_clipGround = false;
            piece.m_notOnFloor = true;
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
            StructureVisuals.Present(piece.gameObject, definition);
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
            if (string.IsNullOrEmpty(__result)) return;
            HaugbuiStructure structure = __instance.GetComponent<HaugbuiStructure>();
            if (structure == null)
            {
                if (__instance.GetComponent<Piece>() != null) __result += FilterHint(__instance);
                return;
            }
            if (structure.Kind == StructureKind.Tomb) TombHover(__instance, structure, ref __result);
            else if (structure.Kind == StructureKind.DeliveryChest) DeliveryHover(__instance, ref __result);
            else if (structure.Kind == StructureKind.CollectionChest)
                __result += Localization.instance.Localize("\n$haugbui_hover_collection");
        }

        private static string FilterHint(Container container)
        {
            ZNetView view = container.m_nview;
            List<string> filter = ChestFilterStore.Read(view != null && view.IsValid() ? view.GetZDO() : null);
            string hint = "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $haugbui_filter_open";
            if (filter.Count == 0) return Localization.instance.Localize(hint);
            string names = string.Join(", ", filter.Select(FilterName));
            return Localization.instance.Localize(hint + "\n$haugbui_filter_list", names);
        }

        private static string FilterName(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            string token = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : prefab;
            return Localization.instance.Localize(token);
        }

        private static void TombHover(Container container, HaugbuiStructure structure, ref string result)
        {
            int firstBreak = result.IndexOf('\n');
            string actions = firstBreak >= 0 ? result.Substring(firstBreak) : string.Empty;
            var controller = container.GetComponent<Creature.TombController>();
            string name = controller != null ? controller.ServantName : Localization.instance.Localize("$haugbui_piece_tomb");
            float health = 0f;
            float max = 0f;
            string state = "";
            string cooldown = Localization.instance.Localize("$haugbui_hover_ready");
            if (controller != null)
            {
                HaugbuiAttributes attributes = controller.Attributes;
                max = attributes.MaxHealth;
                health = controller.Creature != null ? controller.Creature.Character.GetHealth() : controller.EffectiveHealth(max);
                state = controller.StatusLine();
                if (controller.WaitRemaining > 0) cooldown = PresencePolicy.FormatCountdown(controller.WaitRemaining);
            }
            string tribute = controller != null && controller.State != null && Creature.TombController.TributeEnabled
                ? "\n" + Localization.instance.Localize("$haugbui_hover_tribute", controller.State.Balance.ToString(), controller.ShiftCost.ToString())
                : "";
            // The servant name is free text, so it is not passed through the localizer as a token.
            string body = string.Join("\n", new[]
            {
                structure.LevelLine(),
                Localization.instance.Localize("$haugbui_panel_state") + " " + (string.IsNullOrEmpty(state) ? "" : Localization.instance.Localize(state)),
                Localization.instance.Localize("$haugbui_panel_health") + $" {Mathf.CeilToInt(health)}/{max:0}",
                Localization.instance.Localize("$haugbui_panel_cooldown", cooldown),
            });
            result = name + "\n" + body + tribute + actions +
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
            result += Localization.instance.Localize("\n$haugbui_hover_delivery\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $haugbui_delivery_edit") +
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
            if (chest == null)
            {
                if (__instance.GetComponent<HaugbuiStructure>() != null || __instance.GetComponent<Piece>() == null) return true;
                if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return true;
                if (!__instance.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID())) return true;
                UI.ChestFilterPanel.Open(__instance);
                __result = true;
                return false;
            }
            // Same access rule as opening the chest; otherwise vanilla refuses with its own message.
            if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return true;
            if (!__instance.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID())) return true;
            UI.DeliveryPanel.Open(chest);
            __result = true;
            return false;
        }

        /// <summary>
        /// The inventory screen always shows the crafting list. Tomb storage and the two chests are not a workbench,
        /// so that list is hidden while they are open and restored for a vanilla chest or when the inventory closes.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static void HideCraftMenu(Container container)
        {
            SetCraftMenuVisible(!IsHaugbuiStorage(container));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
        private static void RestoreCraftMenu()
        {
            SetCraftMenuVisible(true);
        }

        private static void SetCraftMenuVisible(bool visible)
        {
            if (InventoryGui.instance != null && InventoryGui.instance.m_crafting != null)
                InventoryGui.instance.m_crafting.gameObject.SetActive(visible);
        }

        private static bool IsHaugbuiStorage(Container container)
        {
            HaugbuiStructure structure = container != null ? container.GetComponent<HaugbuiStructure>() : null;
            return structure != null && (structure.Kind == StructureKind.Tomb || StructureDefinitions.IsChest(structure.Kind));
        }
    }
}
