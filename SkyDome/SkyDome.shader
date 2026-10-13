// SkyDome.shader
// Unlit texture shader with fog disabled and ZWrite off.
// Use this on the SkyDome mesh instead of Unlit/Texture so scene fog
// does not wash out the horizon band of the dome.
//
// _Exposure lets you tune brightness without editing the texture.
// 1.0 = no change.  Try 0.8 if the sky looks too bright.

Shader "Custom/SkyDome"
{
    Properties
    {
        _MainTex  ("Sky Texture", 2D)           = "white" {}
        _Exposure ("Exposure", Range(0.1, 2.0)) = 1.0
    }

    SubShader
    {
        // Render before all geometry so the dome never z-fights with terrain.
        // ZWrite Off means it never blocks other objects in the depth buffer.
        Tags { "Queue" = "Background" "RenderType" = "Background" }
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            // Deliberately no #pragma multi_compile_fog — fog is skipped.

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float     _Exposure;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                col.rgb *= _Exposure;
                return col;
            }
            ENDCG
        }
    }
}
