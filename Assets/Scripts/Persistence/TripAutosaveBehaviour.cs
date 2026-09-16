using MediaTrip.Session;
using UnityEngine;

namespace MediaTrip.Persistence
{
    /// <summary>
    /// Drives a <see cref="TripSession"/>'s autosaver from the Unity frame loop and flushes on
    /// the iOS lifecycle events that precede the app being suspended or killed.
    /// One instance per open session; created by <see cref="Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TripAutosaveBehaviour : MonoBehaviour
    {
        public TripSession Session { get; private set; }

        public static TripAutosaveBehaviour Attach(TripSession session)
        {
            var go = new GameObject("[TripAutosave " + (session.Data.TripId ?? "?") + "]");
            go.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var behaviour = go.AddComponent<TripAutosaveBehaviour>();
            behaviour.Session = session;
            return behaviour;
        }

        public void Detach()
        {
            Session?.Autosaver.FlushNow();
            Session = null;
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        private void Update() => Session?.Autosaver.Tick();

        private void OnApplicationPause(bool paused)
        {
            if (paused) Session?.Autosaver.FlushNow();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Session?.Autosaver.FlushNow();
        }

        private void OnApplicationQuit() => Session?.Autosaver.FlushNow();

        private void OnDestroy() => Session?.Autosaver.FlushNow();
    }
}
