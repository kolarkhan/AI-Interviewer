using System;
using System.Collections.Generic;

namespace AIInterviewer.Core.Audio
{
    /// <summary>
    /// Streaming linear-interpolation resampler for mono audio. Keeps state across calls so chunk
    /// boundaries do not click. Linear interpolation is adequate for speech at these rates
    /// (24 kHz &lt;-&gt; 44.1/48 kHz); input is band-limited by the microphone/codec already.
    /// </summary>
    public sealed class StreamingResampler
    {
        private readonly double _step;
        private double _position;      // fractional read position relative to the start of the pending input
        private float _previous;       // last sample of the previous chunk (index -1)
        private bool _hasPrevious;

        public StreamingResampler(int inputRate, int outputRate)
        {
            if (inputRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException();
            InputRate = inputRate;
            OutputRate = outputRate;
            _step = (double)inputRate / outputRate;
        }

        public int InputRate { get; }
        public int OutputRate { get; }

        public void Reset()
        {
            _position = 0;
            _hasPrevious = false;
            _previous = 0;
        }

        /// <summary>Resamples a chunk and appends the output samples to <paramref name="output"/>.</summary>
        public void Process(float[] input, int offset, int count, List<float> output)
        {
            if (count <= 0) return;
            if (InputRate == OutputRate)
            {
                for (int i = 0; i < count; i++) output.Add(input[offset + i]);
                return;
            }
            // Sample at virtual index k: k == -1 is the previous chunk's last sample.
            if (!_hasPrevious)
            {
                _previous = input[offset];
                _hasPrevious = true;
                _position = 0;
            }
            while (true)
            {
                int i0 = (int)Math.Floor(_position);
                if (i0 + 1 >= count)
                {
                    break;
                }
                double frac = _position - i0;
                float a = i0 < 0 ? _previous : input[offset + i0];
                float b = input[offset + i0 + 1];
                output.Add((float)(a + (b - a) * frac));
                _position += _step;
            }
            // Re-base the position so that the last input sample becomes index -1 of the next chunk.
            _position -= count;
            _previous = input[offset + count - 1];
        }

        public float[] Process(float[] input)
        {
            var list = new List<float>((int)(input.Length / _step) + 2);
            Process(input, 0, input.Length, list);
            return list.ToArray();
        }
    }
}
