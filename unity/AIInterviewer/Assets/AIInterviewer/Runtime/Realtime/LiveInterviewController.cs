using System;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Audio;
using AIInterviewer.Avatar;
using AIInterviewer.Core.Api;
using AIInterviewer.Core.Audio;
using AIInterviewer.Core.Interview;
using AIInterviewer.Core.Realtime;
using AIInterviewer.Core.Util;
using AIInterviewer.Net;
using AIInterviewer.Platform;
using AIInterviewer.Scene;
using UnityEngine;

namespace AIInterviewer.Realtime
{
    public enum InterviewFailure
    {
        Network,
        Unauthorized,
        PlanLimit,
        NotFound,
        InProgressElsewhere,
        Microphone,
        Server
    }

    public sealed class InterviewFailureInfo
    {
        public InterviewFailure Kind;
        public string Message;
        public bool CanRetry;
    }

    public sealed class InterviewCompletionInfo
    {
        public string SessionId;
        public string Reason;
        public string ReportStatus;
    }

    /// <summary>
    /// Orchestrates a live interview: connection, microphone, playback, barge-in, avatar, timers and recovery.
    /// All public members are main-thread only.
    /// </summary>
    public sealed class LiveInterviewController : MonoBehaviour
    {
        private const float PingInterval = 20f;
        private const float CompletionDrainTimeout = 12f;

        private AppConfig _config;
        private AuthSession _auth;
        private BackendApi _api;
        private InterviewRoom _room;
        private MicrophoneCapture _mic;
        private NetworkMonitor _network;

        private LiveConnection _connection;
        private EchoGate _echoGate;
        private ReconnectPolicy _reconnect;
        private bool _reconnecting;
        private bool _aiAudioDone = true;
        private bool _ending;
        private bool _finished;
        private InterviewCompletionInfo _pendingCompletion;
        private float _completionDeadline;
        private float _nextPing;
        private float _remainingAtSync;
        private float _syncTime;
        private bool _clockRunning;
        private int _previousSleepTimeout;
        private bool _pausedWhileLive;

        public InterviewStateMachine StateMachine { get; private set; } = new InterviewStateMachine();
        public TranscriptLog Transcript { get; } = new TranscriptLog();
        public InterviewSession Session { get; private set; }
        public bool IsDemo { get; private set; }
        public bool IsMuted { get; private set; }
        public bool IsUserSpeaking { get; private set; }
        public bool IsWrappingUp { get; private set; }
        public bool LiveTranscriptEnabled { get; private set; } = true;
        public string InterviewerName { get; private set; } = "Alex";
        public BargeInMode BargeIn => _echoGate?.Mode ?? BargeInMode.VoiceWithEchoGate;
        public float MicLevel => _mic != null ? _mic.Level : 0f;
        public bool IsConnected => _connection != null && _connection.IsOpen;
        public int TotalSeconds => Session?.DurationSeconds ?? 0;

        public int RemainingSeconds
        {
            get
            {
                if (!_clockRunning) return Mathf.Max(0, Mathf.RoundToInt(_remainingAtSync));
                return Mathf.Max(0, Mathf.RoundToInt(_remainingAtSync - (Time.unscaledTime - _syncTime)));
            }
        }

        public event Action<InterviewState> StateChanged;
        public event Action<string> Notice;
        public event Action<InterviewCompletionInfo> Completed;
        public event Action<InterviewFailureInfo> Failed;

        public void Initialize(AppConfig config, AuthSession auth, BackendApi api, InterviewRoom room,
            MicrophoneCapture mic, NetworkMonitor network)
        {
            _config = config;
            _auth = auth;
            _api = api;
            _room = room;
            _mic = mic;
            _network = network;
            _mic.FrameReady += OnMicFrame;
            _mic.Failed += msg => Fail(InterviewFailure.Microphone, msg, canRetry: true);
        }

