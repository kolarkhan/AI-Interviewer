using System;

namespace AIInterviewer.Core.Audio
{
    /// <summary>Minimal RIFF/WAVE reader for 16-bit PCM (voice previews from the backend).</summary>
    public static class WavFile
    {
        public sealed class Pcm
        {
            public int SampleRate;
            public int Channels;
            /// <summary>Interleaved samples in [-1, 1].</summary>
            public float[] Samples;
        }

        /// <summary>Decodes a 16-bit PCM WAV. Returns null for anything else (no exception for bad input).</summary>
        public static Pcm Decode(byte[] wav)
        {
            if (wav == null || wav.Length < 44 || !Tag(wav, 0, "RIFF") || !Tag(wav, 8, "WAVE")) return null;
            int rate = 0, channels = 0, bits = 0;
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                int size = BitConverter.ToInt32(wav, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > wav.Length) size = wav.Length - body; // tolerate truncated sizes
                if (Tag(wav, pos, "fmt ") && size >= 16)
                {
                    int format = BitConverter.ToInt16(wav, body);
                    channels = BitConverter.ToInt16(wav, body + 2);
                    rate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                    if (format != 1) return null; // PCM only
                }
                else if (Tag(wav, pos, "data"))
                {
                    if (bits != 16 || channels < 1 || rate <= 0) return null;
                    return new Pcm { SampleRate = rate, Channels = channels, Samples = Pcm16.ToFloats(wav, body, size) };
                }
                pos = body + size + (size & 1); // chunks are word aligned
            }
            return null;
        }

        private static bool Tag(byte[] b, int at, string tag)
        {
            if (at + 4 > b.Length) return false;
            for (int i = 0; i < 4; i++)
                if (b[at + i] != (byte)tag[i]) return false;
            return true;
        }
    }
}
