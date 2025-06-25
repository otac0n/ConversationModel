// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Backends
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// Connects to an HTTP backend for autocompletion.
    /// </summary>
    public partial class HttpBackend : IBackend
    {
        private static readonly JsonSerializerOptions JsonOptions;
        private readonly ILogger<HttpBackend> logger;
        private readonly HttpClient httpClient;
        private readonly string languageModel;

        static HttpBackend()
        {
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpBackend"/> class.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> to use for requests.</param>
        /// <param name="languageModel">The language model to request.</param>
        /// <param name="logger">An optional logger.</param>
        public HttpBackend(HttpClient httpClient, string languageModel, ILogger<HttpBackend>? logger = null)
        {
            this.logger = logger ?? NullLogger<HttpBackend>.Instance;
            this.httpClient = httpClient;
            this.languageModel = languageModel;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpBackend"/> class.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> to use for requests.</param>
        /// <param name="baseAddress">The base address for requests.</param>
        /// <param name="languageModel">The language model to request.</param>
        /// <param name="logger">An optional logger.</param>
        public HttpBackend(HttpClient httpClient, Uri baseAddress, string languageModel, ILogger<HttpBackend>? logger = null)
            : this(httpClient, languageModel, logger)
        {
            this.httpClient.BaseAddress = baseAddress;
        }

        /// <inheritdoc/>
        public event EventHandler<TokenReceivedEventArgs>? TokenReceived;

        /// <inheritdoc/>
        public async Task GetNextResponseTokensAsync(IEnumerable<Message> messages, ChannelWriter<string> writer, CancellationToken cancel)
        {
            var requestBody = JsonSerializer.Serialize(
                new
                {
                    model = this.languageModel,
                    messages,
                    temperature = 0.7,
                    max_tokens = -1,
                    stream = true,
                },
                JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };

            LogMessages.RequestingCompletion(this.logger, request.RequestUri!);
            using var response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            LogMessages.ResponseReceived(this.logger, request.RequestUri!, (int)response.StatusCode, response.ReasonPhrase);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            using var reader = new StreamReader(responseStream);

            try
            {
                while (!reader.EndOfStream)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        LogMessages.TokenStreamCanceled(this.logger);
                        cancel.ThrowIfCancellationRequested();
                    }

                    var line = await reader.ReadLineAsync(cancel).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var jsonPart = line[5..].Trim();
                    if (jsonPart == "[DONE]")
                    {
                        LogMessages.FinishedTokens(this.logger);
                        writer.Complete();
                        return;
                    }

                    using var doc = JsonDocument.Parse(jsonPart);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices[0].GetProperty("delta").TryGetProperty("content", out var content))
                    {
                        var tokens = content.GetString();
                        if (!string.IsNullOrEmpty(tokens))
                        {
                            LogMessages.ReceivedTokens(this.logger, tokens);
                            this.TokenReceived?.Invoke(this, new TokenReceivedEventArgs(tokens));
                            await writer.WriteAsync(tokens, cancel).ConfigureAwait(false);
                        }
                    }
                }

                LogMessages.TokenStreamEnded(this.logger);
                throw new FormatException("Unexpected end of token stream.");
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
            }
            finally
            {
                LogMessages.RequestComplete(this.logger, request.RequestUri!);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.httpClient.Dispose();
        }

        private class SerialRequestsWithTimeBufferHandler(TimeSpan interval) : DelegatingHandler(new HttpClientHandler())
        {
            private readonly SemaphoreSlim semaphore = new(1, 1);
            private readonly TimeSpan interval = interval;
            private DateTimeOffset lastCompleted = DateTimeOffset.MinValue;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                await this.semaphore.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    var elapsed = DateTimeOffset.UtcNow - this.lastCompleted;
                    if (elapsed < this.interval)
                    {
                        var remainder = this.interval - elapsed;
                        await Task.Delay(remainder, cancel).ConfigureAwait(false);
                    }

                    try
                    {
                        return await base.SendAsync(request, cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        this.lastCompleted = DateTimeOffset.UtcNow;
                    }
                }
                finally
                {
                    this.semaphore.Release();
                }
            }
        }

        private static partial class LogMessages
        {
            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Requesting completion from '{url}'...")]
            public static partial void RequestingCompletion(ILogger logger, Uri url);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Response received from '{url}': {statusCode} {statusMessage}")]
            public static partial void ResponseReceived(ILogger logger, Uri url, int statusCode, string? statusMessage);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Request to '{url}' complete.")]
            public static partial void RequestComplete(ILogger logger, Uri url);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Received tokens '{tokens}'.")]
            public static partial void ReceivedTokens(ILogger logger, string tokens);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Finished receiving tokens.")]
            public static partial void FinishedTokens(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Token stream ended.")]
            public static partial void TokenStreamEnded(ILogger logger);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Token stream canceled.")]
            public static partial void TokenStreamCanceled(ILogger logger);
        }
    }
}
