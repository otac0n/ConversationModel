// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using ConversationModel.Backends.LLamaSharp;
    using LLama.Common;
    using NUnit.Framework;

    [TestFixture]
    public class LLamaSharpTests
    {
        private static string ModelPath =>
            Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".cache\lm-studio\models"), "*.gguf", SearchOption.AllDirectories).FirstOrDefault();

        [Test]
        public void Dispose_Immediately_Succeeds()
        {
            var modelPath = ModelPath;
            Assume.That(modelPath, Is.Not.Null);

            using var backend = new LLamaBackend(new ModelParams(modelPath!));
            {
            }

            Assert.Pass();
        }

        [Test]
        public async Task GetNextResponseTokensAsync_DoesNotHang()
        {
            var modelPath = ModelPath;
            Assume.That(modelPath, Is.Not.Null);

            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(TimeSpan.FromMinutes(0.5));

            using var backend = new LLamaBackend(new ModelParams(modelPath!));
            {
                var task = backend.GetNextResponseTokensAsync([new Message("system", "User: Hello!\nAgent: ")], channel.Writer, cancel.Token);

                await foreach (var text in channel.Reader.ReadAllAsync())
                {
                    TestContext.Write(text);
                }

                await task;
            }

            Assert.Pass();
        }
    }
}