        /// <summary>Starts (or resumes) the live interview for an existing session.</summary>
        public async Task StartAsync(InterviewSession session, bool usingHeadphones)
        {
            Cleanup();
            Session = session;
            IsDemo = session.IsDemo;
            _finished = false;
            _ending = false;
            _pendingCompletion = null;
            IsMuted = false;
            IsWrappingUp = false;
            Transcript.Clear();
            _remainingAtSync = Mathf.Max(0, session.DurationSeconds - session.ElapsedSeconds);
            _clockRunning = false;
            _echoGate = new EchoGate(usingHeadphones ? BargeInMode.Voice : BargeInMode.VoiceWithEchoGate);
            _reconnect = new ReconnectPolicy();

            StateMachine = new InterviewStateMachine();
            StateMachine.StateChanged += OnStateChanged;
            StateMachine.TryFire(InterviewTrigger.Connect);

            _previousSleepTimeout = Screen.sleepTimeout;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (await MicrophonePermission.RequestAsync(this) != PermissionResult.Granted)
            {
                Fail(InterviewFailure.Microphone, "Microphone access is required for a voice interview.", canRetry: true);
                return;
            }
            if (!_mic.StartCapture())
            {
                return; // Failed event already raised
            }
            await ConnectAsync(initial: true);
        }

        public void ToggleMute()
        {
            if (!StateMachine.IsActive) return;
            IsMuted = !IsMuted;
            Send(LiveProtocol.Mute(IsMuted));
            Notice?.Invoke(IsMuted ? "Microphone muted" : "Microphone on");
        }

        /// <summary>Tap-to-interrupt: stops the interviewer and hands the turn to the candidate.</summary>
        public void InterruptAi()
        {
            if (StateMachine.State != InterviewState.Speaking) return;
            StopPlaybackAndTruncate();
            Send(LiveProtocol.Cancel());
            StateMachine.TryFire(InterviewTrigger.InterruptRequested);
            StateMachine.TryFire(InterviewTrigger.InterruptHandled);
        }

        /// <summary>The candidate ends the interview (after confirming in the UI).</summary>
        public async Task EndAsync()
        {
            if (_finished || _ending) return;
            _ending = true;
            _mic.StopCapture();
            StopPlaybackAndTruncate();
            if (IsConnected)
            {
                Send(LiveProtocol.End());
                float deadline = Time.unscaledTime + 6f;
                while (_pendingCompletion == null && !_finished && Time.unscaledTime < deadline)
                {
                    await Task.Yield();
                }
            }
            if (_finished) return;
            if (_pendingCompletion != null)
            {
                Finish(_pendingCompletion);
                return;
            }
            // Socket unavailable: end through REST so the report still gets generated.
            try
            {
                var s = await _api.CompleteInterviewAsync(Session.Id, "user_ended");
                Finish(new InterviewCompletionInfo { SessionId = s.Id, Reason = "user_ended", ReportStatus = s.ReportStatus });
            }
            catch (ApiException ex)
            {
                Fail(InterviewFailure.Network, "Could not end the interview: " + ex.Message, canRetry: true);
            }
        }

        /// <summary>Leaves without ending (e.g. sign-out). The server keeps the session resumable for a while.</summary>
        public void Abort()
        {
            _finished = true;
            Cleanup();
        }

        public void RetryAfterFailure()
        {
            if (Session == null) return;
            _finished = false;
            if (!_mic.IsRunning && !_mic.StartCapture()) return;
            StateMachine.TryFire(InterviewTrigger.Connect);
            MainThread.Run(() => ConnectAsync(initial: false), "Interview.Retry");
        }

        // ------------------------------------------------------------------ connection

