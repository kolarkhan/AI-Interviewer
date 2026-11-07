using System;
using System.Collections.Generic;
using System.Text;

namespace AIInterviewer.Core.Interview
{
    public enum Speaker
    {
        Interviewer,
        Candidate
    }

    public sealed class TranscriptLine
    {
        public TranscriptLine(string itemId, Speaker speaker)
        {
            ItemId = itemId;
            Speaker = speaker;
        }

        public string ItemId { get; }
        public Speaker Speaker { get; }
        public string Text => _text.ToString();
        public bool IsFinal { get; internal set; }
        public bool Interrupted { get; internal set; }

        internal readonly StringBuilder _text = new StringBuilder();
    }

    /// <summary>Live transcript: merges streaming deltas per item and keeps a bounded history.</summary>
    public sealed class TranscriptLog
    {
        private readonly List<TranscriptLine> _lines = new List<TranscriptLine>();
        private readonly Dictionary<string, TranscriptLine> _byItem = new Dictionary<string, TranscriptLine>();
        private readonly int _maxLines;

        public TranscriptLog(int maxLines = 200)
        {
            _maxLines = maxLines;
        }

        public IReadOnlyList<TranscriptLine> Lines => _lines;

        /// <summary>Raised when a line is added or its text changes.</summary>
        public event Action<TranscriptLine> Changed;

        public void AppendInterviewerDelta(string itemId, string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            var line = GetOrAdd(itemId, Speaker.Interviewer);
            if (line.IsFinal) return;
            line._text.Append(delta);
            Changed?.Invoke(line);
        }

        public void CompleteInterviewer(string itemId, string text, bool interrupted)
        {
            var line = GetOrAdd(itemId, Speaker.Interviewer);
            if (!string.IsNullOrEmpty(text))
            {
                line._text.Clear();
                line._text.Append(text);
            }
            line.IsFinal = true;
            line.Interrupted = interrupted;
            Changed?.Invoke(line);
        }

        public void AddCandidate(string itemId, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var line = GetOrAdd(itemId ?? Guid.NewGuid().ToString("N"), Speaker.Candidate);
            line._text.Clear();
            line._text.Append(text.Trim());
            line.IsFinal = true;
            Changed?.Invoke(line);
        }

        public void Clear()
        {
            _lines.Clear();
            _byItem.Clear();
        }

        private TranscriptLine GetOrAdd(string itemId, Speaker speaker)
        {
            itemId = itemId ?? string.Empty;
            if (_byItem.TryGetValue(itemId, out var existing)) return existing;
            var line = new TranscriptLine(itemId, speaker);
            _lines.Add(line);
            _byItem[itemId] = line;
            if (_lines.Count > _maxLines)
            {
                _byItem.Remove(_lines[0].ItemId);
                _lines.RemoveAt(0);
            }
            return line;
        }
    }
}
