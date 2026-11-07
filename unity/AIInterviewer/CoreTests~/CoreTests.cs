using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIInterviewer.Core.Api;
using AIInterviewer.Core.Audio;
using AIInterviewer.Core.Interview;
using AIInterviewer.Core.LipSync;
using AIInterviewer.Core.Realtime;
using AIInterviewer.Core.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AIInterviewer.CoreTests
{
    public class StateMachineTests
    {
        [Fact]
        public void HappyPathTurnTaking()
        {
            var sm = new InterviewStateMachine();
            var seen = new List<InterviewState>();
            sm.StateChanged += c => seen.Add(c.To);
            Assert.True(sm.TryFire(InterviewTrigger.Connect));
            Assert.True(sm.TryFire(InterviewTrigger.SessionReady));
            Assert.True(sm.TryFire(InterviewTrigger.AiAudioStarted));
            Assert.True(sm.TryFire(InterviewTrigger.AiPlaybackFinished));
            Assert.True(sm.TryFire(InterviewTrigger.UserSpeechStopped));
            Assert.True(sm.TryFire(InterviewTrigger.AiAudioStarted));
            Assert.True(sm.TryFire(InterviewTrigger.UserSpeechStarted));   // barge-in
            Assert.Equal(InterviewState.Interrupted, sm.State);
            Assert.True(sm.TryFire(InterviewTrigger.UserSpeechStopped));
            Assert.Equal(InterviewState.Processing, sm.State);
            Assert.True(sm.TryFire(InterviewTrigger.Complete));
            Assert.True(sm.IsTerminal);
            Assert.Equal(new[]
            {
                InterviewState.Connecting, InterviewState.Ready, InterviewState.Speaking, InterviewState.Listening,
                InterviewState.Processing, InterviewState.Speaking, InterviewState.Interrupted,
                InterviewState.Processing, InterviewState.Completed
            }, seen);
        }

        [Fact]
        public void InvalidTransitionsAreIgnored()
        {
            var sm = new InterviewStateMachine();
            Assert.False(sm.TryFire(InterviewTrigger.AiAudioStarted));
            Assert.False(sm.TryFire(InterviewTrigger.Complete)); // nothing to complete from Idle
            Assert.Equal(InterviewState.Idle, sm.State);
        }

        [Fact]
        public void ConnectionLossAndRecovery()
        {
            var sm = new InterviewStateMachine();
            sm.TryFire(InterviewTrigger.Connect);
            sm.TryFire(InterviewTrigger.SessionReady);
            sm.TryFire(InterviewTrigger.AiAudioStarted);
            Assert.True(sm.TryFire(InterviewTrigger.ConnectionLost));
            Assert.Equal(InterviewState.Reconnecting, sm.State);
            Assert.False(sm.ShouldStreamMicrophone);
            Assert.True(sm.TryFire(InterviewTrigger.SessionReady));
            Assert.Equal(InterviewState.Ready, sm.State);
        }

        [Fact]
        public void FailureFromErrorDoesNotLoop()
        {
            var sm = new InterviewStateMachine();
            sm.TryFire(InterviewTrigger.Connect);
            Assert.True(sm.TryFire(InterviewTrigger.Fail));
            Assert.False(sm.TryFire(InterviewTrigger.Fail));
            Assert.True(sm.TryFire(InterviewTrigger.Connect)); // retry
        }

        [Fact]
        public void ConnectionLostWhileConnectingIsError()
        {
            var sm = new InterviewStateMachine();
            sm.TryFire(InterviewTrigger.Connect);
            sm.TryFire(InterviewTrigger.ConnectionLost);
            Assert.Equal(InterviewState.Error, sm.State);
        }
    }

    public class ProtocolTests
    {
        [Fact]
        public void ClientMessagesMatchBackendSchema()
        {
            var t = JObject.Parse(LiveProtocol.Truncate("item_1", 1500));
            Assert.Equal("playback.truncate", (string)t["type"]);
            Assert.Equal("item_1", (string)t["item_id"]);
            Assert.Equal(1500, (int)t["audio_end_ms"]);
            Assert.Equal("{\"type\":\"input.mute\",\"muted\":true}", LiveProtocol.Mute(true));
            Assert.Equal("{\"type\":\"interview.end\"}", LiveProtocol.End());
            Assert.Equal("{\"type\":\"response.cancel\"}", LiveProtocol.Cancel());
        }

        [Fact]
        public void ParsesSessionReady()
        {
            var ev = LiveProtocol.Parse("{\"type\":\"session.ready\",\"protocol_version\":1,\"session_id\":\"s\"," +
                                        "\"resumed\":false,\"demo\":true,\"interviewer_name\":\"Alex\"," +
                                        "\"audio\":{\"format\":\"pcm16\",\"sample_rate\":24000,\"channels\":1}," +
                                        "\"elapsed_seconds\":0,\"remaining_seconds\":300,\"live_transcript\":true}");
            Assert.Equal(LiveProtocol.ServerEvents.SessionReady, ev.Type);
            Assert.True(ev.Demo);
            Assert.Equal(24000, ev.Audio.SampleRate);
            Assert.Equal(300, ev.RemainingSeconds);
        }

        [Fact]
        public void ParsesErrorsAndUnknownFields()
        {
            var ev = LiveProtocol.Parse("{\"type\":\"error\",\"code\":\"bad_message\",\"message\":\"x\",\"fatal\":false,\"new_field\":1}");
            Assert.Equal("bad_message", ev.Code);
            Assert.False(ev.Fatal);
            Assert.Throws<JsonException>(() => LiveProtocol.Parse("{\"x\":1}"));
        }

        [Fact]
        public void RetryableCloseCodes()
        {
            Assert.True(LiveProtocol.CloseCodes.IsRetryable(1006));
            Assert.True(LiveProtocol.CloseCodes.IsRetryable(4503));
            Assert.False(LiveProtocol.CloseCodes.IsRetryable(4401));
            Assert.False(LiveProtocol.CloseCodes.IsRetryable(4410));
        }
    }

    public class AudioTests
    {
        [Fact]
        public void Pcm16RoundTrip()
        {
            var input = new[] { 0f, 0.5f, -0.5f, 1f, -1f, 1.5f };
            var bytes = Pcm16.FromFloats(input, 0, input.Length);
            Assert.Equal(12, bytes.Length);
            var back = Pcm16.ToFloats(bytes, 0, bytes.Length);
            for (int i = 0; i < 5; i++) Assert.InRange(back[i] - input[i], -0.0002f, 0.0002f);
            Assert.InRange(back[5], 0.999f, 1f); // clipped
            // Little-endian: 0.5 * 32767 = 16384 (rounded) -> 0x00 0x40
            Assert.Equal(0x00, bytes[2]);
            Assert.Equal(0x40, bytes[3]);
        }

        [Fact]
        public void RingBufferWrapsAndTracksPositions()
        {
            var rb = new PcmRingBuffer(8);
            rb.Write(Enumerable.Range(0, 6).Select(i => (float)i).ToArray(), 0, 6);
            var dst = new float[4];
            Assert.Equal(4, rb.Read(dst, 0, 4));
            Assert.Equal(new float[] { 0, 1, 2, 3 }, dst);
            rb.Write(new float[] { 6, 7, 8, 9, 10 }, 0, 5); // wraps
            var all = new float[10];
            Assert.Equal(7, rb.Read(all, 0, 10));
            Assert.Equal(new float[] { 4, 5, 6, 7, 8, 9, 10 }, all.Take(7));
            Assert.Equal(11, rb.TotalWritten);
            Assert.Equal(11, rb.TotalRead);
            Assert.Equal(0, rb.Overflowed);
        }

        [Fact]
        public void RingBufferOverflowDropsOldest()
        {
            var rb = new PcmRingBuffer(4);
            rb.Write(new float[] { 1, 2, 3, 4, 5, 6 }, 0, 6);
            var dst = new float[4];
            rb.Read(dst, 0, 4);
            Assert.Equal(new float[] { 3, 4, 5, 6 }, dst);
            Assert.Equal(2, rb.Overflowed);
        }

        [Fact]
        public void RingBufferClearAdvancesReadPosition()
        {
            var rb = new PcmRingBuffer(16);
            rb.Write(new float[10], 0, 10);
            rb.Read(new float[3], 0, 3);
            Assert.Equal(7, rb.Clear());
            Assert.Equal(10, rb.TotalRead);
            Assert.Equal(0, rb.Count);
        }

        [Fact]
        public async Task RingBufferIsThreadSafe()
        {
            var rb = new PcmRingBuffer(48000);
            const int total = 200000;
            var writer = Task.Run(() =>
            {
                var chunk = new float[480];
                int written = 0;
                while (written < total)
                {
                    if (rb.Count < 40000)
                    {
                        for (int i = 0; i < chunk.Length; i++) chunk[i] = written + i;
                        rb.Write(chunk, 0, chunk.Length);
                        written += chunk.Length;
                    }
                }
            });
            long expected = 0;
            var buf = new float[1024];
            while (expected < total)
            {
                int n = rb.Read(buf, 0, buf.Length);
                for (int i = 0; i < n; i++)
                {
                    Assert.Equal(expected, (long)buf[i]);
                    expected++;
                }
            }
            await writer;
        }

        [Theory]
        [InlineData(48000, 24000)]
        [InlineData(44100, 24000)]
        [InlineData(24000, 48000)]
        [InlineData(16000, 24000)]
        public void ResamplerPreservesDurationAndFrequency(int inRate, int outRate)
        {
            const double freq = 440;
            var r = new StreamingResampler(inRate, outRate);
            var output = new List<float>();
            int total = inRate; // 1 second
            int pos = 0;
            var rnd = new Random(1);
            while (pos < total)
            {
                int n = Math.Min(total - pos, rnd.Next(1, 700)); // irregular chunk sizes
                var chunk = new float[n];
                for (int i = 0; i < n; i++) chunk[i] = (float)Math.Sin(2 * Math.PI * freq * (pos + i) / inRate);
                r.Process(chunk, 0, n, output);
                pos += n;
            }
            Assert.InRange(output.Count, outRate - 3, outRate + 1);
            // Compare against the ideal sine at the output rate (linear interpolation error is small).
            double maxErr = 0;
            for (int i = 0; i < output.Count; i++)
            {
                double ideal = Math.Sin(2 * Math.PI * freq * i / outRate);
                maxErr = Math.Max(maxErr, Math.Abs(ideal - output[i]));
            }
            Assert.True(maxErr < 0.03, $"max error {maxErr}");
        }

        [Fact]
        public void PlaybackTrackerReportsHeardMilliseconds()
        {
            var t = new PlaybackTracker(24000);
            t.BeginItem("a", 0);
            t.BeginItem("b", 48000);
            Assert.True(t.TryGetPlaying(36000, out var item, out var ms));
            Assert.Equal("a", item);
            Assert.Equal(1500, ms);
            Assert.True(t.TryGetPlaying(60000, out item, out ms));
            Assert.Equal("b", item);
            Assert.Equal(500, ms);
            Assert.False(new PlaybackTracker(24000).TryGetPlaying(10, out _, out _));
        }

        [Fact]
        public void EchoGateSilencesEchoButPassesLouderSpeech()
        {
            var gate = new EchoGate(BargeInMode.VoiceWithEchoGate) { HoldFrames = 0 };
            float[] Frame(float amp) => Enumerable.Range(0, 960).Select(i => amp * (float)Math.Sin(i * 0.1)).ToArray();

            var echo = Frame(0.05f);       // speaker bleed
            Assert.False(gate.Process(echo, 0, echo.Length, playbackRms: 0.1f, aiSpeaking: true));
            Assert.All(echo, s => Assert.Equal(0f, s));

            var speech = Frame(0.6f);      // candidate talking over the AI
            Assert.True(gate.Process(speech, 0, speech.Length, playbackRms: 0.1f, aiSpeaking: true));
            Assert.True(gate.LastFrameWasBargeIn);

            var quiet = Frame(0.02f);      // AI not speaking: everything passes
            Assert.True(gate.Process(quiet, 0, quiet.Length, playbackRms: 0f, aiSpeaking: false));
            Assert.NotEqual(0f, quiet[5]);

            var tapOnly = new EchoGate(BargeInMode.TapOnly);
            var loud = Frame(0.9f);
            Assert.False(tapOnly.Process(loud, 0, loud.Length, 0.1f, true));
        }
    }

    public class LipSyncTests
    {
        private static float[] Tone(int rate, double seconds, params (double freq, double amp)[] parts)
        {
            int n = (int)(rate * seconds);
            var s = new float[n];
            for (int i = 0; i < n; i++)
                foreach (var (f, a) in parts) s[i] += (float)(a * Math.Sin(2 * Math.PI * f * i / rate));
            return s;
        }

        private static VisemeFrame Analyse(float[] samples, int rate = 48000)
        {
            var a = new SpectralVisemeAnalyzer(rate);
            // Feed in 20 ms blocks like OnAudioFilterRead, keep the last frame.
            int block = rate / 50;
            for (int i = 0; i + block <= samples.Length; i += block) a.Process(samples, i, block);
            var f = new VisemeFrame();
            a.GetLatest(f);
            return f;
        }

        [Fact]
        public void SilenceIsNeutral()
        {
            var f = Analyse(new float[48000]);
            Assert.Equal(Viseme.Sil, f.Dominant());
            Assert.Equal(0f, f.JawOpen);
        }

        [Fact]
        public void LoudnessOpensJaw()
        {
            var quiet = Analyse(Tone(48000, 0.3, (700, 0.02), (1100, 0.02)));
            var loud = Analyse(Tone(48000, 0.3, (700, 0.3), (1100, 0.3)));
            Assert.True(loud.JawOpen > quiet.JawOpen + 0.2f, $"{loud.JawOpen} vs {quiet.JawOpen}");
        }

        [Fact]
        public void VowelLikeSpectraMapToPlausibleShapes()
        {
            // "ah": F1 ~ 800-1000 Hz; "ee": low F1 + high F2 ~ 2300 Hz; "oo": energy around 300-500 Hz.
            var ah = Analyse(Tone(48000, 0.3, (950, 0.3), (1200, 0.15)));
            var ee = Analyse(Tone(48000, 0.3, (280, 0.08), (2300, 0.3)));
            var oo = Analyse(Tone(48000, 0.3, (350, 0.3), (500, 0.15)));
            Assert.Equal(Viseme.AA, ah.Dominant());
            Assert.Contains(ee.Dominant(), new[] { Viseme.IH, Viseme.E });
            Assert.Contains(oo.Dominant(), new[] { Viseme.OU, Viseme.OH });
            Assert.True(ah.JawOpen > ee.JawOpen, "AA should open the jaw more than EE");
        }

        [Fact]
        public void HissMapsToSibilant()
        {
            var rnd = new Random(3);
            var hiss = new float[48000 / 2];
            float prev = 0;
            for (int i = 0; i < hiss.Length; i++)
            {
                float white = (float)(rnd.NextDouble() * 2 - 1) * 0.3f;
                hiss[i] = white - prev; // crude high-pass emphasis
                prev = white;
            }
            Assert.Equal(Viseme.SS, Analyse(hiss).Dominant());
        }

        [Fact]
        public void SmootherConvergesAndReturnsToNeutral()
        {
            var s = new VisemeSmoother();
            var target = new VisemeFrame { JawOpen = 1f };
            target[Viseme.AA] = 1f;
            for (int i = 0; i < 30; i++) s.Step(target, 1f / 60f);
            Assert.True(s.Current.JawOpen > 0.95f);
            var neutral = new VisemeFrame();
            neutral.SetNeutral();
            for (int i = 0; i < 60; i++) s.Step(neutral, 1f / 60f);
            Assert.True(s.Current.JawOpen < 0.02f);
            Assert.Equal(Viseme.Sil, s.Current.Dominant());
        }
    }

    public class MiscTests
    {
        [Fact]
        public void TranscriptMergesDeltasAndFinals()
        {
            var log = new TranscriptLog(maxLines: 3);
            log.AppendInterviewerDelta("i1", "Hello ");
            log.AppendInterviewerDelta("i1", "there");
            Assert.Equal("Hello there", log.Lines[0].Text);
            log.CompleteInterviewer("i1", "Hello there!", interrupted: true);
            log.AppendInterviewerDelta("i1", " ignored");
            Assert.Equal("Hello there!", log.Lines[0].Text);
            Assert.True(log.Lines[0].Interrupted);
            log.AddCandidate("u1", "  My answer ");
            log.AddCandidate("u2", "   ");
            Assert.Equal(2, log.Lines.Count);
            Assert.Equal("My answer", log.Lines[1].Text);
            log.AddCandidate("u3", "a");
            log.AddCandidate("u4", "b");
            Assert.Equal(3, log.Lines.Count);
            Assert.Equal("My answer", log.Lines[0].Text); // oldest line (i1) was evicted
        }

        [Fact]
        public void BackoffGrowsWithCap()
        {
            var p = new ReconnectPolicy(maxAttempts: 5, baseDelaySeconds: 0.5, maxDelaySeconds: 4, seed: 7);
            var delays = new List<double>();
            while (p.CanRetry) delays.Add(p.NextDelay().TotalSeconds);
            Assert.Equal(5, delays.Count);
            Assert.All(delays, d => Assert.InRange(d, 0.2, 4.0));
            p.Reset();
            Assert.True(p.CanRetry);
        }

        [Fact]
        public void ApiErrorEnvelopeIsParsed()
        {
            var ex = ApiException.FromResponse(402,
                "{\"error\":{\"code\":\"plan_limit\",\"message\":\"Limit reached\",\"details\":{\"upgrade_available\":true}}}");
            Assert.Equal(ApiFailureKind.PlanLimit, ex.Kind);
            Assert.Equal("plan_limit", ex.Code);
            Assert.Equal("Limit reached", ex.Message);
            Assert.Contains("upgrade_available", ex.RawDetails);
            var html = ApiException.FromResponse(502, "<html>Bad gateway</html>");
            Assert.Equal(ApiFailureKind.Server, html.Kind);
            Assert.True(html.IsRetryable);
        }

        [Fact]
        public void ModelsDeserializeBackendPayloads()
        {
            var report = JsonConvert.DeserializeObject<ReportEnvelope>(
                "{\"status\":\"ready\",\"report\":{\"id\":\"r\",\"session_id\":\"s\",\"tier\":\"basic\",\"is_demo\":false," +
                "\"overall_score\":67,\"categories\":{\"technical_knowledge\":{\"assessed\":false,\"score\":null,\"rationale\":\"n/a\"}}," +
                "\"summary\":\"S\",\"strengths\":[\"a\"],\"improvements\":[],\"recommended_topics\":[],\"confidence_indicators\":[]," +
                "\"answer_feedback\":[],\"practice_plan\":[],\"disclaimer\":\"D\",\"created_at\":\"2026-10-15T10:00:00+00:00\"}}");
            Assert.Equal(67, report.Report.OverallScore);
            Assert.Null(report.Report.Categories["technical_knowledge"].Score);

            var cfg = JsonConvert.SerializeObject(new InterviewConfig { RoleId = "backend_developer" }.CloneForRequest());
            Assert.DoesNotContain("custom_role_title", cfg);
            Assert.DoesNotContain("role_title", cfg);

            var upd = JsonConvert.SerializeObject(new ProfileUpdate { Preferences = new PreferencesUpdate { Voice = "cedar" } });
            Assert.Equal("{\"preferences\":{\"voice\":\"cedar\"}}", upd);
        }
    }
}

