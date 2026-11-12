#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// In-game integration tests. Run on a throwaway test world: they build, reload and destroy objects near the
    /// player and remove everything they created at the end.
    /// </summary>
    internal static class SelfTestRunner
    {
        public static readonly string[] Suites = { "basics", "structures", "flight", "transfers", "haugbui", "storage", "chests", "harvest", "tribute", "production", "territory", "stations", "piles", "sleep", "combat", "world", "backup", "integrity", "torture", "perf", "away" };

        /// <summary>Seconds each suite took on the 2026-11-03 full run (20 min 44 s). A failure that waits out a timeout is included, so a clean run can be shorter.</summary>
        private static readonly Dictionary<string, int> ExpectedSeconds = new Dictionary<string, int>
        {
            { "basics", 5 }, { "structures", 25 }, { "flight", 5 }, { "transfers", 5 }, { "haugbui", 74 },
            { "storage", 64 }, { "chests", 245 }, { "harvest", 174 }, { "tribute", 19 }, { "production", 159 },
            { "territory", 50 }, { "stations", 239 }, { "piles", 180 }, { "sleep", 9 }, { "combat", 159 },
            { "world", 3 }, { "backup", 2 }, { "integrity", 2 }, { "torture", 3 }, { "perf", 2 }, { "away", 120 },
        };

        internal static string Estimate(IEnumerable<string> suites)
        {
            int seconds = 0;
            foreach (string suite in suites)
                if (ExpectedSeconds.TryGetValue(suite, out int known)) seconds += known;
            if (seconds < 90) return $"about {Math.Max(1, seconds)} seconds";
            int minutes = (seconds + 59) / 60;
            return $"about {minutes} minutes";
        }

        public const string AndExit = "and_exit";

        public static string OutputFolder => Path.Combine(BepInEx.Paths.BepInExRootPath, "haugbui_selftest");

        public static bool Running { get; private set; }
        public static event Action<SelfTestContext> Finished;

        public static bool Start(IEnumerable<string> suites, bool exitAfter = false)
        {
            if (Running || Player.m_localPlayer == null) return false;
            HaugbuiPlugin.Instance.StartCoroutine(Run(suites.ToList(), exitAfter));
            return true;
        }

        private static IEnumerator Run(List<string> suites, bool exitAfter)
        {
            Running = true;
            var stopwatch = Stopwatch.StartNew();
            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            var context = new SelfTestContext(OutputFolder);
            Log.Info($"Self-test started: {string.Join(", ", suites)}");
            Integration.WorldSafety.SuppressPopup = true;
            for (int i = 0; i < 5 && UnifiedPopup.IsVisible(); i++) UnifiedPopup.Pop();
            Integration.WorldSafety.RecordLive();
            string estimate = Estimate(suites);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Haugbui self-test running, {estimate}.");
            if (Console.instance != null && Console.instance.m_chatWindow != null)
                Console.instance.m_chatWindow.gameObject.SetActive(false);

            yield return context.ClearPresence();
            Registry.WorldRegistry.HideLoadedWorld();
            bool godMode = Player.m_localPlayer != null && Player.m_localPlayer.InGodMode();
            Player.m_localPlayer?.SetGodMode(true);
            if (context.PresenceClear) HaugbuiPlugin.Instance.StartCoroutine(KeepAreaClear(context));
            foreach (string suite in context.PresenceClear ? suites : new List<string>())
            {
                IEnumerator body;
                switch (suite)
                {
                    case "basics": body = new BasicsSuite(context).Run(); break;
                    case "structures": body = new StructureSuite(context).Run(); break;
                    case "flight": body = new FlightSuite(context).Run(); break;
                    case "transfers": body = new TransferSuite(context).Run(); break;
                    case "haugbui": body = new HaugbuiSuite(context).Run(); break;
                    case "storage": body = new StorageSuite(context).Run(); break;
                    case "chests": body = new ChestSuite(context).Run(); break;
                    case "harvest": body = new HarvestSuite(context).Run(); break;
                    case "tribute": body = new TributeSuite(context).Run(); break;
                    case "production": body = new ProductionSuite(context).Run(); break;
                    case "territory": body = new TerritorySuite(context).Run(); break;
                    case "stations": body = new StationsSuite(context).Run(); break;
                    case "piles": body = new PilesSuite(context).Run(); break;
                    case "sleep": body = new SleepSuite(context).Run(); break;
                    case "combat": body = new CombatSuite(context).Run(); break;
                    case "world": body = new WorldSuite(context).Run(); break;
                    case "backup": body = new BackupSuite(context).Run(); break;
                    case "integrity": body = new IntegritySuite(context).Run(); break;
                    case "torture": body = new TortureSuite(context).Run(); break;
                    case "perf": body = new PerfSuite(context).Run(); break;
                    case "away": body = new AwaySuite(context).Run(); break;
                    default:
                        context.Suite = suite;
                        context.Fail("unknown suite", suite);
                        continue;
                }
                // Each suite starts with no servant slots and gives the player's slots back afterwards (HB-137 is permanent).
                long player = Player.m_localPlayer.GetPlayerID();
                Dictionary<string, string> parked = Integration.ServantRegistry.Park(player);
                Creature.TombController.TributeSuspendedForTests = suite != "tribute";
                // Older suites place several tombs close together; only the territory suite uses the HB-T-110 rules.
                Structures.PlacementCheck.SameOwnerSpacing = suite == "territory" ? HaugbuiPlugin.Instance.Settings.TombSpacingSameOwner.Value : 0f;
                Brain.WorkContext.TerritorySuspendedForTests = suite != "territory";
                var suiteWatch = Stopwatch.StartNew();
                yield return Guard(context, suite, body);
                Log.Info($"Self-test suite {suite} took {suiteWatch.Elapsed.TotalSeconds:0}s (estimate {ExpectedSeconds[suite]}s).");
                Integration.TimeSource.Override = null;
                Creature.TombController.TributeSuspendedForTests = false;
                Structures.PlacementCheck.SameOwnerSpacing = HaugbuiPlugin.Instance.Settings.TombSpacingSameOwner.Value;
                Brain.WorkContext.TerritorySuspendedForTests = false;
                context.Cleanup();
                SelfTestContext.DismissPopups();
                yield return SelfTestContext.WaitFrames(5);
                SelfTestContext.DismissPopups();
                Integration.WorldSafety.RecordLive();
                Integration.ServantRegistry.Unpark(player, parked);
            }
            Registry.WorldRegistry.RestoreLoadedWorld();
            Integration.WorldSafety.SuppressPopup = false;
            if (Player.m_localPlayer != null) Player.m_localPlayer.SetGodMode(godMode);

            context.WriteReport(stopwatch.Elapsed);
            int failed = context.Results.Count(r => r.Status == TestStatus.Fail);
            int passed = context.Results.Count(r => r.Status == TestStatus.Pass);
            string summary = $"Haugbui self-test finished: {(failed == 0 ? "PASS" : "FAIL")} ({passed} passed, {failed} failed)";
            Log.Info($"{summary}. Report: {Path.Combine(OutputFolder, "report.txt")}");
            Console.instance?.Print($"{summary}. Report: BepInEx/haugbui_selftest/report.txt");
            string result = $"{summary}\n\nReport: BepInEx/haugbui_selftest/report.txt";
            if (!exitAfter && UnifiedPopup.IsAvailable())
                UnifiedPopup.Push(new WarningPopup("Haugbui self-test", result, UnifiedPopup.Pop, localizeText: false));
            else
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, summary);
            Running = false;
            Finished?.Invoke(context);
            if (!exitAfter) yield break;
            Log.Info("Self-test: closing the game (and_exit).");
            // One realtime second so report.txt and LogOutput.log finish writing before the process dies.
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        private const float MonsterClearRadius = 240f;

        /// <summary>Monsters that wander in during the run would fight the Haugbuis under test.</summary>
        private static IEnumerator KeepAreaClear(SelfTestContext context)
        {
            while (Running)
            {
                if (Player.m_localPlayer != null)
                {
                    int removed = context.ClearMonsters(Player.m_localPlayer.transform.position, MonsterClearRadius);
                    if (removed > 0)
                    {
                        context.MonstersCleared += removed;
                        Log.Info($"Self-test: removed {removed} hostile creature(s) within {MonsterClearRadius} m.");
                    }
                }
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>Runs a suite step by step so an exception fails that suite instead of stopping the whole run.</summary>
        private static IEnumerator Guard(SelfTestContext context, string suite, IEnumerator body)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                bool moved;
                try
                {
                    moved = current.MoveNext();
                }
                catch (Exception e)
                {
                    context.Suite = suite;
                    context.Fail("suite crashed", e.ToString());
                    Log.Error($"Self-test suite {suite} crashed: {e}");
                    yield break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                if (current.Current is IEnumerator nested) stack.Push(nested);
                else yield return current.Current;
            }
        }
    }

    internal sealed class SelfTestCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_selftest";
        public override string Help => "Haugbui: run in-game tests on a TEST world (" + string.Join(", ", SelfTestRunner.Suites) +
                                       ", or all). Optional " + SelfTestRunner.AndExit + " closes the game when the report is written. " +
                                       "Report and pictures in BepInEx/haugbui_selftest.";
        public override bool IsCheat => true;

        public override List<string> CommandOptionList() =>
            SelfTestRunner.Suites.Concat(new[] { "all", SelfTestRunner.AndExit }).ToList();

        protected override void Execute(string[] args)
        {
            bool exitAfter = args.Any(a => a.Equals(SelfTestRunner.AndExit, StringComparison.OrdinalIgnoreCase));
            string[] selected = args.Where(a => !a.Equals(SelfTestRunner.AndExit, StringComparison.OrdinalIgnoreCase)).ToArray();
            IEnumerable<string> suites = selected.Length == 0 || selected.Contains("all") ? SelfTestRunner.Suites : selected;
            string running = SelfTestRunner.Estimate(suites);
            Print(SelfTestRunner.Start(suites, exitAfter)
                ? exitAfter
                    ? "Self-test running; the game will close when it finishes. Report: BepInEx/haugbui_selftest/report.txt"
                    : $"Self-test running; stand still until the result appears on screen ({running})."
                : "Self-test already running or no player.");
        }
    }
}
#endif
