Shader "Custom/2WayRoadMarkings_Overlay"
{
    Properties
    {
        _MarkingsTex ("Markings Texture (RGBA)", 2D) = "white" {}
        _Offset ("Depth Offset", Float) = -1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+10"
        }

        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MarkingsTex;
            float4 _MarkingsTex_ST;

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
                o.uv = TRANSFORM_TEX(v.uv, _MarkingsTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MarkingsTex, i.uv);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
