using System;
using System.Linq;
using PixelPipeline.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PixelPipeline.Editor
{
    /// <summary>
    /// Brings every URP renderer that carries a <see cref="PixelRendererFeature"/> up to date: assigns
    /// the shader references a player build needs, makes sure URP's Screen Space Shadows feature is
    /// present, and gives the pipeline assets using those renderers a shadow map fine enough for
    /// pixel shadow edges.
    /// </summary>
    /// <remarks>
    /// The pixel shadow pass writes URP's screen-space shadow texture, which Lit reads through its
    /// <c>_MAIN_LIGHT_SHADOWS_SCREEN</c> variant. URP strips that variant from a build unless an
    /// active Screen Space Shadows feature is on the renderer, so the feature has to be there even
    /// though the pixel pass replaces its result.
    /// </remarks>
    public static class PixelRendererSetup
    {
        private const string ScreenSpaceShadowsTypeName =
            "UnityEngine.Rendering.Universal.ScreenSpaceShadows, Unity.RenderPipelines.Universal.Runtime";
        private const string UrpScreenSpaceShadowsShader = "Hidden/Universal Render Pipeline/ScreenSpaceShadows";

        // At 1024 over a typical shadow distance one shadow texel spans about four internal pixels,
        // too coarse for the filtered edge to be snapped into a clean pixel line.
        private const int MinimumShadowResolution = 2048;

        [MenuItem("Tools/Pixel Pipeline/Configure Renderers")]
        public static void ConfigureFromMenu()
        {
            int configured = ConfigureAllRenderers();
            Debug.Log($"[PixelPipeline] Configured {configured} renderer(s) that use the Pixel Renderer Feature.");
        }

        public static int ConfigureAllRenderers()
        {
            int configured = 0;
            var pixelRenderers = new System.Collections.Generic.HashSet<ScriptableRendererData>();
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null)
                    continue;

                PixelRendererFeature pixelFeature = rendererData.rendererFeatures.OfType<PixelRendererFeature>().FirstOrDefault();
                if (pixelFeature == null)
                    continue;

                AssignShaders(pixelFeature);
                EnsureScreenSpaceShadows(rendererData);
                if (!rendererData.usesDeferredLighting)
                {
                    Debug.LogWarning(
                        $"[PixelPipeline] {rendererData.name} uses a forward rendering path. Light bands are applied " +
                        "from the G-buffer, so they need Rendering Path set to Deferred+ (or Deferred); on a forward " +
                        "path lit materials keep URP's smooth lighting.",
                        rendererData);
                }
                EditorUtility.SetDirty(rendererData);
                pixelRenderers.Add(rendererData);
                configured++;
            }

            EnsureShadowResolution(pixelRenderers);

            AssetDatabase.SaveAssets();
            return configured;
        }

        private static void EnsureShadowResolution(System.Collections.Generic.HashSet<ScriptableRendererData> pixelRenderers)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (pipeline == null || pipeline.mainLightShadowmapResolution >= MinimumShadowResolution)
                    continue;

                SerializedProperty renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                bool usesPixelRenderer = false;
                for (int index = 0; renderers != null && index < renderers.arraySize; index++)
                    usesPixelRenderer |= pixelRenderers.Contains(renderers.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData);

                if (!usesPixelRenderer)
                    continue;

                Undo.RecordObject(pipeline, "Raise Shadow Resolution");
                pipeline.mainLightShadowmapResolution = MinimumShadowResolution;
                EditorUtility.SetDirty(pipeline);
                Debug.Log($"[PixelPipeline] Raised the main light shadow resolution of {pipeline.name} to {MinimumShadowResolution}.");
            }
        }

        private static void AssignShaders(PixelRendererFeature feature)
        {
            SerializedObject serializedFeature = new(feature);
            AssignShader(serializedFeature, "downsampleShader", "Hidden/PixelPipeline/PointDownsample");
            AssignShader(serializedFeature, "outlineShader", "Hidden/PixelPipeline/Outline");
            AssignShader(serializedFeature, "compositeShader", "Hidden/PixelPipeline/Composite");
            AssignShader(serializedFeature, "cloudShadowsShader", "Hidden/PixelPipeline/CloudShadows");
            AssignShader(serializedFeature, "upscaleShader", "Hidden/PixelPipeline/SharpUpscale");
            AssignShader(serializedFeature, "screenSpaceShadowsShader", "Hidden/PixelPipeline/ScreenSpaceShadows");
            AssignShader(serializedFeature, "godRaysShader", "Hidden/PixelPipeline/GodRays");
            AssignShader(serializedFeature, "deferredLightingShader", "Hidden/PixelPipeline/DeferredLighting");
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignShader(SerializedObject serializedFeature, string propertyName, string shaderName)
        {
            SerializedProperty property = serializedFeature.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null)
                return;

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{shaderName}' was not found.");

            property.objectReferenceValue = shader;
        }

        private static void EnsureScreenSpaceShadows(ScriptableRendererData rendererData)
        {
            Type featureType = Type.GetType(ScreenSpaceShadowsTypeName);
            if (featureType == null)
            {
                Debug.LogWarning("[PixelPipeline] URP's Screen Space Shadows feature type was not found; add it to the renderer by hand.");
                return;
            }

            ScriptableRendererFeature existing = rendererData.rendererFeatures.FirstOrDefault(
                feature => feature != null && feature.GetType() == featureType);
            if (existing != null)
            {
                if (!existing.isActive)
                {
                    existing.SetActive(true);
                    EditorUtility.SetDirty(existing);
                }

                return;
            }

            ScriptableRendererFeature screenSpaceShadows = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            screenSpaceShadows.name = "ScreenSpaceShadows";

            // Without the reference a player cannot find URP's hidden shader and the feature logs an
            // error every frame, even though its output is never used.
            SerializedObject serializedShadows = new(screenSpaceShadows);
            SerializedProperty shaderProperty = serializedShadows.FindProperty("m_Shader");
            if (shaderProperty != null)
            {
                shaderProperty.objectReferenceValue = Shader.Find(UrpScreenSpaceShadowsShader);
                serializedShadows.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.AddObjectToAsset(screenSpaceShadows, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(screenSpaceShadows, out _, out long localId);

            // The same bookkeeping URP's renderer inspector does when a feature is added by hand.
            SerializedObject serializedRenderer = new(rendererData);
            SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = screenSpaceShadows;
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
