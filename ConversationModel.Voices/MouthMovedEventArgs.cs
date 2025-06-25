// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;

    /// <summary>
    /// Raised when a new mouth posture has been reached.
    /// </summary>
    /// <param name="visemeId">The viseme corresponding to the new posture.</param>
    public class MouthMovedEventArgs(uint visemeId) : EventArgs
    {
        /// <summary>
        /// Gets the viseme corresponding to the new posture.
        /// </summary>
        public uint VisemeId { get; } = visemeId;
    }
}
