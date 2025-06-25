// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel
{
    /// <summary>
    /// A chunk of tagged content in a chat history.
    /// </summary>
    /// <param name="Role">The role or originaror of the content.</param>
    /// <param name="Content">The content.</param>
    /// <remarks>
    /// The <see cref="Role"/> will typically be:
    /// <c>"system"</c>, indicating a system prompt;
    /// <c>"user"</c>, indicating a user prompt;
    /// or <c>"assistant"</c>, indicating a response from the backend.
    /// </remarks>
    public record class Message(string Role, string Content);
}
