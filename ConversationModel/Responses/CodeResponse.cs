// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Responses
{
    /// <summary>
    /// A response with a request to run code.
    /// </summary>
    /// <param name="Code">The requested code to run.</param>
    public record class CodeResponse(string Code) : Response
    {
        /// <inheritdoc/>
        public override string ToString() => $"```\n{this.Code}\n```";
    }
}
