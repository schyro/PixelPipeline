using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PixelPipeline.Runtime
{
    public sealed class PixelRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private PixelPipelineSettings settings;

        // Serialised so the player build keeps them. Nothing else references these shaders, and a
        // player strips whatever no asset points at, which leaves Shader.Find with nothing to find.
        [Header("Shaders")]
        [SerializeField] private Shader downsampleShader;
        [SerializeField] private Shader outlineShader;
        [SerializeField] private Shader compositeShader;
        [SerializeField] private Shader cloudShadowsShader;
        [SerializeField] private Shader upscaleShader;
        [SerializeField] private Shader screenSpaceShadowsShader;
        [SerializeField] private Shader godRaysShader;
        [SerializeField] private Shader deferredLightingShader;

        private Material _downsampleMaterial;
        private Material _outlineMaterial;
        private Material _compositeMaterial;
        private Material _cloudMaterial;
        private Material _upscaleMaterial;
        private Material _shadowMaterial;
        private Material _godRayMaterial;
        private Material _deferredLitMaterial;
        private Material _deferredSimpleLitMaterial;
        private bool _usesDeferredLighting;
        private PixelDeferredLightingPass _deferredLightingPass;
        private PixelRenderPass _renderPass;
        private PixelShadowPass _shadowPass;
        private PixelShadowKeywordResetPass _shadowResetPass;
        private PixelRainOcclusionPass _rainOcclusionPass;

        private static readonly int RainOcclusionParamsId = Shader.PropertyToID("_PixelRainOcclusionParams");
        private static readonly int PixelShadowCascadeCountId = Shader.PropertyToID("_PixelShadowCascadeCount");
        private static readonly int PixelShadowmapSizeId = Shader.PropertyToID("_PixelShadowmapSize");

        // URP's StencilUsage material types, which its G-buffer pass writes for every pixel.
        private const int StencilMaterialLit = 0b_0010_0000;
        private const int StencilMaterialSimpleLit = 0b_0100_0000;

        // URP has no render scale of its own per camera, so the pipeline asset's is replaced for
        // the length of one camera and put back when that camera is done.
        private static readonly List<PixelRendererFeature> Features = new();
        private static UniversalRenderPipelineAsset _scaledAsset;
        private static float _restoreRenderScale;
        private static UpscalingFilterSelection _restoreUpscalingFilter;

        public override void Create()
        {
            DisposeMaterials();

            if (!Features.Contains(this))
                Features.Add(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

            _downsampleMaterial = CreateMaterial(downsampleShader, "Hidden/PixelPipeline/PointDownsample");
            _outlineMaterial = CreateMaterial(outlineShader, "Hidden/PixelPipeline/Outline");
            _compositeMaterial = CreateMaterial(compositeShader, "Hidden/PixelPipeline/Composite");
            _cloudMaterial = CreateMaterial(cloudShadowsShader, "Hidden/PixelPipeline/CloudShadows");
            _upscaleMaterial = CreateMaterial(upscaleShader, "Hidden/PixelPipeline/SharpUpscale");
            _shadowMaterial = CreateMaterial(screenSpaceShadowsShader, "Hidden/PixelPipeline/ScreenSpaceShadows");
            _godRayMaterial = CreateMaterial(godRaysShader, "Hidden/PixelPipeline/GodRays");

            // One material per material type: the stencil reference is render state, and Simple Lit
            // stores its G-buffer differently.
            _deferredLitMaterial = CreateMaterial(deferredLightingShader, "Hidden/PixelPipeline/DeferredLighting");
            _deferredSimpleLitMaterial = CreateMaterial(deferredLightingShader, "Hidden/PixelPipeline/DeferredLighting");
            if (_deferredLitMaterial != null)
            {
                _deferredLitMaterial.SetInt("_PixelStencilRef", StencilMaterialLit);
                _deferredSimpleLitMaterial.SetInt("_PixelStencilRef", StencilMaterialSimpleLit);
                _deferredSimpleLitMaterial.EnableKeyword("_PIXEL_SIMPLE_LIT");
            }

            _usesDeferredLighting = ResolveUsesDeferredLighting();
            _deferredLightingPass = new PixelDeferredLightingPass(_deferredLitMaterial, _deferredSimpleLitMaterial)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer
            };

            _renderPass = new PixelRenderPass(
                settings,
                _downsampleMaterial,
                _outlineMaterial,
                _compositeMaterial,
                _cloudMaterial,
                _godRayMaterial,
                _upscaleMaterial)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };

            // After the depth prepass, and after URP's own Screen Space Shadows pass when that feature
            // is installed too, so this result is the one the opaque pass reads.
            _shadowPass = new PixelShadowPass(_shadowMaterial)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 2
            };
            _shadowResetPass = new PixelShadowKeywordResetPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
            _rainOcclusionPass = new PixelRainOcclusionPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null)
                return;

            CameraData cameraData = renderingData.cameraData;
            bool isSceneView = cameraData.cameraType == CameraType.SceneView;

            // Cleared for every camera and raised again only by the occlusion pass of a camera that
            // renders the map, so no camera samples a rain map built for another one.
            Shader.SetGlobalVector(RainOcclusionParamsId, Vector4.zero);

            // Lighting rather than a post effect, so every camera gets it, the mirrored water
            // camera included. The globals it reads are the same for all of them.
            bool deferredLighting = _usesDeferredLighting && _deferredLitMaterial != null;
            if (deferredLighting)
            {
                settings.ApplyLightingGlobals();
                renderer.EnqueuePass(_deferredLightingPass);
            }

            // The mirrored water camera renders straight into its own low resolution target, so it
            // must not be downsampled, outlined, and upscaled a second time.
            if (PixelWaterPlanarReflection.IsReflectionCamera(cameraData.camera))
                return;

            // Water surfaces are ordinary scene geometry, so their globals have to be up to date even
            // for cameras the pixel passes below skip, such as the scene view, and whether or not the
            // passes themselves are able to run at all.
            settings.ApplyWindGlobals();
            settings.ApplyLightingGlobals();
            settings.ApplyWaterGlobals(settings.GetInternalResolution(cameraData.camera.aspect));

            bool isSceneOrGame = isSceneView || cameraData.cameraType == CameraType.Game;

            // Shadows are lighting rather than a post effect, so the scene view gets the same hard
            // edges as the game view whether or not the pixel passes below run there.
            // On the deferred path the lighting pass above bands the shadow itself, and a
            // screen-space shadow texture would force a full depth prepass ahead of the G-buffer.
            if (isSceneOrGame && !deferredLighting && settings.EnablePixelShadows && _shadowMaterial != null)
            {
                renderer.EnqueuePass(_shadowPass);
                renderer.EnqueuePass(_shadowResetPass);
            }

            // Drops are only drawn for the controller's target camera, so no other camera, the
            // scene view included, has a use for the map.
            if (settings.EnableRainImpacts
                && PixelEnvironmentController.IsRainCamera(cameraData.camera)
                && PixelEnvironmentController.TryResolveRain(settings, out Vector3 rainCentre, out Vector3 fallDirection, out _))
            {
                _rainOcclusionPass.Setup(settings.Environment.Rain, rainCentre, fallDirection);
                renderer.EnqueuePass(_rainOcclusionPass);
            }

            if (_renderPass == null || _upscaleMaterial == null || !isSceneOrGame)
                return;

            if (isSceneView && !settings.RenderSceneView)
                return;

            if (settings.BaseCameraOnly && cameraData.renderType != CameraRenderType.Base)
                return;

            // Whether the camera target already is the internal resolution is read off the target
            // itself, so anything that keeps the render scale from applying falls back to the
            // downsample on its own. The upscaled image replaces the camera colour, which only the
            // last camera of a stack may do.
            Vector2Int internalResolution = settings.GetInternalResolution(cameraData.camera.aspect);
            bool nativeResolution = !isSceneView
                                    && cameraData.renderType == CameraRenderType.Base
                                    && cameraData.resolveFinalTarget
                                    && cameraData.cameraTargetDescriptor.width == internalResolution.x
                                    && cameraData.cameraTargetDescriptor.height == internalResolution.y;

            PixelEnvironmentRuntimeState environmentState = PixelEnvironmentController.ResolveState(settings);
            settings.ApplyCloudGlobals(environmentState);
            _renderPass.UpdateSettings(settings, environmentState, nativeResolution);
            renderer.EnqueuePass(_renderPass);
        }

        // A renderer feature is not told which renderer it sits on, so the rendering path is read
        // from the renderer data that lists it. Changing the path recreates the renderer and this.
        private bool ResolveUsesDeferredLighting()
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset)
                return false;

            foreach (ScriptableRendererData data in asset.rendererDataList)
            {
                if (data is UniversalRendererData universalData && universalData.rendererFeatures.Contains(this))
                    return universalData.usesDeferredLighting;
            }

            return false;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            RestoreRenderScale();

            if (camera.cameraType != CameraType.Game || PixelWaterPlanarReflection.IsReflectionCamera(camera))
                return;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset)
                return;

            PixelPipelineSettings settings = null;
            foreach (PixelRendererFeature feature in Features)
            {
                if (feature != null && feature.isActive && feature.settings != null)
                {
                    settings = feature.settings;
                    break;
                }
            }

            if (settings == null)
                return;

            // Overlay cameras draw into the base camera's target after the pixel passes, at screen
            // resolution, so a stack keeps the downsample.
            if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData)
                && (cameraData.renderType != CameraRenderType.Base || cameraData.cameraStack is { Count: > 0 }))
                return;

            if (!settings.TryGetRenderScale(camera, out float scale))
                return;

            _scaledAsset = asset;
            _restoreRenderScale = asset.renderScale;
            _restoreUpscalingFilter = asset.upscalingFilter;
            asset.renderScale = scale;
            // The pixel pass does the upscaling. Linear keeps URP from adding a scaling pass of its
            // own; its final blit then copies the upscaled image one to one.
            asset.upscalingFilter = UpscalingFilterSelection.Linear;
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            RestoreRenderScale();
        }

        private static void RestoreRenderScale()
        {
            if (_scaledAsset == null)
                return;

            _scaledAsset.renderScale = _restoreRenderScale;
            _scaledAsset.upscalingFilter = _restoreUpscalingFilter;
            _scaledAsset = null;
        }

        protected override void Dispose(bool disposing)
        {
            RestoreRenderScale();
            Features.Remove(this);
            if (Features.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            }

            DisposeMaterials();
            _renderPass = null;
            _deferredLightingPass = null;
            _shadowPass = null;
            _shadowResetPass = null;
            _rainOcclusionPass = null;
        }

        // Shader.Find only covers the editor and any assigned-by-hand renderer that predates the
        // serialised fields; a player has to go through the reference.
        private static Material CreateMaterial(Shader shader, string shaderName)
        {
            Shader resolved = shader != null ? shader : Shader.Find(shaderName);
            if (resolved == null)
            {
                Debug.LogError(
                    $"[PixelPipeline] Required shader was not found: {shaderName}. " +
                    "Assign it on the Pixel Renderer Feature so it survives a player build.");
                return null;
            }

            return CoreUtils.CreateEngineMaterial(resolved);
        }

        private void DisposeMaterials()
        {
            CoreUtils.Destroy(_outlineMaterial);
            CoreUtils.Destroy(_downsampleMaterial);
            CoreUtils.Destroy(_compositeMaterial);
            CoreUtils.Destroy(_cloudMaterial);
            CoreUtils.Destroy(_upscaleMaterial);
            CoreUtils.Destroy(_shadowMaterial);
            CoreUtils.Destroy(_godRayMaterial);
            CoreUtils.Destroy(_deferredLitMaterial);
            CoreUtils.Destroy(_deferredSimpleLitMaterial);
        }

        // The atlas URP's main light shadow pass allocates: two cascades share a half-height one.
        private static Vector4 GetShadowmapSize(UniversalShadowData shadowData)
        {
            int atlasWidth = Mathf.Max(1, shadowData.mainLightShadowmapWidth);
            int atlasHeight = Mathf.Max(1, shadowData.mainLightShadowCascadesCount == 2
                ? shadowData.mainLightShadowmapHeight >> 1
                : shadowData.mainLightShadowmapHeight);
            return new Vector4(1f / atlasWidth, 1f / atlasHeight, atlasWidth, atlasHeight);
        }

        /// <summary>
        /// Deferred paths only. Lights every G-buffer pixel with banded lights and ambient between
        /// URP's G-buffer and deferred lighting passes, and takes those pixels away from URP's own
        /// lighting; see PixelDeferredLighting.shader.
        /// </summary>
        private sealed class PixelDeferredLightingPass : ScriptableRenderPass
        {
            private static readonly int[] GBufferIds =
            {
                Shader.PropertyToID("_GBuffer0"),
                Shader.PropertyToID("_GBuffer1"),
                Shader.PropertyToID("_GBuffer2")
            };
            private static readonly int AdditionalLightCountId = Shader.PropertyToID("_PixelAdditionalLightCount");

            private readonly Material _litMaterial;
            private readonly Material _simpleLitMaterial;

            private sealed class PassData
            {
                public Material litMaterial;
                public Material simpleLitMaterial;
                public TextureHandle gBuffer0;
                public TextureHandle gBuffer1;
                public TextureHandle gBuffer2;
                public int cascadeCount;
                public int additionalLightCount;
                public Vector4 shadowmapSize;
            }

            public PixelDeferredLightingPass(Material litMaterial, Material simpleLitMaterial)
            {
                _litMaterial = litMaterial;
                _simpleLitMaterial = simpleLitMaterial;
                profilingSampler = new ProfilingSampler("PixelPipeline.DeferredLighting");
                // The depth-stencil buffer is bound for the stencil test, so depth is read from URP's
                // copy of it, which this makes URP take right after the G-buffer.
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                TextureHandle[] gBuffer = resourceData.gBuffer;
                if (_litMaterial == null
                    || lightData.mainLightIndex < 0
                    || gBuffer == null
                    || gBuffer.Length < 3
                    || !gBuffer[0].IsValid()
                    || !gBuffer[1].IsValid()
                    || !gBuffer[2].IsValid())
                    return;

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    passName,
                    out PassData passData,
                    profilingSampler);

                passData.litMaterial = _litMaterial;
                passData.simpleLitMaterial = _simpleLitMaterial;
                passData.gBuffer0 = gBuffer[0];
                passData.gBuffer1 = gBuffer[1];
                passData.gBuffer2 = gBuffer[2];
                passData.cascadeCount = shadowData.mainLightShadowCascadesCount;
                passData.additionalLightCount = lightData.additionalLightsCount;
                passData.shadowmapSize = GetShadowmapSize(shadowData);

                builder.UseTexture(gBuffer[0], AccessFlags.Read);
                builder.UseTexture(gBuffer[1], AccessFlags.Read);
                builder.UseTexture(gBuffer[2], AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                if (resourceData.mainShadowsTexture.IsValid())
                    builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                if (resourceData.additionalShadowsTexture.IsValid())
                    builder.UseTexture(resourceData.additionalShadowsTexture, AccessFlags.Read);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    cmd.SetGlobalTexture(GBufferIds[0], data.gBuffer0);
                    cmd.SetGlobalTexture(GBufferIds[1], data.gBuffer1);
                    cmd.SetGlobalTexture(GBufferIds[2], data.gBuffer2);
                    cmd.SetGlobalFloat(AdditionalLightCountId, data.additionalLightCount);
                    // Grass, foliage, and water drawn after this read the same two values.
                    cmd.SetGlobalFloat(PixelShadowCascadeCountId, data.cascadeCount);
                    cmd.SetGlobalVector(PixelShadowmapSizeId, data.shadowmapSize);

                    Vector4 scaleBias = new(1f, 1f, 0f, 0f);
                    Blitter.BlitTexture(cmd, scaleBias, data.litMaterial, 0);
                    Blitter.BlitTexture(cmd, scaleBias, data.litMaterial, 1);
                    Blitter.BlitTexture(cmd, scaleBias, data.simpleLitMaterial, 0);
                    Blitter.BlitTexture(cmd, scaleBias, data.simpleLitMaterial, 1);
                });
            }
        }

        // Kept out of the feature's own static initialiser: Unity refuses to create keywords while a
        // ScriptableObject is being constructed, and this class is only touched at render time.
        private static class ShadowKeywords
        {
            public static readonly GlobalKeyword MainLight = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS");
            public static readonly GlobalKeyword Cascades = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS_CASCADE");
            public static readonly GlobalKeyword Screen = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS_SCREEN");
        }

        /// <summary>
        /// Resolves the main-light shadow once per screen pixel with quantised edges and publishes it as
        /// URP's screen-space shadow texture. Opaque Lit and Shader Graph materials then pick it up
        /// through their standard <c>_MAIN_LIGHT_SHADOWS_SCREEN</c> variant.
        /// </summary>
        private sealed class PixelShadowPass : ScriptableRenderPass
        {
            private static readonly int ScreenSpaceShadowmapTextureId = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");

            private readonly Material _material;

            private sealed class PassData
            {
                public Material material;
                public int cascadeCount;
                public Vector4 shadowmapSize;
            }

            public PixelShadowPass(Material material)
            {
                _material = material;
                profilingSampler = new ProfilingSampler("PixelPipeline.ScreenSpaceShadows");
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                if (_material == null || !shadowData.supportsMainLightShadows || lightData.mainLightIndex < 0)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
                TextureDesc descriptor = new(cameraDescriptor.width, cameraDescriptor.height)
                {
                    colorFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render)
                        ? GraphicsFormat.R8_UNorm
                        : GraphicsFormat.B8G8R8A8_UNorm,
                    msaaSamples = MSAASamples.None,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                    name = "_PixelScreenSpaceShadows"
                };
                TextureHandle shadowTexture = renderGraph.CreateTexture(descriptor);

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    passName,
                    out PassData passData,
                    profilingSampler);

                passData.material = _material;
                passData.cascadeCount = shadowData.mainLightShadowCascadesCount;
                passData.shadowmapSize = GetShadowmapSize(shadowData);

                builder.SetRenderAttachment(shadowTexture, 0, AccessFlags.Write);
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(shadowTexture, ScreenSpaceShadowmapTextureId);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(PixelShadowCascadeCountId, data.cascadeCount);
                    context.cmd.SetGlobalVector(PixelShadowmapSizeId, data.shadowmapSize);
                    Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    context.cmd.SetKeyword(ShadowKeywords.MainLight, false);
                    context.cmd.SetKeyword(ShadowKeywords.Cascades, false);
                    context.cmd.SetKeyword(ShadowKeywords.Screen, true);
                });
            }
        }

        /// <summary>
        /// Transparent materials cannot use a screen-space shadow texture, since it holds the shadow of
        /// the opaque surface behind them. This returns them to the shadow-map variants, exactly as
        /// URP's own Screen Space Shadows feature does.
        /// </summary>
        private sealed class PixelShadowKeywordResetPass : ScriptableRenderPass
        {
            private sealed class PassData
            {
                public int cascadeCount;
                public bool supportsMainLightShadows;
            }

            public PixelShadowKeywordResetPass()
            {
                profilingSampler = new ProfilingSampler("PixelPipeline.ResetShadowKeywords");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    passName,
                    out PassData passData,
                    profilingSampler);

                passData.cascadeCount = shadowData.mainLightShadowCascadesCount;
                passData.supportsMainLightShadows = shadowData.supportsMainLightShadows;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetKeyword(ShadowKeywords.Screen, false);
                    context.cmd.SetKeyword(
                        ShadowKeywords.MainLight,
                        data.supportsMainLightShadows && data.cascadeCount == 1);
                    context.cmd.SetKeyword(
                        ShadowKeywords.Cascades,
                        data.supportsMainLightShadows && data.cascadeCount > 1);
                });
            }
        }

        /// <summary>
        /// Renders the depth of everything rain can land on, seen along the falling direction of the
        /// drops, like a shadow map. Each drop lands on the surface stored under its path, and its
        /// splash plays there.
        /// </summary>
        private sealed class PixelRainOcclusionPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId DepthOnlyTag = new("DepthOnly");
            private static readonly int OcclusionTextureId = Shader.PropertyToID("_PixelRainOcclusionTexture");
            private static readonly int WorldToMapId = Shader.PropertyToID("_PixelRainOcclusionWorldToMap");
            private static readonly int MapToWorldId = Shader.PropertyToID("_PixelRainOcclusionMapToWorld");

            private PixelRainSettings _settings;
            private Vector3 _centre;
            private Vector3 _fallDirection;

            private sealed class PassData
            {
                public RendererListHandle rendererList;
                public Matrix4x4 view;
                public Matrix4x4 projection;
                public Matrix4x4 cameraView;
                public Matrix4x4 cameraProjection;
                public Matrix4x4 worldToMap;
                public Matrix4x4 mapToWorld;
                public Vector4 parameters;
            }

            public PixelRainOcclusionPass()
            {
                profilingSampler = new ProfilingSampler("PixelPipeline.RainOcclusion");
            }

            public void Setup(PixelRainSettings rainSettings, Vector3 centre, Vector3 fallDirection)
            {
                _settings = rainSettings;
                _centre = centre;
                _fallDirection = fallDirection;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_settings == null)
                    return;

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                int resolution = _settings.OcclusionResolution;
                float depthRange = _settings.OcclusionDepthRange;
                BuildMatrices(
                    _centre,
                    _fallDirection,
                    _settings.AreaSize,
                    depthRange,
                    resolution,
                    out Matrix4x4 view,
                    out Matrix4x4 projection);
                Matrix4x4 worldToMap = GetMapTransform(projection, view);

                TextureDesc descriptor = new(resolution, resolution)
                {
                    depthBufferBits = DepthBits.Depth32,
                    msaaSamples = MSAASamples.None,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    name = "_PixelRainOcclusionTexture"
                };
                TextureHandle occlusionTexture = renderGraph.CreateTexture(descriptor);

                // The camera's culling results are reused, so only objects that are at least partly in
                // view stop rain. Rain outside the view is not drawn either, so that costs nothing.
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    DepthOnlyTag,
                    renderingData,
                    cameraData,
                    lightData,
                    SortingCriteria.None);
                FilteringSettings filteringSettings = new(RenderQueueRange.all, _settings.OcclusionLayers);
                RendererListParams listParameters = new(renderingData.cullResults, drawingSettings, filteringSettings);

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    passName,
                    out PassData passData,
                    profilingSampler);

                passData.rendererList = renderGraph.CreateRendererList(listParameters);
                passData.view = view;
                passData.projection = projection;
                passData.cameraView = cameraData.GetViewMatrix();
                passData.cameraProjection = cameraData.GetProjectionMatrix();
                passData.worldToMap = worldToMap;
                passData.mapToWorld = worldToMap.inverse;
                passData.parameters = new Vector4(1f, 0f, 1f / resolution, 0f);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachmentDepth(occlusionTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(occlusionTexture, OcclusionTextureId);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    cmd.SetViewProjectionMatrices(data.view, data.projection);
                    cmd.DrawRendererList(data.rendererList);
                    cmd.SetViewProjectionMatrices(data.cameraView, data.cameraProjection);

                    cmd.SetGlobalMatrix(WorldToMapId, data.worldToMap);
                    cmd.SetGlobalMatrix(MapToWorldId, data.mapToWorld);
                    cmd.SetGlobalVector(RainOcclusionParamsId, data.parameters);
                });
            }

            private static void BuildMatrices(
                Vector3 centre,
                Vector3 fallDirection,
                float areaSize,
                float depthRange,
                int resolution,
                out Matrix4x4 view,
                out Matrix4x4 projection)
            {
                // The same axes the drops are dealt out along.
                PixelRainRenderer.GetPlaneAxes(fallDirection, out Vector3 forward, out Vector3 right, out Vector3 up);
                Quaternion rotation = Quaternion.LookRotation(forward, up);

                // Snapping the centre to whole texels keeps the map from swimming while the camera
                // pans, which would otherwise make the drops' landing points flicker.
                float texel = areaSize / resolution;
                Vector3 snappedCentre =
                    right * (Mathf.Round(Vector3.Dot(centre, right) / texel) * texel)
                    + up * (Mathf.Round(Vector3.Dot(centre, up) / texel) * texel)
                    + forward * Vector3.Dot(centre, forward);

                Vector3 eye = snappedCentre - forward * (depthRange * 0.5f);
                // Unity view space looks down -Z.
                view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;

                float halfSize = areaSize * 0.5f;
                projection = Matrix4x4.Ortho(-halfSize, halfSize, -halfSize, halfSize, 0f, depthRange);
            }

            // Same convention as URP's shadow transform: world to [0,1] texture space, with a depth that
            // matches what this platform's depth buffer stores.
            private static Matrix4x4 GetMapTransform(Matrix4x4 projection, Matrix4x4 view)
            {
                if (SystemInfo.usesReversedZBuffer)
                {
                    projection.m20 = -projection.m20;
                    projection.m21 = -projection.m21;
                    projection.m22 = -projection.m22;
                    projection.m23 = -projection.m23;
                }

                Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
                textureScaleAndBias.m00 = 0.5f;
                textureScaleAndBias.m11 = 0.5f;
                textureScaleAndBias.m22 = 0.5f;
                textureScaleAndBias.m03 = 0.5f;
                textureScaleAndBias.m13 = 0.5f;
                textureScaleAndBias.m23 = 0.5f;
                return textureScaleAndBias * projection * view;
            }
        }

        private sealed class PixelRenderPass : ScriptableRenderPass
        {
            private static readonly int OutlineTextureId = Shader.PropertyToID("_OutlineTexture");
            private static readonly int PixelPanOffsetId = Shader.PropertyToID("_PixelPanOffset");
            private static readonly int LineDarkenId = Shader.PropertyToID("_LineDarken");
            private static readonly int LineAlphaId = Shader.PropertyToID("_LineAlpha");
            private static readonly int CreaseBrightenId = Shader.PropertyToID("_CreaseBrighten");
            private static readonly int CreaseAlphaId = Shader.PropertyToID("_CreaseAlpha");
            private static readonly int KernelRadiusId = Shader.PropertyToID("_KernelRadius");
            private static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
            private static readonly int GrazingAngleStartId = Shader.PropertyToID("_GrazingAngleStart");
            private static readonly int GrazingDepthScaleId = Shader.PropertyToID("_GrazingDepthScale");
            private static readonly int CreaseDepthLowId = Shader.PropertyToID("_CreaseDepthLow");
            private static readonly int CreaseDepthHighId = Shader.PropertyToID("_CreaseDepthHigh");
            private static readonly int CreaseNormalLowId = Shader.PropertyToID("_CreaseNormalLow");
            private static readonly int CreaseNormalHighId = Shader.PropertyToID("_CreaseNormalHigh");
            private static readonly int GodRayColorId = Shader.PropertyToID("_GodRayColor");
            private static readonly int GodRaySunDirectionId = Shader.PropertyToID("_GodRaySunDirection");
            private static readonly int GodRayVolumeId = Shader.PropertyToID("_GodRayVolume");
            private static readonly int GodRayGroundFadeId = Shader.PropertyToID("_GodRayGroundFade");
            private static readonly int GodRayMarchId = Shader.PropertyToID("_GodRayMarch");
            private static readonly int GodRayMaskId = Shader.PropertyToID("_GodRayMask");
            private static readonly int GodRayAreaId = Shader.PropertyToID("_GodRayArea");

            private PixelPipelineSettings _settings;
            private PixelEnvironmentRuntimeState _environmentState;
            private bool _nativeResolution;
            private readonly Material _downsampleMaterial;
            private readonly Material _outlineMaterial;
            private readonly Material _compositeMaterial;
            private readonly Material _cloudMaterial;
            private readonly Material _godRayMaterial;
            private readonly Material _upscaleMaterial;

            private sealed class PassData
            {
                public TextureHandle source;
                public TextureHandle secondary;
                public Material material;
            }

            public PixelRenderPass(
                PixelPipelineSettings settings,
                Material downsampleMaterial,
                Material outlineMaterial,
                Material compositeMaterial,
                Material cloudMaterial,
                Material godRayMaterial,
                Material upscaleMaterial)
            {
                _settings = settings;
                _downsampleMaterial = downsampleMaterial;
                _outlineMaterial = outlineMaterial;
                _compositeMaterial = compositeMaterial;
                _cloudMaterial = cloudMaterial;
                _godRayMaterial = godRayMaterial;
                _upscaleMaterial = upscaleMaterial;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public void UpdateSettings(
                PixelPipelineSettings settings,
                PixelEnvironmentRuntimeState environmentState,
                bool nativeResolution)
            {
                _settings = settings;
                _environmentState = environmentState;
                _nativeResolution = nativeResolution;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_settings == null || _upscaleMaterial == null)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle cameraTarget = resourceData.activeColorTexture;

                if (!cameraTarget.IsValid())
                    return;

                Vector2Int resolution = _settings.GetInternalResolution(cameraData.camera.aspect);
                TextureDesc lowResolutionDescriptor = new(resolution.x, resolution.y)
                {
                    colorFormat = cameraData.cameraTargetDescriptor.graphicsFormat,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    clearBuffer = false,
                    name = "_PixelSceneColor"
                };

                // Drawn at the internal resolution already, the camera target is the pixelated scene.
                TextureHandle sceneColor = cameraTarget;
                if (!_nativeResolution)
                {
                    sceneColor = renderGraph.CreateTexture(lowResolutionDescriptor);
                    AddBlitPass(renderGraph, "PixelPipeline.Downsample", cameraTarget, sceneColor, _downsampleMaterial);
                }

                TextureHandle processedColor = sceneColor;
                if (_settings.EnableCloudShadows && _cloudMaterial != null)
                {
                    lowResolutionDescriptor.name = "_PixelCloudColor";
                    TextureHandle cloudColor = renderGraph.CreateTexture(lowResolutionDescriptor);
                    AddBlitPass(renderGraph, "PixelPipeline.CloudShadows", processedColor, cloudColor, _cloudMaterial);
                    processedColor = cloudColor;
                }

                if (_settings.EnableOutline && _outlineMaterial != null && _compositeMaterial != null)
                {
                    ApplyOutlineProperties();

                    TextureDesc outlineDescriptor = lowResolutionDescriptor;
                    outlineDescriptor.name = "_PixelOutlineMask";
                    outlineDescriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                    TextureHandle outlineMask = renderGraph.CreateTexture(outlineDescriptor);
                    AddBlitPass(renderGraph, "PixelPipeline.Outline", processedColor, outlineMask, _outlineMaterial);

                    lowResolutionDescriptor.name = "_PixelCompositeColor";
                    TextureHandle compositeColor = renderGraph.CreateTexture(lowResolutionDescriptor);
                    AddCompositePass(renderGraph, processedColor, outlineMask, compositeColor);
                    processedColor = compositeColor;
                }

                // After the outline, so a shaft in front of an object lights its outline as well.
                if (_settings.EnableGodRays && _godRayMaterial != null && SetupGodRays(frameData, cameraData.camera))
                {
                    lowResolutionDescriptor.name = "_PixelGodRayColor";
                    TextureHandle godRayColor = renderGraph.CreateTexture(lowResolutionDescriptor);
                    AddGodRayPass(renderGraph, processedColor, godRayColor, resourceData.mainShadowsTexture);
                    processedColor = godRayColor;
                }

                Vector2 panOffset = PixelPerfectIsometricCamera.GetPanOffset(cameraData.camera);
                _upscaleMaterial.SetVector(PixelPanOffsetId, new Vector4(panOffset.x, panOffset.y, 0f, 0f));

                if (!_nativeResolution)
                {
                    AddBlitPass(renderGraph, "PixelPipeline.SharpUpscale", processedColor, cameraTarget, _upscaleMaterial);
                    return;
                }

                // The low resolution camera target has no room for the upscaled image, so it goes to
                // a screen-sized texture that takes over as the camera colour. URP's final blit
                // reads whatever the camera colour is by then.
                TextureDesc upscaledDescriptor = lowResolutionDescriptor;
                upscaledDescriptor.width = cameraData.camera.pixelWidth;
                upscaledDescriptor.height = cameraData.camera.pixelHeight;
                upscaledDescriptor.filterMode = FilterMode.Bilinear;
                upscaledDescriptor.name = "_PixelUpscaledColor";
                TextureHandle upscaledColor = renderGraph.CreateTexture(upscaledDescriptor);
                AddBlitPass(renderGraph, "PixelPipeline.SharpUpscale", processedColor, upscaledColor, _upscaleMaterial);
                resourceData.cameraColor = upscaledColor;
            }

            /// <summary>
            /// Fills the god ray material for this camera. False when there is nothing to draw: no
            /// main light, the sun too low, or weather and daylight leaving no strength.
            /// </summary>
            private bool SetupGodRays(ContextContainer frameData, Camera camera)
            {
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                PixelCloudSettings clouds = _settings.Environment.Clouds;
                if (lightData.mainLightIndex < 0 || clouds.Noise == null)
                    return false;

                PixelGodRaySettings godRays = _settings.Environment.GodRays;
                VisibleLight mainLight = lightData.visibleLights[lightData.mainLightIndex];
                Vector3 toSun = -((Vector3)mainLight.localToWorldMatrix.GetColumn(2)).normalized;
                float strength = godRays.Intensity
                                 * _environmentState.GodRayStrength
                                 * godRays.EvaluateSunFade(toSun.y);
                if (strength <= 0.0001f || toSun.y <= 0.01f)
                    return false;

                Color color = godRays.Color.linear * mainLight.finalColor * strength;
                float groundHeight = _settings.Environment.GroundHeight;
                // Around the middle of the view, so the shafts stay around what is on screen.
                if (!PixelMath.TryGetGroundFocus(camera, groundHeight, out Vector3 centre))
                    centre = camera.transform.position;

                godRays.GetCloudLods(clouds, out float gapLod, out float detailLod);
                float height = godRays.TopHeight - groundHeight;

                _godRayMaterial.SetVector(GodRayColorId, new Vector4(color.r, color.g, color.b, 1f));
                _godRayMaterial.SetVector(GodRaySunDirectionId, toSun);
                _godRayMaterial.SetVector(GodRayVolumeId, new Vector4(
                    groundHeight, godRays.TopHeight, godRays.HazeFalloff, godRays.TopFade * height));
                _godRayMaterial.SetFloat(GodRayGroundFadeId, Mathf.Max(0.001f, godRays.GroundFade));
                _godRayMaterial.SetVector(GodRayMarchId, new Vector4(
                    godRays.Steps, godRays.Density, godRays.Bands, godRays.Dither));
                _godRayMaterial.SetVector(GodRayMaskId, new Vector4(
                    godRays.GapThreshold, godRays.GapSoftness, gapLod, detailLod));
                _godRayMaterial.SetVector(GodRayAreaId, new Vector4(
                    centre.x, centre.z, godRays.AreaRadius, godRays.Occlusion ? 1f : 0f));
                return true;
            }

            private void AddGodRayPass(
                RenderGraph renderGraph,
                TextureHandle source,
                TextureHandle destination,
                TextureHandle mainShadows)
            {
                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    "PixelPipeline.GodRays",
                    out PassData passData);

                passData.source = source;
                passData.material = _godRayMaterial;

                builder.UseTexture(source, AccessFlags.Read);
                // The march reads the shadow map through URP's global binding, which only stays
                // valid while the graph knows this pass uses it.
                if (mainShadows.IsValid())
                    builder.UseTexture(mainShadows, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            private void ApplyOutlineProperties()
            {
                _outlineMaterial.SetFloat(LineDarkenId, _settings.LineDarken);
                _outlineMaterial.SetFloat(LineAlphaId, _settings.LineAlpha);
                _outlineMaterial.SetFloat(CreaseBrightenId, _settings.CreaseBrighten);
                _outlineMaterial.SetFloat(CreaseAlphaId, _settings.CreaseAlpha);
                _outlineMaterial.SetFloat(KernelRadiusId, _settings.KernelRadius);
                _outlineMaterial.SetFloat(DepthThresholdId, _settings.DepthThreshold);
                _outlineMaterial.SetFloat(GrazingAngleStartId, _settings.GrazingAngleStart);
                _outlineMaterial.SetFloat(GrazingDepthScaleId, _settings.GrazingDepthScale);
                _outlineMaterial.SetFloat(CreaseDepthLowId, _settings.CreaseDepthLow);
                _outlineMaterial.SetFloat(CreaseDepthHighId, _settings.CreaseDepthHigh);
                _outlineMaterial.SetFloat(CreaseNormalLowId, _settings.CreaseNormalLow);
                _outlineMaterial.SetFloat(CreaseNormalHighId, _settings.CreaseNormalHigh);
            }

            private static void AddBlitPass(
                RenderGraph renderGraph,
                string passName,
                TextureHandle source,
                TextureHandle destination,
                Material material)
            {
                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(passName, out PassData passData);
                passData.source = source;
                passData.material = material;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    if (data.material != null)
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    else
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0, false);
                });
            }

            private void AddCompositePass(
                RenderGraph renderGraph,
                TextureHandle color,
                TextureHandle outline,
                TextureHandle destination)
            {
                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    "PixelPipeline.Composite",
                    out PassData passData);

                passData.source = color;
                passData.secondary = outline;
                passData.material = _compositeMaterial;

                builder.UseTexture(color, AccessFlags.Read);
                builder.UseTexture(outline, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(OutlineTextureId, data.secondary);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }
        }
    }
}
