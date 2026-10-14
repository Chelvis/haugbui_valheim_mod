using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>
    /// HB-T-032..036, HB-T-093, HB-T-025: the tomb owns its Haugbui. Decides presence, spawns and recalls the creature,
    /// keeps health, carried items and overflow in the tomb ZDO. Logic runs only on the tomb's ZDO owner.
    /// </summary>
    public sealed class TombController : MonoBehaviour
    {
        private const float TickSeconds = 0.5f;
        private const float EmergeSeconds = 1.5f;
        private const float EnterSeconds = 0.6f;
        private const float IdleSpeed = 1f;
        private const float IdleHeight = 2.2f;
        private const float ResumeMaxDistanceFactor = 1.5f;

        private static readonly List<TombController> Instances = new List<TombController>();

        private HaugbuiStructure _tomb;
        private PersistentSlot<List<ItemStack>> _carriedSlot;
        private PersistentSlot<List<ItemStack>> _overflowSlot;
        private float _nextTick;
        private bool _destroyed;
        private bool _resumeChecked;
        private int _lastSavedHash;
        private Core.Scheduling.TickHandle _planTick;

        public static IReadOnlyList<TombController> All => Instances;

        internal HaugbuiStateMachine Machine { get; private set; }
        internal CarriedInventory Carried { get; private set; }
        internal List<ItemStack> Overflow { get; private set; } = new List<ItemStack>();
        internal HaugbuiCreature Creature { get; private set; }
        internal HaugbuiStructure Structure => _tomb;
        internal HaugbuiWorker Worker { get; private set; }
        internal string Guid => _tomb.Guid;

        /// <summary>The creature is out and this peer runs its logic.</summary>
        internal bool CanWork => !_destroyed && State != null && IsOwner && Creature != null && Machine.IsOut;
        internal TombState State => _tomb != null ? _tomb.TombState : null;
        internal bool IsOwner => _tomb != null && _tomb.View != null && _tomb.View.IsValid() && _tomb.View.IsOwner();
        internal HaugbuiAttributes Attributes => HaugbuiPlugin.Instance.Settings.ProgressionTable.ForLevel(_tomb.Progress.Level);
        internal bool FullTime => _tomb.Progress.Has(TombAbilities.FullTime);
        internal bool DarkAura => FullTime;
        internal Vector3 Home => transform.position + Vector3.up * 0.8f;

        internal double CooldownRemaining
        {
            get
            {
                TombState state = State;
                if (state == null || Machine.State != HaugbuiState.Dead) return 0;
                return PresencePolicy.CooldownRemaining(TimeSource.Current.Now, state.DeathTime,
                    HaugbuiPlugin.Instance.Settings.CooldownSeconds.Value);
            }
        }

        private void Awake()
        {
            _tomb = GetComponent<HaugbuiStructure>();
            Machine = new HaugbuiStateMachine(() => TimeSource.Current.Now);
            Carried = new CarriedInventory(1, 1);
            Worker = new HaugbuiWorker(this);
            if (_tomb.Zdo == null) return;

            var store = new ZdoBlobStore(_tomb.Zdo);
            _carriedSlot = new PersistentSlot<List<ItemStack>>(store, ZdoKeys.Carried, ItemStacksSerializer.Instance);
            _overflowSlot = new PersistentSlot<List<ItemStack>>(store, ZdoKeys.Overflow, ItemStacksSerializer.Instance);
            Carried.Restore(Refresh(_carriedSlot.Load() ?? new List<ItemStack>()));
            Overflow = Refresh(_overflowSlot.Load() ?? new List<ItemStack>());
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
            if (_destroyed || State == null || !IsOwner || CreaturePrefab.Prefab == null) return;
            if (!_resumeChecked)
            {
                _resumeChecked = true;
                ResumeAfterLoad();
            }
            StepState();
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickSeconds;
            Tick();
        }

        /// <summary>Forces the next presence decision now (self-test and panel).</summary>
        internal void TickNow()
        {
            _nextTick = 0;
            if (!_destroyed && State != null && IsOwner) Tick();
        }

        private PresenceInput Input()
        {
            ITimeSource time = TimeSource.Current;
            Config.HaugbuiConfig settings = HaugbuiPlugin.Instance.Settings;
            return new PresenceInput
            {
                IsNight = time.IsNight,
                DayFraction = time.DayFraction,
                DayLengthSeconds = time.DayLengthSeconds,
                FullTime = FullTime,
                Resting = State.Has(TombToggles.Resting),
                Dead = Machine.State == HaugbuiState.Dead,
                Now = time.Now,
                DeathTime = State.DeathTime,
                CooldownSeconds = settings.CooldownSeconds.Value,
                DawnReturnLeadSeconds = settings.DawnReturnLeadSeconds.Value,
            };
        }

        private void ResumeAfterLoad()
        {
            Creature = HaugbuiCreature.Find(_tomb.Guid);
            if (Creature != null) Adopt(Creature);
            if (!Machine.IsOut || Creature != null) return;

            if (PresencePolicy.ShouldBeOut(Input()))
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
                    Emerge();
                    break;
                case PresenceDecision.Return:
                    if (Machine.RequestReturn() && Creature != null) Creature.MoveTo(Home);
                    break;
                case PresenceDecision.StayOut:
                    if (Machine.ReturnPending) Machine.CancelReturn();
                    // E.g. the Altar was built or Rest turned off while it flew home.
                    if (Machine.State == HaugbuiState.Returning && Machine.TryEnter(HaugbuiState.Idle)) Creature?.Stop();
                    break;
            }

            if (Machine.IsOut && Creature != null)
            {
                SyncHealth(attributes);
                Vector3 p = Creature.transform.position;
                state.PositionX = p.x;
                state.PositionY = p.y;
                state.PositionZ = p.z;
                Creature.Visuals?.SetDarkAura(DarkAura);
            }
            state.CreatureState = (int)HaugbuiStateMachine.Persisted(Machine.State);
            SaveStateIfChanged();
        }

        private void StepState()
        {
            if (Machine.IsOut && Creature == null)
            {
                Worker.ReleaseAll();
                // Destroyed by something else than death (e.g. removed with a command): count it as back inside.
                Machine.Restore(HaugbuiState.Dormant);
                EnteredTomb();
                return;
            }
            if (Creature == null) return;

            bool working = Worker.Step();
            Creature.SetState(Machine.State);
            switch (Machine.State)
            {
                case HaugbuiState.Emerging:
                    if (Machine.TimeInState >= EmergeSeconds) Machine.TryEnter(HaugbuiState.Idle);
                    break;
                case HaugbuiState.Idle:
                    if (working) break;
                    Creature.MaxSpeed = Mathf.Min(IdleSpeed, Attributes.MaxSpeed);
                    if (!Creature.HasTarget || Creature.Arrived) Creature.MoveTo(IdlePoint());
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

        private Vector3 IdlePoint()
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
            Machine.TryEnter(HaugbuiState.Emerging);
            Creature.SetState(Machine.State);
            State.CreatureState = (int)HaugbuiState.Idle;
            SaveStateIfChanged();
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
            Creature.Visuals?.SetDarkAura(DarkAura);
            if (emergeEffect) PlayEffect(position);
        }

        private void Adopt(HaugbuiCreature creature)
        {
            creature.Died -= OnCreatureDied;
            creature.Died += OnCreatureDied;
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
            if (Creature != null) PlayEffect(Creature.transform.position);
            Creature = null;
            Machine.Kill();
            double now = TimeSource.Current.Now;
            State.DeathTime = now;
            State.Health = 0;
            State.HealthTime = now;
            State.CreatureState = (int)HaugbuiState.Dead;
            DepositCarried();
            SaveStateIfChanged();
            Log.Info($"Tomb {_tomb.Guid}: Haugbui died; returns in {PresencePolicy.FormatCountdown(HaugbuiPlugin.Instance.Settings.CooldownSeconds.Value)} (world time).");
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

            State.Set(toggle, on);
            if (toggle == TombToggles.Combat && on) State.Set(TombToggles.CombatAcknowledged, true);
            SaveStateIfChanged();
            TickNow();
            return true;
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

            if (Creature != null)
            {
                PlayEffect(Creature.transform.position);
                ZNetScene.instance.Destroy(Creature.gameObject);
                Creature = null;
            }

            foreach (HaugbuiStructure upgrade in StructureRegistry.Upgrades.Where(u => u.BoundTombGuid == _tomb.Guid && u.View.IsOwner()).ToList())
                upgrade.Zdo.Set(ZdoKeys.TombGuid, string.Empty);
            StructureRegistry.MarkDirty();
            Log.Info($"Tomb {_tomb.Guid} destroyed: {dropped} carried/overflow items dropped, upgrades unbound.");
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
            double cooldown = CooldownRemaining;
            if (cooldown > 0) return Localization.instance.Localize("$haugbui_status_cooldown", PresencePolicy.FormatCountdown(cooldown));
            if (Machine.State == HaugbuiState.Dead) return "$haugbui_status_waiting_night";
            if (State.Has(TombToggles.Resting)) return "$haugbui_status_resting";
            return "$haugbui_state_" + Machine.State.ToString().ToLowerInvariant();
        }
    }
}
