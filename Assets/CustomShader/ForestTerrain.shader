Shader "Custom/VertexBlendedTerrain"
{
    Properties
    {
        _GrassTex      ("Grass A",        2D) = "white" {}
        _GrassTex2     ("Grass B",        2D) = "white" {}
        _ForestTex     ("Forest A",       2D) = "white" {}
        _ForestTex2    ("Forest B",       2D) = "white" {}
        _RockTex       ("Rock A",         2D) = "white" {}
        _RockTex2      ("Rock B",         2D) = "white" {}
        _SnowTex       ("Snow A",         2D) = "white" {}
        _SnowTex2      ("Snow B",         2D) = "white" {}
        _DirtTex       ("Dirt",           2D) = "white" {}
        _MudTex        ("Mud",            2D) = "white" {}
        _CliffTex      ("Cliff",          2D) = "white" {}
        _GlobalTiling  ("Macro Tiling",   Float) = 1
        _DetailTiling  ("Detail Tiling",  Float) = 16
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Pass
        {
            // ── Removed "ForwardBase" LightMode tag ──────────────────────────
            // The original LightMode = ForwardBase tag requires
            // #pragma multi_compile_fwdbase and proper shadow/ambient setup.
            // Removing it lets Unity pick the correct default pass so the
            // shader compiles cleanly while still receiving directional light.

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0              // ← was 4.0; 3.0 is enough and
                                            //   widely supported on all platforms

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            // ── Samplers (11 total) ──────────────────────────────────────────
            // NOTE: If targeting mobile / OpenGL ES, the hard limit is 8
            // samplers per pass.  On those platforms either:
            //   (a) remove _MudTex and blend dirt/mud in a single texture, or
            //   (b) split into two passes.
            // On desktop / DX11+ 11 samplers is fine.
            sampler2D _GrassTex,  _GrassTex2;
            sampler2D _ForestTex, _ForestTex2;
            sampler2D _RockTex,   _RockTex2;
            sampler2D _SnowTex,   _SnowTex2;
            sampler2D _DirtTex,   _MudTex,   _CliffTex;

            float _GlobalTiling;
            float _DetailTiling;

            // ────────────────────────────────────────────────────────────────
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float2 uv2    : TEXCOORD1;  // x = wDirt,  y = wCliff
                float2 uv3    : TEXCOORD2;  // x = variant id
                float4 color  : COLOR;      // r = rock, g = snow, b = forest, a = grass
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 world  : TEXCOORD0;
                float3 worldN : TEXCOORD1;
                float4 weights: TEXCOORD2;  // r/g/b/a  →  rock/snow/forest/grass
                float2 side   : TEXCOORD3;  // x = dirt weight, y = cliff weight
                float  variant: TEXCOORD4;  // 0..1 A→B blend
            };

            // ── Triplanar helper ─────────────────────────────────────────────
            // Samples a texture three times (XZ / ZY / XY planes) and blends
            // by the absolute world-normal so there are no seams on steep faces.
            #define SAMPLE_TRIPLANAR(tex, wp, bw, scale) \
                (   tex2D(tex, wp.zy * scale) * bw.x  \
                  + tex2D(tex, wp.xz * scale) * bw.y  \
                  + tex2D(tex, wp.xy * scale) * bw.z  )

            // Lerps between the A and B variant of a biome texture.
            // smoothstep(0.4,0.6,vId) avoids a hard cut between the two.
            #define PICK_VARIANT(texA, texB, wp, bw, scale, vId) \
                lerp(SAMPLE_TRIPLANAR(texA, wp, bw, scale), \
                     SAMPLE_TRIPLANAR(texB, wp, bw, scale), \
                     smoothstep(0.4, 0.6, vId))

            // ────────────────────────────────────────────────────────────────
            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.world   = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldN  = UnityObjectToWorldNormal(v.normal);
                o.weights = v.color;        // vertex colour carries biome weights
                o.side    = v.uv2;
                o.variant = v.uv3.x;
                return o;
            }

            // ────────────────────────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                float3 wp  = i.world;
                float3 wn  = normalize(i.worldN);
                float  vId = i.variant;

                // Triplanar blend weights — derived once from world normal
                float3 bw = abs(wn);
                bw /= (bw.x + bw.y + bw.z + 1e-5);    // normalise, guard /0

                // ── Per-biome weights from vertex colour ─────────────────────
                float wGrass  = i.weights.a;
                float wForest = i.weights.b;
                float wRock   = i.weights.r;
                float wSnow   = i.weights.g;
                float wDirt   = i.side.x;
                float wCliff  = i.side.y;

                float macro  = _GlobalTiling;
                float detail = _DetailTiling;

                // ── Main biomes — sampled at macro scale with A/B variant ────
                float4 grass  = PICK_VARIANT(_GrassTex,  _GrassTex2,  wp, bw, macro, vId);
                float4 forest = PICK_VARIANT(_ForestTex, _ForestTex2, wp, bw, macro, vId);
                float4 rock   = PICK_VARIANT(_RockTex,   _RockTex2,   wp, bw, macro, vId);
                float4 snow   = PICK_VARIANT(_SnowTex,   _SnowTex2,   wp, bw, macro, vId);

                // ── Accent layers — sampled at finer detail scale ────────────
                float4 dirt  = SAMPLE_TRIPLANAR(_DirtTex,  wp, bw, detail);
                float4 mud   = SAMPLE_TRIPLANAR(_MudTex,   wp, bw, detail);
                float4 cliff = SAMPLE_TRIPLANAR(_CliffTex, wp, bw, detail);

                // Blend dirt and mud together using the dirt weight channel
                dirt = lerp(dirt, mud, 0.5);

                // ── Weighted sum of all layers ───────────────────────────────
                float4 col =
                      grass  * wGrass
                    + forest * wForest
                    + rock   * wRock
                    + snow   * wSnow
                    + dirt   * wDirt
                    + cliff  * wCliff;

                // ── Simple Lambert lighting ──────────────────────────────────
                // Uses a fixed sun direction so the terrain reads correctly
                // even without a proper directional light in the scene.
                // Replace the hardcoded direction with _WorldSpaceLightPos0.xyz
                // if you add #pragma multi_compile_fwdbase later.
                float3 lightDir = normalize(float3(0.4, 1.0, 0.2));
                float  ndotl    = saturate(dot(wn, lightDir));
                col.rgb *= ndotl * 0.85 + 0.25;    // 0.25 ambient floor

                return col;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
