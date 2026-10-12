Shader "PixelPipeline/WindStreak"
{
    Properties
    {
        [HDR] _StreakColor ("Streak Color", Color) = (0.85, 0.94, 1, 0.55)
        _StreakIntensity ("Intensity", Range(0, 4)) = 1
        _StreakTailWidth ("Tail Width Scale", Range(0, 1)) = 0.15
        _StreakHeadFade ("Head Fade", Range(0, 0.5)) = 0.06
        _StreakTailFade ("Tail Fade", Range(0, 1)) = 0.6
        _StreakEdgeSoftness ("Edge Softness", Range(0, 1)) = 0.35
        _StreakLifeFade ("Life Fade", Range(0.01, 0.5)) = 0.22
        _StreakAlphaSteps ("Alpha Steps", Range(0, 8)) = 3
        _StreakCurveAmplitude ("Curve Amplitude", Float) = 0.32
        _StreakCurveFrequency ("Curve Frequency", Range(0.05, 3)) = 0.55
        _StreakCurveScroll ("Curve Scroll Speed", Float) = 0.35
        _StreakCurveVertical ("Curve Vertical", Range(0, 1)) = 0.35
        _StreakTime ("Time", Float) = 0

        // Blend state is the one value the profile cannot push, because blending is baked into
        // the material rather than the property block. Additive by default.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "WindStreak"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define PIXEL_STREAK_TWO_PI 6.28318530718

            CBUFFER_START(UnityPerMaterial)
                half4 _StreakColor;
                float _StreakIntensity;
                float _StreakTailWidth;
                float _StreakHeadFade;
                float _StreakTailFade;
                float _StreakEdgeSoftness;
                float _StreakLifeFade;
                float _StreakAlphaSteps;
                float _StreakCurveAmplitude;
                float _StreakCurveFrequency;
                float _StreakCurveScroll;
                float _StreakCurveVertical;
                float _StreakTime;
            CBUFFER_END

            float4 _PixelWindDirection;

            // x: per-streak seed, y: normalised age, z: length in metres, w: head width in metres.
            UNITY_INSTANCING_BUFFER_START(WindStreakProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StreakData)
            UNITY_INSTANCING_BUFFER_END(WindStreakProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float life01 : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The spine of one streak. u is 0 at the leading head and 1 at the tail, so the whole
            // ribbon trails behind the simulated point and the curve stays anchored to the head.
            float3 StreakSpine(
                float u,
                float3 headWS,
                float3 forwardWS,
                float3 lateralWS,
                float streakLength,
                float seed)
            {
                float scroll = _StreakCurveScroll * _StreakTime * PIXEL_STREAK_TWO_PI;
                float headAngle = seed * PIXEL_STREAK_TWO_PI - scroll;
                float angle = headAngle + u * _StreakCurveFrequency * PIXEL_STREAK_TWO_PI;

                // Subtracting the head sample keeps the offset zero at u = 0, so the curve reads as
                // a path the head has already travelled instead of a sideways slide of the whole streak.
                float lateral = (sin(angle) - sin(headAngle)) * _StreakCurveAmplitude;
                float vertical = (sin(angle * 0.63 + 1.7) - sin(headAngle * 0.63 + 1.7))
                               * _StreakCurveAmplitude * _StreakCurveVertical;

                return headWS
                     - forwardWS * (u * streakLength)
                     + lateralWS * lateral
                     + float3(0.0, vertical, 0.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 data = UNITY_ACCESS_INSTANCED_PROP(WindStreakProps, _StreakData);
                float3 headWS = UNITY_MATRIX_M._m03_m13_m23;
                float3 forwardWS = normalize(float3(_PixelWindDirection.x, 0.0, _PixelWindDirection.z));
                float3 lateralWS = normalize(cross(float3(0.0, 1.0, 0.0), forwardWS));

                float u = input.positionOS.x;
                float side = input.positionOS.y;

                float3 spineWS = StreakSpine(u, headWS, forwardWS, lateralWS, data.z, data.x);
                float3 aheadWS = StreakSpine(u + 0.02, headWS, forwardWS, lateralWS, data.z, data.x);
                float3 tangentWS = normalize(aheadWS - spineWS);

                // Orthographic cameras have no eye position worth using, so the ribbon is flattened
                // against the view plane instead.
                float3 viewAxisWS = float3(UNITY_MATRIX_V._m20, UNITY_MATRIX_V._m21, UNITY_MATRIX_V._m22);
                float3 toEyeWS = unity_OrthoParams.w > 0.5
                    ? viewAxisWS
                    : normalize(GetCameraPositionWS() - spineWS);

                float3 widthAxisWS = cross(tangentWS, toEyeWS);
                float widthAxisLength = length(widthAxisWS);
                widthAxisWS = widthAxisLength > 1e-4
                    ? widthAxisWS / widthAxisLength
                    : float3(0.0, 1.0, 0.0);

                float width = data.w * lerp(1.0, _StreakTailWidth, u);
                float3 positionWS = spineWS + widthAxisWS * (side * width);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = float2(u, input.uv.y);
                output.life01 = data.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float u = input.uv.x;

                float edge = abs(input.uv.y * 2.0 - 1.0);
                float inner = saturate(1.0 - _StreakEdgeSoftness);
                float across = 1.0 - smoothstep(inner, max(inner + 1e-4, 1.0), edge);

                float head = _StreakHeadFade > 1e-4 ? smoothstep(0.0, _StreakHeadFade, u) : 1.0;
                float tail = _StreakTailFade > 1e-4 ? smoothstep(0.0, _StreakTailFade, 1.0 - u) : 1.0;
                float life = smoothstep(0.0, _StreakLifeFade, input.life01)
                           * smoothstep(0.0, _StreakLifeFade, 1.0 - input.life01);

                float alpha = saturate(_StreakColor.a * _StreakIntensity * across * head * tail * life);

                // Ceil rather than round: a thin streak keeps its lowest band instead of vanishing
                // once the ramp is quantised for the pixel look.
                if (_StreakAlphaSteps >= 1.0)
                    alpha = ceil(alpha * _StreakAlphaSteps) / _StreakAlphaSteps;

                clip(alpha - 0.003);
                return half4(_StreakColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
