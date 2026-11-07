using System.Collections;
using AIInterviewer.Avatar;
using AIInterviewer.Scene;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIInterviewer.Tests
{
    /// <summary>
    /// Play Mode smoke tests (Window > General > Test Runner > PlayMode). They exercise the real audio thread:
    /// streamed PCM must play, drive the lip-sync analyser, and stop instantly on interruption.
    /// </summary>
    public class RoomPlayModeTests
    {
        private GameObject _host;
        private InterviewRoom _room;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("TestHost");
            _room = InterviewRoom.Create(_host.transform);
            _room.SetAvatar(AvatarFactory.PlaceholderId, new SpectralLipSync());
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
        }

        private static byte[] Tone(float seconds, float freq = 700f, float amp = 0.4f)
        {
            int n = (int)(24000 * seconds);
            var bytes = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                short s = (short)(amp * 32767 * Mathf.Sin(2 * Mathf.PI * freq * i / 24000f));
                bytes[2 * i] = (byte)(s & 0xFF);
                bytes[2 * i + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        [UnityTest]
        public IEnumerator PlaceholderAvatarIsLabeled()
        {
            yield return null;
            Assert.IsTrue(_room.AvatarIsPlaceholder);
            Assert.IsNotNull(_room.Avatar.Rig.Head);
        }

        [UnityTest]
        public IEnumerator StreamedAudioDrivesLipSyncAndInterruptStopsIt()
        {
            var voice = _room.Voice;
            voice.BeginItem("item_test");
            var audio = Tone(2f);
            voice.Enqueue(audio, 0, audio.Length);
            voice.MarkEndOfStream();
            _room.Avatar.SetMood(AvatarMood.Speaking);

            float maxVolume = 0f;
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                maxVolume = Mathf.Max(maxVolume, _room.Avatar.SpeakingVolume);
            }
            Assert.Greater(maxVolume, 0.2f, "Lip-sync should react to the played audio");
            Assert.IsTrue(voice.IsAudible);

            var result = voice.Interrupt();
            Assert.AreEqual("item_test", result.ItemId);
            Assert.Greater(result.PlayedMs, 0);
            Assert.IsFalse(voice.HasBufferedAudio);
        }
    }
}
