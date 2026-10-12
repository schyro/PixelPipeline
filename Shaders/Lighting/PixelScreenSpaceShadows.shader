// Screen-space main-light shadows with pixel-quantised edges. Written into URP's
// _ScreenSpaceShadowmapTexture, so URP Lit, Simple Lit, and Shader Graph materials all receive the
// hard edge without any change of their own.
Shader "Hidden/PixelPipeline/ScreenSpaceShadows"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "PixelScreenSpaceShadows"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float deviceDepth = LoadSceneDepth(input.positionCS.xy);
                #if UNITY_REVERSED_Z
                    if (deviceDepth <= 0.0)
                        return 1.0h;
                #else
                    if (deviceDepth >= 1.0)
                        return 1.0h;
                    deviceDepth = deviceDepth * 2.0 - 1.0;
                #endif

                float3 positionWS = ComputeWorldSpacePosition(input.texcoord.xy, deviceDepth, unity_MatrixInvVP);
                return PixelPipelineSampleMainLightShadow(positionWS);
            }
            ENDHLSL
        }
    }
}
