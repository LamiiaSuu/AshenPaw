Shader "Ashen Paw/Wolf Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _FlashColor ("Flash Tint", Color) = (1,1,1,0)
        _WhiteCutoff ("White Background Cutoff", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _FlashColor;
            half _WhiteCutoff;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 pixel = tex2D(_MainTex, input.uv);
                half low = min(pixel.r, min(pixel.g, pixel.b));
                half high = max(pixel.r, max(pixel.g, pixel.b));
                clip(max(_WhiteCutoff - low, high - low - 0.12));
                fixed4 color = pixel * input.color;
                color.rgb = lerp(color.rgb, _FlashColor.rgb, _FlashColor.a);
                return color;
            }
            ENDCG
        }
    }
}
