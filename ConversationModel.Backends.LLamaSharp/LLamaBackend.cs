// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Backends.LLamaSharp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using LLama;
    using LLama.Common;
    using LoadTask = System.Threading.Tasks.Task<(LLama.LLamaWeights Weights, LLama.LLamaContext Context)>;

    /// <summary>
    /// Use LLamaSharp for autocompletion.
    /// </summary>
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
        private LoadTask? loadTask;
        private bool disposedValue;

        public LLamaBackend(ModelParams modelParams)
        {
            this.disposeCancel = new CancellationTokenSource();
            this.loadTask = LoadAsync(modelParams, this.disposeCancel.Token);
        }

        /// <inheritdoc/>
        public event EventHandler<TokenReceivedEventArgs>? TokenReceived;

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        [SuppressMessage("Maintainability", "CA1513:Use ObjectDisposedException throw helper", Justification = "Fails definite assignment.")]
        public async Task GetNextResponseTokensAsync(IEnumerable<Message> messages, ChannelWriter<string> writer, CancellationToken cancel)
        {
            if (this.loadTask is not LoadTask loadTask)
            {
                throw new ObjectDisposedException(typeof(LLamaBackend).FullName);
            }

            try
            {
                var (_, context) = await loadTask.ConfigureAwait(false);
                var executor = new InteractiveExecutor(context);

                var prompt = new StringBuilder();
                foreach (var message in messages)
                {
                    prompt.Append(message.Content);
                }

                var queue = new Queue<string>();
                var buffer = new StringBuilder();
                var bufferIndex = 0;

                await foreach (var token in executor.InferAsync(prompt.ToString(), InferenceParams, cancel).ConfigureAwait(false))
                {
                    queue.Enqueue(token);
                    buffer.Append(token);

                    bool? clean = true;
                    while (bufferIndex < buffer.Length && clean == true)
                    {
                        var rest = buffer.ToString(bufferIndex, buffer.Length - bufferIndex);
                        foreach (var stop in InferenceParams.AntiPrompts)
                        {
                            if (stop.Length > rest.Length && stop.StartsWith(rest, StringComparison.Ordinal))
                            {
                                clean = null;
                                break;
                            }
                            else if (rest.StartsWith(stop, StringComparison.Ordinal))
                            {
                                clean = false;

                                if (bufferIndex > 0)
                                {
                                    var emit = buffer.ToString(0, bufferIndex);
                                    this.TokenReceived?.Invoke(this, new(emit));
                                    await writer.WriteAsync(emit, cancel).ConfigureAwait(false);
                                }

                                break;
                            }
                        }

                        if (clean == true)
                        {
                            bufferIndex++;
                            if (queue.Peek()!.Length <= bufferIndex)
                            {
                                var emit = queue.Dequeue();
                                buffer.Remove(0, emit.Length);
                                bufferIndex -= emit.Length;
                                this.TokenReceived?.Invoke(this, new(emit));
                                await writer.WriteAsync(emit, cancel).ConfigureAwait(false);
                            }
                        }
                    }

                    if (clean == false)
                    {
                        break;
                    }
                }

                writer.TryComplete();
            }
            catch (Exception error)
            {
                writer.TryComplete(error);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.disposeCancel.Cancel();
                    if (this.loadTask is LoadTask task)
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

        private static async LoadTask LoadAsync(ModelParams modelParams, CancellationToken cancel)
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
