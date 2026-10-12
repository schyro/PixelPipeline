Shader "PixelPipeline/GrassTerrain"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.478, 0.620, 0.353, 1)
        _Color2 ("Patch Color 2", Color) = (0.553, 0.710, 0.353, 1)
        _Color3 ("Patch Color 3", Color) = (0.290, 0.431, 0.165, 1)

        [NoScaleOffset] _Noise2 ("Patch Noise 2", 2D) = "white" {}
        _Noise2Scale ("Noise 2 World Scale", Range(0.0, 1.0)) = 0.05
        _Noise2Threshold ("Noise 2 Threshold", Range(0.0, 1.0)) = 0.3

        [NoScaleOffset] _Noise3 ("Patch Noise 3", 2D) = "white" {}
        _Noise3Scale ("Noise 3 World Scale", Range(0.0, 0.1)) = 0.03
        _Noise3Threshold ("Noise 3 Threshold", Range(0.0, 1.0)) = 0.4

        [HideInInspector] _BaseMap ("Base Map", 2D) = "white" {}
        [HideInInspector] _Cutoff ("Cutoff", Float) = 0.5
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _AlphaClip ("Alpha Clip", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TerrainVertex
            #pragma fragment TerrainFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelAdditionalLights.hlsl"

            struct TerrainAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TerrainVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_Noise2);
            SAMPLER(sampler_Noise2);
            TEXTURE2D(_Noise3);
            SAMPLER(sampler_Noise3);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color2;
                half4 _Color3;
                float _Noise2Scale;
                float _Noise2Threshold;
                float _Noise3Scale;
                float _Noise3Threshold;
            CBUFFER_END

            TerrainVaryings TerrainVertex(TerrainAttributes input)
            {
                TerrainVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 TerrainFragment(TerrainVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float noise2 = SAMPLE_TEXTURE2D(
                    _Noise2,
                    sampler_Noise2,
                    input.positionWS.xz * _Noise2Scale).r;
                float noise3 = SAMPLE_TEXTURE2D(
                    _Noise3,
                    sampler_Noise3,
                    input.positionWS.xz * _Noise3Scale).r;

                half3 albedo = _BaseColor.rgb;
                if (noise2 > _Noise2Threshold)
                    albedo = _Color2.rgb;
                if (noise3 > _Noise3Threshold)
                    albedo = _Color3.rgb;

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainDiffuse = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS);
                lighting += mainLight.color * (
                    mainDiffuse * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                // Smooth like the rest of the terrain's light, so no bands.
                lighting += PixelPipelineAdditionalLights(
                    input.positionWS,
                    GetNormalizedScreenSpaceUV(input.positionHCS),
                    normalWS,
                    0.0h,
                    0.0h);

                half3 color = MixFog(albedo * lighting, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Unlit/DepthNormalsOnly"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