namespace AIInterviewer.CoreTests
{
    using AIInterviewer.Core.Graphics;

    public class GraphicsTests
    {
        private const string Head =
            "M186 422 L178 350 C132 318 108 272 110 218 C112 146 172 94 246 94 C318 94 362 144 366 202 C367 218 370 230 " +
            "376 240 L398 274 C401 280 398 285 391 286 L378 288 C380 300 378 316 374 326 C370 340 358 348 342 348 L324 348 " +
            "C314 348 308 356 308 366 L307 398 Z M205.22 275.40 A50.0 50.0 0 0 1 205.22 196.60 A12.0 12.0 0 0 1 219.99 " +
            "215.51 A26.0 26.0 0 0 0 219.99 256.49 A12.0 12.0 0 0 1 205.22 275.40 Z";

        [Fact]
        public void ParsesTheAvinteraHead()
        {
            var path = SvgPath.Parse(Head);
            Assert.Equal(2, path.FindAll(c => c.Op == PathOp.MoveTo).Count);
            Assert.Equal(2, path.FindAll(c => c.Op == PathOp.Close).Count);
            var (minX, minY, maxX, maxY) = SvgPath.Bounds(path);
            Assert.InRange(minX, 100, 112);   // control points reach ~108
            Assert.InRange(minY, 90, 95);
            Assert.InRange(maxX, 398, 402);
            Assert.InRange(maxY, 421, 423);
        }

