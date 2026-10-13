using System;
using System.Text.Json;
using Haugbui.Core.Diagnostics;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class JsonWriterTests
    {
        [Test]
        public void Writes_nested_structures_that_parse_back()
        {
            string json = new JsonWriter()
                .BeginObject()
                .Property("name", "Smelter \"basic\"\n")
                .Property("maxOre", 10)
                .Property("secPerProduct", 30.5)
                .Property("roof", false)
                .Name("conversions").BeginArray()
                    .BeginObject().Property("from", "CopperOre").Property("to", "Copper").EndObject()
                    .BeginObject().Property("from", "TinOre").Property("to", "Tin").EndObject()
                .EndArray()
                .Name("empty").BeginArray().EndArray()
                .Property("nothing", (string)null)
                .EndObject()
                .ToString();

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Smelter \"basic\"\n"));
            Assert.That(root.GetProperty("maxOre").GetInt32(), Is.EqualTo(10));
            Assert.That(root.GetProperty("secPerProduct").GetDouble(), Is.EqualTo(30.5));
            Assert.That(root.GetProperty("conversions")[1].GetProperty("to").GetString(), Is.EqualTo("Tin"));
            Assert.That(root.GetProperty("empty").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("nothing").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        [Test]
        public void Uses_invariant_culture_and_nulls_non_finite_numbers()
        {
            string json = new JsonWriter().BeginArray().Value(1.5).Value(double.NaN).EndArray().ToString();

            Assert.That(json, Is.EqualTo("[1.5,null]"));
        }

        [Test]
        public void Unclosed_structure_throws()
        {
            Assert.Throws<InvalidOperationException>(() => new JsonWriter().BeginObject().ToString());
        }
    }
}
