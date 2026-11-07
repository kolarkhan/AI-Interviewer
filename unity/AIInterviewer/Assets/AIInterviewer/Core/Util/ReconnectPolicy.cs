using System;

namespace AIInterviewer.Core.Util
{
    /// <summary>Exponential backoff with full jitter and a bounded number of attempts.</summary>
    public sealed class ReconnectPolicy
    {
        private readonly Random _random;

        public ReconnectPolicy(int maxAttempts = 6, double baseDelaySeconds = 0.5, double maxDelaySeconds = 8,
            int? seed = null)
        {
            MaxAttempts = maxAttempts;
            BaseDelaySeconds = baseDelaySeconds;
            MaxDelaySeconds = maxDelaySeconds;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int MaxAttempts { get; }
        public double BaseDelaySeconds { get; }
        public double MaxDelaySeconds { get; }
        public int Attempt { get; private set; }

        public bool CanRetry => Attempt < MaxAttempts;

        /// <summary>Returns the delay before the next attempt and advances the counter.</summary>
        public TimeSpan NextDelay()
        {
            double cap = Math.Min(MaxDelaySeconds, BaseDelaySeconds * Math.Pow(2, Attempt));
            Attempt++;
            // "Full jitter" keeps many reconnecting clients from synchronising, with a small minimum.
            double delay = Math.Max(0.2, _random.NextDouble() * cap);
            return TimeSpan.FromSeconds(delay);
        }

        public void Reset() => Attempt = 0;
    }
}
