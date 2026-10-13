// PanoramaCylinderBlend.shader
// Unlit, alpha-TESTED (cutout) texture shader for Panorama Cylinder segments.
//
// Was originally alpha-BLENDED, but that left a faint visible line at the
// mesh's silhouette edge even where the texture is fully transparent — a
// well-known artifact where edge anti-aliasing blends partial coverage right
// at a mesh's geometric boundary regardless of its own alpha. Cutout avoids
// this entirely: a pixel is either fully drawn or fully discarded (clip()),
// so there's no partial-coverage edge left for anti-aliasing to blend into
// a visible seam.
//
// Deliberately NOT a built-in shader like Legacy Shaders/Transparent/
// Cutout/Diffuse — that one is LIT (Lambert diffuse), which made one side of
// the cylinder render flat grey wherever it faced away from the directional
// light. This mountain backdrop needs to look the same regardless of scene
// lighting, same reasoning SkyDome is unlit.
//
// Trade-off versus the old blended version: the overlap seam cross-fade
// (PanoramaCylinderBuilder's vertex-alpha 1→0 fade across a segment's
// overlap region) becomes a hard on/off cutoff at _Cutoff instead of a
// smooth gradient — clip() can't blend. Not a real loss for a single
// pre-stitched image (no overlap in use), but worth knowing if overlap
// blending across multiple tiles is needed again later.
//
// Two things this does that a plain Unlit/Transparent Cutout can't:
//  1. Clamps UV to [0,1] before sampling (saturate), so a segment mesh that
//     extends past its own tile's native slice (the "overlap" region used
//     for the seam cross-fade) just stretches that tile's own edge pixel
//     into the overlap, instead of wrapping/repeating the texture oddly.
//  2. Multiplies the sampled alpha by vertex color alpha (see trade-off
//     note above) before the cutoff test.

Shader "Custom/PanoramaCylinderBlend"
{
    Properties
    {
        _MainTex ("Tile Texture", 2D) = "white" {}
        _Cutoff  ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" }
        Cull Back
        // No Blend / ZWrite Off here — a cutout pixel is either fully
        // opaque or fully discarded, so it can use normal opaque-style
        // depth writing (unlike the old blended version, which needed
        // ZWrite Off and careful per-tile render-queue ordering to avoid
        // the overlap "ghosting" bug from two partially-transparent layers
        // compositing over each other).

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed     _Cutoff;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                o.color  = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 clampedUV = saturate(i.uv);
                fixed4 col = tex2D(_MainTex, clampedUV);
                col.a *= i.color.a;
                clip(col.a - _Cutoff);
                return col;
            }
            ENDCG
        }
    }
}
