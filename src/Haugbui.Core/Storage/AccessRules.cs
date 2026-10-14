using System.Collections.Generic;
using Haugbui.Core.Structures;

namespace Haugbui.Core.Storage
{
    /// <summary>Mirror of Container.PrivacySetting (Valheim 1.0.17); the values are the game's.</summary>
    public enum ContainerPrivacy
    {
        Private = 0,
        Group = 1,
        Public = 2
    }

    public enum ContainerUse
    {
        /// <summary>Receives sorted items (HB-062).</summary>
        StorageDestination,

        /// <summary>Gives items for deliveries and production (HB-064, HB-084, HB-113).</summary>
        StorageSource,

        /// <summary>Receives the items of its own requests (HB-084): Delivery Chests only.</summary>
        DeliveryTarget
    }

    /// <summary>Facts about one container, read from the game.</summary>
    public struct ContainerFacts
    {
        public bool IsPlayerTombstone;
        public bool IsVehicle;
        public bool IsObliterator;
        public bool AutoDestroyEmpty;
        public bool PlacedByPlayer;
        public long CreatorId;
        public ContainerPrivacy Privacy;
        public StructureKind HaugbuiKind;
    }

    public enum ContainerVerdict
    {
        Allowed,
        PlayerTombstone,
        Vehicle,
        Obliterator,
        AutoDestroyEmpty,
        NotPlayerBuilt,
        HaugbuiRole,
        Private
    }

    /// <summary>HB-066, HB-150: which containers a Haugbui acting for <c>ownerId</c> may use, before wards and loading.</summary>
    public static class ContainerRules
    {
        public static ContainerVerdict Check(ContainerFacts facts, long ownerId, ContainerUse use)
        {
            if (facts.IsPlayerTombstone) return ContainerVerdict.PlayerTombstone;
            // Before Vehicle: the obliterator's container also uses a root object override.
            if (facts.IsObliterator) return ContainerVerdict.Obliterator;
            if (facts.IsVehicle) return ContainerVerdict.Vehicle;
            if (facts.AutoDestroyEmpty) return ContainerVerdict.AutoDestroyEmpty;
            if (!IsAllowedRole(facts.HaugbuiKind, use)) return ContainerVerdict.HaugbuiRole;
            if (!facts.PlacedByPlayer) return ContainerVerdict.NotPlayerBuilt;
            switch (facts.Privacy)
            {
                case ContainerPrivacy.Public:
                    return ContainerVerdict.Allowed;
                case ContainerPrivacy.Private:
                    return facts.CreatorId == ownerId ? ContainerVerdict.Allowed : ContainerVerdict.Private;
                default:
                    // Vanilla grants nobody access to Group chests (Container.CheckAccess).
                    return ContainerVerdict.Private;
            }
        }

        /// <summary>
        /// Tomb storage is only ever used by its own tomb (not through this rule); Collection Chests are sources, never
        /// destinations; Delivery Chests only receive their own requests and never give items.
        /// </summary>
        private static bool IsAllowedRole(StructureKind kind, ContainerUse use)
        {
            switch (kind)
            {
                case StructureKind.None:
                    return use != ContainerUse.DeliveryTarget;
                case StructureKind.CollectionChest:
                    return use == ContainerUse.StorageSource;
                case StructureKind.DeliveryChest:
                    return use == ContainerUse.DeliveryTarget;
                default:
                    return false;
            }
        }
    }

    /// <summary>One ward (PrivateArea) as seen from a point.</summary>
    public struct WardFacts
    {
        public bool Enabled;
        public bool Covers;
        public long CreatorId;
        public bool OwnerPermitted;
    }

    /// <summary>
    /// HB-150/151: same outcome as a player opening a chest (PrivateArea.CheckAccess without wardCheck), evaluated for
    /// the tomb owner: inside no active ward, or inside at least one where the owner is creator or permitted.
    /// Proximity never grants access.
    /// </summary>
    public static class WardAccess
    {
        public static bool Allows(IEnumerable<WardFacts> wards, long ownerId)
        {
            bool covered = false;
            foreach (WardFacts ward in wards)
            {
                if (!ward.Enabled || !ward.Covers) continue;
                if (ward.CreatorId == ownerId || ward.OwnerPermitted) return true;
                covered = true;
            }
            return !covered;
        }
    }
}
