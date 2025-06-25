// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using ConversationModel.Voices;
    using NUnit.Framework;

    [TestFixture]
    public class PhoeneticReplacerTests
    {
        [Test]
        public void Replace_GivesAReplacedString()
        {
            var replacer = new PhoeneticReplacer(new Dictionary<string, string>
            {
                { "b", "xxx" },
                { "efg", "y" },
            });

            var mapping = replacer.Replace("abcdefgh");

            Assert.That(mapping.Replaced, Is.EqualTo("axxxcdyh"));
        }

        [Test]
        public void Replace_GivesAnAccurateMapping()
        {
            var replacer = new PhoeneticReplacer(new Dictionary<string, string>
            {
                { "b", "xxx" },
                { "efg", "y" },
            });
            var input = "abcdefgh";
            var mapping = replacer.Replace(input);

            var indices = string.Concat(Enumerable.Range(0, mapping.Replaced.Length).Select(mapping.MapOutputIndex));
            Assert.That(indices, Is.EqualTo("01112347"));
        }
    }
}
