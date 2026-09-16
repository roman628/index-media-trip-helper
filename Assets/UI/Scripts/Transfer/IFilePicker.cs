using System;

namespace MediaTrip.UI.Transfer
{
    /// <summary>
    /// A platform file picker. The same UI code runs in the Editor (EditorUtility panels),
    /// in a standalone build (a folder under persistentDataPath), and on iOS (the native
    /// UIDocumentPicker plugin). Callbacks always arrive on the main thread.
    /// </summary>
    public interface IFilePicker
    {
        /// <summary>Human-readable name of the route, e.g. "Files app", "file dialog".</summary>
        string Name { get; }

        /// <summary>True when a real picker can be shown on this platform.</summary>
        bool CanPick { get; }

        /// <summary>Let the user pick a .zip or .json to import. onPicked gets a readable local path. onCancelled gets a message (null for a plain cancel).</summary>
        void PickImport(Action<string> onPicked, Action<string> onCancelled);

        /// <summary>Let the user save a file somewhere (Files app, iCloud, a folder). onDone gets a message for the toast.</summary>
        void ExportFile(string sourcePath, Action<string> onDone, Action<string> onCancelled);

        /// <summary>Share sheet (AirDrop, Mail, ...) on iOS; reveal in the OS file browser elsewhere.</summary>
        void ShareFile(string sourcePath, Action<string> onDone, Action<string> onCancelled);
    }
}
