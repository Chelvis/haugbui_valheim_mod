#if DEBUG
using System;
using System.Collections;
using System.Linq;
using Haugbui.Diagnostics;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// Unattended run: valheim.exe -haugbui-autotest "Character" "World" [-haugbui-autotest-quit]. Selects the character
    /// and world from the main menu, waits for the player to spawn, runs every suite, writes the report and optionally quits.
    /// </summary>
    internal static class AutoTest
    {
        private const string Flag = "-haugbui-autotest";
        private const string QuitFlag = "-haugbui-autotest-quit";
        private const float MenuTimeout = 120f;
        private const float WorldTimeout = 300f;
        private const float SettleSeconds = 10f;

        public static void StartIfRequested(MonoBehaviour host)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(args, a => a.Equals(Flag, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            if (index + 2 >= args.Length)
            {
                Log.Error($"{Flag} needs a character and a world name.");
                return;
            }
            bool quit = args.Any(a => a.Equals(QuitFlag, StringComparison.OrdinalIgnoreCase));
            host.StartCoroutine(Run(args[index + 1], args[index + 2], quit));
        }

        private static IEnumerator Run(string character, string world, bool quit)
        {
            Log.Info($"Autotest: waiting for the main menu (character '{character}', world '{world}').");
            float deadline = Time.realtimeSinceStartup + MenuTimeout;
            while (Time.realtimeSinceStartup < deadline && !MenuReady()) yield return null;
            if (!MenuReady())
            {
                Finish("main menu not ready", quit);
                yield break;
            }
            yield return new WaitForSecondsRealtime(2f);

            FejdStartup menu = FejdStartup.instance;
            PlayerProfile profile = menu.m_profiles.FirstOrDefault(p =>
                p.GetFilename().Equals(character, StringComparison.OrdinalIgnoreCase)
                || p.GetName().Equals(character, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                Finish($"character '{character}' not found", quit);
                yield break;
            }
            menu.SetSelectedProfile(profile.GetFilename());
            menu.OnCharacterStart();
            yield return new WaitForSecondsRealtime(1f);

            World target = menu.FindWorld(world);
            if (target == null)
            {
                Finish($"world '{world}' not found", quit);
                yield break;
            }
            menu.m_world = target;
            menu.OnWorldStart();

            deadline = Time.realtimeSinceStartup + WorldTimeout;
            while (Time.realtimeSinceStartup < deadline && !InWorld()) yield return null;
            if (!InWorld())
            {
                Finish("world did not load", quit);
                yield break;
            }
            yield return new WaitForSecondsRealtime(SettleSeconds);

            bool done = false;
            SelfTestRunner.Finished += _ => done = true;
            if (!SelfTestRunner.Start(SelfTestRunner.Suites))
            {
                Finish("self-test could not start", quit);
                yield break;
            }
            while (!done) yield return null;
            Finish(null, quit);
        }

        private static bool MenuReady() =>
            FejdStartup.instance != null && FejdStartup.instance.m_profiles != null && FejdStartup.instance.m_profiles.Count > 0;

        private static bool InWorld() =>
            Player.m_localPlayer != null && ZNetScene.instance != null && !Player.m_localPlayer.IsTeleporting()
            && ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded();

        private static void Finish(string error, bool quit)
        {
            if (error != null) Log.Error("Autotest aborted: " + error);
            else Log.Info("Autotest finished.");
            if (quit) Application.Quit();
        }
    }
}
#endif