        [Fact]
        public void ImplicitLineTosAndHV()
        {
            var p = SvgPath.Parse("M18.0 0 266.5 -730.0H498.5L755.0 0V-10h5v5z");
            Assert.Equal(PathOp.MoveTo, p[0].Op);
            Assert.Equal(PathOp.LineTo, p[1].Op);
            Assert.Equal(266.5, p[1].X);
            Assert.Equal(-730.0, p[1].Y);
            Assert.Equal(498.5, p[2].X);
            Assert.Equal(-730.0, p[2].Y);   // H keeps y
            Assert.Equal(755.0, p[4].X);
            Assert.Equal(-10, p[4].Y);      // V keeps x
            Assert.Equal(760.0, p[5].X);    // relative h
            Assert.Equal(-5.0, p[6].Y);     // relative v
            Assert.Equal(PathOp.Close, p[7].Op);
        }

        [Fact]
        public void QuadraticBecomesExactCubic()
        {
            var p = SvgPath.Parse("M0 0 Q 50 100 100 0");
            var c = p[1];
            Assert.Equal(PathOp.CubicTo, c.Op);
            Assert.Equal(100.0 / 3, c.C1X, 6);
            Assert.Equal(200.0 / 3, c.C1Y, 6);
            Assert.Equal(200.0 / 3, c.C2X, 6);
            Assert.Equal(200.0 / 3, c.C2Y, 6);
        }

