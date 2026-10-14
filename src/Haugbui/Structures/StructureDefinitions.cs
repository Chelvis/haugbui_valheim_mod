using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Recipes;
using Haugbui.Core.Structures;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>A vanilla prefab whose meshes are copied into a structure's look.</summary>
    internal readonly struct VisualPart
    {
        public VisualPart(string prefab, Vector3 offset, Vector3 euler, float scale = 1f, Color? emission = null)
        {
            Prefab = prefab;
            Offset = offset;
            Euler = euler;
            Scale = scale;
            Emission = emission;
        }

        public string Prefab { get; }

        /// <summary>Relative to the top centre of the host's visible bounds (or the root when the host is hidden).</summary>
        public Vector3 Offset { get; }
        public Vector3 Euler { get; }
        public float Scale { get; }

        /// <summary>Replaces the hue of the part's glow, keeping its brightness; null keeps the original.</summary>
        public Color? Emission { get; }
    }

    internal sealed class StructureDefinition
    {
        public StructureKind Kind { get; set; }
        public string PieceId { get; set; }

        /// <summary>Candidate vanilla prefabs the structure is saved as; the first one that exists is used.</summary>
        public string[] HostCandidates { get; set; }

        public string NameToken { get; set; }
        public string DescriptionToken { get; set; }
        public string IconItem { get; set; }
        public bool HideHost { get; set; }
        public VisualPart[] Parts { get; set; } = new VisualPart[0];

        public string Host { get; set; }
    }

    /// <summary>
    /// First-pass looks kitbashed from vanilla item and piece meshes; offsets are tuned in game (DEVELOPMENT_GUIDE §4.5).
    /// </summary>
    internal static class StructureDefinitions
    {
        public static readonly Color HaugbuiPurple = new Color(0.62f, 0.2f, 1f);

        private static readonly string[] TableHosts = { "piece_table", "piece_table_oak", "piece_bench01", "piece_chest_wood" };

        public static readonly IReadOnlyList<StructureDefinition> All = new[]
        {
            new StructureDefinition
            {
                Kind = StructureKind.Tomb, PieceId = PieceIds.Tomb, HostCandidates = new[] { "piece_chest_wood" },
                NameToken = "$haugbui_piece_tomb", DescriptionToken = "$haugbui_piece_tomb_desc", IconItem = "BoneFragments",
                HideHost = true,
                Parts = new[]
                {
                    new VisualPart("Player_tombstone", Vector3.zero, Vector3.zero, emission: HaugbuiPurple),
                    new VisualPart("TrophySkeleton", new Vector3(0.45f, 0.05f, 0.35f), new Vector3(0, 200, 0)),
                    new VisualPart("BoneFragments", new Vector3(-0.45f, 0.05f, 0.3f), Vector3.zero),
                },
            },
            Upgrade(StructureKind.Offering, PieceIds.Offering, "offering", "CookedMeat",
                new VisualPart("CookedMeat", new Vector3(-0.15f, 0, 0), Vector3.zero),
                new VisualPart("Honey", new Vector3(0.2f, 0, 0.1f), Vector3.zero),
                new VisualPart("Thistle", new Vector3(0.05f, 0, -0.2f), Vector3.zero)),
            Upgrade(StructureKind.PersonalTreasure, PieceIds.PersonalTreasure, "treasure", "Coins",
                new VisualPart("Coins", new Vector3(-0.2f, 0, 0), Vector3.zero),
                new VisualPart("Ruby", new Vector3(0.1f, 0, 0.1f), Vector3.zero),
                new VisualPart("AmberPearl", new Vector3(0.2f, 0, -0.1f), Vector3.zero),
                new VisualPart("Amber", new Vector3(0, 0, -0.2f), Vector3.zero)),
            Upgrade(StructureKind.SilverBracelets, PieceIds.SilverBracelets, "bracelets", "SilverNecklace",
                new VisualPart("SilverNecklace", new Vector3(-0.1f, 0, 0), Vector3.zero),
                new VisualPart("Silver", new Vector3(0.2f, 0, 0.05f), Vector3.zero)),
            Upgrade(StructureKind.Longsword, PieceIds.Longsword, "longsword", "SwordBlackmetal",
                new VisualPart("SwordBlackmetal", new Vector3(0, 0.05f, 0), new Vector3(0, 0, 90), 1.3f)),
            Upgrade(StructureKind.Altar, PieceIds.Altar, "altar", "Eitr",
                new VisualPart("BlackMarble", new Vector3(-0.15f, 0, 0), Vector3.zero),
                new VisualPart("Eitr", new Vector3(0.15f, 0, 0), Vector3.zero),
                new VisualPart("TrophySkeleton", new Vector3(0, 0, -0.2f), new Vector3(0, 180, 0))),
            Chest(StructureKind.CollectionChest, PieceIds.CollectionChest, "collection", "BoneFragments",
                new VisualPart("BoneFragments", new Vector3(-0.35f, 0, 0), Vector3.zero),
                new VisualPart("BoneFragments", new Vector3(0.35f, 0, 0), new Vector3(0, 90, 0)),
                new VisualPart("Resin", new Vector3(0, 0, 0.1f), Vector3.zero, emission: HaugbuiPurple)),
            Chest(StructureKind.DeliveryChest, PieceIds.DeliveryChest, "delivery", "GreydwarfEye",
                new VisualPart("TrophySkeleton", new Vector3(0, 0, 0), new Vector3(0, 180, 0), 0.8f),
                new VisualPart("GreydwarfEye", new Vector3(-0.35f, 0, 0.1f), Vector3.zero, emission: HaugbuiPurple),
                new VisualPart("GreydwarfEye", new Vector3(0.35f, 0, 0.1f), Vector3.zero, emission: HaugbuiPurple)),
        };

        /// <summary>Haugbui chests keep the 10 slots of the wood chest (HB-070, HB-080).</summary>
        public const int ChestWidth = 5;
        public const int ChestHeight = 2;

        public static bool IsChest(StructureKind kind) => kind == StructureKind.CollectionChest || kind == StructureKind.DeliveryChest;

        /// <summary>Name shown on the chest UI of a Haugbui container; null keeps the host's.</summary>
        public static string ContainerName(StructureKind kind)
        {
            switch (kind)
            {
                case StructureKind.Tomb: return "$haugbui_tomb_storage";
                case StructureKind.CollectionChest: return "$haugbui_piece_collection";
                case StructureKind.DeliveryChest: return "$haugbui_piece_delivery";
                default: return null;
            }
        }

        private static StructureDefinition Chest(StructureKind kind, string pieceId, string token, string icon, params VisualPart[] parts) =>
            new StructureDefinition
            {
                Kind = kind, PieceId = pieceId, HostCandidates = new[] { "piece_chest_wood" },
                NameToken = "$haugbui_piece_" + token, DescriptionToken = "$haugbui_piece_" + token + "_desc",
                IconItem = icon, HideHost = false, Parts = parts,
            };

        private static StructureDefinition Upgrade(StructureKind kind, string pieceId, string token, string icon, params VisualPart[] parts) =>
            new StructureDefinition
            {
                Kind = kind, PieceId = pieceId, HostCandidates = TableHosts,
                NameToken = "$haugbui_piece_" + token, DescriptionToken = "$haugbui_piece_" + token + "_desc",
                IconItem = icon, HideHost = false, Parts = parts,
            };

        public static StructureDefinition For(StructureKind kind) => All.FirstOrDefault(d => d.Kind == kind);

        public static StructureDefinition ForPieceId(string pieceId) => All.FirstOrDefault(d => d.PieceId == pieceId);
    }
}
