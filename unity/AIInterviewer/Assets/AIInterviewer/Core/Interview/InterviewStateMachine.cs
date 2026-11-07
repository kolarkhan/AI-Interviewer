using System;
using System.Collections.Generic;

namespace AIInterviewer.Core.Interview
{
    /// <summary>Visible interview states (drives UI, avatar animation and microphone behaviour).</summary>
    public enum InterviewState
    {
        Idle,
        Connecting,
        Ready,          // connected, waiting for the interviewer's first words
        Listening,      // candidate's turn: microphone open
        Processing,     // candidate finished speaking, AI is thinking
        Speaking,       // interviewer audio is playing
        Interrupted,    // candidate barged in; playback stopped, truncation sent
        Reconnecting,
        Completed,
        Error
    }

    public enum InterviewTrigger
    {
        Connect,
        SessionReady,
        UserSpeechStarted,
        UserSpeechStopped,
        AiAudioStarted,
        AiPlaybackFinished,
        AiResponseFinishedWithoutAudio,
        InterruptRequested,
        InterruptHandled,
        ConnectionLost,
        Complete,
        Fail,
        Reset
    }

    public sealed class StateChange
    {
        public StateChange(InterviewState from, InterviewState to, InterviewTrigger trigger)
        {
            From = from;
            To = to;
            Trigger = trigger;
        }

        public InterviewState From { get; }
        public InterviewState To { get; }
        public InterviewTrigger Trigger { get; }
    }

    /// <summary>
    /// Deterministic state machine for a live interview. Pure C# (no Unity dependency) so it is unit tested.
    /// Unknown transitions are ignored (and reported via <see cref="TryFire"/>), never thrown, because network
    /// events can legitimately arrive out of order.
    /// </summary>
    public sealed class InterviewStateMachine
    {
        private static readonly HashSet<InterviewState> ActiveStates = new HashSet<InterviewState>
        {
            InterviewState.Ready, InterviewState.Listening, InterviewState.Processing,
            InterviewState.Speaking, InterviewState.Interrupted
        };

        private readonly Dictionary<(InterviewState, InterviewTrigger), InterviewState> _table =
            new Dictionary<(InterviewState, InterviewTrigger), InterviewState>();

        public InterviewStateMachine()
        {
            State = InterviewState.Idle;

            Add(InterviewState.Idle, InterviewTrigger.Connect, InterviewState.Connecting);
            Add(InterviewState.Error, InterviewTrigger.Connect, InterviewState.Connecting);
            Add(InterviewState.Connecting, InterviewTrigger.SessionReady, InterviewState.Ready);
            Add(InterviewState.Reconnecting, InterviewTrigger.SessionReady, InterviewState.Ready);
            Add(InterviewState.Reconnecting, InterviewTrigger.Connect, InterviewState.Reconnecting);

            Add(InterviewState.Ready, InterviewTrigger.AiAudioStarted, InterviewState.Speaking);
            Add(InterviewState.Ready, InterviewTrigger.UserSpeechStarted, InterviewState.Listening);
            Add(InterviewState.Ready, InterviewTrigger.AiResponseFinishedWithoutAudio, InterviewState.Listening);

            Add(InterviewState.Listening, InterviewTrigger.UserSpeechStopped, InterviewState.Processing);
            Add(InterviewState.Listening, InterviewTrigger.AiAudioStarted, InterviewState.Speaking);

            Add(InterviewState.Processing, InterviewTrigger.AiAudioStarted, InterviewState.Speaking);
            Add(InterviewState.Processing, InterviewTrigger.UserSpeechStarted, InterviewState.Listening);
            Add(InterviewState.Processing, InterviewTrigger.AiResponseFinishedWithoutAudio, InterviewState.Listening);

            Add(InterviewState.Speaking, InterviewTrigger.AiPlaybackFinished, InterviewState.Listening);
            Add(InterviewState.Speaking, InterviewTrigger.UserSpeechStarted, InterviewState.Interrupted);
            Add(InterviewState.Speaking, InterviewTrigger.InterruptRequested, InterviewState.Interrupted);

            Add(InterviewState.Interrupted, InterviewTrigger.InterruptHandled, InterviewState.Listening);
            Add(InterviewState.Interrupted, InterviewTrigger.UserSpeechStopped, InterviewState.Processing);
            Add(InterviewState.Interrupted, InterviewTrigger.AiAudioStarted, InterviewState.Speaking);

            foreach (var s in ActiveStates)
            {
                Add(s, InterviewTrigger.ConnectionLost, InterviewState.Reconnecting);
            }
            Add(InterviewState.Connecting, InterviewTrigger.ConnectionLost, InterviewState.Error);

            foreach (InterviewState s in Enum.GetValues(typeof(InterviewState)))
            {
                if (s != InterviewState.Completed && s != InterviewState.Idle)
                {
                    Add(s, InterviewTrigger.Complete, InterviewState.Completed);
                }
                if (s != InterviewState.Completed && s != InterviewState.Error)
                {
                    Add(s, InterviewTrigger.Fail, InterviewState.Error);
                }
                Add(s, InterviewTrigger.Reset, InterviewState.Idle);
            }
        }

        public InterviewState State { get; private set; }

        /// <summary>Raised after every successful transition (on the caller's thread).</summary>
        public event Action<StateChange> StateChanged;

        public bool IsActive => ActiveStates.Contains(State);

        public bool IsTerminal => State == InterviewState.Completed || State == InterviewState.Error;

        /// <summary>Microphone audio should be streamed in these states.</summary>
        public bool ShouldStreamMicrophone =>
            State == InterviewState.Ready || State == InterviewState.Listening || State == InterviewState.Processing ||
            State == InterviewState.Speaking || State == InterviewState.Interrupted;

        public bool CanFire(InterviewTrigger trigger) => _table.ContainsKey((State, trigger));

        public bool TryFire(InterviewTrigger trigger)
        {
            if (!_table.TryGetValue((State, trigger), out var next))
            {
                return false;
            }
            var change = new StateChange(State, next, trigger);
            State = next;
            StateChanged?.Invoke(change);
            return true;
        }

        private void Add(InterviewState from, InterviewTrigger trigger, InterviewState to)
        {
            _table[(from, trigger)] = to;
        }
    }
}
