using System;

namespace AIInterviewer.Core.Audio
{
    /// <summary>
    /// Thread-safe single-producer/single-consumer float ring buffer for streamed audio.
    /// Written by the network thread, read by the audio thread. Critical sections are tiny
    /// (array copies), so a lock is cheaper and simpler than lock-free code here.
    /// Tracks absolute read/write positions so playback progress can be mapped back to items.
    /// </summary>
    public sealed class PcmRingBuffer
    {
        private readonly float[] _data;
        private readonly object _gate = new object();
        private int _readIndex;
        private int _count;

        public PcmRingBuffer(int capacitySamples)
        {
            if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
            _data = new float[capacitySamples];
        }

        public int Capacity => _data.Length;

        /// <summary>Total samples ever written (including those dropped by <see cref="Clear"/>).</summary>
        public long TotalWritten { get; private set; }

        /// <summary>Total samples consumed by the reader or discarded by <see cref="Clear"/>.</summary>
        public long TotalRead { get; private set; }

        /// <summary>Samples dropped because the buffer was full (should stay 0 in normal operation).</summary>
        public long Overflowed { get; private set; }

        public int Count
        {
            get { lock (_gate) return _count; }
        }

        /// <summary>Writes samples. If the buffer is full the oldest samples are overwritten.</summary>
        public void Write(float[] samples, int offset, int length)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (offset < 0 || length < 0 || offset + length > samples.Length) throw new ArgumentOutOfRangeException();
            lock (_gate)
            {
                if (length > _data.Length)
                {
                    int skip = length - _data.Length;
                    Overflowed += skip;
                    offset += skip;
                    length = _data.Length;
                    TotalWritten += skip;
                    TotalRead += skip;
                }
                int free = _data.Length - _count;
                if (length > free)
                {
                    int drop = length - free;
                    _readIndex = (_readIndex + drop) % _data.Length;
                    _count -= drop;
                    TotalRead += drop;
                    Overflowed += drop;
                }
                int writeIndex = (_readIndex + _count) % _data.Length;
                int first = Math.Min(length, _data.Length - writeIndex);
                Array.Copy(samples, offset, _data, writeIndex, first);
                if (length > first)
                {
                    Array.Copy(samples, offset + first, _data, 0, length - first);
                }
                _count += length;
                TotalWritten += length;
            }
        }

        /// <summary>Reads up to <paramref name="length"/> samples. Returns the number read (rest untouched).</summary>
        public int Read(float[] destination, int offset, int length)
        {
            lock (_gate)
            {
                int n = Math.Min(length, _count);
                int first = Math.Min(n, _data.Length - _readIndex);
                Array.Copy(_data, _readIndex, destination, offset, first);
                if (n > first)
                {
                    Array.Copy(_data, 0, destination, offset + first, n - first);
                }
                _readIndex = (_readIndex + n) % _data.Length;
                _count -= n;
                TotalRead += n;
                return n;
            }
        }

        /// <summary>Discards all buffered samples (used on interruption). Returns how many were dropped.</summary>
        public int Clear()
        {
            lock (_gate)
            {
                int dropped = _count;
                TotalRead += dropped;
                _readIndex = 0;
                _count = 0;
                return dropped;
            }
        }
    }
}
