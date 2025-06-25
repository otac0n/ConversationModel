// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;

    /// <summary>
    /// Raised when a new text segment has been reached.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="index">The starting index.</param>
    /// <param name="length">The length of the segment.</param>
    public class IndexReachedEventArgs(string text, int index, int length) : EventArgs
    {
        /// <summary>
        /// Gets the source text.
        /// </summary>
        public string Text { get; } = text;

        /// <summary>
        /// Gets the starting index.
        /// </summary>
        public int Index { get; } = index;

        /// <summary>
        /// Gets the length of the segment.
        /// </summary>
        public int Length { get; } = length;
    }
}
