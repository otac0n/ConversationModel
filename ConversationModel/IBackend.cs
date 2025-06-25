// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// An abstract representation of an autocompletion backend.
    /// </summary>
    public interface IBackend : IDisposable
    {
        /// <summary>
        /// Raised when a token is received.
        /// </summary>
        public event EventHandler<TokenReceivedEventArgs>? TokenReceived;

        /// <summary>
        /// Requests the next response to the given prompt as a stream.
        /// </summary>
        /// <param name="messages">The messages in the history.</param>
        /// <param name="writer">A channel writer that will be populated with tokens.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> to terminate the stream.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task GetNextResponseTokensAsync(IEnumerable<Message> messages, ChannelWriter<string> writer, CancellationToken cancel);
    }
}
