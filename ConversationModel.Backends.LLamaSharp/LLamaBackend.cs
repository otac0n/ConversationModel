// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Backends.LLama
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using global::LLama;
    using global::LLama.Common;

    public class LLamaBackend : IBackend, IDisposable
    {
        private static readonly InferenceParams InferenceParams = new()
        {
            AntiPrompts = [
                "<|begin_of_thought|>",
                "\n---\n",
                "User:",
                "System:",
                "Output:",
                "Error:",
            ],
        };

        private readonly CancellationTokenSource disposeCancel;
        private Task<(LLamaWeights, LLamaContext)>? loadTask;
        private bool disposedValue;

        public LLamaBackend(ModelParams modelParams)
        {
            this.disposeCancel = new CancellationTokenSource();
            this.loadTask = LoadAsync(modelParams, this.disposeCancel.Token);
        }

        public event EventHandler<TokenReceivedEventArgs>? TokenReceived;

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async Task GetNextResponseTokensAsync(IEnumerable<Message> messages, ChannelWriter<string> writer, CancellationToken cancel)
        {
            var (_, context) = await this.loadTask.ConfigureAwait(false);
            var executor = new InteractiveExecutor(context);

            var prompt = new StringBuilder();
            foreach (var message in messages)
            {
                prompt.Append(message.Content);
            }

            await foreach (var text in executor.InferAsync(prompt.ToString(), InferenceParams, cancel).ConfigureAwait(false))
            {
                this.TokenReceived?.Invoke(this, new(text));
                await writer.WriteAsync(text, cancel).ConfigureAwait(false);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.disposeCancel.Cancel();
                    if (this.loadTask is Task<(LLamaWeights, LLamaContext)> task)
                    {
                        try
                        {
                            task.Wait();
                        }
                        catch
                        {
                        }

                        if (task.IsCompletedSuccessfully)
                        {
                            var (weights, context) = task.Result;
                            weights.Dispose();
                            context.Dispose();
                        }

                        this.loadTask = null;
                    }
                }

                this.disposedValue = true;
            }
        }

        private static async Task<(LLamaWeights Weights, LLamaContext Context)> LoadAsync(ModelParams modelParams, CancellationToken cancel)
        {
            LLamaWeights? weights = null;
            try
            {
                weights = await LLamaWeights.LoadFromFileAsync(modelParams, cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                LLamaContext? context = null;
                try
                {
                    context = weights.CreateContext(modelParams);

                    var ret = (weights, context);
                    weights = null;
                    context = null;
                    return ret;
                }
                finally
                {
                    (context as IDisposable)?.Dispose();
                }
            }
            finally
            {
                (weights as IDisposable)?.Dispose();
            }
        }
    }
}
