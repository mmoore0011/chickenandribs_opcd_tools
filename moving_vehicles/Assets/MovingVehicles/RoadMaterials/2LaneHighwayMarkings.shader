Shader "Custom/2LaneHighwayMarkings"
{
    Properties
    {
        _MainTex ("Asphalt Texture", 2D) = "gray" {}
        _Color ("Asphalt Tint", Color) = (0.45, 0.45, 0.45, 1)
        _MarkingsTex ("Markings Texture (RGBA)", 2D) = "black" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.08
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _MarkingsTex;
        fixed4 _Color;
        half _Smoothness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_MarkingsTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 asphalt = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 mark = tex2D(_MarkingsTex, IN.uv_MarkingsTex);
            o.Albedo = lerp(asphalt.rgb, mark.rgb, mark.a);
            o.Smoothness = _Smoothness;
            o.Metallic = _Metallic;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
