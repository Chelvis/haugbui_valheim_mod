using System.IO;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    internal static class RepoPaths
    {
        public static string Root
        {
            get
            {
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Haugbui.sln")))
                    dir = dir.Parent;
                Assert.That(dir, Is.Not.Null, "Haugbui.sln not found above the test directory");
                return dir.FullName;
            }
        }

        public static string Translations => Path.Combine(Root, "src", "Haugbui", "Translations");

        public static string ReferenceManaged => Path.Combine(Root, ".reference", "Managed");
    }
}
