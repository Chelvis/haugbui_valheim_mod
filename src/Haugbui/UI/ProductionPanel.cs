using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Haugbui.Brain;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration.StationAdapters;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>HB-T-060: per-station job settings (enable, recipe, finite/infinite, target) and progress. Shift+E on the station.</summary>
    internal static class ProductionPanel
    {
        private const float Width = 520f;
        private const float Height = 470f;

        private static GameObject _root;
        private static Text _title;
        private static Text _owner;
        private static Dropdown _recipes;
        private static Dropdown _mode;
        private static InputField _target;
        private static Text _progress;
        private static Text _status;
        private static Text _enableLabel;
        private static MonoBehaviour _station;
        private static float _openedAt;
        private static readonly List<StationRecipe> Shown = new List<StationRecipe>();

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static MonoBehaviour Current => IsOpen ? _station : null;
        internal static IReadOnlyList<StationRecipe> Recipes => Shown;

        public static void Open(MonoBehaviour station)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || station == null) return;
            if (_root == null) Build();
            _station = station;
            _openedAt = Time.time;
            LoadRecipes();
            StationJobState job = StationJobs.Read(station);
            int selected = job != null ? Shown.FindIndex(r => r.Input == job.InputPrefab && r.Output == job.OutputPrefab) : -1;
            _recipes.value = Math.Max(0, selected);
            _recipes.RefreshShownValue();
            _mode.value = job != null && job.Mode == JobMode.Infinite || FuelOnly ? 1 : 0;
            _mode.RefreshShownValue();
            _target.text = job != null && job.Mode == JobMode.Finite ? job.Target.ToString() : "";
            Refresh();
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                GUIManager.BlockInput(true);
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            _root.SetActive(false);
            GUIManager.BlockInput(false);
            _station = null;
        }

        /// <summary>Called every frame by the plugin.</summary>
        public static void Update()
        {
            if (!IsOpen) return;
            if (_station == null || Player.m_localPlayer == null || Input.GetKeyDown(KeyCode.Escape)
                || (Time.time - _openedAt > 0.2f && !Typing() && Input.GetKeyDown(KeyCode.E)))
            {
                Close();
                return;
            }
            if (Time.frameCount % 15 == 0) Refresh();
        }

        private static bool FuelOnly => _station != null && StationAdapterRegistry.For(_station)?.Kind(_station) == StationKind.FuelOnly;

        /// <summary>Self-test hook: the same path as the Save button. Null when nothing was saved.</summary>
        internal static bool Apply(bool enabled, StationRecipe recipe, JobMode mode, int target)
        {
            if (_station == null || recipe == null) return false;
            if (FuelOnly) mode = JobMode.Infinite;
            if (mode == JobMode.Finite && target <= 0) return false;
            bool saved = StationJobs.Edit(_station, job =>
            {
                ProductionJob.Edit(job, enabled, recipe.Input, recipe.Output, mode, mode == JobMode.Finite ? target : 0);
                return true;
            });
            if (saved) Log.Debug($"Station {StationAdapterRegistry.IdOf(_station)}: job {(enabled ? "on" : "off")} {recipe.Key} {mode} {target}");
            Refresh();
            return saved;
        }

        private static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            _title = gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -35f),
                gui.AveriaSerifBold, 26, gui.ValheimOrange, true, Color.black, Width - 40f, 40f, false).GetComponent<Text>();
            _owner = Line(gui, -72f, 15, new Color(0.8f, 0.8f, 0.8f));

            Label(gui, "$haugbui_job_recipe", -115f);
            _recipes = gui.CreateDropDown(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330f, -115f), 16, 300f, 32f)
                .GetComponent<Dropdown>();
            Label(gui, "$haugbui_job_mode", -160f);
            _mode = gui.CreateDropDown(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(255f, -160f), 16, 150f, 32f)
                .GetComponent<Dropdown>();
            _mode.ClearOptions();
            _mode.AddOptions(new List<string>
            {
                Localization.instance.Localize("$haugbui_job_finite"),
                Localization.instance.Localize("$haugbui_job_infinite"),
            });
            _target = gui.CreateInputField(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(410f, -160f),
                InputField.ContentType.IntegerNumber, Localization.instance.Localize("$haugbui_job_target"), 16, 140f, 32f).GetComponent<InputField>();

            _progress = Line(gui, -215f, 17, Color.white);
            _status = Line(gui, -260f, 16, new Color(0.9f, 0.75f, 0.4f));

            GameObject enable = Button(gui, "", -170f, OnToggleEnabled);
            _enableLabel = enable.GetComponentInChildren<Text>();
            Button(gui, "$haugbui_job_save", 0f, OnSave);
            Button(gui, "$haugbui_panel_close", 170f, Close);
            _root.SetActive(false);
        }

        private static Text Line(GUIManager gui, float y, int size, Color color) =>
            gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, y),
                gui.AveriaSerifBold, size, color, true, Color.black, Width - 50f, 40f, false).GetComponent<Text>();

        private static void Label(GUIManager gui, string token, float y) =>
            gui.CreateText(Localization.instance.Localize(token), _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(95f, y), gui.AveriaSerifBold, 17, Color.white, true, Color.black, 140f, 30f, false);

        private static GameObject Button(GUIManager gui, string label, float x, Action action)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(label), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 45f), 155f, 40f);
            button.GetComponent<Button>().onClick.AddListener(() => action());
            return button;
        }

        /// <summary>HB-116: only conversions whose input and output the player has discovered.</summary>
        private static void LoadRecipes()
        {
            Shown.Clear();
            Player player = Player.m_localPlayer;
            IStationAdapter adapter = StationAdapterRegistry.For(_station);
            if (adapter == null || player == null) return;
            Shown.AddRange(adapter.Recipes(_station).Where(r => ProductionGate.Known(player, r)));
            _recipes.ClearOptions();
            _recipes.AddOptions(Shown.Select(RecipeName).ToList());
            _title.text = Localization.instance.Localize(adapter.Name(_station));
        }

        private static string RecipeName(StationRecipe recipe)
        {
            string output = recipe.OutputItem != null ? Localization.instance.Localize(recipe.OutputItem.m_itemData.m_shared.m_name) : "";
            if (recipe.InputItem == null)
                return output.Length > 0 ? output : Localization.instance.Localize("$haugbui_job_fuel_only");
            return Localization.instance.Localize(recipe.InputItem.m_itemData.m_shared.m_name) + " → " + output;
        }

        private static StationRecipe SelectedRecipe() =>
            _recipes.value >= 0 && _recipes.value < Shown.Count ? Shown[_recipes.value] : null;

        private static void Refresh()
        {
            if (_station == null) return;
            StationJobState job = StationJobs.Read(_station);
            bool enabled = job != null && job.Enabled;
            _enableLabel.text = Localization.instance.Localize(enabled ? "$haugbui_job_disable" : "$haugbui_job_enable");
            _target.interactable = !FuelOnly;
            _mode.interactable = !FuelOnly;
            _owner.text = Localization.instance.Localize(OwnerText(job));
            _progress.text = job != null && job.Enabled ? ProgressText(job) : "";
            // HB-121: items still cooking when it goes home may burn, as for a player.
            if (StationAdapterRegistry.For(_station)?.Kind(_station) == StationKind.Cooking)
                _progress.text += (_progress.text.Length > 0 ? "\n" : "") + Localization.instance.Localize("$haugbui_job_cooking_warning");
            _status.text = Localization.instance.Localize(StatusText(_station, job));
        }

        private static string ProgressText(StationJobState job)
        {
            string remaining = job.Mode == JobMode.Finite
                ? Localization.instance.Localize("$haugbui_job_remaining", ProductionJob.Remaining(job).ToString())
                : Localization.instance.Localize("$haugbui_job_infinite");
            return Localization.instance.Localize("$haugbui_job_progress", job.Supplied.ToString(), job.Produced.ToString()) + "   " + remaining;
        }

        private static string OwnerText(StationJobState job)
        {
            if (job == null || string.IsNullOrEmpty(job.TombGuid)) return "$haugbui_job_no_owner";
            TombController tomb = TombController.All.FirstOrDefault(t => t != null && t.Guid == job.TombGuid);
            return tomb != null ? "$haugbui_job_owner " + tomb.ServantName : "$haugbui_job_owner_away";
        }

        /// <summary>Panel and hover: why the job does or does not run (HB-115/119/120, HB-T-069).</summary>
        internal static string StatusText(MonoBehaviour station, StationJobState job)
        {
            Vector3 position = station.transform.position;
            List<TombController> near = TombController.All
                .Where(t => t != null && t.State != null && Vector3.Distance(t.transform.position, position) <= t.Attributes.Radius).ToList();
            if (near.Count == 0) return "$haugbui_job_no_tomb";
            if (!near.Any(ProductionGate.Unlocked))
                return Localization.instance.Localize("$haugbui_job_needs_level", HaugbuiPlugin.Instance.Settings.ProductionUnlockLevel.Value.ToString());
            if (!near.Any(ProductionGate.Enabled)) return "$haugbui_job_toggle_off";
            if (job == null || !job.Enabled) return "$haugbui_job_disabled";
            string blocked = StationAdapterRegistry.For(station)?.Read(station).Blocked;
            return ProductionStatus.Get(StationAdapterRegistry.IdOf(station)) ?? blocked ?? "$haugbui_job_idle";
        }

        private static void OnToggleEnabled()
        {
            StationJobState job = StationJobs.Read(_station);
            bool configured = job != null && (job.Enabled || job.InputPrefab.Length > 0 || job.OutputPrefab.Length > 0);
            if (configured)
            {
                StationJobs.Edit(_station, j =>
                {
                    j.Enabled = !j.Enabled;
                    return true;
                });
                Refresh();
                return;
            }
            OnSave(true);
        }

        private static void OnSave() => OnSave(StationJobs.Read(_station)?.Enabled ?? true);

        private static void OnSave(bool enabled)
        {
            int.TryParse(_target.text, out int target);
            var mode = _mode.value == 1 ? JobMode.Infinite : JobMode.Finite;
            if (!Apply(enabled, SelectedRecipe(), mode, target))
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize(SelectedRecipe() == null ? "$haugbui_job_err_recipe" : "$haugbui_job_err_target"));
        }
    }

    /// <summary>HB-T-060/120: Shift+E on a supported station's switch opens the panel; its hover shows the job status.</summary>
    [HarmonyPatch]
    internal static class ProductionPanelPatches
    {
        private static readonly Dictionary<Switch, MonoBehaviour> StationBySwitch = new Dictionary<Switch, MonoBehaviour>();

        internal static MonoBehaviour StationOf(Switch sw)
        {
            if (sw == null) return null;
            if (StationBySwitch.TryGetValue(sw, out MonoBehaviour station) && station != null) return station;
            station = StationAdapterRegistry.StationOf(sw);
            if (station != null) StationBySwitch[sw] = station;
            return station;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
        private static bool OpenOnAltInteract(Switch __instance, bool hold, bool alt, ref bool __result)
        {
            if (!alt || hold) return true;
            try
            {
                MonoBehaviour station = StationOf(__instance);
                if (station == null) return true;
                // Same rule as using the station; otherwise vanilla refuses with its own message.
                if (!PrivateArea.CheckAccess(station.transform.position, 0f, flash: true)) return true;
                ProductionPanel.Open(station);
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("production-interact", $"Production panel: {e}");
                return true;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Interact))]
        private static bool OpenOnFermenter(Fermenter __instance, bool hold, bool alt, ref bool __result) =>
            OpenOnAlt(__instance, hold, alt, ref __result);

        [HarmonyPrefix, HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
        private static bool OpenOnCookingStation(CookingStation __instance, bool hold, bool alt, ref bool __result) =>
            OpenOnAlt(__instance, hold, alt, ref __result);

        [HarmonyPostfix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
        private static void FermenterHover(Fermenter __instance, ref string __result) => AppendHover(__instance, ref __result);

        [HarmonyPostfix, HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
        private static void CookingHover(CookingStation __instance, ref string __result) => AppendHover(__instance, ref __result);

        private static bool OpenOnAlt(MonoBehaviour station, bool hold, bool alt, ref bool result)
        {
            if (!alt || hold || StationAdapterRegistry.For(station) == null) return true;
            try
            {
                if (!PrivateArea.CheckAccess(station.transform.position, 0f, flash: true)) return true;
                ProductionPanel.Open(station);
                result = true;
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("production-interact", $"Production panel: {e}");
                return true;
            }
        }

        private static void AppendHover(MonoBehaviour station, ref string result)
        {
            try
            {
                if (StationAdapterRegistry.For(station) != null && !string.IsNullOrEmpty(result)) result += HoverLine(station);
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("production-hover", $"Production hover: {e}");
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
        private static void AddHoverLine(Switch __instance, ref string __result)
        {
            try
            {
                MonoBehaviour station = StationOf(__instance);
                if (station == null) return;
                __result += HoverLine(station);
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("production-hover", $"Production hover: {e}");
            }
        }

        internal static string HoverLine(MonoBehaviour station)
        {
            StationJobState job = StationJobs.Read(station);
            string line = "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $haugbui_job_open";
            if (job != null && job.Enabled)
            {
                string progress = job.Mode == JobMode.Finite ? $" {job.Produced}/{job.Target}" : "";
                line += "\n$haugbui_job_hover" + progress + ": " + ProductionPanel.StatusText(station, job);
            }
            return Localization.instance.Localize(line);
        }
    }
}
