// Settings window preview: the cloud shadow mask over a square of ground, evaluated with the same
// function and the same globals as the in-game cloud pass, optionally with the gaps god rays fall
// through drawn on top. Lives under Editor, so builds never see it.
Shader "Hidden/PixelPipeline/Editor/CloudShadowPreview"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/PostProcess/CloudCommon.hlsl"

            // xy: world XZ of the square's centre, z: its width in metres, w: ground height.
            float4 _PreviewArea;
            float4 _PreviewLitColor;
            float4 _PreviewShadowColor;
            float4 _PreviewGapColor;
            // x: 1 to draw gaps, y: gap threshold, z: gap softness, w: unused.
            float4 _PreviewGap;
            // x: mip level of the cloud around, y: mip level of the gap itself.
            float4 _PreviewGapLods;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 groundXZ = _PreviewArea.xy + (input.uv - 0.5) * _PreviewArea.z;
                float3 groundWS = float3(groundXZ.x, _PreviewArea.w, groundXZ.y);
                float transmission = PixelPipelineCloudTransmission(groundWS);
                half3 color = lerp(_PreviewShadowColor.rgb, _PreviewLitColor.rgb, transmission);

                // GodRayGap in GodRays.shader.
                if (_PreviewGap.x > 0.5)
                {
                    float here = PixelPipelineCloudShapeLod(groundWS, _PreviewGapLods.y);
                    float around = PixelPipelineCloudShapeLod(groundWS, _PreviewGapLods.x);
                    float gap = smoothstep(_PreviewGap.y, _PreviewGap.y + _PreviewGap.z, here - around);
                    color = lerp(color, _PreviewGapColor.rgb, gap * 0.85);
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
