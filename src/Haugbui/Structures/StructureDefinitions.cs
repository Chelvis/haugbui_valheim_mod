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
        public VisualPart(string prefab, Vector3 offset, Vector3 euler, float scale = 1f, Color? emission = null, Color? albedo = null, string glowFrom = null, string onlyChild = null, bool openChest = false, PartTint tint = PartTint.None, bool itemAttach = false, bool aura = false, bool mist = false, bool onTop = false, string albedoFile = null, string emissionFile = null, bool emissionMask = false)
        {
            Prefab = prefab;
            Offset = offset;
            Euler = euler;
            Scale = scale;
            Emission = emission;
            Albedo = albedo;
            GlowFrom = glowFrom;
            OnlyChild = onlyChild;
            OpenChest = openChest;
            Tint = tint;
            ItemAttach = itemAttach;
            Aura = aura;
            Mist = mist;
            OnTop = onTop;
            AlbedoFile = albedoFile;
            EmissionFile = emissionFile;
            EmissionMask = emissionMask;
        }

        public string Prefab { get; }

        /// <summary>Relative to the top centre of the host's visible bounds (or the root when the host is hidden).</summary>
        public Vector3 Offset { get; }
        public Vector3 Euler { get; }
        public float Scale { get; }

        /// <summary>Replaces the hue of the part's glow, keeping its brightness; null keeps the original.</summary>
        public Color? Emission { get; }

        /// <summary>Tint applied on a copy of the material. Null keeps the original colour.</summary>
        public Color? Albedo { get; }

        /// <summary>PNG file name under textures/ next to the plugin. Replaces _MainTex on the material copy.</summary>
        public string AlbedoFile { get; }

        /// <summary>PNG file name under textures/ next to the plugin. Replaces _EmissionMap on the material copy.</summary>
        public string EmissionFile { get; }

        /// <summary>Drop the hue of the emission map so <see cref="Emission"/> alone chooses the glow colour.</summary>
        public bool EmissionMask { get; }

        /// <summary>Prefab whose light and one particle system are copied onto this part. Null adds neither.</summary>
        public string GlowFrom { get; }

        /// <summary>Copy only this child, including when that child is inactive.</summary>
        public string OnlyChild { get; }

        /// <summary>Use the open lid when the chest has one, and drop the closed lid.</summary>
        public bool OpenChest { get; }

        public PartTint Tint { get; }

        /// <summary>Pose the part on the host item stand the way the game attaches a real item.</summary>
        public bool ItemAttach { get; }

        /// <summary>A dark purple light, without a particle orb.</summary>
        public bool Aura { get; }

        /// <summary>Wraith smoke, parented to this part. Skipped when effects quality is Low.</summary>
        public bool Mist { get; }

        /// <summary>Sit this part on the top of the previous one. Offset is then only a nudge.</summary>
        public bool OnTop { get; }
    }

    internal enum PartTint
    {
        None,
        Silver,
        Lead,
        PurpleVeins,
        Clean,
        LightMarble,
        Aura,
        SilverBar
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

        /// <summary>File name under icons/ next to the plugin. Missing files keep <see cref="IconItem"/>.</summary>
        public string IconFile { get; set; }
        public bool HideHost { get; set; }

        /// <summary>Uniform scale of the whole placed piece, collider included. 1 leaves the host size.</summary>
        public float Scale { get; set; } = 1f;

        /// <summary>Replace the host colliders with a box around the visible meshes.</summary>
        public bool FitCollider { get; set; }

        public VisualPart[] Parts { get; set; } = new VisualPart[0];

        /// <summary>PNG file name under textures/ next to the plugin. Replaces the host's wood colour. Null leaves it.</summary>
        public string HostAlbedoFile { get; set; }

        /// <summary>PNG file name under textures/ next to the plugin. Replaces the host's relief. Null leaves it.</summary>
        public string HostNormalFile { get; set; }

        /// <summary>Rich text shown on a floating sign icon. Null adds none.</summary>
        public string FloatingText { get; set; }

        public string Host { get; set; }
    }

    /// <summary>
    /// Kitbashed from vanilla pieces and item meshes. Offsets are judged from the self-test pictures.
    /// </summary>
    internal static class StructureDefinitions
    {
        public static readonly Color HaugbuiPurple = new Color(0.62f, 0.2f, 1f);
        public static readonly Color HaugbuiLead = new Color(0.40f, 0.41f, 0.44f);

        private static readonly string[] TableHosts = { "piece_table", "piece_table_oak", "piece_bench01", "piece_chest_wood" };

        /// <summary>Flip off to restore the black-marble column if the brazier test fails.</summary>
        private const bool BraceletsUseBrazier = true;

        private static readonly VisualPart[] BraceletsOnColumn =
        {
            new VisualPart("piece_blackmarble_column_2", Vector3.zero, Vector3.zero, 0.5f, tint: PartTint.LightMarble),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(0f, 0.04f, 0f), new Vector3(90, 15, 0), onTop: true, tint: PartTint.Silver),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(0.1f, 0.08f, 0.04f), new Vector3(88, 100, 8), 0.95f, onTop: true, tint: PartTint.Silver),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(-0.08f, 0.06f, -0.03f), new Vector3(92, 190, -6), 0.95f, onTop: true, tint: PartTint.Silver),
        };

        private static readonly VisualPart[] BraceletsOnBrazier =
        {
            new VisualPart("piece_brazierfloor01", Vector3.zero, Vector3.zero),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(0.171f, -0.3f, 0.051f), new Vector3(8f, 25f, 0f), onTop: true, tint: PartTint.SilverBar),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(-0.12f, -0.29f, 0.137f), new Vector3(5f, 150f, 6f), onTop: true, tint: PartTint.SilverBar),
            new VisualPart("TrinketBlackDamageHealth", new Vector3(0.017f, -0.3f, -0.17f), new Vector3(10f, 255f, -4f), onTop: true, tint: PartTint.SilverBar),
        };

        public static readonly IReadOnlyList<StructureDefinition> All = new[]
        {
            new StructureDefinition
            {
                Kind = StructureKind.Tomb, PieceId = PieceIds.Tomb, HostCandidates = new[] { "piece_chest_wood" },
                NameToken = "$haugbui_piece_tomb", DescriptionToken = "$haugbui_piece_tomb_desc",
                IconItem = "BoneFragments", IconFile = "tomb.png", HideHost = true, FitCollider = true, Scale = 1.2f,
                Parts = new[]
                {
                    new VisualPart("piece_chest_grausten", Vector3.zero, new Vector3(0, 180, 0), tint: PartTint.Clean, emissionFile: "graustenchest_veins.png"),
                },
            },
            Upgrade(StructureKind.Offering, PieceIds.Offering, "offering", "CookedMeat", "offering.png", false, 0.8f, false, null,
                new VisualPart("FeastMeadows", new Vector3(0f, 0.02f, 0f), Vector3.zero, onlyChild: "full"),
                new VisualPart("Honey", new Vector3(-0.55f, 0.04f, -0.12f), Vector3.zero),
                new VisualPart("Tankard", new Vector3(-1.0f, 0.07f, -0.42f), new Vector3(0, 20, 0))),
            Upgrade(StructureKind.PersonalTreasure, PieceIds.PersonalTreasure, "treasure", "Coins", "treasure.png", true, 1f, true, null,
                new VisualPart("piece_chest_treasure", Vector3.zero, Vector3.zero, tint: PartTint.Lead)),
            Upgrade(StructureKind.SilverBracelets, PieceIds.SilverBracelets, "bracelets", "SilverNecklace", "bracelets.png", true, 1f, true, null,
                BraceletsUseBrazier ? BraceletsOnBrazier : BraceletsOnColumn),
            Upgrade(StructureKind.Longsword, PieceIds.Longsword, "longsword", "SwordBlackmetal", "longsword.png", false, 1f, false, "itemstand",
                new VisualPart("THSwordSlayerNature", Vector3.zero, Vector3.zero, itemAttach: true)),
            Upgrade(StructureKind.Altar, PieceIds.Altar, "altar", "Eitr", "altar.png", true, 1f, true, null,
                new VisualPart("piece_magetable_ext", Vector3.zero, Vector3.zero, emission: new Color(0.2f, 0.45f, 1f), mist: true, albedoFile: "Runetable_d.png", emissionMask: true)),
            Chest(StructureKind.CollectionChest, PieceIds.CollectionChest, "collection", "BoneFragments", "collection.png", "collection.png", null, "<color=#5EFFC0>\u2B06\uFE0F</color>"),
            Chest(StructureKind.DeliveryChest, PieceIds.DeliveryChest, "delivery", "GreydwarfEye", "delivery.png", "delivery.png", null, "<color=#6ED4FF>\u2B07\uFE0F</color>"),
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

        private static StructureDefinition Chest(StructureKind kind, string pieceId, string token, string icon, string iconFile, string hostAlbedoFile = null, string hostNormalFile = null, string floatingText = null, params VisualPart[] parts) =>
            new StructureDefinition
            {
                Kind = kind, PieceId = pieceId, HostCandidates = new[] { "piece_chest_wood" },
                NameToken = "$haugbui_piece_" + token, DescriptionToken = "$haugbui_piece_" + token + "_desc",
                IconItem = icon, IconFile = iconFile, HideHost = false, HostAlbedoFile = hostAlbedoFile, HostNormalFile = hostNormalFile,
                FloatingText = floatingText, Parts = parts,
            };

        private static StructureDefinition Upgrade(StructureKind kind, string pieceId, string token, string icon, string iconFile, bool hideHost, float scale, bool fitCollider, string host, params VisualPart[] parts) =>
            new StructureDefinition
            {
                Kind = kind, PieceId = pieceId,
                HostCandidates = host == "itemstand" ? new[] { "itemstand", "itemstandh", "piece_itemstand" }
                    : host != null ? new[] { host } : TableHosts,
                NameToken = "$haugbui_piece_" + token, DescriptionToken = "$haugbui_piece_" + token + "_desc",
                IconItem = icon, IconFile = iconFile, HideHost = hideHost, Scale = scale, FitCollider = fitCollider, Parts = parts,
            };

        public static StructureDefinition For(StructureKind kind) => All.FirstOrDefault(d => d.Kind == kind);

        public static StructureDefinition ForPieceId(string pieceId) => All.FirstOrDefault(d => d.PieceId == pieceId);
    }
}
