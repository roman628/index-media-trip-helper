// Runs on the Mac at iOS build time. Compiled only when the active build target is iOS,
// so the Windows editor (no iOS module) still compiles this project.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MediaTrip.EditorTools
{
    /// <summary>
    /// Info.plist flags so the app's Documents folder shows up in the Files app (drag a zip in
    /// or out) and files can be opened in place. The native picker plugin in
    /// Assets/Plugins/iOS is compiled by Xcode automatically; nothing to add for it here.
    /// </summary>
    public sealed class IosPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            var projectPath = report.summary.outputPath;
            var plistPath = Path.Combine(projectPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogError("[MediaTrip] Info.plist not found at " + plistPath);
                return;
            }
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("UIFileSharingEnabled", true);
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            plist.WriteToFile(plistPath);

            // Make sure the plugin links against the frameworks it uses.
            var pbxPath = PBXProject.GetPBXProjectPath(projectPath);
            var pbx = new PBXProject();
            pbx.ReadFromFile(pbxPath);
            var mainTarget = pbx.GetUnityMainTargetGuid();
            var frameworkTarget = pbx.GetUnityFrameworkTargetGuid();
            foreach (var t in new[] { mainTarget, frameworkTarget })
            {
                pbx.AddFrameworkToProject(t, "UniformTypeIdentifiers.framework", true);
                pbx.AddFrameworkToProject(t, "UIKit.framework", false);
            }
            pbx.WriteToFile(pbxPath);
            Debug.Log("[MediaTrip] Info.plist: UIFileSharingEnabled + LSSupportsOpeningDocumentsInPlace set; frameworks linked.");
        }
    }
}
#endif
