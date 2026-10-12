Shader "Hidden/PixelPipeline/Composite"
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

            TEXTURE2D(_OutlineTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half4 outline = SAMPLE_TEXTURE2D(_OutlineTexture, sampler_PointClamp, input.texcoord);
                return half4(lerp(color.rgb, outline.rgb, outline.a), color.a);
            }
            ENDHLSL
        }
    }
}
