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

        public static string ReferenceManaged
        {
            get
            {
                string inside = Path.Combine(Root, ".reference", "Managed");
                return Directory.Exists(inside) ? inside : Path.Combine(Root, "..", "haugbui-reference", "Managed");
            }
        }
    }
}
