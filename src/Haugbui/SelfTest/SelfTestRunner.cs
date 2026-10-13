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
        public static readonly string[] Suites = { "basics", "structures", "flight", "transfers", "haugbui" };

        public static string OutputFolder => Path.Combine(BepInEx.Paths.BepInExRootPath, "haugbui_selftest");

        public static bool Running { get; private set; }
        public static event Action<SelfTestContext> Finished;

        public static bool Start(IEnumerable<string> suites)
        {
            if (Running || Player.m_localPlayer == null) return false;
            HaugbuiPlugin.Instance.StartCoroutine(Run(suites.ToList()));
            return true;
        }

        private static IEnumerator Run(List<string> suites)
        {
            Running = true;
            var stopwatch = Stopwatch.StartNew();
            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            var context = new SelfTestContext(OutputFolder);
            Log.Info($"Self-test started: {string.Join(", ", suites)}");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Haugbui self-test running…");

            foreach (string suite in suites)
            {
                IEnumerator body;
                switch (suite)
                {
                    case "basics": body = new BasicsSuite(context).Run(); break;
                    case "structures": body = new StructureSuite(context).Run(); break;
                    case "flight": body = new FlightSuite(context).Run(); break;
                    case "transfers": body = new TransferSuite(context).Run(); break;
                    case "haugbui": body = new HaugbuiSuite(context).Run(); break;
                    default:
                        context.Suite = suite;
                        context.Fail("unknown suite", suite);
                        continue;
                }
                yield return Guard(context, suite, body);
                Integration.TimeSource.Override = null;
                context.Cleanup();
                yield return SelfTestContext.WaitFrames(5);
            }

            context.WriteReport(stopwatch.Elapsed);
            int failed = context.Results.Count(r => r.Status == TestStatus.Fail);
            int passed = context.Results.Count(r => r.Status == TestStatus.Pass);
            string summary = $"Haugbui self-test finished: {(failed == 0 ? "PASS" : "FAIL")} ({passed} passed, {failed} failed)";
            Log.Info($"{summary}. Report: {Path.Combine(OutputFolder, "report.txt")}");
            Console.instance?.Print($"{summary}. Report: BepInEx/haugbui_selftest/report.txt");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, summary);
            Running = false;
            Finished?.Invoke(context);
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
                                       ", or all). Report and pictures in BepInEx/haugbui_selftest.";
        public override bool IsCheat => true;

        public override List<string> CommandOptionList() => SelfTestRunner.Suites.Concat(new[] { "all" }).ToList();

        protected override void Execute(string[] args)
        {
            IEnumerable<string> suites = args.Length == 0 || args.Contains("all") ? SelfTestRunner.Suites : args;
            Print(SelfTestRunner.Start(suites)
                ? "Self-test running; stand still until the result appears on screen (1–2 minutes)."
                : "Self-test already running or no player.");
        }
    }
}
#endif