        private async Task ConnectAsync(bool initial)
        {
            DisposeConnection();
            string token;
            try
            {
                token = await _auth.GetFreshAccessTokenAsync();
            }
            catch (ApiException ex) when (ex.Kind == ApiFailureKind.Network)
            {
                token = null;
            }
            if (string.IsNullOrEmpty(token))
            {
                if (_auth.IsSignedIn || _auth.HasStoredSession) ScheduleReconnect();
                else Fail(InterviewFailure.Unauthorized, "Your session expired. Please sign in again.", canRetry: false);
                return;
            }

            var connection = new LiveConnection(_room.Voice);
            _connection = connection;
            string path = Session.Live?.WebSocketPath ?? $"/v1/interviews/{Session.Id}/live";
            var uri = new Uri(_config.WebSocketBaseUrl + path);
            try
            {
                await connection.ConnectAsync(uri, token, _echoGate.Mode == BargeInMode.Voice ? "headset" : "speaker",
                    TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                if (_connection != connection) return; // superseded
                Debug.LogWarning("[Interview] Connect failed: " + ex.Message);
                if (initial && !_network.IsOnline)
                {
                    Fail(InterviewFailure.Network, "You're offline. Check your connection and try again.", canRetry: true);
                }
                else
                {
                    ScheduleReconnect();
                }
            }
        }

        private void ScheduleReconnect()
        {
            if (_finished || _ending || _reconnecting) return;
            if (!_reconnect.CanRetry)
            {
                Fail(InterviewFailure.Network, "We couldn't reconnect to the interviewer.", canRetry: true);
                return;
            }
            StateMachine.TryFire(InterviewTrigger.ConnectionLost);
            _reconnecting = true;
            MainThread.Run(async () =>
            {
                try
                {
                    var delay = _reconnect.NextDelay();
                    Notice?.Invoke("Reconnecting…");
                    float until = Time.unscaledTime + (float)delay.TotalSeconds;
                    while (Time.unscaledTime < until || !_network.IsOnline)
                    {
                        if (_finished) return;
                        await Task.Yield();
                    }
                    StateMachine.TryFire(InterviewTrigger.Connect);
                }
                finally
                {
                    _reconnecting = false;
                }
                await ConnectAsync(initial: false);
            }, "Interview.Reconnect");
        }

        private void Send(string json)
        {
            if (_connection == null || !_connection.IsOpen) return;
            var c = _connection;
            MainThread.Run(() => c.SendTextAsync(json), "Interview.Send");
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_connection != null)
            {
                int budget = 64;
                while (budget-- > 0 && _connection != null && _connection.TryDequeue(out var ev))
                {
                    Handle(ev);
                }
            }

            if (StateMachine.State == InterviewState.Speaking && _aiAudioDone && !_room.Voice.IsAudible)
            {
                StateMachine.TryFire(InterviewTrigger.AiPlaybackFinished);
            }

            if (_pendingCompletion != null && !_finished &&
                (!_room.Voice.IsAudible || Time.unscaledTime > _completionDeadline))
            {
                Finish(_pendingCompletion);
            }

            if (IsConnected && Time.unscaledTime >= _nextPing)
            {
                _nextPing = Time.unscaledTime + PingInterval;
                Send(LiveProtocol.Ping());
            }
        }

