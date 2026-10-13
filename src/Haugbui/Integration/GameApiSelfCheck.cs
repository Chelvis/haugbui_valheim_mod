using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.GameApi;
using Haugbui.Diagnostics;

namespace Haugbui.Integration
{
    internal static class GameApiSelfCheck
    {
        public static GameApiReport Report { get; private set; } = new GameApiReport(0, Array.Empty<ApiMember>());

        public static GameApiReport Run()
        {
            Report = GameApiChecker.Check(MembersToCheck(), (assembly, type) => Type.GetType($"{type}, {assembly}"));

            if (Report.Passed)
            {
                Log.Info($"Game API self-check passed ({Report.CheckedCount} members).");
            }
            else
            {
                foreach (ApiMember member in Report.Missing)
                    Log.Error($"Game API self-check: missing {member} (feature {member.Feature} disabled).");
                Log.Error($"Game API self-check failed: {Report.Missing.Count} of {Report.CheckedCount} members missing. " +
                          $"Disabled features: {string.Join(", ", Report.DisabledFeatures)}.");
            }
            return Report;
        }

        private static IEnumerable<ApiMember> MembersToCheck()
        {
            IEnumerable<ApiMember> members = GameApiManifest.Members;
#if DEBUG
            // Lets a tester simulate a game update that removed a member of the given feature.
            string fakeFeature = Environment.GetEnvironmentVariable("HAUGBUI_FAKE_MISSING_FEATURE");
            if (!string.IsNullOrEmpty(fakeFeature))
                members = members.Concat(new[] { new ApiMember("assembly_valheim", "ZNet", "HaugbuiFakeMember", MemberKind.Method, fakeFeature) });
#endif
            return members;
        }

        public static bool IsAvailable(string feature) => Report.IsFeatureAvailable(feature);
    }
}
