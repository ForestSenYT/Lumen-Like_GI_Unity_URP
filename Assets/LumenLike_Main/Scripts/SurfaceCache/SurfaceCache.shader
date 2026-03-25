Shader "Hidden/LumenLike/SurfaceCache"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4x4 _CurrentInvViewProjectionMatrix;
            float4x4 _PrevViewProjectionMatrix;
            float4x4 _PrevInvViewProjectionMatrix;
            float _TemporalBlend;
            float _NormalReject;
            float _DepthReject;
            float _HasHistory;
            float _DebugExposure;
            float _DebugMode;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UpdateRadiance"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CurrentSurfaceCacheDepth);
            TEXTURE2D_X(_CurrentSurfaceCacheNormal);
            TEXTURE2D_X(_PrevSurfaceCacheRadiance);
            TEXTURE2D_X(_PrevSurfaceCacheNormal);
            TEXTURE2D_X(_PrevSurfaceCacheDepth);

            bool IsDepthValid(float depth)
            {
            #if UNITY_REVERSED_Z
                return depth > 0.00001;
            #else
                return depth < 0.99999;
            #endif
            }

            float ComputeHistoryValidity(float3 currentNormal, float3 prevNormal, float3 worldPos, float3 prevWorldPos)
            {
                float normalWeight = saturate((dot(currentNormal, prevNormal) - _NormalReject) / max(1.0 - _NormalReject, 0.0001));
                float3 deltaWS = prevWorldPos - worldPos;
                float planeDelta = abs(dot(deltaWS, currentNormal));
                float3 tangentDeltaWS = deltaWS - currentNormal * dot(deltaWS, currentNormal);
                float tangentDelta = length(tangentDeltaWS);
                float horizontalSurface = saturate(abs(currentNormal.y));
                float planeReject = max(_DepthReject, 0.0001);
                float tangentReject = max(planeReject * lerp(3.0, 12.0, horizontalSurface), 0.01);
                float planeWeight = saturate(1.0 - planeDelta / planeReject);
                float tangentWeight = saturate(1.0 - tangentDelta / tangentReject);
                float depthWeight = saturate(planeWeight * lerp(0.65, 1.0, tangentWeight));
                return saturate(normalWeight * depthWeight);
            }

            void AccumulateHistorySample(
                float2 sampleUv,
                float3 worldPos,
                float3 currentNormal,
                inout float3 radianceSum,
                inout float weightSum,
                inout float alphaSum,
                inout float validitySum,
                inout float validSampleCount,
                inout float bestValidity)
            {
                if (any(sampleUv < 0.0) || any(sampleUv > 1.0))
                {
                    return;
                }

                float prevDepth = SAMPLE_TEXTURE2D_X(_PrevSurfaceCacheDepth, sampler_PointClamp, sampleUv).r;
                if (!IsDepthValid(prevDepth))
                {
                    return;
                }

                float4 prevRadiance = SAMPLE_TEXTURE2D_X(_PrevSurfaceCacheRadiance, sampler_LinearClamp, sampleUv);
                float3 prevNormal = SafeNormalize(SAMPLE_TEXTURE2D_X(_PrevSurfaceCacheNormal, sampler_PointClamp, sampleUv).xyz);
                float3 prevWorldPos = ComputeWorldSpacePosition(sampleUv, prevDepth, _PrevInvViewProjectionMatrix);
                float validity = ComputeHistoryValidity(currentNormal, prevNormal, worldPos, prevWorldPos);
                if (validity <= 1e-4)
                {
                    return;
                }

                float sampleWeight = 0.15 + validity;
                radianceSum += prevRadiance.rgb * sampleWeight;
                weightSum += sampleWeight;
                alphaSum += prevRadiance.a * sampleWeight;
                validitySum += validity;
                validSampleCount += 1.0;
                bestValidity = max(bestValidity, validity);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 cache = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                if (_DebugMode > 2.5)
                {
                    return float4(cache.rgb * cache.a * _DebugExposure, 1.0);
                }

                if (_DebugMode > 1.5)
                {
                    return float4(cache.aaa, 1.0);
                }

                return float4(cache.rgb * _DebugExposure, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CopyNormal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_GBuffer2);

            half4 Frag(Varyings input) : SV_Target
            {
                float4 cache = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                if (_DebugMode > 2.5)
                {
                    return float4(cache.rgb * cache.a * _DebugExposure, 1.0);
                }

                if (_DebugMode > 1.5)
                {
                    return float4(cache.aaa, 1.0);
                }

                return float4(cache.rgb * _DebugExposure, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CopyDepth"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float4 cache = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                if (_DebugMode > 2.5)
                {
                    return float4(cache.rgb * cache.a * _DebugExposure, 1.0);
                }

                if (_DebugMode > 1.5)
                {
                    return float4(cache.aaa, 1.0);
                }

                return float4(cache.rgb * _DebugExposure, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DebugView"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CameraDepthNormalsTexture);
            SAMPLER(sampler_CameraDepthNormalsTexture);

            bool IsDebugDepthValid(float depth)
            {
            #if UNITY_REVERSED_Z
                return depth > 0.00001;
            #else
                return depth < 0.99999;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 cache = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                if (_DebugMode > 2.5)
                {
                    return float4(cache.rgb * cache.a * _DebugExposure, 1.0);
                }

                if (_DebugMode > 1.5)
                {
                    return float4(cache.aaa, 1.0);
                }

                return float4(cache.rgb * _DebugExposure, 1.0);
            }
            ENDHLSL
        }
    }
}


