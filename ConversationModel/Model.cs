// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using ConversationModel.Responses;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Pegasus.Common;

    /// <summary>
    /// Coordinates a conversation with an autocompletion backend.
    /// </summary>
    public partial class Model : IDisposable
    {
        private readonly ILogger<Model> logger;
        private readonly IBackend backend;
        private readonly Func<CharacterResponse, CancellationToken, Task<CharacterResponse?>> speechFunction;
        private readonly Func<CodeResponse, CancellationToken, Task<string>> codeFunction;
        private readonly List<Message> messages = [];
        private CancellationTokenSource cts = new();
        private Task? activeWork;
        private bool disposedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Model"/> class.
        /// </summary>
        /// <param name="backend">The autocompletion backend.</param>
        /// <param name="systemPrompt">The initial system prompt.</param>
        /// <param name="speechFunction">A function called when the model would speak.</param>
        /// <param name="codeFunction">A function called when the model would invoke code.</param>
        /// <param name="logger">An optional logger.</param>
        public Model(IBackend backend, string systemPrompt, Func<CharacterResponse, CancellationToken, Task<CharacterResponse?>> speechFunction, Func<CodeResponse, CancellationToken, Task<string>> codeFunction, ILogger<Model>? logger = null)
        {
            this.logger = logger ?? NullLogger<Model>.Instance;
            this.backend = backend;
            this.backend.TokenReceived += this.Backend_TokenReceived;
            this.speechFunction = speechFunction;
            this.codeFunction = codeFunction;
            this.messages.Add(new("system", systemPrompt));
        }

        /// <summary>
        /// Raised when a token is received.
        /// </summary>
        public event EventHandler<TokenReceivedEventArgs>? TokenReceived;

        /// <summary>
        /// Adds a user message to the history and allows the backend to respond.
        /// </summary>
        /// <param name="content">The content of the message.</param>
        /// <param name="userPrefix"><c>true</c>, to previx the message with <c>"User: "</c>, <c>false</c> to allow the inclusion of non-chat messages.</param>
        /// <returns>A task tracking the response.</returns>
        public async Task AddUserMessageAsync(string content, bool userPrefix = true)
        {
            ObjectDisposedException.ThrowIf(this.disposedValue, this);

            LogMessages.ReceivedUserMessage(this.logger, content);

            LogMessages.CancelingActiveGeneration(this.logger);
            await this.cts.CancelAsync().ConfigureAwait(false);
            if (this.activeWork is Task activeWork)
            {
                LogMessages.AwaitingActiveGeneration(this.logger);

                try
                {
                    await activeWork.ConfigureAwait(false);
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }

            LogMessages.CanceledActiveGeneration(this.logger);

            lock (this.messages)
            {
                this.messages.Add(new Message("user", userPrefix ? $"User: {content.Trim()}\n" : $"{content.Trim()}\n"));
            }

            this.cts = new CancellationTokenSource();
            var work = this.ProcessNextResponsesAsync(this.cts.Token);
            this.activeWork = work;
            await work.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// When overriden in a base class, disposes managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to dispose managed resources; <c>false</c> to only dispose unmanaged resoruces.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                this.disposedValue = true;

                if (disposing)
                {
                    this.cts.Cancel();
                    this.backend.TokenReceived -= this.Backend_TokenReceived;
                }
            }
        }

        private static async IAsyncEnumerable<Response> ParseResponsesAsync(ChannelReader<string> reader, [EnumeratorCancellation] CancellationToken cancel)
        {
            var parser = new Parser();
            var remaining = string.Empty;
            await foreach (var token in reader.ReadAllAsync(cancel).ConfigureAwait(false))
            {
                remaining += token;

                var cursor = new Cursor(remaining);
                while (true)
                {
                    var startCursor = cursor;
                    var parsed = parser.Exported.Response(ref cursor);
                    if (parsed != null && cursor.Location < remaining.Length)
                    {
                        yield return parsed.Value;
                    }
                    else
                    {
                        cursor = startCursor;
                        break;
                    }
                }

                remaining = remaining[cursor.Location..];
            }

            foreach (var parsed in parser.Parse(remaining))
            {
                yield return parsed;
            }
        }

        private void Backend_TokenReceived(object? sender, TokenReceivedEventArgs e)
        {
            LogMessages.ReceivedToken(this.logger, e.Token);
            this.TokenReceived?.Invoke(sender, e);
        }

        private async Task ProcessNextResponsesAsync(CancellationToken cancel)
        {
            var getNextResponse = true;
            try
            {
                while (getNextResponse)
                {
                    getNextResponse = false;
                    cancel.ThrowIfCancellationRequested();

                    var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = true,
                    });

                    Task producer;
                    lock (this.messages)
                    {
                        producer = this.backend.GetNextResponseTokensAsync(this.messages, channel.Writer, cancel);
                    }

                    try
                    {
                        await foreach (var response in ParseResponsesAsync(channel.Reader, cancel).ConfigureAwait(false))
                        {
                            switch (response)
                            {
                                case CharacterResponse characterResponse:
                                    var content = $"{characterResponse.Name}{(string.IsNullOrWhiteSpace(characterResponse.Mood) ? string.Empty : $" [{characterResponse.Mood}]")}: {characterResponse.Text}";

                                    LogMessages.ReceivedAgentMessage(this.logger, content);

                                    var updatedResponse = await this.speechFunction(characterResponse, cancel).ConfigureAwait(false);
                                    if (updatedResponse != null)
                                    {
                                        lock (this.messages)
                                        {
                                            this.messages.Add(new Message("assistant", $"{updatedResponse.Name}{(string.IsNullOrWhiteSpace(updatedResponse.Mood) ? string.Empty : $" [{updatedResponse.Mood}]")}: {updatedResponse.Text}\n"));
                                        }
                                    }

                                    break;

                                case CodeResponse codeResponse:
                                    getNextResponse = true;
                                    lock (this.messages)
                                    {
                                        this.messages.Add(new Message("assistant", $"```\n{codeResponse.Code.Trim()}\n```\n"));
                                    }

                                    string output;
                                    try
                                    {
                                        output = await this.codeFunction(codeResponse, cancel).ConfigureAwait(false);
                                        output = $"{output.Trim()}\nSystem: Task Status Completed";
                                    }
                                    catch (Exception ex)
                                    {
                                        output = $"{ex.ToString().Trim()}\nSystem: Task Status Faulted";
                                    }

                                    this.messages.Add(new Message("assistant", $"{output}\n"));
                                    break;
                            }

                            cancel.ThrowIfCancellationRequested();
                        }
                    }
                    catch (FormatException ex)
                    {
                        LogMessages.RetryingDueToParseFailure(this.logger, ex);
                        getNextResponse = true;
                        continue;
                    }
                    finally
                    {
                        await producer.ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessages.ProcessingFailed(this.logger, ex);
                throw;
            }
        }

        private static partial class LogMessages
        {
            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Received user message \"{message}\".")]
            public static partial void ReceivedUserMessage(ILogger logger, string message);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Received agent message \"{message}\".")]
            public static partial void ReceivedAgentMessage(ILogger logger, string message);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Canceling active generation...")]
            public static partial void CancelingActiveGeneration(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Awaiting active generation...")]
            public static partial void AwaitingActiveGeneration(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Canceled active generation.")]
            public static partial void CanceledActiveGeneration(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Processing responses failed.")]
            public static partial void ProcessingFailed(ILogger logger, Exception error);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Received token '{token}'.")]
            public static partial void ReceivedToken(ILogger logger, string token);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Token stream ended.")]
            public static partial void TokenStreamEnded(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Token stream canceled.")]
            public static partial void TokenStreamCanceled(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Retrying due to parse failure.")]
            public static partial void RetryingDueToParseFailure(ILogger logger, Exception error);
        }
    }
}
