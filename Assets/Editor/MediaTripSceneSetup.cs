using MediaTrip.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.EditorTools
{
    /// <summary>
    /// Media Trip > Set Up Scene: creates the PanelSettings asset (runtime theme, scale with
    /// screen size at the design's 1180x820), and puts a UIDocument + AppController on a
    /// "MediaTripApp" object in the open scene. Safe to run again.
    /// </summary>
    public static class MediaTripSceneSetup
    {
        private const string PanelPath = "Assets/UI/PanelSettings.asset";
        private const string ThemePath = "Assets/UI/UnityDefaultRuntimeTheme.tss";
        private const string UxmlPath = "Assets/UI/App.uxml";

        [MenuItem("Media Trip/Set Up Scene")]
        public static void SetUp()
        {
            var tss = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tss == null || uxml == null)
            {
                Debug.LogError("Missing " + (tss == null ? ThemePath : UxmlPath) + "; reimport Assets/UI.");
                return;
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = tss;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1180, 820);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();

            var go = GameObject.Find("MediaTripApp");
            if (go == null) go = new GameObject("MediaTripApp");
            var doc = go.GetComponent<UIDocument>();
            if (doc == null) doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            if (go.GetComponent<AppController>() == null) go.AddComponent<AppController>();
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveScene(go.scene);
            Debug.Log("Media Trip scene set up: " + go.scene.path);
        }
    }
}
