using System;
using System.IO;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MediaTrip.UI.Transfer
{
    public static class FilePickerFactory
    {
        public static IFilePicker Create()
        {
#if UNITY_EDITOR
            return new EditorFilePicker();
#elif UNITY_IOS
            return new IosFilePicker();
#else
            return new DesktopFilePicker();
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor on Windows and Mac: the normal OS open/save dialogs.</summary>
    public sealed class EditorFilePicker : IFilePicker
    {
        public string Name => "file dialog";
        public bool CanPick => true;

        public void PickImport(Action<string> onPicked, Action<string> onCancelled)
        {
            var path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Import a trip (.zip or bundle .json) or a single document (.json)", "", new[] { "Trip files", "zip,json", "All files", "*" });
            if (string.IsNullOrEmpty(path)) onCancelled?.Invoke(null); else onPicked?.Invoke(path);
        }

        public void Share(string sourcePath, Action<string> onDone, Action<string> onCancelled)
        {
            var ext = Path.GetExtension(sourcePath).TrimStart('.');
            var dest = UnityEditor.EditorUtility.SaveFilePanel("Save", "", Path.GetFileName(sourcePath), ext);
            if (string.IsNullOrEmpty(dest)) { onCancelled?.Invoke(null); return; }
            try
            {
                File.Copy(sourcePath, dest, true);
                onDone?.Invoke("Saved " + Path.GetFileName(dest));
            }
            catch (Exception ex) { onCancelled?.Invoke("Could not save: " + ex.Message); }
        }
    }
#endif

    /// <summary>Standalone desktop: no dialogs without a plugin, so share means "written to the Export folder, opened in the file browser" and import lists the Import folder.</summary>
    public sealed class DesktopFilePicker : IFilePicker
    {
        public string Name => "app folder";
        public bool CanPick => false;

        public void PickImport(Action<string> onPicked, Action<string> onCancelled) =>
            onCancelled?.Invoke("Put the file in " + TripTransfer.ImportDir + " and choose it from the list.");

        public void Share(string sourcePath, Action<string> onDone, Action<string> onCancelled)
        {
            try { Application.OpenURL("file://" + Path.GetDirectoryName(sourcePath)); } catch (Exception ex) { Debug.LogWarning(ex.Message); }
            onDone?.Invoke("Written to " + sourcePath);
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>iOS: UIDocumentPickerViewController for import, UIActivityViewController for share (Assets/Plugins/iOS/MediaTripFiles.mm). Results come back through NativeBridge.</summary>
    public sealed class IosFilePicker : IFilePicker
    {
        [DllImport("__Internal")] private static extern void _MediaTrip_ImportFile(string extensionsCsv);
        [DllImport("__Internal")] private static extern void _MediaTrip_ShareFile(string path);

        public string Name => "Files app";
        public bool CanPick => true;

        public void PickImport(Action<string> onPicked, Action<string> onCancelled)
        {
            NativeBridge.Expect(onPicked, onCancelled);
            try { _MediaTrip_ImportFile("zip,json"); }
            catch (Exception ex) { NativeBridge.Clear(); onCancelled?.Invoke("Native picker failed to open: " + ex.Message); }
        }

        public void Share(string sourcePath, Action<string> onDone, Action<string> onCancelled)
        {
            NativeBridge.Expect(r => onDone?.Invoke("Shared" + (string.IsNullOrEmpty(r) || r == "shared" ? "" : " via " + r)), onCancelled);
            try { _MediaTrip_ShareFile(sourcePath); }
            catch (Exception ex) { NativeBridge.Clear(); onCancelled?.Invoke("Share sheet failed to open: " + ex.Message); }
        }
    }
#endif
}
