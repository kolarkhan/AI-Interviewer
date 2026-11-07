using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInterviewer.Audio;
using AIInterviewer.Core.Realtime;
using UnityEngine;

namespace AIInterviewer.Realtime
{
    /// <summary>
    /// One WebSocket connection to the backend live relay (<c>/v1/interviews/{id}/live</c>).
    ///
    /// Threading: the receive loop runs on a background thread. Interviewer audio frames and the
    /// <c>ai.audio.started</c> marker go straight to the <see cref="StreamingAudioPlayer"/> (thread-safe) so
    /// item boundaries line up exactly with the audio stream; every other event is queued and consumed on
    /// the main thread via <see cref="TryDequeue"/>.
    /// </summary>
    public sealed class LiveConnection : IDisposable
    {
        private readonly ClientWebSocket _ws = new ClientWebSocket();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<LiveServerEvent> _events = new ConcurrentQueue<LiveServerEvent>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly StreamingAudioPlayer _player;
        private int _pendingAudioFrames;

        public LiveConnection(StreamingAudioPlayer player)
        {
            _player = player;
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        }

        public bool IsOpen => _ws.State == WebSocketState.Open;

        /// <summary>Close status reported by the server (null until closed).</summary>
        public int? CloseCode { get; private set; }
        public string CloseReason { get; private set; }

        /// <summary>Bytes of interviewer audio received (diagnostics).</summary>
        public long AudioBytesReceived;

        public const string ClosedEventType = "_connection.closed";

        public async Task ConnectAsync(Uri uri, string accessToken, string audioRoute, TimeSpan timeout)
        {
            _ws.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            var full = new UriBuilder(uri) { Query = "audio_route=" + Uri.EscapeDataString(audioRoute) }.Uri;
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                connectCts.CancelAfter(timeout);
                await _ws.ConnectAsync(full, connectCts.Token);
            }
            _ = Task.Run(ReceiveLoopAsync);
        }

        public bool TryDequeue(out LiveServerEvent ev) => _events.TryDequeue(out ev);

        public Task SendTextAsync(string json) => SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text);

        /// <summary>Sends a microphone frame. Drops frames if the uplink is backed up (&gt;2 s queued).</summary>
        public void SendAudio(byte[] pcm16)
        {
            if (!IsOpen) return;
            if (Interlocked.Increment(ref _pendingAudioFrames) > 50)
            {
                Interlocked.Decrement(ref _pendingAudioFrames);
                return;
            }
            _ = SendAudioCoreAsync(pcm16);
        }

        private async Task SendAudioCoreAsync(byte[] pcm16)
        {
            try
            {
                await SendAsync(pcm16, WebSocketMessageType.Binary);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingAudioFrames);
            }
        }

        private async Task SendAsync(byte[] data, WebSocketMessageType type)
        {
            if (!IsOpen) return;
            try
            {
                await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                return; // connection is closing
            }
            try
            {
                if (IsOpen)
                {
                    await _ws.SendAsync(new ArraySegment<byte>(data), type, true, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException ||
                                       ex is ObjectDisposedException)
            {
                // The receive loop reports the closure.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        CloseCode = (int?)result.CloseStatus ?? (int)WebSocketCloseStatus.NormalClosure;
                        CloseReason = result.CloseStatusDescription;
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        AudioBytesReceived += message.Length;
                        _player.Enqueue(message.GetBuffer(), 0, (int)message.Length);
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    LiveServerEvent ev;
                    try
                    {
                        ev = LiveProtocol.Parse(json);
                    }
                    catch (Exception)
                    {
                        Debug.LogWarning("[Live] Ignoring malformed server message");
                        continue;
                    }
                    if (ev.Type == LiveProtocol.ServerEvents.AiAudioStarted)
                    {
                        _player.BeginItem(ev.ItemId); // before the audio frames that follow it
                    }
                    else if (ev.Type == LiveProtocol.ServerEvents.AiAudioDone)
                    {
                        _player.MarkEndOfStream();
                    }
                    _events.Enqueue(ev);
                }
            }
            catch (OperationCanceledException)
            {
                CloseCode = CloseCode ?? LiveProtocol.CloseCodes.Normal;
            }
            catch (Exception ex) when (ex is WebSocketException || ex is IOException || ex is ObjectDisposedException)
            {
                CloseCode = CloseCode ?? 1006; // abnormal closure (network loss)
                CloseReason = ex.Message;
            }
            finally
            {
                if (CloseCode == null) CloseCode = 1006;
                _events.Enqueue(new LiveServerEvent { Type = ClosedEventType, Code = CloseCode.ToString(), Message = CloseReason });
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closing", cts.Token);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
            finally
            {
                _cts.Cancel();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _ws.Dispose();
            _cts.Dispose();
        }
    }
}
