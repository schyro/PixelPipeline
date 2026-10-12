// Used by PixelWaterShoreline to find the shore. Scene meshes are drawn looking straight down from the
// water plane, with the near plane on the water plane itself, so everything above the water is cut
// away. Two tests decide whether a texel is inside a solid, i.e. land:
//   pass 0 - the first surface below the plane faces down, so the view is looking at the underside
//            of a solid from inside it. Works for open meshes such as planes and terrain.
//   pass 1 - more surfaces facing down than up lie below the plane: the ray leaves more closed solids
//            than it enters. Works where solids overlap, such as a rock sunk into a ledge.
Shader "Hidden/PixelPipeline/WaterShoreMask"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float4 _ShoreMaskUp;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
        output.normalWS = TransformObjectToWorldNormal(input.normalOS, false);
        return output;
    }

    // Facing comes from the interpolated vertex normals, which are unreliable on surfaces seen nearly
    // edge-on, such as the equator of a smooth rock. Those are skipped: they come in entry/exit pairs
    // near a silhouette and would only flip the parity test.
    float Facing(Varyings input)
    {
        float facing = dot(normalize(input.normalWS), _ShoreMaskUp.xyz);
        clip(abs(facing) - 0.2);
        return facing;
    }

    bool FacesDown(Varyings input)
    {
        return Facing(input) < 0.0;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FirstSurface"
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half land = FacesDown(input) ? 1.0h : 0.0h;
                return half4(land, 0.0h, 0.0h, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SurfaceCount"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // r counts surfaces facing down, g surfaces facing up.
            half4 Frag(Varyings input) : SV_Target
            {
                return FacesDown(input) ? half4(1.0h, 0.0h, 0.0h, 0.0h) : half4(0.0h, 1.0h, 0.0h, 0.0h);
            }
            ENDHLSL
        }
    }
}
