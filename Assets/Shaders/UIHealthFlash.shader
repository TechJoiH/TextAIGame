Shader "UI/Health Flash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1, 0, 0, 0.35)
        _Mode ("Mode", Float) = 0
        _Progress ("Progress", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Softness ("Softness", Range(0.15, 4)) = 1.6
        _NoiseStrength ("Noise Strength", Range(0, 0.2)) = 0.04
        _Pulse ("Pulse", Range(0, 0.5)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            float _Mode;
            float _Progress;
            float _Intensity;
            float _Softness;
            float _NoiseStrength;
            float _Pulse;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color;
                return output;
            }

            float Hash(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 centerOffset = input.uv - 0.5;
                float centerDistance = saturate(length(centerOffset) * 2.0);
                float cornerDistance = saturate(length(abs(centerOffset) * 2.0) / 1.41421356);
                float softness = max(0.01, _Softness);

                float cornerGlow = pow(cornerDistance, softness);
                float centerGlow = pow(saturate(1.0 - centerDistance), softness);
                centerGlow += 0.24 * pow(saturate(1.0 - centerDistance), softness * 0.45);

                float damageMask = smoothstep(0.2, 1.0, cornerGlow);
                float healMask = saturate(centerGlow);
                float mask = lerp(damageMask, healMask, saturate(_Mode));

                float noise = Hash(floor(input.uv * 72.0) + floor(_Progress * 18.0));
                float pulse = 1.0 + sin(saturate(_Progress) * 3.14159265) * _Pulse;
                float alpha = saturate(mask + (noise - 0.5) * _NoiseStrength);

                fixed4 color = _TintColor * input.color;
                color.a = saturate(alpha * _TintColor.a * _Intensity * pulse) * input.color.a;
                return color;
            }
            ENDCG
        }
    }
}
