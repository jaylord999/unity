// A soft additive glow that always faces the camera (true billboard) and is radial,
// so it reads as a round glow that can be placed right under a grass sprite.
// No per-frame script needed - the shader does the billboarding.
Shader "MysticMap/BillboardGlow"
{
    Properties
    {
        _Color ("Glow Color", Color) = (0.3, 1.0, 0.6, 1)
        _Intensity ("Glow Strength", Float) = 1
        _Falloff ("Falloff", Float) = 3
        _Size ("Diameter (m)", Float) = 2
        _Aspect ("Height / width", Float) = 0.6
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
            float _Size;
            float _Aspect;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // World center of this quad.
                float3 cw = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                // Center in view space, then expand in the screen plane -> true billboard.
                float3 cv = UnityWorldToViewPos(cw);
                float3 p = cv + float3(v.vertex.x * _Size, v.vertex.y * _Size * _Aspect, 0.0);
                o.pos = mul(UNITY_MATRIX_P, float4(p, 1.0));
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Radial: strongest at the center, fading to the rim.
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
