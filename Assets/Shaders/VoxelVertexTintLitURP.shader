Shader "Custom/VoxelVertexTintLitURP"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0,1)) = 0.0
        _SpecularStrength("Specular Strength", Range(0,1)) = 0.06
        _AmbientBoost("Ambient Boost", Range(0,2)) = 0.9
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Smoothness;
                float _SpecularStrength;
                float _AmbientBoost;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.shadowCoord = TransformWorldToShadowCoord(o.positionWS);
                o.color = v.color;
                return o;
            }

            half SpecTerm(half3 n, half3 v, half3 l, half smoothness)
            {
                half3 h = normalize(l + v);
                half ndoth = saturate(dot(n, h));
                half p = lerp(8.0h, 96.0h, smoothness);
                return pow(ndoth, p);
            }

            half FaceShade(half3 n)
            {
                half ay = abs(n.y);
                half isUp = step(0.9h, n.y);
                half isDown = step(0.9h, -n.y);
                half isSide = 1.0h - max(isUp, isDown);
                return isUp * 1.0h + isSide * 0.82h + isDown * 0.55h;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * i.color.rgb;

                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));

                half face = FaceShade(n);

                half3 sh = SampleSH(n) * _AmbientBoost;

                Light mainLight = GetMainLight(i.shadowCoord);
                half3 ldir = normalize(mainLight.direction);
                half ndotl = saturate(dot(n, ldir));

                half3 diffuse = albedo * (mainLight.color * ndotl * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half spec = _SpecularStrength * mainLight.distanceAttenuation * mainLight.shadowAttenuation * SpecTerm(n, v, ldir, _Smoothness);
                half3 specCol = spec * mainLight.color;

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    half3 ld = normalize(l.direction);
                    half ndl = saturate(dot(n, ld));
                    diffuse += albedo * (l.color * ndl * l.distanceAttenuation * l.shadowAttenuation);
                    half s = _SpecularStrength * l.distanceAttenuation * l.shadowAttenuation * SpecTerm(n, v, ld, _Smoothness);
                    specCol += s * l.color;
                }
                #endif

                half3 gi = albedo * sh;

                half3 col = (diffuse + gi + specCol) * face;
                return half4(col, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
