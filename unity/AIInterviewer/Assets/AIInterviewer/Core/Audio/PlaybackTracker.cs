using System;
using System.Collections.Generic;

namespace AIInterviewer.Core.Audio
{
    /// <summary>
    /// Maps the absolute playback position of the AI audio stream (in 24 kHz samples) back to the
    /// conversation item being heard, so that on interruption we can tell the server exactly how many
    /// milliseconds of that item the candidate actually heard (`conversation.item.truncate`).
    /// Thread-safe: items are registered from the network thread, positions read from the main thread.
    /// </summary>
    public sealed class PlaybackTracker
    {
        private readonly object _gate = new object();
        private readonly List<ItemSpan> _items = new List<ItemSpan>();
        private readonly int _sampleRate;

        public PlaybackTracker(int sampleRate)
        {
            _sampleRate = sampleRate;
        }

        private struct ItemSpan
        {
            public string ItemId;
            public long StartSample;
        }

        /// <summary>Registers that audio for <paramref name="itemId"/> starts at stream position <paramref name="writePosition"/>.</summary>
        public void BeginItem(string itemId, long writePosition)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            lock (_gate)
            {
                _items.Add(new ItemSpan { ItemId = itemId, StartSample = writePosition });
                if (_items.Count > 32) _items.RemoveAt(0);
            }
        }

        /// <summary>
        /// Returns the item being heard at <paramref name="playedPosition"/> and the milliseconds of it played so far.
        /// </summary>
        public bool TryGetPlaying(long playedPosition, out string itemId, out int playedMs)
        {
            lock (_gate)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i].StartSample <= playedPosition)
                    {
                        itemId = _items[i].ItemId;
                        playedMs = (int)Math.Max(0, (playedPosition - _items[i].StartSample) * 1000L / _sampleRate);
                        return true;
                    }
                }
            }
            itemId = null;
            playedMs = 0;
            return false;
        }

        public void Clear()
        {
            lock (_gate) _items.Clear();
        }
    }
}
