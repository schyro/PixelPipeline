Shader "Hidden/PixelPipeline/PointDownsample"
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

            half4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0);
            }
            ENDHLSL
        }
    }
}
