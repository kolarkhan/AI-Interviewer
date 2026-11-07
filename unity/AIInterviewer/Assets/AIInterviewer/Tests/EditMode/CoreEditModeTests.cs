using AIInterviewer.Core.Audio;
using AIInterviewer.Core.Interview;
using AIInterviewer.Core.LipSync;
using AIInterviewer.Core.Realtime;
using NUnit.Framework;

namespace AIInterviewer.Tests
{
    /// <summary>
    /// Runs inside Unity (Window > General > Test Runner > EditMode). The same Core logic is covered more
    /// extensively by the .NET test project in CoreTests~ (run with `dotnet test`).
    /// </summary>
    public class CoreEditModeTests
    {
        [Test]
        public void StateMachineBargeIn()
        {
            var sm = new InterviewStateMachine();
            sm.TryFire(InterviewTrigger.Connect);
            sm.TryFire(InterviewTrigger.SessionReady);
            sm.TryFire(InterviewTrigger.AiAudioStarted);
            Assert.IsTrue(sm.TryFire(InterviewTrigger.UserSpeechStarted));
            Assert.AreEqual(InterviewState.Interrupted, sm.State);
        }

        [Test]
        public void ProtocolTruncateMessage()
        {
            StringAssert.Contains("\"audio_end_ms\":1200", LiveProtocol.Truncate("item_9", 1200));
        }

        [Test]
        public void ResamplerProducesExpectedLength()
        {
            var r = new StreamingResampler(48000, 24000);
            var output = r.Process(new float[48000]);
            Assert.That(output.Length, Is.InRange(23997, 24001));
        }

        [Test]
        public void AnalyzerOpensJawForLoudVowel()
        {
            var a = new SpectralVisemeAnalyzer(48000);
            var s = new float[48000 / 5];
            for (int i = 0; i < s.Length; i++) s[i] = 0.3f * (float)System.Math.Sin(2 * System.Math.PI * 900 * i / 48000.0);
            a.Process(s, 0, s.Length);
            var f = new VisemeFrame();
            a.GetLatest(f);
            Assert.Greater(f.JawOpen, 0.3f);
        }
    }
}
