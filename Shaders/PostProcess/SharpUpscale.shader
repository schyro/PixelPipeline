Shader "Hidden/PixelPipeline/SharpUpscale"
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

            float4 _PixelPanOffset;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord + _PixelPanOffset.xy;
                float2 sourceTexel = _BlitTexture_TexelSize.xy;
                float2 footprint = clamp(fwidth(uv) / sourceTexel, 1e-5, 1.0);
                float2 grid = uv / sourceTexel - 0.5 * footprint;
                float2 blend = smoothstep(1.0 - footprint, 1.0, frac(grid));
                float2 sampleUv = (floor(grid) + 0.5 + blend) * sourceTexel;
                return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, sampleUv, 0);
            }
            ENDHLSL
        }
    }
}
