Shader "CARE-XR/Experiment Setup Waiting Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.10, 0.42, 0.73, 1)
        _HorizonColor ("Horizon", Color) = (0.55, 0.78, 0.91, 1)
        _LowerColor ("Lower Sky", Color) = (0.28, 0.58, 0.72, 1)
        _CloudColor ("Cloud", Color) = (0.88, 0.92, 0.94, 1)
        _CloudSpeed ("Cloud Drift", Range(0, 0.04)) = 0.009
        _CloudCoverage ("Cloud Coverage", Range(0.35, 0.8)) = 0.57
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WaitingSky"
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _LowerColor;
                half4 _CloudColor;
                float _CloudSpeed;
                float _CloudCoverage;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, local.x);
                float nx10 = lerp(n010, n110, local.x);
                float nx01 = lerp(n001, n101, local.x);
                float nx11 = lerp(n011, n111, local.x);
                float nxy0 = lerp(nx00, nx10, local.y);
                float nxy1 = lerp(nx01, nx11, local.y);
                return lerp(nxy0, nxy1, local.z);
            }

            float Fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int octave = 0; octave < 5; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    p = p * 2.03 + float3(7.1, 3.7, 5.9);
                    amplitude *= 0.5;
                }
                return value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float upper = saturate(direction.y);
                float lower = saturate(-direction.y);
                float horizon = pow(saturate(1.0 - abs(direction.y)), 3.0);

                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(upper, 0.55));
                sky = lerp(sky, _LowerColor.rgb, pow(lower, 0.45));
                sky += horizon * half3(0.035, 0.045, 0.05);

                // Clouds drift independently while the horizon remains fixed. The
                // low speed is intentional for a comfortable headset waiting view.
                float timeOffset = _Time.y * _CloudSpeed;
                float3 cloudPoint = direction * 3.7 + float3(timeOffset, 0.0, timeOffset * 0.32);
                float broadCloud = Fbm(cloudPoint);
                float cloudDetail = Fbm(cloudPoint * 1.83 + float3(9.4, 1.7, 4.2));
                float cloudField = broadCloud * 0.78 + cloudDetail * 0.22;
                float altitudeMask = smoothstep(-0.08, 0.12, direction.y);
                float cloud = smoothstep(_CloudCoverage, _CloudCoverage + 0.13, cloudField);
                cloud *= altitudeMask;

                float softEdge = saturate(cloud * (0.72 + broadCloud * 0.28));
                half3 cloudShade = _CloudColor.rgb * lerp(0.82, 1.0, saturate(direction.y + 0.35));
                sky = lerp(sky, cloudShade, softEdge * 0.88);

                float3 sunDirection = normalize(float3(-0.42, 0.52, 0.74));
                float sunFacing = saturate(dot(direction, sunDirection));
                float sunGlow = pow(sunFacing, 28.0) * 0.10 + pow(sunFacing, 180.0) * 0.08;
                sky += sunGlow * half3(1.0, 0.91, 0.70);

                return half4(saturate(sky), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
