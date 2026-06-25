Shader "Custom/ForestTerrain"
{
    Properties
    {
        [Header(Albedo Textures)]
        _GrassTex    ("Plain Grass  (A)",    2D) = "white" {}
        _ForestTex   ("Forest Floor (B)",    2D) = "white" {}
        _BankTex     ("River Bank   (G)",    2D) = "white" {}
        _RockTex     ("Mountain Rock(R)",    2D) = "gray"  {}
        _SnowTex     ("Snow (height blend)", 2D) = "white" {}
        _WaterTex    ("Water Texture",       2D) = "blue"  {}

        [Header(Normal Maps)]
        _GrassNorm   ("Grass Normal",        2D) = "bump"  {}
        _ForestNorm  ("Forest Normal",       2D) = "bump"  {}
        _BankNorm    ("Bank Normal",         2D) = "bump"  {}
        _RockNorm    ("Rock Normal",         2D) = "bump"  {}
        _SnowNorm    ("Snow Normal",         2D) = "bump"  {}
        _WaterNorm   ("Water Normal",        2D) = "bump"  {}

        [Header(Settings)]
        _TexScale    ("World Texture Scale", Float)      = 20.0
        _NormalStr   ("Normal Strength",     Range(0,2)) = 0.8
        _SnowStart   ("Snow Start Height",   Float)      = 140.0
        _SnowEnd     ("Snow End Height",     Float)      = 180.0
        _Smoothness  ("Smoothness",          Range(0,1)) = 0.12
        _SlopeRock   ("Slope -> Rock Start", Range(0,1)) = 0.45
        _WaterSmooth ("Water Smoothness",    Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassTex);  SAMPLER(sampler_GrassTex);
            TEXTURE2D(_ForestTex); SAMPLER(sampler_ForestTex);
            TEXTURE2D(_BankTex);   SAMPLER(sampler_BankTex);
            TEXTURE2D(_RockTex);   SAMPLER(sampler_RockTex);
            TEXTURE2D(_SnowTex);   SAMPLER(sampler_SnowTex);
            TEXTURE2D(_WaterTex);  SAMPLER(sampler_WaterTex);

            TEXTURE2D(_GrassNorm); SAMPLER(sampler_GrassNorm);
            TEXTURE2D(_ForestNorm);SAMPLER(sampler_ForestNorm);
            TEXTURE2D(_BankNorm);  SAMPLER(sampler_BankNorm);
            TEXTURE2D(_RockNorm);  SAMPLER(sampler_RockNorm);
            TEXTURE2D(_SnowNorm);  SAMPLER(sampler_SnowNorm);
            TEXTURE2D(_WaterNorm); SAMPLER(sampler_WaterNorm);

            CBUFFER_START(UnityPerMaterial)
                float _TexScale;
                float _NormalStr;
                float _SnowStart;
                float _SnowEnd;
                float _Smoothness;
                float _SlopeRock;
                float _WaterSmooth;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float4 tang   : TANGENT;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct Varyings
            {
                float4 posCS    : SV_POSITION;
                float3 posWS    : TEXCOORD0;
                float3 normWS   : TEXCOORD1;
                float3 tangWS   : TEXCOORD2;
                float3 btanWS   : TEXCOORD3;
                float4 color    : TEXCOORD4;
                float  fogCoord : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.posOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normOS, IN.tang);

                OUT.posCS    = vpi.positionCS;
                OUT.posWS    = vpi.positionWS;
                OUT.normWS   = vni.normalWS;
                OUT.tangWS   = vni.tangentWS;
                OUT.btanWS   = vni.bitangentWS;
                OUT.color    = IN.color;
                OUT.fogCoord = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half3 BlendNormal(half3 tsNorm, float3 tWS, float3 bWS, float3 nWS, float str)
            {
                tsNorm.xy *= str;
                return normalize(tsNorm.x * tWS + tsNorm.y * bWS + tsNorm.z * nWS);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 wUV = IN.posWS.xz / _TexScale;

                // ── Sample all textures & normals ──
                half4 aGrass  = SAMPLE_TEXTURE2D(_GrassTex,  sampler_GrassTex,  wUV);
                half4 aForest = SAMPLE_TEXTURE2D(_ForestTex, sampler_ForestTex, wUV);
                half4 aBank   = SAMPLE_TEXTURE2D(_BankTex,   sampler_BankTex,   wUV);
                half4 aRock   = SAMPLE_TEXTURE2D(_RockTex,   sampler_RockTex,   wUV);
                half4 aSnow   = SAMPLE_TEXTURE2D(_SnowTex,   sampler_SnowTex,   wUV);
                half4 aWater  = SAMPLE_TEXTURE2D(_WaterTex,  sampler_WaterTex,  wUV);

                half3 nGrass  = UnpackNormal(SAMPLE_TEXTURE2D(_GrassNorm,  sampler_GrassNorm,  wUV));
                half3 nForest = UnpackNormal(SAMPLE_TEXTURE2D(_ForestNorm, sampler_ForestNorm, wUV));
                half3 nBank   = UnpackNormal(SAMPLE_TEXTURE2D(_BankNorm,   sampler_BankNorm,   wUV));
                half3 nRock   = UnpackNormal(SAMPLE_TEXTURE2D(_RockNorm,   sampler_RockNorm,   wUV));
                half3 nSnow   = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNorm,   sampler_SnowNorm,   wUV));
                half3 nWater  = UnpackNormal(SAMPLE_TEXTURE2D(_WaterNorm,  sampler_WaterNorm,  wUV));

                // ── Unpack biome weights from vertex color ──
                // R = mountain, G = bank, B = forest, A = water (or grass if water=0)
                float wRock     = saturate(IN.color.r);
                float wBank     = saturate(IN.color.g);
                float wForest   = saturate(IN.color.b);
                float wWater    = saturate(IN.color.a);  // 0 = no water, 1 = full water
                
                // If wWater is 0, alpha was used for grass
                // We need a separate way to get grass weight
                // For simplicity, if water is 0, compute grass from inverse
                float wGrass    = (1.0f - wRock - wBank - wForest);
                wGrass = saturate(wGrass * (1.0f - wWater));

                // ── Slope-based rock override ──
                float3 geomN   = normalize(IN.normWS);
                float  slopeUp = saturate(dot(geomN, float3(0,1,0)));
                float  slopeRock = saturate(((_SlopeRock - slopeUp) / max(_SlopeRock, 0.0001f)));
                wRock   = max(wRock, slopeRock);
                wGrass  *= (1.0f - slopeRock);
                wForest *= (1.0f - slopeRock);
                wBank   *= (1.0f - slopeRock);

                // ── Snow blend by height ──
                float snowBlend = saturate((IN.posWS.y - _SnowStart) / max(_SnowEnd - _SnowStart, 0.001f));
                snowBlend *= saturate((slopeUp - 0.4f) / 0.6f);
                // No snow on water or river banks
                snowBlend *= (1.0f - wWater);

                // ── Normalize land weights ──
                float total = wRock + wBank + wForest + wGrass;
                if (total < 0.0001f) total = 1.0f;
                wRock /= total;
                wBank /= total;
                wForest /= total;
                wGrass /= total;

                // ── Blend albedo for land ──
                half4 albedo = aGrass  * wGrass
                             + aForest * wForest
                             + aBank   * wBank
                             + aRock   * wRock;

                // ── Apply snow ──
                albedo = lerp(albedo, aSnow, snowBlend);

                // ── Blend normals for land ──
                half3 tsN = nGrass  * wGrass
                          + nForest * wForest
                          + nBank   * wBank
                          + nRock   * wRock;
                tsN = lerp(tsN, nSnow, snowBlend);
                tsN = normalize(tsN);

                // ── Apply water (overrides everything if > 0) ──
                if (wWater > 0.01f)
                {
                    albedo = lerp(albedo, aWater, wWater);
                    tsN    = lerp(tsN, nWater, wWater);
                }

                // ── Build world-space normal ──
                float3 N = BlendNormal(tsN, IN.tangWS, IN.btanWS, geomN, _NormalStr);

                // ── Build PBR inputs ──
                InputData id = (InputData)0;
                id.positionWS      = IN.posWS;
                id.normalWS        = N;
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.posWS);
                id.shadowCoord     = TransformWorldToShadowCoord(IN.posWS);
                id.fogCoord        = IN.fogCoord;

                SurfaceData sd = (SurfaceData)0;
                sd.albedo     = albedo.rgb;
                sd.normalTS   = tsN;
                sd.smoothness = lerp(_Smoothness, _WaterSmooth, wWater);
                sd.occlusion  = 1.0f;
                sd.alpha      = 1.0f;

                half4 col = UniversalFragmentPBR(id, sd);
                col.rgb = MixFog(col.rgb, IN.fogCoord);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On  ColorMask 0
            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On  ColorMask 0
            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
