Shader "Hidden/PixelPipeline/CloudShadows"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CloudCommon.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                float rawDepth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.0001)
                        return color;
                    float deviceDepth = rawDepth;
                #else
                    if (rawDepth >= 0.9999)
                        return color;
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float3 worldPosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                color.rgb *= PixelPipelineCloudTransmission(worldPosition);
                return color;
            }
            ENDHLSL
        }
    }
}
