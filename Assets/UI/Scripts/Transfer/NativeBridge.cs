using System;
using UnityEngine;

namespace MediaTrip.UI.Transfer
{
    /// <summary>
    /// Receives UnitySendMessage callbacks from the iOS plugin. The GameObject must be named
    /// "MediaTripNative" (the plugin sends to that name). One request is outstanding at a
    /// time; a second request replaces the first's callbacks.
    /// </summary>
    public sealed class NativeBridge : MonoBehaviour
    {
        public const string ObjectName = "MediaTripNative";

        private static NativeBridge _instance;
        private static Action<string> _onOk;
        private static Action<string> _onCancel;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = GameObject.Find(ObjectName) ?? new GameObject(ObjectName);
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.GetComponent<NativeBridge>() ?? go.AddComponent<NativeBridge>();
        }

        public static void Expect(Action<string> onOk, Action<string> onCancel)
        {
            Ensure();
            _onOk = onOk;
            _onCancel = onCancel;
        }

        public static void Clear()
        {
            _onOk = null;
            _onCancel = null;
        }

        // ---- called by the plugin via UnitySendMessage

        public void OnFileImported(string path)
        {
            var cb = _onOk; Clear();
            Debug.Log("[MediaTrip] native import: " + path);
            cb?.Invoke(path);
        }

        public void OnFileImportCancelled(string reason)
        {
            var cb = _onCancel; Clear();
            Debug.Log("[MediaTrip] native import cancelled: " + reason);
            cb?.Invoke(string.IsNullOrEmpty(reason) || reason == "cancel" ? null : reason);
        }

        public void OnExportDone(string result)
        {
            var cb = _onOk; Clear();
            Debug.Log("[MediaTrip] native export: " + result);
            cb?.Invoke(result);
        }

        public void OnExportCancelled(string reason)
        {
            var cb = _onCancel; Clear();
            Debug.Log("[MediaTrip] native export cancelled: " + reason);
            cb?.Invoke(string.IsNullOrEmpty(reason) || reason == "cancel" ? null : reason);
        }

        public void OnNativeError(string message)
        {
            var cb = _onCancel; Clear();
            Debug.LogError("[MediaTrip] native error: " + message);
            cb?.Invoke("Native plugin error: " + message);
        }
    }
}
