// Copyright © John Gietzen. All Rights Reserved. This source is subject to the MIT license. Please see license.md for more information.

namespace ConversationModel.Voices
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.Versioning;
    using System.Speech.Synthesis;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    /// <summary>
    /// Implements a <see cref="Voice"/> using <see cref="System.Speech.Synthesis"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class SystemSpeechVoice : Voice, IDisposable
    {
        private readonly ILogger<SystemSpeechVoice> logger;
        private readonly SpeechSynthesizer synth = new();
        private readonly string voiceName;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemSpeechVoice"/> class.
        /// </summary>
        /// <param name="voiceGender">An optional <see cref="VoiceGender"/> hint.</param>
        /// <param name="voiceAge">An optional <see cref="VoiceAge"/> hint.</param>
        /// <param name="voiceCulture">An optional <see cref="CultureInfo"/> hint.</param>
        /// <param name="phoeneticReplacer">An optional <see cref="PhoeneticReplacer"/>.</param>
        /// <param name="logger">An optional logger.</param>
        [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "This enumerates all cultures.")]
        public SystemSpeechVoice(VoiceGender voiceGender = VoiceGender.NotSet, VoiceAge voiceAge = VoiceAge.NotSet, CultureInfo? voiceCulture = null, PhoeneticReplacer? phoeneticReplacer = null, ILogger<SystemSpeechVoice>? logger = null)
            : base(phoeneticReplacer)
        {
            this.logger = logger ?? NullLogger<SystemSpeechVoice>.Instance;

            if (voiceCulture?.Name == "uk-UA")
            {
                LogMessages.ReplacingCulture(this.logger, voiceCulture?.Name!, "ru-RU");
                voiceCulture = CultureInfo.GetCultureInfo("ru-RU"); // Closest accent available in Microsoft TTS.
            }

            var voice = (from v in this.synth.GetInstalledVoices()
                         orderby v.VoiceInfo.Gender == voiceGender descending,
                                 voiceCulture == null || v.VoiceInfo.Culture.LCID == voiceCulture.LCID descending,
                                 v.VoiceInfo.Age == voiceAge descending
                         select v).First();
            this.voiceName = voice.VoiceInfo.Name;
            LogMessages.SelectedVoice(this.logger, this.voiceName, voiceGender, voiceAge, voiceCulture?.Name);
            this.synth.SelectVoice(this.voiceName);
            this.synth.VisemeReached += this.Synth_VisemeReached;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.synth.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override Task SayImplAsync(string text, Action<int, int> indexReached, CancellationToken cancel)
        {
            var tcs = new TaskCompletionSource();
            Prompt? prompt = null;
            CancellationTokenRegistration ctr = default;

            void ProgressHandler(object? sender, SpeakProgressEventArgs e)
            {
                indexReached(e.CharacterPosition, e.CharacterCount);
            }

            void FinishHandler(object? e, SpeakCompletedEventArgs a)
            {
                if (a.Prompt == prompt)
                {
                    indexReached(text.Length, 0);
                    this.InvokeMouthMoved(0);
                    LogMessages.SpeakingCompleted(this.logger, this.voiceName);
                    tcs.TrySetResult();
                    Dispose();
                }
            }

            void Dispose()
            {
                ctr.Dispose();
                this.synth.SpeakProgress -= ProgressHandler;
                this.synth.SpeakCompleted -= FinishHandler;
            }

            try
            {
                this.synth.SpeakCompleted += FinishHandler;
                this.synth.SpeakProgress += ProgressHandler;

                LogMessages.SpeakingText(this.logger, this.voiceName, text);
                prompt = this.synth.SpeakAsync(text);

                ctr = cancel.Register(() =>
                {
                    if (prompt != null && !prompt.IsCompleted)
                    {
                        LogMessages.CancelingSpeaking(this.logger, this.voiceName);
                        this.synth.SpeakAsyncCancel(prompt);
                    }

                    this.InvokeMouthMoved(0);
                    tcs.TrySetCanceled();
                    Dispose();
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                Dispose();
            }

            return tcs.Task;
        }

        private void Synth_VisemeReached(object? sender, VisemeReachedEventArgs e)
        {
            this.InvokeMouthMoved((uint)e.Viseme);
        }

        private static partial class LogMessages
        {
            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Replacing culture '{replaced}' with phoenetic approximation '{replacement}'.")]
            public static partial void ReplacingCulture(ILogger logger, string replaced, string replacement);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Selected voice from Gender = '{voiceGender}', Age = '{voiceAge}', Culture = '{voiceCulture}'")]
            public static partial void SelectedVoice(ILogger logger, string voiceName, VoiceGender voiceGender, VoiceAge voiceAge, string? voiceCulture);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Canceling speaking...")]
            public static partial void CancelingSpeaking(ILogger logger, string voiceName);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Speaking text \"{text}\"")]
            public static partial void SpeakingText(ILogger logger, string voiceName, string text);

            [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{voiceName}: Speaking text completed.")]
            public static partial void SpeakingCompleted(ILogger logger, string voiceName);
        }
    }
}
