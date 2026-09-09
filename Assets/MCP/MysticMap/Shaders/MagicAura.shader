// Soft additive "magic aura" glow: strongest in the middle, fades outward.
// Built-in pipeline friendly (no post-processing bloom needed).
Shader "MysticMap/MagicAura"
{
    Properties
    {
        _Color ("Glow Color", Color) = (0.3, 1.0, 0.6, 1)
        _Intensity ("Glow Strength", Float) = 1
        _Falloff ("Falloff (softness)", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _Falloff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // UV (0..1). Distance from the center -> 0 in the middle, 1 at the rim.
                float2 c = i.uv - 0.5;
                float r = length(c) * 2.0;
                float shape = pow(saturate(1.0 - r), _Falloff);
                fixed3 col = _Color.rgb * _Intensity * shape;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