        [Fact]
        public void HalfCircleArcStaysOnTheCircle()
        {
            // Semicircle of radius 50 around (50,0), sweeping through y = +50.
            var p = SvgPath.Parse("M0 0 A50 50 0 0 0 100 0");
            var cubics = p.FindAll(c => c.Op == PathOp.CubicTo);
            Assert.Equal(2, cubics.Count);
            Assert.Equal(100, cubics[1].X, 6);
            Assert.Equal(0, cubics[1].Y, 6);
            // Mid point of the arc (end of first segment) lies on the circle.
            double dx = cubics[0].X - 50, dy = cubics[0].Y;
            Assert.Equal(50, Math.Sqrt(dx * dx + dy * dy), 4);
            Assert.True(Math.Abs(dy) > 49); // apex of the half circle
        }

        [Fact]
        public void RejectsGarbage()
        {
            Assert.Throws<FormatException>(() => SvgPath.Parse("10 20"));
            Assert.Throws<FormatException>(() => SvgPath.Parse("M0 0 X 1 2"));
        }

        [Fact]
        public void CssCubicBezierEasing()
        {
            var fade = new CubicBezierEasing(0.2, 0.7, 0.2, 1);
            Assert.Equal(0, fade.Evaluate(0));
            Assert.Equal(1, fade.Evaluate(1));
            Assert.True(fade.Evaluate(0.3) > 0.6); // strong ease-out
            var spring = new CubicBezierEasing(0.3, 1.4, 0.5, 1);
            double peak = 0;
            for (double x = 0; x <= 1; x += 0.01) peak = Math.Max(peak, spring.Evaluate(x));
            Assert.InRange(peak, 1.05, 1.25);     // overshoots like the CSS bar animation
            var linear = new CubicBezierEasing(0, 0, 1, 1);
            Assert.Equal(0.37, linear.Evaluate(0.37), 4);
        }
    }
}

