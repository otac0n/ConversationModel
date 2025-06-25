// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Responses
{
    /// <summary>
    /// A response made by a character with an optional mood tag.
    /// </summary>
    /// <param name="Name">The responding character.</param>
    /// <param name="Text">The response.</param>
    /// <param name="Mood">The optional mood.</param>
    public record class CharacterResponse(string Name, string Text, string? Mood) : Response
    {
        /// <inheritdoc/>
        public override string ToString() => $"{this.Name} [{this.Mood}]: {this.Text}";
    }
}
