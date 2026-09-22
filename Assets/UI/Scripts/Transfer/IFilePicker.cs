using System;

namespace MediaTrip.UI.Transfer
{
    /// <summary>
    /// The platform's two file gestures. The same UI code runs in the Editor (OS dialogs),
    /// in a standalone build (a known folder), and on iOS (the native plugin). Callbacks
    /// always arrive on the main thread; a null message on cancel means a plain cancel.
    /// </summary>
    public interface IFilePicker
    {
        /// <summary>Human-readable name of the route, e.g. "Files app", "file dialog", "app folder".</summary>
        string Name { get; }

        /// <summary>True when a real file chooser can be shown for import on this platform.</summary>
        bool CanPick { get; }

        /// <summary>
        /// Let the user pick a file to import. onPicked gets a readable local path.
        /// <paramref name="extensionsCsv"/> narrows what the chooser offers: "zip,json" for
        /// anything, "json" when one kind of document is being imported.
        /// </summary>
        void PickImport(Action<string> onPicked, Action<string> onCancelled, string extensionsCsv = "zip,json");

        /// <summary>
        /// Share a file the app has written: the iOS share sheet (Files, AirDrop, Mail...), a
        /// save dialog in the Editor, or a default folder in a standalone build. onDone gets a
        /// message for the toast.
        /// </summary>
        void Share(string sourcePath, Action<string> onDone, Action<string> onCancelled);
    }
}
