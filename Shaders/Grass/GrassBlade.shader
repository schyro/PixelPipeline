Shader "PixelPipeline/GrassBlade"
{
    Properties
    {
        _GrassTexture ("Grass Sprite", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.35, 0.55, 0.2, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        _Color2 ("Color 2 (Patch)", Color) = (0.3, 0.5, 0.2, 1)
        _Noise2 ("Noise 2", 2D) = "white" {}
        _Noise2Scale ("Noise 2 Scale", Range(0.0, 1.0)) = 0.005
        _Noise2Threshold ("Noise 2 Threshold", Range(0.0, 1.0)) = 0.604

        _Color3 ("Color 3 (Patch)", Color) = (0.25, 0.45, 0.15, 1)
        _Noise3 ("Noise 3", 2D) = "white" {}
        _Noise3Scale ("Noise 3 Scale", Range(0.0, 0.1)) = 0.003
        _Noise3Threshold ("Noise 3 Threshold", Range(0.0, 1.0)) = 0.661

        _AccentTexture1 ("Accent 1 Sprite", 2D) = "white" {}
        _AccentColor1 ("Accent 1 Color", Color) = (0.4, 0.6, 0.1, 1)
        _AccentFrequency1 ("Accent 1 Frequency", Range(0.0, 0.05)) = 0.001
        _AccentHeight1 ("Accent 1 Height Offset", Range(0.0, 1.0)) = 0.5
        _AccentScale1 ("Accent 1 Scale", Range(0.0, 2.0)) = 1.0

        _AccentTexture2 ("Accent 2 Sprite", 2D) = "white" {}
        _AccentColor2 ("Accent 2 Color", Color) = (0.5, 0.7, 0.15, 1)
        _AccentFrequency2 ("Accent 2 Frequency", Range(0.0, 0.05)) = 0.1
        _AccentHeight2 ("Accent 2 Height Offset", Range(0.0, 1.0)) = 0.08
        _AccentScale2 ("Accent 2 Scale", Range(0.0, 2.0)) = 1.0

        _Cuts ("Cuts", Range(1, 8)) = 3
        _Steepness ("Steepness", Range(1, 8)) = 1.0
        _Wrap ("Wrap", Range(-2.0, 2.0)) = 0.0
        _ThresholdGradientSize ("Threshold Gradient Size", Range(0.0, 1.0)) = 0.2

        _WindNoise ("Wind Noise", 2D) = "gray" {}
        _WindNoiseScale ("Wind Noise Scale", Range(0.0, 0.2)) = 0.071
        _WindNoiseSpeed ("Wind Noise Speed", Range(0.0, 0.2)) = 0.025
        _WindNoiseThreshold ("Wind Noise Threshold", Range(-1.0, 1.0)) = 0.365
        _WindSwayAngle ("Wind Sway Angle (degrees)", Range(0.0, 180.0)) = 60.0
        _NoiseDivergeAngle ("Noise Diverge Angle (degrees)", Range(0.0, 45.0)) = 10.0

        _FakePerspectiveScale ("Fake Perspective Scale", Range(-0.15, 0.6)) = 0.3

        [Toggle] _UseDither ("Use Dither", Float) = 0
        _DitherStrength ("Dither Strength", Range(0.0, 1.0)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.schyro.pixel-pipeline/Shaders/Grass/GrassBladeCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelAdditionalLights.hlsl"

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 instancePosWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalWS : NORMAL;
                float2 seeds : TEXCOORD3;
                float windNoiseSample : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float GLSLMod(float x, float y)
            {
                return x - y * floor(x / y);
            }

            // 4x4 Bayer dither.
            float BayerDither(float2 screenPos)
            {
                int x = int(fmod(screenPos.x, 4.0));
                int y = int(fmod(screenPos.y, 4.0));

                float bayer[16] = {
                     0.0,  8.0,  2.0, 10.0,
                    12.0,  4.0, 14.0,  6.0,
                     3.0, 11.0,  1.0,  9.0,
                    15.0,  7.0, 13.0,  5.0
                };
                return bayer[y * 4 + x] / 16.0;
            }

            Varyings vert(GrassBladeAttributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                GrassBlade blade = GrassBladeExpand(input);
                output.positionHCS = TransformWorldToHClip(blade.positionWS);
                output.instancePosWS = blade.instancePositionWS;
                output.uv = input.uv;
                output.seeds = blade.seeds;
                output.windNoiseSample = blade.windNoiseSample;

                float4 terrainNorm = UNITY_ACCESS_INSTANCED_PROP(InstancedProps, _TerrainNormal);
                output.normalWS = length(terrainNorm.xyz) < 0.1 ? float3(0, 1, 0) : terrainNorm.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 uv = GrassBladeSpriteUV(input.uv, input.windNoiseSample);
                half4 spriteColor = GrassBladeSampleSprite(input.seeds, uv);
                clip(spriteColor.a - _Cutoff);

                half3 albedo;
                if (input.seeds.x < _AccentFrequency1)
                {
                    albedo = _AccentColor1.rgb;
                }
                else if (input.seeds.y < _AccentFrequency2)
                {
                    albedo = _AccentColor2.rgb;
                }
                else
                {
                    float noise2Val = SAMPLE_TEXTURE2D(_Noise2, sampler_Noise2, input.instancePosWS.xz * _Noise2Scale).r;
                    float noise3Val = SAMPLE_TEXTURE2D(_Noise3, sampler_Noise3, input.instancePosWS.xz * _Noise3Scale).r;

                    albedo = _BaseColor.rgb;
                    if (noise2Val > _Noise2Threshold) albedo = _Color2.rgb;
                    if (noise3Val > _Noise3Threshold) albedo = _Color3.rgb;
                }

                float3 normalWS = normalize(input.normalWS);
                float3 billboardWS = input.instancePosWS + float3(0, 0.15, 0);
                billboardWS.xz += (input.seeds - 0.03) * 0.03;
                Light mainLight = GetMainLight();
                // Sampled from the shadow map at the blade root rather than from the screen-space
                // texture, which would hold the shadow of whatever blade happens to cover that pixel.
                mainLight.shadowAttenuation = PixelPipelineMainLightShadow(billboardWS);
                float diffuseAmount = dot(normalWS, mainLight.direction) + _Wrap;
                diffuseAmount *= _Steepness;

                if (_UseDither > 0.5)
                {
                    float2 screenPos = input.positionHCS.xy;
                    float bayerVal = BayerDither(screenPos);
                    diffuseAmount = saturate(diffuseAmount + (bayerVal - 0.5) * _DitherStrength * 0.25);
                }

                // Hybrid toon stepping.
                float cutsInv = 1.0 / float(_Cuts);
                float cut = cutsInv;

                float originalIndex = ceil(diffuseAmount * float(_Cuts));
                float originalStepped = saturate(originalIndex * cut);
                float diffuseStepped = saturate(diffuseAmount + GLSLMod(1.0 - diffuseAmount, cutsInv));
                if (_ThresholdGradientSize > 0.0)
                {
                    float nearestK = floor(diffuseAmount / cut + 0.5);
                    float threshold = nearestK * cut;

                    if (nearestK >= 0.0 && nearestK <= float(_Cuts))
                    {
                        float halfWidth = 0.5 * cut * _ThresholdGradientSize;
                        float low = max(0.0, threshold - halfWidth);
                        float high = min(1.0, threshold + halfWidth);

                        float blend = 0.0;
                        if (high > low)
                            blend = smoothstep(low, high, diffuseAmount);
                        else
                            blend = step(threshold, diffuseAmount);
                        float leftValue = threshold;
                        float rightValue = min(threshold + cut, 1.0);
                        diffuseStepped = lerp(leftValue, rightValue, blend);
                        diffuseStepped = saturate(diffuseStepped);
                    }
                    else
                    {
                        diffuseStepped = originalStepped;
                    }
                }

                float3 finalLighting = diffuseStepped * mainLight.color * mainLight.shadowAttenuation;
                // Lit at the blade root like the shadow above, so a blade is one tone from root to tip.
                finalLighting += PixelPipelineAdditionalLights(
                    billboardWS,
                    GetNormalizedScreenSpaceUV(input.positionHCS),
                    normalWS,
                    _Wrap,
                    _Cuts);
                float3 ambient = SampleSH(normalWS);
                half3 finalColor = albedo * (finalLighting + ambient);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex GrassDepthVertex
            #pragma fragment GrassDepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.schyro.pixel-pipeline/Shaders/Grass/GrassBladeDepthPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GrassDepthVertex
            #pragma fragment GrassDepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.schyro.pixel-pipeline/Shaders/Grass/GrassBladeDepthPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.schyro.pixel-pipeline/Shaders/Grass/GrassBladeCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 seeds : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(GrassBladeAttributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                GrassBlade blade = GrassBladeExpand(input);
                output.positionHCS = TransformWorldToHClip(ApplyShadowBias(blade.positionWS, float3(0, 1, 0), _LightDirection));
                output.uv = input.uv;
                output.seeds = blade.seeds;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(GrassBladeSampleSprite(input.seeds, input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
