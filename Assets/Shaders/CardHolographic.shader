// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Holographic Card Shader
//  Rarity-based card effects: shimmer, prismatic, holo
// ═══════════════════════════════════════════════════════
Shader "DualCraft/CardHolographic"
{
    Properties
    {
        _MainTex ("Card Artwork", 2D) = "white" {}
        _FrameTex ("Frame Texture", 2D) = "white" {}
        _ElementColor ("Element Color", Color) = (1, 0.45, 0.09, 1)
        _RarityLevel ("Rarity Level (0=common, 1=rare, 2=epic, 3=legendary)", Range(0, 3)) = 0
        _HoloIntensity ("Holographic Intensity", Range(0, 1)) = 0.5
        _ShimmerSpeed ("Shimmer Speed", Range(0, 5)) = 1.0
        _PrismaticStrength ("Prismatic Strength", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        LOD 200

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
            };

            sampler2D _MainTex;
            sampler2D _FrameTex;
            float4 _ElementColor;
            float _RarityLevel;
            float _HoloIntensity;
            float _ShimmerSpeed;
            float _PrismaticStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 artwork = tex2D(_MainTex, i.uv);
                fixed4 frame = tex2D(_FrameTex, i.uv);

                // Base card color
                fixed4 col = lerp(artwork, frame * _ElementColor, frame.a * 0.5);

                // Common: no effects
                if (_RarityLevel < 0.5)
                    return col;

                // Rare: shimmer sweep
                if (_RarityLevel < 1.5)
                {
                    float shimmer = sin((i.uv.x + i.uv.y) * 10.0 + _Time.y * _ShimmerSpeed) * 0.5 + 0.5;
                    col.rgb += shimmer * _HoloIntensity * 0.15 * _ElementColor.rgb;
                    return col;
                }

                // Epic: prismatic color shift
                if (_RarityLevel < 2.5)
                {
                    float angle = dot(i.viewDir, i.worldNormal);
                    float3 prism;
                    prism.r = sin(angle * 6.28 + _Time.y * 2.0) * 0.5 + 0.5;
                    prism.g = sin(angle * 6.28 + _Time.y * 2.0 + 2.09) * 0.5 + 0.5;
                    prism.b = sin(angle * 6.28 + _Time.y * 2.0 + 4.18) * 0.5 + 0.5;
                    col.rgb += prism * _PrismaticStrength;
                    return col;
                }

                // Legendary: full holographic + sparkle
                float fresnel = pow(1.0 - saturate(dot(i.viewDir, i.worldNormal)), 3.0);
                float3 holoColor;
                holoColor.r = sin(i.uv.x * 20.0 + _Time.y * 3.0) * 0.5 + 0.5;
                holoColor.g = sin(i.uv.y * 20.0 + _Time.y * 3.0 + 2.09) * 0.5 + 0.5;
                holoColor.b = sin((i.uv.x + i.uv.y) * 15.0 + _Time.y * 2.5 + 4.18) * 0.5 + 0.5;
                col.rgb += holoColor * fresnel * _HoloIntensity;

                // Sparkle particles
                float sparkle = frac(sin(dot(i.uv * 100.0 + _Time.y, float2(12.9898, 78.233))) * 43758.5453);
                if (sparkle > 0.98)
                    col.rgb += float3(1, 1, 1) * 0.8;

                // Gold border pulse
                float border = step(i.uv.x, 0.02) + step(1.0 - i.uv.x, 0.02) + step(i.uv.y, 0.02) + step(1.0 - i.uv.y, 0.02);
                float pulse = sin(_Time.y * 2.0) * 0.3 + 0.7;
                col.rgb += border * float3(0.98, 0.75, 0.14) * pulse * 0.5;

                return col;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
