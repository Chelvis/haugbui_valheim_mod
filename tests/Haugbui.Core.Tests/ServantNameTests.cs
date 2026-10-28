using Haugbui.Core.Persistence;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class ServantNameTests
    {
        [Test]
        public void Empty_or_whitespace_restores_the_default()
        {
            Assert.That(ServantSlots.NormalizeName(null, 0), Is.EqualTo("Haugbui"));
            Assert.That(ServantSlots.NormalizeName("   ", 0), Is.EqualTo("Haugbui"));
            Assert.That(ServantSlots.NormalizeName(" \t ", 1), Is.EqualTo("Haugbui #2"));
        }

        [Test]
        public void Name_is_trimmed_and_capped()
        {
            Assert.That(ServantSlots.NormalizeName("  Draugr  ", 0), Is.EqualTo("Draugr"));
            string longName = new string('a', ServantSlots.MaxNameLength + 8);
            Assert.That(ServantSlots.NormalizeName(longName, 2), Has.Length.EqualTo(ServantSlots.MaxNameLength));
        }

        [Test]
        public void Control_characters_are_removed()
        {
            Assert.That(ServantSlots.NormalizeName("Draugr\n\u0001", 0), Is.EqualTo("Draugr"));
            Assert.That(ServantSlots.NormalizeName("\0\0", 0), Is.EqualTo("Haugbui"));
        }

        [Test]
        public void Normalized_name_survives_the_slot_blob()
        {
            var slot = new ServantSlot
            {
                Index = 1,
                Name = ServantSlots.NormalizeName("  Second  ", 1),
                TombGuid = "abc",
                Toggles = TombToggles.Defaults,
            };
            BlobReadStatus status = ServantSlots.Decode(ServantSlots.Encode(slot), out ServantSlot again);
            Assert.That(status, Is.EqualTo(BlobReadStatus.Ok));
            Assert.That(again.Name, Is.EqualTo("Second"));
            Assert.That(again.Index, Is.EqualTo(1));
            Assert.That(again.TombGuid, Is.EqualTo("abc"));
        }
    }
}
