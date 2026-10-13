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
            Container container = prefab.GetComponent<Container>();
            if (container != null && definition.Kind == StructureKind.Tomb) container.m_name = "$haugbui_tomb_storage";
            StructureVisuals.Apply(prefab, definition);
            PieceManager.Instance.AddPiece(piece);
            Placeholders[definition.Kind] = prefab;
            Log.Debug($"Registered {definition.PieceId} on host {definition.Host}");
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
            if (zdo == null || piece.GetComponent<HaugbuiStructure>() != null) return;

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

            Container container = piece.GetComponent<Container>();
            if (container != null && kind == StructureKind.Tomb)
            {
                container.m_name = "$haugbui_tomb_storage";
                if (container.m_inventory != null) container.m_inventory.m_name = container.m_name;
            }

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
        private static void TombHover(Container __instance, ref string __result)
        {
            HaugbuiStructure structure = __instance.GetComponent<HaugbuiStructure>();
            if (structure == null || structure.Kind != StructureKind.Tomb || string.IsNullOrEmpty(__result)) return;

            int firstBreak = __result.IndexOf('\n');
            string actions = firstBreak >= 0 ? __result.Substring(firstBreak) : string.Empty;
            __result = Localization.instance.Localize($"$haugbui_piece_tomb\n{structure.LevelLine()}") + actions;
        }
    }
}
