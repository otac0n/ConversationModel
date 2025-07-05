// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CognitiveServices.Speech;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// Implements a <see cref="Voice"/> using <see cref="Microsoft.CognitiveServices.Speech"/>.
    /// </summary>
    public partial class AzureCognitiveVoice : Voice, IDisposable
    {
        private readonly ILogger<AzureCognitiveVoice> logger;
        private readonly string voiceName;
        private readonly SpeechSynthesizer synth;
        private readonly CancellationTokenSource cancel = new();
        private Task? lastCancelTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureCognitiveVoice"/> class.
        /// </summary>
        /// <param name="speechEndpoint">The address of the speech endpoint to use.</param>
        /// <param name="speechKey">The key to use with the speech endpoint.</param>
        /// <param name="voiceName">The selected voice.</param>
        /// <param name="phoeneticReplacer">An optional <see cref="PhoeneticReplacer"/>.</param>
        /// <param name="logger">An optional logger.</param>
        public AzureCognitiveVoice(Uri speechEndpoint, string speechKey, string voiceName, PhoeneticReplacer? phoeneticReplacer = null, ILogger<AzureCognitiveVoice>? logger = null)
            : base(phoeneticReplacer)
        {
            this.logger = logger ?? NullLogger<AzureCognitiveVoice>.Instance;
            this.voiceName = voiceName;
            var speechConfig = SpeechConfig.FromEndpoint(speechEndpoint, speechKey);
            speechConfig.SpeechSynthesisVoiceName = voiceName;
            this.synth = new(speechConfig);
            this.synth.VisemeReceived += this.Synth_VisemeReceived;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.cancel.Cancel();
                this.synth.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override async Task SayImplAsync(string text, Action<int, int> indexReached, CancellationToken cancel)
        {
            var innerCancel = new CancellationTokenSource();
            using var innerRegistration = cancel.Register(innerCancel.Cancel);
            using var disposeRegistration = this.cancel.Token.Register(innerCancel.Cancel);

            if (this.lastCancelTask is Task cancelTask && !cancelTask.IsCompleted)
            {
                LogMessages.WaitingOnCancelTask(this.logger, this.voiceName);
                await cancelTask.ConfigureAwait(false);
            }

            using var ctr = innerCancel.Token.Register(() =>
            {
                LogMessages.CancelingSpeaking(this.logger, this.voiceName);
                this.lastCancelTask = this.synth.StopSpeakingAsync();
            });

            LogMessages.SpeakingText(this.logger, this.voiceName, text);

            void Synth_WordBoundary(object? sender, SpeechSynthesisWordBoundaryEventArgs e)
            {
                indexReached((int)e.TextOffset, (int)e.WordLength);
            }

            try
            {
                this.synth.WordBoundary += Synth_WordBoundary;
                await this.synth.SpeakTextAsync(text).ConfigureAwait(false);
            }
            finally
            {
                this.synth.WordBoundary -= Synth_WordBoundary;

                if (!innerCancel.IsCancellationRequested)
                {
                    indexReached(text.Length, 0);
                }

                this.InvokeMouthMoved(0);
            }

            LogMessages.SpeakingCompleted(this.logger, this.voiceName);
        }

        private void Synth_VisemeReceived(object? sender, SpeechSynthesisVisemeEventArgs e)
        {
            this.InvokeMouthMoved(e.VisemeId);
        }

        private static partial class LogMessages
        {
            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Waiting on cancel task...")]
            public static partial void WaitingOnCancelTask(ILogger logger, string voiceName);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Canceling speaking...")]
            public static partial void CancelingSpeaking(ILogger logger, string voiceName);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Speaking text \"{text}\"")]
            public static partial void SpeakingText(ILogger logger, string voiceName, string text);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Speaking text completed.")]
            public static partial void SpeakingCompleted(ILogger logger, string voiceName);
        }
    }
}
