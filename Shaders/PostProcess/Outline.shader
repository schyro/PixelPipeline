Shader "Hidden/PixelPipeline/Outline"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _LineDarken;
            float _LineAlpha;
            float _CreaseBrighten;
            float _CreaseAlpha;
            float _KernelRadius;
            float _DepthThreshold;
            float _GrazingAngleStart;
            float _GrazingDepthScale;
            float _CreaseDepthLow;
            float _CreaseDepthHigh;
            float _CreaseNormalLow;
            float _CreaseNormalHigh;

            // Grass and foliage write this impossible normal into DepthNormals to opt out of outlines.
            // It is tested on the raw texture value, which SampleSceneNormals returns unchanged here.
            float IsOutlineExcluded(float3 encodedNormal)
            {
                return encodedNormal.r > 0.98 &&
                       abs(encodedNormal.g) < 0.02 &&
                       encodedNormal.b > 0.98;
            }

            float GetEyeDepth(float rawDepth)
            {
                return unity_OrthoParams.w > 0.5
                    ? LinearDepthToEyeDepth(rawDepth)
                    : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelStep = _BlitTexture_TexelSize.xy * _KernelRadius;
                static const int2 offsets[9] =
                {
                    int2(-1, -1), int2(0, -1), int2(1, -1),
                    int2(-1,  0), int2(0,  0), int2(1,  0),
                    int2(-1,  1), int2(0,  1), int2(1,  1)
                };

                float depths[9];
                float3 normals[9];
                float2 sampleUvs[9];
                float excluded[9];

                // The centre decides whether the pixel can carry a line at all, so an excluded pixel
                // (a large share of the screen under grass and foliage) skips the other 16 taps.
                float3 centreEncodedNormal = SampleSceneNormals(saturate(uv));
                if (IsOutlineExcluded(centreEncodedNormal) > 0.5)
                    return half4(0.0, 0.0, 0.0, 0.0);

                [unroll]
                for (int index = 0; index < 9; index++)
                {
                    sampleUvs[index] = saturate(uv + float2(offsets[index]) * texelStep);
                    float3 encodedNormal = index == 4 ? centreEncodedNormal : SampleSceneNormals(sampleUvs[index]);
                    depths[index] = GetEyeDepth(SampleSceneDepth(sampleUvs[index]));
                    normals[index] = normalize(encodedNormal);
                    excluded[index] = IsOutlineExcluded(encodedNormal);
                }

                float3 centerNormal = normals[4];
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, centerNormal);
                float grazing = 1.0 - abs(viewNormal.z);
                float grazingRange = max(0.0001, 1.0 - _GrazingAngleStart);
                float grazingWeight = saturate((grazing - _GrazingAngleStart) / grazingRange);
                float silhouetteThreshold = _DepthThreshold * (1.0 + grazingWeight * _GrazingDepthScale);

                bool hasSilhouette = false;
                int closestIndex = 4;
                float closestDepth = depths[4];
                static const int neighbors[8] = { 0, 1, 2, 3, 5, 6, 7, 8 };

                [unroll]
                for (int neighbor = 0; neighbor < 8; neighbor++)
                {
                    int index = neighbors[neighbor];
                    if (excluded[index] > 0.5)
                        continue;

                    if (depths[4] - depths[index] > silhouetteThreshold)
                        hasSilhouette = true;

                    if (depths[index] < closestDepth)
                    {
                        closestDepth = depths[index];
                        closestIndex = index;
                    }
                }

                float cardinalDepthDifference =
                    abs(depths[1] - depths[4]) +
                    abs(depths[3] - depths[4]) +
                    abs(depths[5] - depths[4]) +
                    abs(depths[7] - depths[4]);
                float silhouetteSuppression = smoothstep(
                    _CreaseDepthLow,
                    _CreaseDepthHigh,
                    cardinalDepthDifference);

                float topDifference = excluded[1] > 0.5 ? 0.0 : 1.0 - dot(centerNormal, normals[1]);
                float leftDifference = excluded[3] > 0.5 ? 0.0 : 1.0 - dot(centerNormal, normals[3]);
                float rightDifference = excluded[5] > 0.5 ? 0.0 : 1.0 - dot(centerNormal, normals[5]);
                float bottomDifference = excluded[7] > 0.5 ? 0.0 : 1.0 - dot(centerNormal, normals[7]);
                float directionalContrast = max(
                    abs(topDifference - bottomDifference),
                    abs(leftDifference - rightDifference));
                float creaseWeight = smoothstep(
                    _CreaseNormalLow,
                    _CreaseNormalHigh,
                    directionalContrast) * (1.0 - silhouetteSuppression);

                if (hasSilhouette)
                {
                    float3 neighborColor = SAMPLE_TEXTURE2D(
                        _BlitTexture,
                        sampler_LinearClamp,
                        sampleUvs[closestIndex]).rgb;
                    return half4(neighborColor * _LineDarken, _LineAlpha);
                }

                if (creaseWeight > 0.0)
                {
                    float3 centerColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
                    return half4(centerColor * (1.0 + _CreaseBrighten), creaseWeight * _CreaseAlpha);
                }

                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
