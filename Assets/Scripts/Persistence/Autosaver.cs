using System;

namespace MediaTrip.Persistence
{
    /// <summary>
    /// Debounced write scheduler with no Unity dependency, so it is testable with a fake clock.
    /// <see cref="MarkDirty"/> arms it; <see cref="Tick"/> (called every frame) flushes once
    /// edits have been quiet for <see cref="DebounceSeconds"/>, or once <see cref="MaxLatencySeconds"/>
    /// have passed since the first unsaved edit, whichever comes first.
    /// </summary>
    public sealed class Autosaver
    {
        private readonly Func<double> _clock;
        private readonly Action _flush;
        private bool _dirty;
        private double _firstDirtyAt;
        private double _lastDirtyAt;

        public double DebounceSeconds { get; set; } = 0.75;
        public double MaxLatencySeconds { get; set; } = 5.0;
        public bool IsDirty => _dirty;
        public int SaveCount { get; private set; }
        public Exception LastError { get; private set; }

        /// <summary>Raised on the calling thread when a flush throws. The dirty flag stays set so it retries.</summary>
        public event Action<Exception> SaveFailed;
        public event Action Saved;

        public Autosaver(Func<double> clock, Action flush)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _flush = flush ?? throw new ArgumentNullException(nameof(flush));
        }

        public void MarkDirty()
        {
            var now = _clock();
            if (!_dirty) _firstDirtyAt = now;
            _dirty = true;
            _lastDirtyAt = now;
        }

        /// <summary>Call once per frame. Returns true if a save ran.</summary>
        public bool Tick()
        {
            if (!_dirty) return false;
            var now = _clock();
            var quietFor = now - _lastDirtyAt;
            var pendingFor = now - _firstDirtyAt;
            if (quietFor < DebounceSeconds && pendingFor < MaxLatencySeconds) return false;
            return FlushNow();
        }

        /// <summary>Save immediately if dirty. Returns true if a save ran successfully.</summary>
        public bool FlushNow()
        {
            if (!_dirty) return false;
            try
            {
                _flush();
                _dirty = false;
                LastError = null;
                SaveCount++;
                Saved?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                // Push the retry out by one debounce window rather than hammering the disk.
                _lastDirtyAt = _clock();
                _firstDirtyAt = _lastDirtyAt;
                SaveFailed?.Invoke(ex);
                return false;
            }
        }
    }
}
