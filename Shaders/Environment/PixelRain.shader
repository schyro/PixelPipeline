// Rain drops. Every drop is procedural: RainDrops.hlsl turns the instance id and the time into the
// point the drop lands on and when, and this draws the streak falling along the rain direction
// towards it. The splash shader derives the same drops, so each splash plays where its drop ends.
Shader "PixelPipeline/PixelRain"
{
    Properties
    {
        [HDR] _RainColor ("Rain Color", Color) = (0.58, 0.78, 1, 0.62)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PixelRain"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Environment/RainDrops.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RainColor;
            CBUFFER_END

            // Uploaded by PixelRainRenderer.
            // x: minimum width, y: maximum width, z: streak length at the average speed, w: average speed.
            float4 _PixelRainDropShape;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // x: across the streak, y: 0 at the tail, 1 at the head.
                float2 uv : TEXCOORD0;
            };

            static const float2 QuadCorners[6] =
            {
                float2(-1.0, 0.0), float2(-1.0, 1.0), float2(1.0, 0.0),
                float2(1.0, 0.0), float2(-1.0, 1.0), float2(1.0, 1.0)
            };

            Varyings Collapsed()
            {
                Varyings output = (Varyings)0;
                // Outside the clip volume on every platform, so the triangle is discarded.
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                return output;
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                PixelRainDrop drop;
                if (!PixelRainGetDrop(instanceID, drop))
                    return Collapsed();

                // Distances along the path, measured back up from the landing point.
                float pathLength = _PixelRainTiming.w;
                float streak = _PixelRainDropShape.z * drop.speed / _PixelRainDropShape.w;
                float head = -drop.sinceLanding * drop.speed;
                float tail = head + streak;
                if (head > pathLength || tail <= 0.0)
                    return Collapsed();

                // The streak grows out of nothing where the drop appears. On a surface its head stops
                // there and the rest of it runs in while the splash starts; with nothing under it,
                // it falls on through.
                tail = min(tail, pathLength);
                if (drop.onSurface)
                    head = max(head, 0.0);

                float3 fall = _PixelRainFall.xyz;
                float3 across = cross(fall, GetWorldSpaceNormalizeViewDir(drop.landingWS));
                across = dot(across, across) > 1e-6 ? normalize(across) : UNITY_MATRIX_V[0].xyz;
                float width = lerp(_PixelRainDropShape.x, _PixelRainDropShape.y, GenerateHashedRandomFloat(drop.seed + 4u));

                float2 corner = QuadCorners[vertexID % 6];
                float3 positionWS = drop.landingWS
                                  - fall * lerp(head, tail, corner.y)
                                  + across * (corner.x * width * 0.5);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = float2(corner.x * 0.5 + 0.5, 1.0 - corner.y);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half width = 1.0h - smoothstep(0.18h, 0.5h, abs(input.uv.x - 0.5h));
                half ends = smoothstep(0.0h, 0.12h, input.uv.y)
                          * smoothstep(0.0h, 0.18h, 1.0h - input.uv.y);
                return half4(_RainColor.rgb * _PixelRainLight.rgb, _RainColor.a * width * ends);
            }
            ENDHLSL
        }
    }
}
