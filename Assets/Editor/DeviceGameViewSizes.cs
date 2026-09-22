using System;
using System.Reflection;
using UnityEditor;

namespace MediaTrip.EditorTools
{
    /// <summary>
    /// Adds iPad and iPhone resolutions, in both orientations, to the Game view's
    /// resolution dropdown. Unity keeps these in per-user editor preferences rather than
    /// in the project, so run this once per machine.
    /// </summary>
    public static class DeviceGameViewSizes
    {
        static readonly (string name, int width, int height)[] Sizes =
        {
            ("iPad Pro 13 (4:3) Landscape", 2752, 2064),
            ("iPad Pro 13 (4:3) Portrait", 2064, 2752),
            ("iPad 9th gen (4:3) Landscape", 2160, 1620),
            ("iPad 9th gen (4:3) Portrait", 1620, 2160),
            ("iPad Pro 11 Landscape", 2420, 1668),
            ("iPad Pro 11 Portrait", 1668, 2420),
            ("iPad Air 11 / iPad 10th Landscape", 2360, 1640),
            ("iPad Air 11 / iPad 10th Portrait", 1640, 2360),
            ("iPad mini Landscape", 2266, 1488),
            ("iPad mini Portrait", 1488, 2266),
            ("iPhone 16 Pro Max Portrait", 1320, 2868),
            ("iPhone 16 Pro Max Landscape", 2868, 1320),
            ("iPhone 16 Pro Portrait", 1206, 2622),
            ("iPhone 16 Pro Landscape", 2622, 1206),
            ("iPhone 16 Portrait", 1179, 2556),
            ("iPhone 16 Landscape", 2556, 1179),
            ("iPhone SE Portrait", 750, 1334),
            ("iPhone SE Landscape", 1334, 750),
        };

        [MenuItem("Tools/Media Trip Helper/Add Device Game View Sizes")]
        public static void AddFromMenu()
        {
            var added = Add();
            EditorUtility.DisplayDialog("Game View Sizes",
                added == 0 ? "All device sizes were already present." : $"Added {added} device sizes.",
                "OK");
        }

        public static int Add()
        {
            var editorAssembly = typeof(Editor).Assembly;
            var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
            var sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
            var sizeKindType = editorAssembly.GetType("UnityEditor.GameViewSizeType");

            var instance = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var getGroup = sizesType.GetMethod("GetGroup");
            var constructor = sizeType.GetConstructor(
                new[] { sizeKindType, typeof(int), typeof(int), typeof(string) });
            var fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");

            var added = 0;
            // The iOS group only exists once iOS Build Support is installed (e.g. on the Mac).
            foreach (var groupType in new[] { GameViewSizeGroupType.Standalone, GameViewSizeGroupType.iOS })
            {
                object group;
                try { group = getGroup.Invoke(instance, new object[] { groupType }); }
                catch (TargetInvocationException) { continue; }
                if (group == null) continue;

                var existing = (string[])group.GetType().GetMethod("GetDisplayTexts").Invoke(group, null);
                var addCustomSize = group.GetType().GetMethod("AddCustomSize");

                foreach (var (name, width, height) in Sizes)
                {
                    if (Array.Exists(existing, text => text.StartsWith(name, StringComparison.Ordinal)))
                        continue;
                    addCustomSize.Invoke(group, new[] { constructor.Invoke(new[] { fixedResolution, width, height, (object)name }) });
                    added++;
                }
            }

            sizesType.GetMethod("SaveToHDD").Invoke(instance, null);
            return added;
        }
    }
}
