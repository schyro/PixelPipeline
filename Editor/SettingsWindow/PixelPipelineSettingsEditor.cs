using PixelPipeline.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace PixelPipeline.Editor
{
    /// <summary>
    /// The settings asset is edited in the Pixel Pipeline window, where it is split into pages with
    /// previews. The Inspector keeps a way there and the full list behind a foldout.
    /// </summary>
    [CustomEditor(typeof(PixelPipelineSettings))]
    public sealed class PixelPipelineSettingsEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            root.Add(new HelpBox(
                "Edit these settings in the Pixel Pipeline window, where they are grouped into pages with previews.",
                HelpBoxMessageType.Info));

            var open = new Button(() => PixelPipelineSettingsWindow.Open((PixelPipelineSettings)target))
            {
                text = "Open Pixel Pipeline Window"
            };
            open.style.height = 28;
            open.style.marginTop = 4;
            open.style.marginBottom = 8;
            root.Add(open);

            var everything = new Foldout { text = "All settings", value = false };
            InspectorElement.FillDefaultInspector(everything, serializedObject, this);
            root.Add(everything);
            return root;
        }
    }
}
