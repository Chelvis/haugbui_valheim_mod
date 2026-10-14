using System.Collections.Generic;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Integration
{
    internal interface IAccessPolicy
    {
        ContainerVerdict CheckContainer(Container container, long ownerId, ContainerUse use);

        /// <summary>Wards only (HB-150): may the owner use something at this point?</summary>
        bool WardsAllow(Vector3 point, long ownerId);
    }

    /// <summary>HB-T-042: rules evaluated for the tomb owner's player ID, never the nearest player (HB-151).</summary>
    internal sealed class SinglePlayerAccessPolicy : IAccessPolicy
    {
        public static readonly SinglePlayerAccessPolicy Instance = new SinglePlayerAccessPolicy();

        private readonly List<WardFacts> _wards = new List<WardFacts>();

        public ContainerVerdict CheckContainer(Container container, long ownerId, ContainerUse use)
        {
            ContainerVerdict verdict = ContainerRules.Check(Facts(container), ownerId, use);
            if (verdict != ContainerVerdict.Allowed) return verdict;
            return WardsAllow(container.transform.position, ownerId) ? ContainerVerdict.Allowed : ContainerVerdict.Private;
        }

        public bool WardsAllow(Vector3 point, long ownerId)
        {
            _wards.Clear();
            foreach (PrivateArea ward in PrivateArea.m_allAreas)
            {
                if (ward == null || ward.m_nview == null || !ward.m_nview.IsValid()) continue;
                _wards.Add(new WardFacts
                {
                    Enabled = ward.IsEnabled(),
                    Covers = ward.IsInside(point, 0f),
                    CreatorId = ward.m_piece != null ? ward.m_piece.GetCreator() : 0,
                    OwnerPermitted = ward.IsPermitted(ownerId),
                });
            }
            return WardAccess.Allows(_wards, ownerId);
        }

        public static ContainerFacts Facts(Container container)
        {
            Piece piece = container.m_piece;
            HaugbuiStructure structure = container.GetComponent<HaugbuiStructure>();
            return new ContainerFacts
            {
                IsPlayerTombstone = container.GetComponent<TombStone>() != null,
                IsVehicle = container.m_wagon != null || container.m_rootObjectOverride != null || container.GetComponentInParent<Ship>() != null,
                IsObliterator = container.GetComponentInParent<Incinerator>() != null,
                AutoDestroyEmpty = container.m_autoDestroyEmpty,
                PlacedByPlayer = piece != null && piece.IsPlacedByPlayer(),
                CreatorId = piece != null ? piece.GetCreator() : 0,
                Privacy = (ContainerPrivacy)(int)container.m_privacy,
                HaugbuiKind = structure != null ? structure.Kind : StructureKind.None,
            };
        }
    }
}
