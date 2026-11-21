Shader "Ashen Paw/Stone Surface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StoneTex ("Stone", 2D) = "white" {}
        [Toggle] _Coping ("Use sprite height for coping", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {

            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _StoneTex;
            float4 _StoneTex_ST;
            float _Coping;

            struct SpriteVertex
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct SurfaceVertex
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
                fixed4 color : COLOR;
            };

            SurfaceVertex Vertex(SpriteVertex input)
            {
                SurfaceVertex output;
                output.position = UnityObjectToClipPos(input.position);
                output.world = mul(unity_ObjectToWorld, input.position).xy;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 Fragment(SurfaceVertex input) : SV_Target
            {
                float2 uv = input.world * _StoneTex_ST.xy + _StoneTex_ST.zw;
                uv.y = lerp(uv.y, saturate(input.uv.y) * _StoneTex_ST.y + _StoneTex_ST.w, _Coping);
                fixed4 stone = tex2D(_StoneTex, uv);
                float bottomShade = lerp(0.73, 1.0, smoothstep(0.0, 0.32, input.uv.y));
                stone.rgb *= bottomShade;
                stone.a *= tex2D(_MainTex, input.uv).a;
                return stone * input.color;
            }
            ENDCG
        }
    }
}
