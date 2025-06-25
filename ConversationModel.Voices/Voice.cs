// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An abstract representation of a speech synthesis voice.
    /// </summary>
    public abstract class Voice : IDisposable
    {
        private readonly PhoeneticReplacer phoeneticReplacer;

        /// <summary>
        /// Initializes a new instance of the <see cref="Voice"/> class.
        /// </summary>
        /// <param name="phoeneticReplacer">The <see cref="PhoeneticReplacer"/> to use for pronunciation modification.</param>
        protected Voice(PhoeneticReplacer? phoeneticReplacer)
        {
            this.phoeneticReplacer = phoeneticReplacer ?? PhoeneticReplacer.Null;
        }

        /// <summary>
        /// Raised when a new mouth posture has been reached.
        /// </summary>
        public event EventHandler<MouthMovedEventArgs>? MouthMoved;

        /// <summary>
        /// Raised when a new text segment has been reached.
        /// </summary>
        public event EventHandler<IndexReachedEventArgs>? IndexReached;

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Synthesize voice from the specified text.
        /// </summary>
        /// <param name="text">The text to speak.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> that will interrupt the speech.</param>
        /// <returns>A task with the text spoken before interruption.</returns>
        /// <remarks>When the task is interrupted before completion, the text will be truncated and `--` will be added if a sentence was cut off.</remarks>
        public async Task<string> SayAsync(string text, CancellationToken cancel)
        {
            var progress = 0;

            var mapping = this.phoeneticReplacer.Replace(text);

            void IndexReached(int index, int length)
            {
                var ix = mapping.MapOutputIndex(index);
                var end = mapping.MapOutputIndex(index + length);
                progress = ix;
                this.InvokeIndexReached(text, ix, end - ix);
            }

            try
            {
                await this.SayImplAsync(mapping.Replaced, IndexReached, cancel).ConfigureAwait(false);
            }
            finally
            {
                if (progress != text.Length)
                {
                    text = text[..progress].TrimEnd();
                    if (text is not "" && text[^1..] is not ("." or "?" or "!"))
                    {
                        text += "--";
                    }
                }
            }

            return text;
        }

        /// <summary>
        /// When overriden in a base class, disposes managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to dispose managed resources; <c>false</c> to only dispose unmanaged resoruces.</param>
        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// When overridden in a base class, synthesize voice from the specified text.
        /// </summary>
        /// <param name="text">The text to speak.</param>
        /// <param name="indexReached">An action invoked when indices are reached.</param>
        /// <param name="cancel">A <see cref="CancellationToken"/> that will interrupt the speech.</param>
        /// <returns>A task representing the ongoing operation.</returns>
        protected abstract Task SayImplAsync(string text, Action<int, int> indexReached, CancellationToken cancel);

        /// <summary>
        /// Invokes the <see cref="MouthMoved"/> event.
        /// </summary>
        /// <param name="visemeId">The mouth posture.</param>
        protected void InvokeMouthMoved(uint visemeId)
        {
            this.MouthMoved?.Invoke(this, new MouthMovedEventArgs(visemeId));
        }

        private void InvokeIndexReached(string text, int index, int length)
        {
            this.IndexReached?.Invoke(this, new IndexReachedEventArgs(text, index, length));
        }
    }
}
