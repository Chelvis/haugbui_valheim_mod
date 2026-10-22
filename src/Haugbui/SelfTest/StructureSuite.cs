#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Core.Recipes;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>M2: placement rules, host persistence, level and abilities, reload, missing-mod behaviour, rebinding.</summary>
    internal sealed class StructureSuite
    {
        private const string BedPrefab = "bed";
        private const string StorageItem = "Wood";
        private const int StorageAmount = 10;

        private readonly SelfTestContext _t;
        private Vector3 _arena;
        private HaugbuiStructure _tomb;
        private readonly List<HaugbuiStructure> _upgrades = new List<HaugbuiStructure>();
        private Bed _bed;

        public StructureSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Structures";
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 9f);

            if (!StructureDefinitions.All.All(d => HostPrefabSystem.IsRegistered(d.Kind)))
            {
                _t.Fail("all structure pieces registered", "see the BepInEx log for the disabled piece");
                yield break;
            }

            yield return PlacementRules();
            if (_tomb == null) yield break;
            yield return Upgrades();
            yield return Storage();
            yield return Pictures();
            yield return ReloadAll();
            yield return MissingMod();
            yield return MissingModOthers();
            yield return RemoveUpgrade();
            yield return Rebind();
        }

        private IEnumerator PlacementRules()
        {
            Player player = _t.Player;
            Vector3 lonely = SelfTestContext.OnGround(_arena + new Vector3(0, 0, 80));
            bool ownBedNearLonely = Object.FindObjectsByType<Bed>(FindObjectsSortMode.None)
                .Any(b => b.GetOwner() == player.GetPlayerID() && Vector3.Distance(b.transform.position, lonely) <= PlacementCheck.BedDistance);
            if (ownBedNearLonely) _t.Skip("tomb refused without own bed", "an own bed is within range of the test spot");
            else ExpectRule("tomb refused without own bed", StructureKind.Tomb, lonely, PlacementResult.NeedOwnBed);

            GameObject bedObject = _t.SpawnVanilla(BedPrefab, SelfTestContext.OnGround(_arena + new Vector3(-6, 0, 0)), Quaternion.identity);
            if (bedObject == null)
            {
                _t.Fail("spawn test bed", $"prefab '{BedPrefab}' not found");
                yield break;
            }
            _bed = bedObject.GetComponent<Bed>();
            yield return null;

            _bed.SetOwner(12345L, "someone else");
            ExpectRule("tomb refused near another player's bed", StructureKind.Tomb, _arena, PlacementResult.NeedOwnBed);
            _bed.SetOwner(player.GetPlayerID(), player.GetPlayerName());
            ExpectRule("tomb allowed near own bed", StructureKind.Tomb, _arena, PlacementResult.Ok);
            ExpectRule("upgrade refused without tomb", StructureKind.Offering, _arena, PlacementResult.NeedTomb);

            _tomb = Place(StructureKind.Tomb, _arena);
            if (!_t.Check("tomb placed", _tomb != null)) yield break;
            yield return SelfTestContext.WaitFrames(2);

            ZDO zdo = _tomb.Zdo;
            StructureDefinition definition = _tomb.Definition;
            _t.Check("tomb saved as vanilla host", zdo.GetPrefab() == definition.Host.GetStableHashCode(), definition.Host);
            _t.Check("tomb kind and guid written", zdo.GetInt(ZdoKeys.Kind) == (int)StructureKind.Tomb && _tomb.Guid.Length == 32);
            TombState state = _tomb.TombState;
            _t.Check("tomb state owned by placer", state != null && state.OwnerId == player.GetPlayerID());
            _t.Check("tomb toggles default (combat off)", state != null && state.Toggles == TombToggles.Defaults);
            _t.Check("tomb level 1", _tomb.Progress.Level == 1, $"level {_tomb.Progress.Level}");
            ExpectRule("second tomb refused within spacing", StructureKind.Tomb, _arena + new Vector3(3, 0, 0), PlacementResult.TombTooClose);
        }

        private IEnumerator Upgrades()
        {
            StructureKind[] order =
            {
                StructureKind.Altar, StructureKind.Offering, StructureKind.Longsword, StructureKind.SilverBracelets,
                StructureKind.PersonalTreasure
            };
            for (int i = 0; i < order.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / order.Length;
                Vector3 position = SelfTestContext.OnGround(_arena + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 3.5f);
                HaugbuiStructure upgrade = Place(order[i], position);
                if (!_t.Check($"{order[i]} placed", upgrade != null)) continue;
                _upgrades.Add(upgrade);
                yield return SelfTestContext.WaitFrames(2);

                _t.Check($"{order[i]} bound to tomb", upgrade.BoundTombGuid == _tomb.Guid);
                _t.Check($"level {i + 2} after {order[i]}", _tomb.Progress.Level == i + 2, $"level {_tomb.Progress.Level}");
                if (order[i] == StructureKind.Longsword)
                {
                    _t.Check("combat warning shown for the Longsword", UnifiedPopup.IsVisible());
                    if (UnifiedPopup.IsVisible()) UnifiedPopup.Pop();
                }
            }

            _t.Check("all upgrades: level 6", _tomb.Progress.Level == 6, $"level {_tomb.Progress.Level}");
            _t.Check("all upgrades: combat and full-time", _tomb.Progress.Has(TombAbilities.Combat | TombAbilities.FullTime));
            ExpectRule("duplicate upgrade refused", StructureKind.Offering, _arena + new Vector3(0, 0, -2), PlacementResult.UpgradeExists);
            _t.Check("tomb level cache saved", _tomb.TombState != null && _tomb.TombState.LevelCache == 6);
            yield return new WaitForSeconds(1f);
            List<HaugbuiStructure> near = _upgrades.Where(u => Vector3.Distance(u.transform.position, _t.Player.transform.position) <= 10f).ToList();
            if (near.Count == 0) _t.Skip("beams shown near the player", "test spot is more than 10 m from the player");
            else _t.Check("beams shown near the player", near.All(u => u.GetComponent<UpgradeBeam>()?.IsShown == true),
                $"{near.Count(u => u.GetComponent<UpgradeBeam>()?.IsShown == true)}/{near.Count}");
        }

        private IEnumerator Storage()
        {
            Container container = _tomb.GetComponent<Container>();
            _t.Check("tomb storage is a 10-slot container", container != null && container.m_width * container.m_height == 10);
            _t.Check("tomb storage named", container != null && container.m_name == "$haugbui_tomb_storage", container?.m_name);
            container?.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(StorageItem), StorageAmount);
            yield return null;
        }

        private IEnumerator Pictures()
        {
            yield return Screenshots.Capture(_t.OutputFolder, "tomb", _tomb.transform.position);
            foreach (HaugbuiStructure upgrade in _upgrades.Where(u => u != null))
                yield return Screenshots.Capture(_t.OutputFolder, upgrade.Kind.ToString(), upgrade.transform.position, 2f);
            yield return Screenshots.Capture(_t.OutputFolder, "tomb_and_upgrades", _arena, 7f);
            _t.Check("visual parts all found", StructureVisuals.MissingParts.Count == 0, string.Join(", ", StructureVisuals.MissingParts));
            foreach (KeyValuePair<string, string> tint in StructureVisuals.MaterialDiagnostics)
                _t.Check($"glow recoloured: {tint.Key}", true, tint.Value);
            if (StructureVisuals.MaterialDiagnostics.Count == 0)
                _t.Fail("glow recoloured", "no tinted material had an _EmissionColor property");
            foreach (StructureDefinition definition in StructureDefinitions.All)
            {
                foreach (VisualPart part in definition.Parts)
                {
                    StructureVisuals.PartDiagnostics.TryGetValue(definition.PieceId + "/" + part.Prefab, out string detail);
                    _t.Check($"{definition.Kind} shows {part.Prefab}", StructureVisuals.CopiedCount(definition.PieceId, part.Prefab) > 0, detail ?? "not applied");
                }
            }
        }

        private IEnumerator ReloadAll()
        {
            var structures = new List<HaugbuiStructure> { _tomb };
            structures.AddRange(_upgrades);
            var reloaded = new List<HaugbuiStructure>();
            foreach (HaugbuiStructure structure in structures.Where(s => s != null))
            {
                StructureKind kind = structure.Kind;
                GameObject again = null;
                yield return _t.Reload(structure.gameObject, go => again = go);
                HaugbuiStructure restored = again != null ? again.GetComponent<HaugbuiStructure>() : null;
                if (!_t.Check($"{kind} restored after reload", restored != null && restored.Kind == kind)) continue;
                reloaded.Add(restored);

                _t.Check($"{kind} look restored", again.transform.Find(StructureVisuals.RootName) != null);
                PieceRecipe recipe = StartupValidation.Results[restored.Definition.PieceId].Recipe;
                string refund = string.Join(",", again.GetComponent<Piece>().m_resources.Select(r => $"{r.m_resItem?.name}:{r.m_amount}"));
                _t.Check($"{kind} refunds the Haugbui recipe", refund == recipe.IngredientsToConfig(), refund);
            }

            _tomb = reloaded.FirstOrDefault(s => s.Kind == StructureKind.Tomb);
            _upgrades.Clear();
            _upgrades.AddRange(reloaded.Where(s => s.Kind != StructureKind.Tomb));
            yield return SelfTestContext.WaitFrames(2);
            if (_tomb == null) yield break;

            _t.Check("level 6 after reload", _tomb.Progress.Level == 6, $"level {_tomb.Progress.Level}");
            _t.Check("tomb storage kept after reload", CountStorage(_tomb.gameObject) == StorageAmount, $"{CountStorage(_tomb.gameObject)} {StorageItem}");
        }

        private IEnumerator MissingMod()
        {
            if (_tomb == null) yield break;
            string guid = _tomb.Guid;

            HostPrefabSystem.SuppressAugmentation = true;
            GameObject plain = null;
            try
            {
                yield return _t.Reload(_tomb.gameObject, go => plain = go);
            }
            finally
            {
                HostPrefabSystem.SuppressAugmentation = false;
            }

            if (_t.Check("without the mod the tomb loads as a vanilla chest", plain != null && plain.GetComponent<Container>() != null
                                                                                && plain.GetComponent<HaugbuiStructure>() == null))
            {
                _t.Check("without the mod: items kept", CountStorage(plain) == StorageAmount, $"{CountStorage(plain)} {StorageItem}");
                ZDO zdo = plain.GetComponent<ZNetView>().GetZDO();
                _t.Check("without the mod: Haugbui keys kept", zdo.GetString(ZdoKeys.Guid) == guid && zdo.GetInt(ZdoKeys.Kind) == (int)StructureKind.Tomb);
                _t.Check("without the mod: no Haugbui look", plain.transform.Find(StructureVisuals.RootName) == null);
                yield return Screenshots.Capture(_t.OutputFolder, "tomb_without_mod", plain.transform.position);
            }
            if (plain == null) yield break;

            GameObject back = null;
            yield return _t.Reload(plain, go => back = go);
            _tomb = back != null ? back.GetComponent<HaugbuiStructure>() : null;
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("with the mod again: tomb restored with level 6", _tomb != null && _tomb.Guid == guid && _tomb.Progress.Level == 6,
                _tomb != null ? $"level {_tomb.Progress.Level}" : "missing");
        }

        private HaugbuiStructure _restored;

        /// <summary>Every other structure, plus a delivery request and a station job, through the same mod-missing reload.</summary>
        private IEnumerator MissingModOthers()
        {
            if (_tomb == null) yield break;
            HaugbuiStructure collection = Place(StructureKind.CollectionChest, SelfTestContext.OnGround(_arena + new Vector3(6f, 0f, 1f)));
            HaugbuiStructure delivery = Place(StructureKind.DeliveryChest, SelfTestContext.OnGround(_arena + new Vector3(6f, 0f, -1.5f)));
            GameObject smelterObject = _t.PlaceVanilla("smelter", SelfTestContext.OnGround(_arena + new Vector3(-2f, 0f, 6f)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(3);
            DeliveryChest chest = delivery != null ? delivery.GetComponent<DeliveryChest>() : null;
            Smelter smelter = smelterObject != null ? smelterObject.GetComponentInChildren<Smelter>() : null;
            bool requested = chest != null && chest.Edit(ledger => ledger.Request("Wood", 5) == RequestEditResult.Ok);
            bool jobSet = smelter != null && StationJobs.Edit(smelter, job =>
            {
                job.Enabled = true;
                job.InputPrefab = "CopperOre";
                job.OutputPrefab = "Copper";
                job.Mode = JobMode.Finite;
                job.Target = 3;
                job.Supplied = 1;
                job.TombGuid = _tomb.Guid;
                return true;
            });
            _t.Check("delivery request and station job saved for the missing-mod pass", requested && jobSet);
            byte[] requestBytes = delivery != null ? delivery.Zdo.GetByteArray(ZdoKeys.Requests.GetStableHashCode()) : null;
            ZDO stationZdo = smelter != null ? smelter.m_nview.GetZDO() : null;
            byte[] jobBytes = stationZdo != null ? stationZdo.GetByteArray(ZdoKeys.Job.GetStableHashCode()) : null;

            var kept = new List<HaugbuiStructure>();
            foreach (HaugbuiStructure upgrade in _upgrades.Where(u => u != null).ToList())
            {
                yield return RoundTrip(upgrade.gameObject, upgrade.Kind.ToString(), upgrade.Kind, upgrade.Guid, null, null);
                if (_restored != null) kept.Add(_restored);
            }
            _upgrades.Clear();
            _upgrades.AddRange(kept);

            if (collection != null)
                yield return RoundTrip(collection.gameObject, "CollectionChest", StructureKind.CollectionChest, collection.Guid, null, null);
            if (delivery != null)
                yield return RoundTrip(delivery.gameObject, "DeliveryChest", StructureKind.DeliveryChest, delivery.Guid, requestBytes, "request");

            if (smelterObject == null || jobBytes == null) yield break;
            GameObject stationAgain = null;
            yield return _t.Reload(smelterObject, go => stationAgain = go);
            ZDO again = stationAgain != null ? stationAgain.GetComponentInChildren<Smelter>()?.m_nview.GetZDO() : null;
            byte[] jobNow = again != null ? again.GetByteArray(ZdoKeys.Job.GetStableHashCode()) : null;
            _t.Check("without a special host: station job kept across reload", jobNow != null && jobNow.SequenceEqual(jobBytes),
                jobNow == null ? "missing" : jobNow.Length + " bytes");
            if (UnifiedPopup.IsVisible()) UnifiedPopup.Pop();
        }

        private IEnumerator RoundTrip(GameObject go, string label, StructureKind kind, string guid, byte[] extra, string extraName)
        {
            _restored = null;
            HostPrefabSystem.SuppressAugmentation = true;
            GameObject plain = null;
            try
            {
                yield return _t.Reload(go, reloaded => plain = reloaded);
            }
            finally
            {
                HostPrefabSystem.SuppressAugmentation = false;
            }

            if (_t.Check($"{label}: without the mod it is a vanilla object", plain != null && plain.GetComponent<HaugbuiStructure>() == null))
            {
                ZDO zdo = plain.GetComponent<ZNetView>().GetZDO();
                _t.Check($"{label}: keys kept", zdo.GetString(ZdoKeys.Guid) == guid && zdo.GetInt(ZdoKeys.Kind) == (int)kind);
                _t.Check($"{label}: no Haugbui look", plain.transform.Find(StructureVisuals.RootName) == null);
                if (extra != null)
                {
                    byte[] now = zdo.GetByteArray((extraName == "request" ? ZdoKeys.Requests : ZdoKeys.Job).GetStableHashCode());
                    _t.Check($"{label}: {extraName} kept", now != null && now.SequenceEqual(extra));
                }
            }
            if (plain == null) yield break;

            GameObject back = null;
            yield return _t.Reload(plain, reloaded => back = reloaded);
            _restored = back != null ? back.GetComponent<HaugbuiStructure>() : null;
            _t.Check($"{label}: returns with the mod", _restored != null && _restored.Kind == kind && _restored.Guid == guid);
        }

        private IEnumerator RemoveUpgrade()
        {
            HaugbuiStructure sword = _upgrades.FirstOrDefault(u => u != null && u.Kind == StructureKind.Longsword);
            if (sword == null || _tomb == null) yield break;
            _upgrades.Remove(sword);
            ZNetScene.instance.Destroy(sword.gameObject);
            yield return SelfTestContext.WaitFrames(3);
            _t.Check("removing the Longsword: level 5", _tomb.Progress.Level == 5, $"level {_tomb.Progress.Level}");
            _t.Check("removing the Longsword: combat ability gone", !_tomb.Progress.Has(TombAbilities.Combat));
        }

        private IEnumerator Rebind()
        {
            if (_tomb == null) yield break;
            string oldGuid = _tomb.Guid;
            ZNetScene.instance.Destroy(_tomb.gameObject);
            yield return SelfTestContext.WaitFrames(3);

            _tomb = Place(StructureKind.Tomb, _arena);
            if (!_t.Check("rebuilt tomb placed", _tomb != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            _t.Check("rebuilt tomb has a new guid", _tomb.Guid != oldGuid);
            _t.Check("upgrades rebound to the rebuilt tomb", _upgrades.Where(u => u != null).All(u => u.BoundTombGuid == _tomb.Guid));
            _t.Check("rebuilt tomb level 5", _tomb.Progress.Level == 5, $"level {_tomb.Progress.Level}");
        }

        private HaugbuiStructure Place(StructureKind kind, Vector3 position)
        {
            StructureDefinition definition = StructureDefinitions.For(kind);
            Piece piece = ZNetScene.instance.GetPrefab(definition.PieceId)?.GetComponent<Piece>();
            if (piece == null) return null;
            _t.Player.PlacePiece(piece, position, Quaternion.identity, doAttack: false);
            HaugbuiStructure placed = StructureRegistry.Loaded
                .Where(s => s.Kind == kind && Vector3.Distance(s.transform.position, position) < 0.1f)
                .OrderByDescending(s => s.GetInstanceID())
                .FirstOrDefault();
            return placed != null ? _t.Track(placed) : null;
        }

        private void ExpectRule(string name, StructureKind kind, Vector3 position, PlacementResult expected)
        {
            PlacementResult actual = PlacementCheck.Check(kind, position, _t.Player);
            _t.Check(name, actual == expected, $"got {actual}");
        }

        private static int CountStorage(GameObject go)
        {
            Container container = go.GetComponent<Container>();
            if (container == null) return -1;
            string token = ObjectDB.instance.GetItemPrefab(StorageItem).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            return container.GetInventory().CountItems(token);
        }
    }
}
#endif
