// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel
{
    using System;

    /// <summary>
    /// Raised when a token is received.
    /// </summary>
    /// <remarks>A token is roughly one syllable or four characters, but varies widely.</remarks>
    /// <param name="token">The newly received token.</param>
    public class TokenReceivedEventArgs(string token) : EventArgs
    {
        /// <summary>
        /// Gets the newly received token.
        /// </summary>
        public string Token { get; } = token;
    }
}
