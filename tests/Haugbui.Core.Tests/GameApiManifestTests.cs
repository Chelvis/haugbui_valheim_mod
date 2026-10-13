using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Haugbui.Core.GameApi;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class GameApiManifestTests
    {
        [Test]
        public void Manifest_matches_the_reference_game_assemblies()
        {
            if (!Directory.Exists(RepoPaths.ReferenceManaged))
                Assert.Ignore("Game assemblies not available in .reference/Managed");

            string[] dlls = Directory.GetFiles(RepoPaths.ReferenceManaged, "*.dll");
            using var context = new MetadataLoadContext(new PathAssemblyResolver(dlls), "mscorlib");
            var assemblies = new Dictionary<string, Assembly>();

            Type Resolve(string assemblyName, string typeName)
            {
                if (!assemblies.TryGetValue(assemblyName, out Assembly assembly))
                    assemblies[assemblyName] = assembly = context.LoadFromAssemblyPath(Path.Combine(RepoPaths.ReferenceManaged, assemblyName + ".dll"));
                return assembly.GetType(typeName);
            }

            GameApiReport report = GameApiChecker.Check(GameApiManifest.Members, Resolve);

            Assert.That(report.Missing.Select(m => m.ToString()), Is.Empty);
            Assert.That(report.CheckedCount, Is.EqualTo(GameApiManifest.Members.Count));
        }

        [Test]
        public void Missing_member_disables_only_its_feature()
        {
            var members = new[]
            {
                new ApiMember("x", typeof(string).FullName, "Length", MemberKind.Property, "A"),
                new ApiMember("x", typeof(string).FullName, "DoesNotExist", MemberKind.Method, "B"),
                new ApiMember("x", "No.Such.Type", "Anything", MemberKind.Field, "C"),
            };

            GameApiReport report = GameApiChecker.Check(members, (_, name) => Type.GetType(name));

            Assert.That(report.Passed, Is.False);
            Assert.That(report.IsFeatureAvailable("A"), Is.True);
            Assert.That(report.DisabledFeatures, Is.EquivalentTo(new[] { "B", "C" }));
        }

        [Test]
        public void Parameter_types_select_the_overload()
        {
            var match = new ApiMember("x", typeof(string).FullName, "IndexOf", MemberKind.Method, "A", "System.Char");
            var mismatch = new ApiMember("x", typeof(string).FullName, "IndexOf", MemberKind.Method, "B", "System.Guid");

            GameApiReport report = GameApiChecker.Check(new[] { match, mismatch }, (_, name) => Type.GetType(name));

            Assert.That(report.DisabledFeatures, Is.EquivalentTo(new[] { "B" }));
        }

        [Test]
        public void Private_members_of_base_types_are_found()
        {
            var member = new ApiMember("x", typeof(Derived).FullName, "_secret", MemberKind.Field, "A");

            GameApiReport report = GameApiChecker.Check(new[] { member }, (_, __) => typeof(Derived));

            Assert.That(report.Passed, Is.True);
        }

        private class Base
        {
#pragma warning disable CS0169
            private int _secret;
#pragma warning restore CS0169
        }

        private class Derived : Base
        {
        }
    }
}
