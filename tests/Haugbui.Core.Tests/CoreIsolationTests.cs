using System.Linq;
using Haugbui.Core.Progression;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class CoreIsolationTests
    {
        [Test]
        public void Core_references_no_game_or_modding_assemblies()
        {
            string[] references = typeof(ProgressionTable).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            Assert.That(references, Has.None.Match("^(UnityEngine|assembly_|Jotunn|BepInEx|0Harmony)"));
        }
    }
}
