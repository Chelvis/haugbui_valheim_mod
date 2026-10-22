using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Core.Tribute;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>
    /// HB-T-032..036, HB-T-093, HB-T-025, HB-T-100..105: the tomb owns its Haugbui. Decides presence and pays the shift,
    /// spawns and recalls the creature, keeps health, carried items, overflow and tribute in the tomb ZDO and the
    /// servant's identity in its slot. Logic runs only on the tomb's ZDO owner.
    /// </summary>
    public sealed class TombController : MonoBehaviour
    {
        private const float TickSeconds = 0.5f;
        private const float EmergeSeconds = 1.5f;
        private const float EnterSeconds = 0.6f;
        private const float IdleSpeed = 1f;
        private const float IdleHeight = 2.2f;
        private const float ResumeMaxDistanceFactor = 1.5f;
        private const int FastStepsPerFrame = 20;

        private static readonly List<TombController> Instances = new List<TombController>();

        private HaugbuiStructure _tomb;
        private PersistentSlot<List<ItemStack>> _carriedSlot;
        private PersistentSlot<List<ItemStack>> _overflowSlot;
        private float _nextTick;
        private bool _destroyed;
        private bool _resumeChecked;
        private int _lastSavedHash;
        private Core.Scheduling.TickHandle _planTick;
        private float _nextWorkCheck;
        private bool _slotChecked;

        public static IReadOnlyList<TombController> All => Instances;

        internal HaugbuiStateMachine Machine { get; private set; }
        internal CarriedInventory Carried { get; private set; }
        internal List<ItemStack> Overflow { get; private set; } = new List<ItemStack>();
        internal HaugbuiCreature Creature { get; private set; }
        internal HaugbuiStructure Structure => _tomb;
        internal HaugbuiWorker Worker { get; private set; }
        internal CombatTaskProvider Combat { get; private set; }
        internal string Guid => _tomb.Guid;

        /// <summary>SetToggle refusal: the first enable must confirm the combat warning (HB-026).</summary>
        internal const string CombatNeedsConfirmation = "combat_needs_confirmation";

        /// <summary>HB-140/013: Longsword bound, toggle on and acknowledged, and the game API it needs is there.</summary>
        internal bool CombatEnabled =>
            State != null && _tomb.Progress.Has(TombAbilities.Combat) && State.Has(TombToggles.Combat | TombToggles.CombatAcknowledged)
            && GameApiSelfCheck.IsAvailable(Core.GameApi.Features.Combat);

        /// <summary>The servant bound to this tomb (HB-137); null until bound or when every slot is taken.</summary>
        internal ServantSlot Slot { get; private set; }
        internal string ServantName => Slot != null && !string.IsNullOrEmpty(Slot.Name) ? Slot.Name : ServantSlots.DefaultName(0);

        /// <summary>Why it stays inside during its work window (HB-167/168); null when it may work.</summary>
        internal ShiftDecision? Waiting { get; private set; }
        internal int ShiftCost => HaugbuiPlugin.Instance.Settings.TributeTable.CostFor(_tomb.Progress.Level);
        internal static bool TributeEnabled => HaugbuiPlugin.Instance.Settings.TributeEnabled.Value && !TributeSuspended;

#if DEBUG
        /// <summary>Self-test suites written before the tribute rules run with it off.</summary>
        internal static bool TributeSuspendedForTests { get; set; }
        private static bool TributeSuspended => TributeSuspendedForTests;
#else
        private const bool TributeSuspended = false;
#endif

        /// <summary>The creature is out and this peer runs its logic.</summary>
        internal bool CanWork => !_destroyed && State != null && IsOwner && Creature != null && Machine.IsOut;
        internal TombState State => _tomb != null ? _tomb.TombState : null;
        internal bool IsOwner => _tomb != null && _tomb.View != null && _tomb.View.IsValid() && _tomb.View.IsOwner();
        internal HaugbuiAttributes Attributes => HaugbuiPlugin.Instance.Settings.ProgressionTable.ForLevel(_tomb.Progress.Level);
        internal bool FullTime => _tomb.Progress.Has(TombAbilities.FullTime);
        internal bool DarkAura => FullTime;
        internal Vector3 Home => transform.position + Vector3.up * 0.8f;

        internal static double CooldownSeconds =>
            HaugbuiPlugin.Instance.Settings.CooldownDays.Value * TimeSource.Current.DayLengthSeconds;

        /// <summary>HB-132/136: time until death or a new tomb stop keeping it inside (countdown label, hover).</summary>
        internal double WaitRemaining => State != null ? PresencePolicy.WaitRemaining(Input()) : 0;

        internal ShiftTimer Timer => PresencePolicy.Timer(Input());

        /// <summary>HB-T-098: world time banked while the player sleeps.</summary>
        internal WorkBudget Budget { get; } = new WorkBudget();

#if DEBUG
        /// <summary>Self-test: behave as if the game were skipping time (sleeping).</summary>
        internal static bool SkippingForTests { get; set; }
#else
        private const bool SkippingForTests = false;
#endif

        /// <summary>The game is fast-forwarding to morning (EnvMan.IsTimeSkipping) and sleep fast mode is on.</summary>
        internal static bool SleepFastMode =>
            HaugbuiPlugin.Instance.Settings.SleepFastMode.Value
            && (SkippingForTests || EnvMan.instance != null && EnvMan.instance.IsTimeSkipping());

        private void Awake()
        {
            _tomb = GetComponent<HaugbuiStructure>();
            Machine = new HaugbuiStateMachine(() => TimeSource.Current.Now);
            Carried = new CarriedInventory(1, 1);
            Worker = new HaugbuiWorker(this);
            Combat = new CombatTaskProvider(this);
            if (_tomb.Zdo == null) return;

            var store = new ZdoBlobStore(_tomb.Zdo);
            _carriedSlot = new PersistentSlot<List<ItemStack>>(store, ZdoKeys.Carried, ItemStacksSerializer.Instance);
            _overflowSlot = new PersistentSlot<List<ItemStack>>(store, ZdoKeys.Overflow, ItemStacksSerializer.Instance);
            List<ItemStack> carried = _carriedSlot.Load();
            List<ItemStack> overflow = _overflowSlot.Load();
            if (_tomb.BlobsBlocked || _carriedSlot.State == SlotState.ReadOnly || _overflowSlot.State == SlotState.ReadOnly)
            {
                if (!_tomb.BlobsBlocked)
                    Log.Error($"Tomb {_tomb.Guid}: carried inventory unreadable; it is kept untouched and this tomb will not run.");
                _tomb.BlobsBlocked = true;
                WearNTear blocked = GetComponent<WearNTear>();
                if (blocked != null) blocked.m_onDestroyed += () => WorldSafety.RecordLive(_tomb.Guid);
                return;
            }
            carried = carried ?? new List<ItemStack>();
            overflow = overflow ?? new List<ItemStack>();
            if (BlobIntegrity.RepairStacks(carried))
            {
                Log.Warning($"Tomb {_tomb.Guid}: carried stacks had invalid amounts; they were clamped.");
                _carriedSlot.TrySave(carried);
            }
            if (BlobIntegrity.RepairStacks(overflow))
            {
                Log.Warning($"Tomb {_tomb.Guid}: overflow stacks had invalid amounts; they were clamped.");
                _overflowSlot.TrySave(overflow);
            }
            Carried.Restore(Refresh(carried));
            Overflow = Refresh(overflow);
            Carried.Changed += SaveCarried;
            if (State != null) Machine.Restore((HaugbuiState)State.CreatureState);

            WearNTear wear = GetComponent<WearNTear>();
            if (wear != null) wear.m_onDestroyed += OnTombDestroyed;
            gameObject.AddComponent<CountdownLabel>();
            Instances.Add(this);
            _planTick = HaugbuiPlugin.Instance.Scheduler.Register("plan:" + _tomb.Guid,
                HaugbuiPlugin.Instance.Settings.PlanningIntervalSeconds.Value, Worker.Tick, Time.timeAsDouble);
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            HaugbuiPlugin.Instance?.Scheduler?.Unregister(_planTick);
            Worker?.ReleaseAll();
            // Zone unload or logout: the creature goes with the tomb; the saved state resumes it on the next load.
            if (Creature != null && Creature.View != null && Creature.View.IsValid() && Creature.View.IsOwner() && ZNetScene.instance != null)
                ZNetScene.instance.Destroy(Creature.gameObject);
        }

        private void Update()
        {
            if (_destroyed || State == null || _tomb.BlobsBlocked || !IsOwner || CreaturePrefab.Prefab == null) return;
            if (!_resumeChecked)
            {
                _resumeChecked = true;
                ResumeAfterLoad();
            }
            bool fast = SleepFastMode;
            Budget.Observe(TimeSource.Current.Now, fast, CanWork && !Machine.ReturnPending);
            StepState(fast);
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickSeconds;
            Tick();
        }

        /// <summary>Forces the next presence decision now (self-test and panel).</summary>
        internal void TickNow()
        {
            _nextTick = 0;
            _nextWorkCheck = 0;
            if (!_destroyed && State != null && IsOwner) Tick();
        }

        private PresenceInput Input() => InputFor(State, Machine.State == HaugbuiState.Dead);

        /// <summary>Also used by non-owners with the state read from the ZDO.</summary>
        internal PresenceInput InputFor(TombState state, bool dead)
        {
            ITimeSource time = TimeSource.Current;
            return new PresenceInput
            {
                Now = time.Now,
                DayLengthSeconds = time.DayLengthSeconds,
                FullTime = FullTime,
                Resting = state.Has(TombToggles.Resting),
                Dead = dead,
                DeathTime = state.DeathTime,
                CooldownSeconds = CooldownSeconds,
                DawnReturnLeadSeconds = HaugbuiPlugin.Instance.Settings.DawnReturnLeadSeconds.Value,
                StartTime = state.StartTime,
            };
        }

        private long CurrentShift => WorldClock.ShiftId(TimeSource.Current.Now, TimeSource.Current.DayLengthSeconds);

        /// <summary>
        /// HB-T-101: may this shift be worked? Charges the shift once, at its start, and only when there is work; checks
        /// again every WorkCheckSeconds while it waits. The hook for away mode (M12) and sleep fast mode (HB-T-098),
        /// which charge the same way.
        /// </summary>
        internal bool StartShift()
        {
            if (State.SlotIndex < 0)
            {
                Waiting = null;
                return false;
            }
            if (!TributeEnabled)
            {
                Waiting = null;
                return true;
            }
            long shift = CurrentShift;
            if (shift == State.LastPaidShift)
            {
                Waiting = null;
                return true;
            }
            if (Time.time < _nextWorkCheck) return false;
            _nextWorkCheck = Time.time + HaugbuiPlugin.Instance.Settings.WorkCheckSeconds.Value;

            int cost = ShiftCost;
            ShiftDecision decision = ShiftRules.Decide(true, shift, State.LastPaidShift, Worker.HasWork(), State.Balance, cost);
            if (decision == ShiftDecision.Charge)
            {
                var balance = new TributeBalance(State.Balance, State.CheatedBalance);
                balance.TryCharge(cost);
                State.Balance = balance.Total;
                State.CheatedBalance = balance.Cheated;
                State.LastPaidShift = shift;
                SaveStateIfChanged();
                Log.Info($"Tomb {_tomb.Guid}: shift {shift} paid ({cost}), balance {State.Balance}.");
            }
            Waiting = ShiftRules.MayWork(decision) ? (ShiftDecision?)null : decision;
            return Waiting == null;
        }

        private void ResumeAfterLoad()
        {
            Creature = HaugbuiCreature.Find(_tomb.Guid);
            if (Creature != null) Adopt(Creature);
            if (!Machine.IsOut || Creature != null) return;

            if (PresencePolicy.ShouldBeOut(Input()) && State.SlotIndex >= 0 && (!TributeEnabled || State.LastPaidShift == CurrentShift))
            {
                var saved = new Vector3(State.PositionX, State.PositionY, State.PositionZ);
                bool valid = !float.IsNaN(State.PositionX)
                             && Vector3.Distance(saved, transform.position) <= Attributes.Radius * ResumeMaxDistanceFactor;
                Spawn(valid ? saved : Home, emergeEffect: false);
                Machine.Restore(HaugbuiState.Idle);
                Creature.SetState(HaugbuiState.Idle);
                Log.Debug($"Tomb {_tomb.Guid}: Haugbui resumed at {(valid ? "its saved position" : "the tomb")}");
            }
            else
            {
                Machine.Restore(HaugbuiState.Dormant);
                EnteredTomb();
            }
        }

        private void Tick()
        {
            TombState state = State;
            HaugbuiAttributes attributes = Attributes;
            Carried.Slots = attributes.Slots;
            Carried.Capacity = attributes.CarryCapacity;

            switch (PresencePolicy.Decide(Input(), Machine.IsOut))
            {
                case PresenceDecision.Emerge:
                    if (StartShift()) Emerge();
                    break;
                case PresenceDecision.Return:
                    Waiting = null;
                    if (Machine.RequestReturn() && Creature != null) Creature.MoveTo(Home);
                    break;
                case PresenceDecision.StayOut:
                    // With the Altar it stays out across dusk and dawn; each new shift is paid like any other.
                    if (!StartShift())
                    {
                        if (Waiting != null && Machine.RequestReturn() && Creature != null) Creature.MoveTo(Home);
                        break;
                    }
                    if (Machine.ReturnPending) Machine.CancelReturn();
                    // E.g. the Altar was built or Rest turned off while it flew home.
                    if (Machine.State == HaugbuiState.Returning && Machine.TryEnter(HaugbuiState.Idle)) Creature?.Stop();
                    break;
                default:
                    Waiting = null;
                    break;
            }

            if (Machine.IsOut && Creature != null)
            {
                SyncHealth(attributes);
                SyncCombatFactors(attributes);
                Vector3 p = Creature.transform.position;
                state.PositionX = p.x;
                state.PositionY = p.y;
                state.PositionZ = p.z;
                Creature.Visuals?.SetDarkAura(DarkAura);
            }
            state.CreatureState = (int)HaugbuiStateMachine.Persisted(Machine.State);
            SaveStateIfChanged();
        }

        private void StepState(bool fast)
        {
            if (Machine.IsOut && Creature == null)
            {
                Worker.ReleaseAll();
                Combat.Reset();
                // Destroyed by something else than death (e.g. removed with a command): count it as back inside.
                Machine.Restore(HaugbuiState.Dormant);
                EnteredTomb();
                return;
            }
            if (Creature == null) return;

            Combat.Step(fast);
            bool working = fast ? Worker.FastForward(Budget, FastStepsPerFrame) : Worker.Step();
            Creature.SetState(Machine.State);
            switch (Machine.State)
            {
                case HaugbuiState.Emerging:
                    if (Machine.TimeInState >= EmergeSeconds) Machine.TryEnter(HaugbuiState.Idle);
                    break;
                case HaugbuiState.Idle:
                    if (working) break;
                    Creature.MaxSpeed = Mathf.Min(IdleSpeed, Attributes.MaxSpeed);
                    if (!Creature.HasTarget || Creature.Arrived) Creature.MoveTo(HoverPoint());
                    break;
                case HaugbuiState.Returning:
                    Creature.MaxSpeed = Attributes.MaxSpeed;
                    if (!Creature.HasTarget) Creature.MoveTo(Home);
                    if (Creature.Arrived && Machine.TryEnter(HaugbuiState.Entering)) PlayEffect(Creature.transform.position);
                    break;
                case HaugbuiState.Entering:
                    if (Machine.TimeInState >= EnterSeconds) Enter();
                    break;
            }
        }

        /// <summary>A point circling above the tomb: idle hover, and where a hurt Haugbui waits without entering (HB-144).</summary>
        internal Vector3 HoverPoint()
        {
            float angle = Time.time * 0.3f + _tomb.Guid.GetHashCode() % 7;
            return transform.position + new Vector3(Mathf.Cos(angle) * 2.5f, IdleHeight, Mathf.Sin(angle) * 2.5f);
        }

        private void Emerge()
        {
            if (Machine.State == HaugbuiState.Dead) Machine.Restore(HaugbuiState.Dormant);
            if (Creature == null) Creature = HaugbuiCreature.Find(_tomb.Guid);
            if (Creature == null) Spawn(Home, emergeEffect: true);
            else Adopt(Creature);
            // The killing blow reports its damage after the death, so a new shift starts without old threats.
            Combat.Reset();
            Machine.TryEnter(HaugbuiState.Emerging);
            Creature.SetState(Machine.State);
            State.CreatureState = (int)HaugbuiState.Idle;
            SaveStateIfChanged();
            if (Slot != null && !double.IsNaN(Slot.DeathTime)) UpdateSlot(s => s.DeathTime = double.NaN);
        }

        private void Spawn(Vector3 position, bool emergeEffect)
        {
            GameObject go = Instantiate(CreaturePrefab.Prefab, position, Quaternion.LookRotation(transform.forward));
            Creature = go.GetComponent<HaugbuiCreature>();
            Creature.Bind(_tomb.Guid);
            Adopt(Creature);

            HaugbuiAttributes attributes = Attributes;
            Creature.Character.SetMaxHealth(attributes.MaxHealth);
            Creature.Character.SetHealth(EffectiveHealth(attributes.MaxHealth));
            SyncCombatFactors(attributes);
            Creature.Visuals?.SetDarkAura(DarkAura);
            if (emergeEffect) PlayEffect(position);
        }

        private void Adopt(HaugbuiCreature creature)
        {
            creature.Died -= OnCreatureDied;
            creature.Died += OnCreatureDied;
            creature.Damaged -= Combat.OnDamaged;
            creature.Damaged += Combat.OnDamaged;
            SyncCombatFactors(Attributes);
        }

        private void SyncCombatFactors(HaugbuiAttributes attributes)
        {
            if (Creature == null) return;
            Creature.DealtFactor = Core.Combat.CombatMath.DealtFactor(attributes.CombatMultiplier,
                HaugbuiPlugin.Instance.Settings.CombatDamageMultiplier.Value);
            Creature.TakenFactor = Core.Combat.CombatMath.TakenFactor(attributes.CombatMultiplier);
        }

        /// <summary>Health with dormant regeneration applied up to now; saved health is "full" when negative.</summary>
        internal float EffectiveHealth(float max)
        {
            TombState state = State;
            float health = state.Health < 0 ? max : Mathf.Min(state.Health, max);
            if (Machine.IsOut || double.IsNaN(state.HealthTime)) return health;
            return HealthMath.Regenerate(health, max, TimeSource.Current.Now - state.HealthTime,
                HaugbuiPlugin.Instance.Settings.RestFullHealSeconds.Value);
        }

        private void SyncHealth(HaugbuiAttributes attributes)
        {
            Character character = Creature.Character;
            float max = character.GetMaxHealth();
            float health = character.GetHealth();
            if (!Mathf.Approximately(max, attributes.MaxHealth))
            {
                health = HealthMath.Rescale(health, max, attributes.MaxHealth);
                character.SetMaxHealth(attributes.MaxHealth);
                character.SetHealth(health);
            }
            RecordHealth(health, attributes.MaxHealth);
        }

        private void RecordHealth(float health, float max)
        {
            State.Health = health >= max ? -1f : health;
            State.HealthTime = TimeSource.Current.Now;
        }

        private void Enter()
        {
            Worker.ReleaseAll();
            Combat.Reset();
            if (Creature != null)
            {
                RecordHealth(Creature.Character.GetHealth(), Creature.Character.GetMaxHealth());
                ZNetScene.instance.Destroy(Creature.gameObject);
                Creature = null;
            }
            Machine.TryEnter(HaugbuiState.Dormant);
            EnteredTomb();
        }

        /// <summary>HB-044: every entry empties the carried inventory into tomb storage.</summary>
        private void EnteredTomb()
        {
            DepositCarried();
            State.CreatureState = (int)HaugbuiState.Dormant;
            if (double.IsNaN(State.HealthTime)) State.HealthTime = TimeSource.Current.Now;
            SaveStateIfChanged();
        }

        private void OnCreatureDied()
        {
            if (!IsOwner || State == null) return;
            Worker.ReleaseAll();
            Combat.Reset();
            if (Creature != null) PlayEffect(Creature.transform.position);
            Creature = null;
            Machine.Kill();
            double now = TimeSource.Current.Now;
            State.DeathTime = now;
            State.Health = 0;
            State.HealthTime = now;
            State.CreatureState = (int)HaugbuiState.Dead;
            // HB-168: no refund, and its return is a new shift that is charged again.
            State.LastPaidShift = WorldClock.NoShift;
            DepositCarried();
            SaveStateIfChanged();
            UpdateSlot(s => s.DeathTime = now);
            Log.Info($"Tomb {_tomb.Guid}: Haugbui died; returns in {PresencePolicy.FormatCountdown(WaitRemaining)} (world time).");
        }

        /// <summary>
        /// HB-175: destination first, then the source, in the same frame. Whatever storage cannot take goes to the
        /// overflow blob; nothing is destroyed.
        /// </summary>
        internal bool DepositCarried()
        {
            if (Carried.IsEmpty) return true;
            if (!_carriedSlot.CanWrite || !_overflowSlot.CanWrite)
            {
                Log.ErrorRateLimited("deposit:" + _tomb.Guid, $"Tomb {_tomb.Guid}: carried items kept (unreadable saved data).");
                return false;
            }

            Inventory storage = GetComponent<Container>()?.GetInventory();
            List<ItemStack> rest = DepositPlanner.Deposit(Carried.Stacks.ToList(), stack => ItemBridge.AddTo(storage, stack));
            Overflow = DepositPlanner.Merge(Overflow, rest);
            _overflowSlot.TrySave(Overflow);
            Carried.TakeAll();
            Creature?.Visuals?.Pulse();
            if (rest.Count > 0) Log.Info($"Tomb {_tomb.Guid}: storage full, {rest.Sum(s => s.Amount)} items kept in overflow.");
            return true;
        }

        private void SaveCarried()
        {
            if (!_carriedSlot.TrySave(Carried.Stacks.ToList()))
                Log.ErrorRateLimited("carried:" + _tomb.Guid, $"Tomb {_tomb.Guid}: carried items not saved ({_carriedSlot.State}).");
        }

        private void SaveStateIfChanged()
        {
            int hash = TombStateSerializer.Instance.Serialize(State).Aggregate(17, (h, b) => h * 31 + b);
            if (hash == _lastSavedHash) return;
            _lastSavedHash = hash;
            _tomb.SaveTombState();
        }

        internal bool SetToggle(TombToggles toggle, bool on, out string refusal)
        {
            refusal = null;
            if (on && toggle == TombToggles.Combat && !_tomb.Progress.Has(TombAbilities.Combat)) refusal = "$haugbui_panel_needs_longsword";
            if (on && toggle == TombToggles.Production && _tomb.Progress.Level < HaugbuiPlugin.Instance.Settings.ProductionUnlockLevel.Value)
                refusal = "$haugbui_panel_needs_level";
            if (refusal != null || State == null || !IsOwner) return false;
            if (on && toggle == TombToggles.Combat && !State.Has(TombToggles.CombatAcknowledged))
            {
                refusal = CombatNeedsConfirmation;
                return false;
            }

            State.Set(toggle, on);
            SaveStateIfChanged();
            TombToggles toggles = State.Toggles;
            UpdateSlot(s => s.Toggles = toggles);
            TickNow();
            return true;
        }

        /// <summary>HB-026: the player confirmed the combat warning; later enables need no confirmation.</summary>
        internal bool AcknowledgeCombat()
        {
            if (State == null || !IsOwner || !_tomb.Progress.Has(TombAbilities.Combat)) return false;
            State.Set(TombToggles.CombatAcknowledged, true);
            SaveStateIfChanged();
            TombToggles toggles = State.Toggles;
            UpdateSlot(s => s.Toggles = toggles);
            return true;
        }

        /// <summary>HB-T-102: takes one valuable from the player's inventory and adds its worth to the balance.</summary>
        internal bool Offer(string prefab, Player player)
        {
            int value = HaugbuiPlugin.Instance.Settings.TributeTable.ValueOf(prefab);
            if (value <= 0 || player == null || State == null || !IsOwner || _destroyed) return false;
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData item = inventory.GetAllItems().FirstOrDefault(i => ItemBridge.PrefabName(i) == prefab);
            if (item == null) return false;
            bool cheated = item.m_cheated;
            if (!inventory.RemoveItem(item, 1)) return false;

            var balance = new TributeBalance(State.Balance, State.CheatedBalance);
            balance.Offer(value, cheated);
            State.Balance = balance.Total;
            State.CheatedBalance = balance.Cheated;
            SaveStateIfChanged();
            Log.Debug($"Tomb {_tomb.Guid}: offered {prefab} (+{value}{(cheated ? ", cheated" : "")}), balance {State.Balance}.");
            TickNow();
            return true;
        }

        /// <summary>Called once the tomb's state exists after placement (the controller woke up before it).</summary>
        internal void OnStateCreated(ServantSlot slot)
        {
            Slot = slot;
            _slotChecked = true;
            Machine.Restore((HaugbuiState)State.CreatureState);
            _lastSavedHash = 0;
        }

        /// <summary>HB-T-104 migration: loaded tombs without a slot (built before 0.0.6) bind in GUID order.</summary>
        internal static void BindPendingSlots()
        {
            if (!ServantRegistry.Available) return;
            foreach (TombController tomb in Instances.Where(t => !t._slotChecked && !t._destroyed && t.State != null && t.IsOwner)
                         .OrderBy(t => t.Guid, System.StringComparer.Ordinal).ToList())
                tomb.EnsureSlot();
        }

        private void EnsureSlot()
        {
            _slotChecked = true;
            int before = State.SlotIndex;
            int stateBefore = State.CreatureState;
            Slot = ServantRegistry.Bind(State);
            if (Slot == null)
            {
                Log.Warning($"Tomb {_tomb.Guid}: every servant slot of player {State.OwnerId} is taken; this tomb stays empty.");
                return;
            }
            // Only an inherited death changes the state; a running Haugbui is left alone.
            if (State.CreatureState != stateBefore && !Machine.IsOut) Machine.Restore((HaugbuiState)State.CreatureState);
            SaveStateIfChanged();
            if (before != State.SlotIndex) Log.Info($"Tomb {_tomb.Guid}: bound to servant slot {State.SlotIndex} ({Slot.Name}).");
        }

        private void UpdateSlot(System.Action<ServantSlot> change)
        {
            if (State == null || State.SlotIndex < 0) return;
            ServantSlot current = ServantRegistry.Read(State.OwnerId, State.SlotIndex);
            if (current == null || current.TombGuid != _tomb.Guid) return;
            change(current);
            ServantRegistry.Write(State.OwnerId, current);
            Slot = current;
        }

        /// <summary>HB-T-025: tomb removed or destroyed. Carried and overflow items drop here; storage drops itself.</summary>
        private void OnTombDestroyed()
        {
            if (!IsOwner || _destroyed) return;
            _destroyed = true;
            Worker.ReleaseAll();
            Vector3 at = transform.position + Vector3.up * 0.5f;
            List<ItemStack> all = DepositPlanner.Merge(Carried.Stacks, Overflow);
            int dropped = all.Sum(s => ItemBridge.Drop(s, at));
            Overflow = new List<ItemStack>();
            _overflowSlot.TrySave(Overflow);
            Carried.TakeAll();
            RefundTribute(at);
            UpdateSlot(s => s.TombGuid = string.Empty);

            if (Creature != null)
            {
                PlayEffect(Creature.transform.position);
                ZNetScene.instance.Destroy(Creature.gameObject);
                Creature = null;
            }

            foreach (HaugbuiStructure upgrade in StructureRegistry.Upgrades.Where(u => u.BoundTombGuid == _tomb.Guid && u.View.IsOwner()).ToList())
                upgrade.Zdo.Set(ZdoKeys.TombGuid, string.Empty);
            StructureRegistry.MarkDirty();
            WorldSafety.RecordLive(_tomb.Guid);
            Log.Info($"Tomb {_tomb.Guid} destroyed: {dropped} carried/overflow items dropped, upgrades unbound.");
        }

        /// <summary>HB-159: the balance drops as Coins; only the part paid with cheated valuables comes back cheated.</summary>
        private void RefundTribute(Vector3 at)
        {
            if (State.Balance <= 0) return;
            (int clean, int cheated) = new TributeBalance(State.Balance, State.CheatedBalance).TakeAll();
            State.Balance = 0;
            State.CheatedBalance = 0;
            _tomb.SaveTombState();
            if (!HaugbuiPlugin.Instance.Settings.RefundTribute)
            {
                Log.Info($"Tomb {_tomb.Guid}: tribute balance of {clean + cheated} forfeited.");
                return;
            }
            int dropped = DropCoins(clean, false, at) + DropCoins(cheated, true, at);
            Log.Info($"Tomb {_tomb.Guid}: tribute balance refunded as {dropped} Coins ({cheated} cheated).");
        }

        private static int DropCoins(int amount, bool cheated, Vector3 at)
        {
            if (amount <= 0) return 0;
            var coins = new ItemStack { Prefab = "Coins", Amount = amount, WorldLevel = Game.m_worldLevel, Cheated = cheated };
            ItemBridge.RefreshShared(coins);
            return ItemBridge.Drop(coins, at);
        }

        private void PlayEffect(Vector3 position)
        {
            Character template = CreaturePrefab.Prefab != null ? CreaturePrefab.Prefab.GetComponent<Character>() : null;
            template?.m_deathEffects?.Create(position, Quaternion.identity);
        }

        private static List<ItemStack> Refresh(List<ItemStack> stacks)
        {
            foreach (ItemStack stack in stacks) ItemBridge.RefreshShared(stack);
            return stacks;
        }

        /// <summary>Panel and hover status, already localizable.</summary>
        internal string StatusLine()
        {
            if (State == null) return string.Empty;
            if (_slotChecked && State.SlotIndex < 0) return "$haugbui_status_no_slot";
            double wait = WaitRemaining;
            if (wait > 0)
                return Localization.instance.Localize(Machine.State == HaugbuiState.Dead ? "$haugbui_status_cooldown" : "$haugbui_status_starts_in",
                    PresencePolicy.FormatCountdown(wait));
            if (Machine.State == HaugbuiState.Dead) return "$haugbui_status_waiting_night";
            if (State.Has(TombToggles.Resting)) return "$haugbui_status_resting";
            if (!Machine.IsOut && Waiting == ShiftDecision.NoTribute) return "$haugbui_status_no_tribute";
            if (!Machine.IsOut && Waiting == ShiftDecision.NoWork) return "$haugbui_status_no_work";
            return "$haugbui_state_" + Machine.State.ToString().ToLowerInvariant();
        }
    }
}
