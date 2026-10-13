using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class LocalizationParityTests
    {
        private const string English = "English";
        private const string PortugueseBrazilian = "Portuguese_Brazilian";

        private static Dictionary<string, string> Load(string language)
        {
            string path = Path.Combine(RepoPaths.Translations, language, "haugbui.json");
            Assert.That(File.Exists(path), Is.True, path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }

        [Test]
        public void English_and_portuguese_have_the_same_keys()
        {
            Dictionary<string, string> en = Load(English);
            Dictionary<string, string> pt = Load(PortugueseBrazilian);

            Assert.That(en.Keys.Except(pt.Keys), Is.Empty, "missing in Portuguese_Brazilian");
            Assert.That(pt.Keys.Except(en.Keys), Is.Empty, "missing in English");
        }

        [TestCase(English)]
        [TestCase(PortugueseBrazilian)]
        public void Keys_are_prefixed_and_values_not_empty(string language)
        {
            foreach (KeyValuePair<string, string> entry in Load(language))
            {
                Assert.That(entry.Key, Does.Match("^haugbui_[a-z0-9_]+$"), entry.Key);
                Assert.That(string.IsNullOrWhiteSpace(entry.Value), Is.False, entry.Key);
            }
        }

        [Test]
        public void Only_known_language_folders_exist()
        {
            string[] folders = Directory.GetDirectories(RepoPaths.Translations).Select(Path.GetFileName).ToArray();

            Assert.That(folders, Is.EquivalentTo(new[] { English, PortugueseBrazilian }));
        }
    }
}