        private void Handle(LiveServerEvent ev)
        {
            switch (ev.Type)
            {
                case LiveProtocol.ServerEvents.SessionReady:
                    _reconnect.Reset();
                    IsDemo = ev.Demo ?? IsDemo;
                    InterviewerName = ev.InterviewerName ?? InterviewerName;
                    LiveTranscriptEnabled = ev.LiveTranscript ?? true;
                    SyncClock(ev.RemainingSeconds);
                    if (IsMuted) Send(LiveProtocol.Mute(true));
                    if (!_mic.IsRunning) _mic.StartCapture();
                    bool wasReconnecting = StateMachine.State == InterviewState.Reconnecting;
                    StateMachine.TryFire(InterviewTrigger.SessionReady);
                    if (wasReconnecting || (ev.Resumed ?? false)) Notice?.Invoke("Connected — the interview continues");
                    break;

                case LiveProtocol.ServerEvents.UserSpeechStarted:
                    IsUserSpeaking = true;
                    if (StateMachine.State == InterviewState.Speaking || _room.Voice.IsAudible)
                    {
                        StopPlaybackAndTruncate();
                        _room.Avatar?.OnInterrupted();
                    }
                    StateMachine.TryFire(InterviewTrigger.UserSpeechStarted);
                    break;

                case LiveProtocol.ServerEvents.UserSpeechStopped:
                    IsUserSpeaking = false;
                    StateMachine.TryFire(InterviewTrigger.UserSpeechStopped);
                    break;

                case LiveProtocol.ServerEvents.TranscriptUser:
                    if (!string.IsNullOrEmpty(ev.Text)) Transcript.AddCandidate(ev.ItemId, ev.Text);
                    break;

                case LiveProtocol.ServerEvents.AiAudioStarted:
                    _aiAudioDone = false;
                    StateMachine.TryFire(InterviewTrigger.AiAudioStarted);
                    break;

                case LiveProtocol.ServerEvents.AiAudioDone:
                    _aiAudioDone = true;
                    break;

                case LiveProtocol.ServerEvents.TranscriptAiDelta:
                    Transcript.AppendInterviewerDelta(ev.ItemId, ev.Delta);
                    break;

                case LiveProtocol.ServerEvents.TranscriptAiDone:
                    Transcript.CompleteInterviewer(ev.ItemId, ev.Text, ev.Interrupted ?? false);
                    break;

                case LiveProtocol.ServerEvents.AiResponseDone:
                    _aiAudioDone = true;
                    if (!_room.Voice.IsAudible)
                    {
                        StateMachine.TryFire(InterviewTrigger.AiResponseFinishedWithoutAudio);
                    }
                    break;

                case LiveProtocol.ServerEvents.InterviewTime:
                    SyncClock(ev.RemainingSeconds);
                    break;

                case LiveProtocol.ServerEvents.InterviewWrappingUp:
                    IsWrappingUp = true;
                    Notice?.Invoke("Time's up — the interviewer is wrapping up");
                    break;

                case LiveProtocol.ServerEvents.InterviewCompleted:
                    _mic.StopCapture();
                    _pendingCompletion = new InterviewCompletionInfo
                    {
                        SessionId = Session.Id, Reason = ev.Reason, ReportStatus = ev.ReportStatus
                    };
                    _completionDeadline = Time.unscaledTime + CompletionDrainTimeout;
                    break;

                case LiveProtocol.ServerEvents.Error:
                    if (ev.Fatal == true) Debug.LogWarning($"[Interview] Fatal server error {ev.Code}: {ev.Message}");
                    else if (!string.IsNullOrEmpty(ev.Message) && ev.Code != "bad_audio_frame") Notice?.Invoke(ev.Message);
                    _lastServerError = ev;
                    break;

                case LiveConnection.ClosedEventType:
                    OnClosed(int.TryParse(ev.Code, out int code) ? code : 1006);
                    break;
            }
        }

        private LiveServerEvent _lastServerError;

        private void OnClosed(int code)
        {
            DisposeConnection();
            if (_finished) return;
            if (_pendingCompletion != null || _ending) return; // completion is being finalised

            string serverMessage = _lastServerError?.Message;
            _lastServerError = null;
            switch (code)
            {
                case LiveProtocol.CloseCodes.SessionEnded:
                    MainThread.Run(FinishFromServerStateAsync, "Interview.Ended");
                    return;
                case LiveProtocol.CloseCodes.Unauthorized:
                    MainThread.Run(async () =>
                    {
                        if (await _auth.RefreshAsync()) ScheduleReconnect();
                        else Fail(InterviewFailure.Unauthorized, "Your session expired. Please sign in again.", false);
                    }, "Interview.Reauth");
                    return;
                case LiveProtocol.CloseCodes.PlanLimit:
                    Fail(InterviewFailure.PlanLimit, serverMessage ?? "You've reached your plan's limit.", canRetry: false);
                    return;
                case LiveProtocol.CloseCodes.NotFound:
                    Fail(InterviewFailure.NotFound, "This interview no longer exists.", canRetry: false);
                    return;
                case LiveProtocol.CloseCodes.Conflict:
                    Fail(InterviewFailure.InProgressElsewhere, serverMessage ?? "Another interview is in progress.", false);
                    return;
                case LiveProtocol.CloseCodes.Replaced:
                    Fail(InterviewFailure.InProgressElsewhere, "This interview continued on another device.", false);
                    return;
                case LiveProtocol.CloseCodes.Normal:
                    MainThread.Run(FinishFromServerStateAsync, "Interview.Closed");
                    return;
                default:
                    _room.Voice.Interrupt();
                    ScheduleReconnect();
                    return;
            }
        }

