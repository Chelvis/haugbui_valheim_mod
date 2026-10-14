using System;
using Haugbui.Core.Persistence;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>
    /// Haugbui behaviour on a structure. Present on the placement prefab and added to the vanilla host on load.
    /// </summary>
    public sealed class HaugbuiStructure : MonoBehaviour, IPlaced, Hoverable
    {
        public int m_kind;

        private PersistentSlot<TombState> _tombSlot;

        internal StructureKind Kind => (StructureKind)m_kind;
        internal ZNetView View { get; private set; }
        internal StructureDefinition Definition => StructureDefinitions.For(Kind);
        internal TombState TombState { get; private set; }
        internal TombProgress Progress { get; set; } = new TombProgress(ProgressionTable.MinLevel, TombAbilities.None);

        internal ZDO Zdo => View != null && View.IsValid() ? View.GetZDO() : null;
        internal string Guid => Zdo?.GetString(ZdoKeys.Guid) ?? string.Empty;
        internal string BoundTombGuid => Zdo?.GetString(ZdoKeys.TombGuid) ?? string.Empty;

        private void Awake()
        {
            View = GetComponent<ZNetView>();
            if (Zdo == null) return; // placement ghost

            int stored = Zdo.GetInt(ZdoKeys.Kind);
            if (stored != 0) m_kind = stored;
            if (m_kind == 0) m_kind = (int)(StructureDefinitions.ForPieceId(Utils.GetPrefabName(gameObject))?.Kind ?? StructureKind.None);
            if (Kind == StructureKind.Tomb)
            {
                LoadTombState();
                gameObject.AddComponent<Creature.TombController>();
            }
            else if (TombProgression.IsUpgrade(Kind)) gameObject.AddComponent<UpgradeBeam>();
            else if (Kind == StructureKind.DeliveryChest) gameObject.AddComponent<DeliveryChest>();
            StructureRegistry.Register(this);
        }

        private void OnDestroy()
        {
            StructureRegistry.Unregister(this);
        }

        /// <summary>Runs once on the placing client, right after the vanilla placement flow.</summary>
        public void OnPlaced()
        {
            ZDO zdo = Zdo;
            StructureDefinition definition = Definition;
            if (zdo == null || definition == null) return;

            zdo.Set(ZdoKeys.Kind, (int)Kind);
            zdo.Set(ZdoKeys.Version, ZdoKeys.SchemaVersion);
            zdo.Set(ZdoKeys.Guid, System.Guid.NewGuid().ToString("N"));
            // Saved as the vanilla host from now on, so the world keeps the object if Haugbui is removed.
            zdo.SetPrefab(definition.Host.GetStableHashCode());

            if (Kind == StructureKind.Tomb)
            {
                LoadTombState();
                TombState = new TombState { Guid = Guid, OwnerId = Player.m_localPlayer.GetPlayerID() };
                SaveTombState();
            }
            else if (TombProgression.IsUpgrade(Kind))
            {
                PlacementResult result = PlacementCheck.CheckUpgrade(Kind, transform.position, out TombInfo tomb);
                if (result == PlacementResult.Ok)
                {
                    zdo.Set(ZdoKeys.TombGuid, tomb.Guid);
                    if (Kind == StructureKind.Longsword) CombatWarning.Show();
                }
                else
                {
                    Log.Warning($"{definition.PieceId} placed without a tomb to bind ({result}); it binds when a tomb is built nearby.");
                }
            }
            StructureRegistry.MarkDirty();
        }

        internal void SaveTombState()
        {
            if (_tombSlot == null || TombState == null || !View.IsOwner()) return;
            if (!_tombSlot.TrySave(TombState))
                Log.ErrorRateLimited("tomb_save:" + Guid, $"Tomb {Guid}: state not saved ({_tombSlot.State}, {_tombSlot.LastReadStatus}).");
        }

        private void LoadTombState()
        {
            _tombSlot = new PersistentSlot<TombState>(new ZdoBlobStore(Zdo), ZdoKeys.TombState, TombStateSerializer.Instance);
            TombState = _tombSlot.Load();
            if (_tombSlot.State == SlotState.ReadOnly)
                Log.Error($"Tomb {Guid}: saved state unreadable ({_tombSlot.LastReadStatus}); it is kept untouched.");
        }

        public string GetHoverText()
        {
            string text = $"{Definition?.NameToken}\n{LevelLine()}";
            return Localization.instance.Localize(text);
        }

        public string GetHoverName() => Definition?.NameToken ?? string.Empty;

        public float GetHoverOffset() => 0f;

        internal string LevelLine()
        {
            HaugbuiStructure tomb = Kind == StructureKind.Tomb ? this : StructureRegistry.FindTomb(BoundTombGuid);
            if (tomb == null) return "$haugbui_hover_unbound";
            return Localization.instance.Localize("$haugbui_hover_level",
                tomb.Progress.Level.ToString(), ProgressionTable.MaxLevel.ToString());
        }
    }
}
