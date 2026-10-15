#if DEBUG
using System.Collections;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// HB-T-098: sleep fast mode on the fake clock. Skipping is simulated (TombController.SkippingForTests) and world time
    /// is pushed forward per frame, as EnvMan.UpdateTimeSkip does while everyone sleeps.
    /// </summary>
    internal sealed class SleepSuite
    {
        private const int Requested = 20;

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _arena;
        private TombController _tomb;
        private Container _source;
        private DeliveryChest _delivery;

        public SleepSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Sleep";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                yield return Afternoon();
                yield return Night();
            }
            finally
            {
                TombController.SkippingForTests = false;
                TimeSource.Override = null;
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 14f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-6, 0, -6)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            _time.Fraction = FakeTime.Noon;
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            HaugbuiStructure delivery = _t.PlaceStructure(StructureKind.DeliveryChest, Ground(new Vector3(3, 0, 3)));
            GameObject source = _t.PlaceVanilla("piece_chest_wood", Ground(new Vector3(-8, 0, 8)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(3);
            _source = source != null ? source.GetComponentInChildren<Container>() : null;
            _delivery = delivery != null ? delivery.GetComponent<DeliveryChest>() : null;
            if (!_t.Check("tomb, delivery chest and wood chest placed", tomb != null && _delivery != null && _source != null)) yield break;
            var wood = new ItemStack { Prefab = "Wood", Amount = 30, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(wood);
            ItemBridge.AddTo(_source.GetInventory(), wood);
            _tomb = tomb.GetComponent<TombController>();
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
        }

        private IEnumerator Afternoon()
        {
            TombController.SkippingForTests = true;
            for (int i = 0; i < 60; i++)
            {
                _time.Offset += 10;
                yield return null;
            }
            _t.Check("without the Altar, a skipped afternoon adds no work time", _tomb.Budget.Seconds == 0 && !_tomb.Machine.IsOut,
                $"budget {_tomb.Budget.Seconds:0}, state {_tomb.Machine.State}");
            TombController.SkippingForTests = false;
        }

        private IEnumerator Night()
        {
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Idle, 10f);
            _t.Check("emerges at night", _tomb.Machine.State == HaugbuiState.Idle, _tomb.Machine.State.ToString());

            TombController.SkippingForTests = true;
            int fastBefore = _tomb.Worker.FastSteps;
            _delivery.Edit(l => l.Request("Wood", Requested) == RequestEditResult.Ok);
            yield return new WaitForSeconds(2f);
            _t.Check("work time is budgeted: 2 s of world time do not finish the trip", _delivery.Ledger.Remaining("Wood") > 0,
                $"remaining {_delivery.Ledger.Remaining("Wood")}, fast steps {_tomb.Worker.FastSteps - fastBefore}");

            for (int i = 0; i < 100 && _delivery.Ledger.Remaining("Wood") > 0; i++)
            {
                _time.Offset += 10;
                yield return null;
            }
            yield return SelfTestContext.WaitFrames(3);
            int delivered = ItemBridge.CountPrefab(_delivery.Container.GetInventory(), "Wood");
            int left = ItemBridge.CountPrefab(_source.GetInventory(), "Wood");
            _t.Check("delivery done while sleeping, without flight", _delivery.Ledger.Remaining("Wood") == 0 && _tomb.Worker.FastSteps > fastBefore,
                $"remaining {_delivery.Ledger.Remaining("Wood")}, fast steps {_tomb.Worker.FastSteps - fastBefore}");
            _t.Check("no item lost or duplicated during the skip", delivered + left + _tomb.Carried.CountPrefab("Wood") == 30,
                $"delivered {delivered}, left {left}, carried {_tomb.Carried.CountPrefab("Wood")}");

            TombController.SkippingForTests = false;
            yield return null;
            _t.Check("the bank empties when the skip ends", _tomb.Budget.Seconds == 0);
            yield return SetTime(FakeTime.Noon);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
        }

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
