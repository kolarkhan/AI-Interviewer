using System;

namespace AIInterviewer.Core.Audio
{
    /// <summary>Conversions between Unity float samples [-1, 1] and PCM16 little-endian bytes.</summary>
    public static class Pcm16
    {
        public static byte[] FromFloats(float[] samples, int offset, int count)
        {
            var bytes = new byte[count * 2];
            FromFloats(samples, offset, count, bytes, 0);
            return bytes;
        }

        public static void FromFloats(float[] samples, int offset, int count, byte[] destination, int destOffset)
        {
            for (int i = 0; i < count; i++)
            {
                float f = samples[offset + i];
                if (f > 1f) f = 1f;
                else if (f < -1f) f = -1f;
                short s = (short)Math.Round(f * 32767f);
                destination[destOffset + 2 * i] = (byte)(s & 0xFF);
                destination[destOffset + 2 * i + 1] = (byte)((s >> 8) & 0xFF);
            }
        }

        /// <summary>Decodes PCM16 LE bytes. A trailing odd byte is ignored.</summary>
        public static float[] ToFloats(byte[] bytes, int offset, int count)
        {
            int n = count / 2;
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                short s = (short)(bytes[offset + 2 * i] | (bytes[offset + 2 * i + 1] << 8));
                samples[i] = s / 32768f;
            }
            return samples;
        }

        public static float Rms(float[] samples, int offset, int count)
        {
            if (count <= 0) return 0f;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                float v = samples[offset + i];
                sum += v * v;
            }
            return (float)Math.Sqrt(sum / count);
        }
    }
}
