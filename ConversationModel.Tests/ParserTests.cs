// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Tests
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    public class ParserTests
    {
        [Test]
        public void Parse_WithInvalidText_ThrowsFormatException()
        {
            var parser = new Parser();

            void Parse() => parser.Parse("#-INVALID-#\n");

            Assert.That(Parse, Throws.InstanceOf<FormatException>());
        }
    }
}