namespace AIInterviewer.CoreTests
{
    using System.IO;
    using System.Text;
    using AIInterviewer.Core.Audio;

    public class WavFileTests
    {
        private static byte[] Wav(short[] samples, int rate = 24000, bool extraChunk = false)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int extra = extraChunk ? 8 + 5 + 1 : 0; // odd-sized LIST chunk + pad byte
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + extra + samples.Length * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
            w.Write((short)2); w.Write((short)16);
            if (extraChunk)
            {
                w.Write(Encoding.ASCII.GetBytes("LIST")); w.Write(5); w.Write(new byte[] { 1, 2, 3, 4, 5, 0 });
            }
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(samples.Length * 2);
            foreach (var s in samples) w.Write(s);
            return ms.ToArray();
        }

        [Fact]
        public void DecodesPcm16Mono()
        {
            var pcm = WavFile.Decode(Wav(new short[] { 0, 16384, -32768, 32767 }));
            Assert.NotNull(pcm);
            Assert.Equal(24000, pcm.SampleRate);
            Assert.Equal(1, pcm.Channels);
            Assert.Equal(4, pcm.Samples.Length);
            Assert.Equal(0.5f, pcm.Samples[1], 4);
            Assert.Equal(-1f, pcm.Samples[2], 4);
        }

        [Fact]
        public void SkipsUnknownOddSizedChunks()
        {
            var pcm = WavFile.Decode(Wav(new short[] { 100, 200, 300 }, 16000, extraChunk: true));
            Assert.NotNull(pcm);
            Assert.Equal(16000, pcm.SampleRate);
            Assert.Equal(3, pcm.Samples.Length);
        }

        [Fact]
        public void RejectsNonWav()
        {
            Assert.Null(WavFile.Decode(null));
            Assert.Null(WavFile.Decode(Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"x\"}} padding padding padding padding")));
        }
    }
}