        private async Task FinishFromServerStateAsync()
        {
            try
            {
                var s = await _api.GetInterviewAsync(Session.Id);
                if (s.Status == "completed" || s.Status == "abandoned" || s.Status == "expired")
                {
                    Finish(new InterviewCompletionInfo { SessionId = s.Id, Reason = s.EndReason, ReportStatus = s.ReportStatus });
                }
                else
                {
                    ScheduleReconnect();
                }
            }
            catch (ApiException)
            {
                ScheduleReconnect();
            }
        }

        // ------------------------------------------------------------------ audio

        private void OnMicFrame(float[] frame)
        {
            if (IsMuted || _connection == null || !_connection.IsOpen || !StateMachine.ShouldStreamMicrophone) return;
            bool aiSpeaking = _room.Voice.IsAudible;
            _echoGate.Process(frame, 0, frame.Length, _room.Voice.PlaybackRms, aiSpeaking);
            _connection.SendAudio(Pcm16.FromFloats(frame, 0, frame.Length));
        }

        private void StopPlaybackAndTruncate()
        {
            var result = _room.Voice.Interrupt();
            if (!string.IsNullOrEmpty(result.ItemId))
            {
                Send(LiveProtocol.Truncate(result.ItemId, result.PlayedMs));
            }
            _aiAudioDone = true;
        }

        // ------------------------------------------------------------------ state & lifecycle

        private void OnStateChanged(StateChange change)
        {
            if (_room.Avatar != null)
            {
                switch (change.To)
                {
                    case InterviewState.Speaking: _room.Avatar.SetMood(AvatarMood.Speaking); break;
                    case InterviewState.Processing: _room.Avatar.SetMood(AvatarMood.Thinking); break;
                    case InterviewState.Listening:
                    case InterviewState.Interrupted: _room.Avatar.SetMood(AvatarMood.Listening); break;
                    default: _room.Avatar.SetMood(AvatarMood.Idle); break;
                }
            }
            StateChanged?.Invoke(change.To);
        }

        private void SyncClock(int? remaining)
        {
            if (!remaining.HasValue) return;
            _remainingAtSync = remaining.Value;
            _syncTime = Time.unscaledTime;
            _clockRunning = true;
        }

        private void Finish(InterviewCompletionInfo info)
        {
            if (_finished) return;
            _finished = true;
            _pendingCompletion = null;
            _clockRunning = false;
            StateMachine.TryFire(InterviewTrigger.Complete);
            Cleanup();
            Completed?.Invoke(info);
        }

        private void Fail(InterviewFailure kind, string message, bool canRetry)
        {
            if (_finished) return;
            _clockRunning = false;
            _mic.StopCapture();
            _room.Voice.Interrupt();
            DisposeConnection();
            StateMachine.TryFire(InterviewTrigger.Fail);
            Failed?.Invoke(new InterviewFailureInfo { Kind = kind, Message = message, CanRetry = canRetry });
        }

        private void DisposeConnection()
        {
            var c = _connection;
            _connection = null;
            if (c == null) return;
            MainThread.Run(async () =>
            {
                await c.CloseAsync();
                c.Dispose();
            }, "Interview.Close");
        }

        private void Cleanup()
        {
            _mic?.StopCapture();
            DisposeConnection();
            if (_room != null)
            {
                _room.Voice.Interrupt();
                _room.Avatar?.ResetFace();
                _room.Avatar?.SetMood(AvatarMood.Idle);
            }
            if (Session != null) Screen.sleepTimeout = _previousSleepTimeout;
        }

        private void OnApplicationPause(bool paused)
        {
            if (Session == null || _finished || _ending) return;
            if (paused && StateMachine.IsActive)
            {
                // Mobile OSes suspend audio and sockets in the background. Disconnect cleanly; the backend keeps
                // the session open for a grace period and resumes it (with context) when we come back.
                _pausedWhileLive = true;
                _room.Voice.Interrupt();
                DisposeConnection();
                StateMachine.TryFire(InterviewTrigger.ConnectionLost);
            }
            else if (!paused && _pausedWhileLive)
            {
                _pausedWhileLive = false;
                _reconnect.Reset();
                StateMachine.TryFire(InterviewTrigger.Connect);
                _mic.StartCapture();
                MainThread.Run(() => ConnectAsync(initial: false), "Interview.Resume");
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
