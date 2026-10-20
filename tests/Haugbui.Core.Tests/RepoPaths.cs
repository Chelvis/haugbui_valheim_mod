using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    internal static class RepoPaths
    {
        public static string Root
        {
            get
            {
                // The build may live outside the source tree (HAUGBUI_BUILD_ROOT), so the build records where it came from.
                string recorded = typeof(RepoPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "RepoRoot")?.Value;
                if (recorded != null && File.Exists(Path.Combine(recorded, "Haugbui.sln"))) return recorded;

                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Haugbui.sln")))
                    dir = dir.Parent;
                Assert.That(dir, Is.Not.Null, "Haugbui.sln not found above the test directory");
                return dir.FullName;
            }
        }

        public static string Translations => Path.Combine(Root, "src", "Haugbui", "Translations");

        /// <summary>
        /// .reference/Managed, ../haugbui-reference/Managed, then the game install the build used (VALHEIM_MANAGED from
        /// Directory.Build.props/Environment.props) or the VALHEIM_MANAGED / VALHEIM_INSTALL environment variables.
        /// </summary>
        public static string ReferenceManaged
        {
            get
            {
                string recorded = typeof(RepoPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "ValheimManaged")?.Value;
                string install = System.Environment.GetEnvironmentVariable("VALHEIM_INSTALL");
                string[] candidates =
                {
                    Path.Combine(Root, ".reference", "Managed"),
                    Path.Combine(Root, "..", "haugbui-reference", "Managed"),
                    recorded,
                    System.Environment.GetEnvironmentVariable("VALHEIM_MANAGED"),
                    string.IsNullOrEmpty(install) ? null : Path.Combine(install, "valheim_Data", "Managed"),
                };
                string found = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "assembly_valheim.dll")));
                return found ?? candidates[0];
            }
        }
    }
}
