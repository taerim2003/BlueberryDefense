// 선택된 카드 바깥을 두르는 노란 테.
//
// 왜 셰이더인가:
// - uGUI `Outline`은 메쉬를 (±d,±d) 네 방향으로 복제할 뿐이라 곡선·모서리에서 두께가 어긋난다.
// - 판 그림을 확대해 뒤에 까는 방법도 안 된다 — 확대는 중심에서 먼 쪽이 더 벌어져서
//   가로로 긴 판은 좌우 테가 상하 테보다 두꺼워진다(실측 12.6px vs 4.7px).
// 여기서는 실루엣 밖 픽셀마다 "반경 _OutlineWidth(화면 px) 안에 실루엣이 있는가"를 직접 물어본다.
// 거리로 판정하므로 두께가 방향과 무관하게 일정하다.
//
// 판 그림(`..._색칠`)은 둘레에 투명 여백이 있어(좌우 26px·위 14px·아래 22px) 테가 그 안에 그려진다 —
// 오브젝트를 키우거나 복제할 필요가 없다.
//
// 색은 텍스처가 아니라 `Image.color`(정점 색)에서만 온다. 판 그림이 순수 검정이어도 노랗게 나온다.
Shader "UI/SelectOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width (screen px)", Range(0, 32)) = 20
        _OutlineSoftness ("Outline Softness (screen px)", Range(0, 16)) = 3

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _OutlineWidth;
            float _OutlineSoftness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 실루엣 안이면 1.
            // 🔴 임계값이 사실상 0인 이유: 판 그림 둘레에는 알파가 아주 옅은 픽셀이 몇 줄 있고
            //    **그것도 어두운 배경 위에서는 보인다.** 조금이라도 잘라내면 테가 그 띠 아래에서 시작해
            //    판에 가려지는데, 띠 두께가 방향마다 다르다(좌우 7텍셀·상하 3.6텍셀).
            //    그래서 반경이 등방인데도 보이는 두께가 좌우 9px·상하 14px로 어긋났다(실측).
            inline float Solid(float2 uv)
            {
                return step(0.004, tex2D(_MainTex, saturate(uv)).a);
            }

            // 방향 × 반경으로 훑어 실루엣까지의 거리를 잰다. 링을 여러 겹 두는 이유는
            // 바깥 가장자리를 부드럽게 빼기 위해서다 — 한 겹이면 거리를 모르고 있다/없다만 알 수 있어
            // 테가 딱 끊긴다.
            #define OUTLINE_DIRS  16
            #define OUTLINE_RINGS 12

            fixed4 frag(v2f IN) : SV_Target
            {
                // 반경은 **화면 픽셀** 기준이다. `_MainTex_TexelSize`를 쓰면 안 된다 —
                // uGUI는 스프라이트를 CanvasRenderer가 텍스처 슬롯에 직접 바인딩하므로
                // 머티리얼의 TexelSize가 채워지지 않는다. 반경이 0이 되어 테가 통째로 사라진다(실측).
                //
                // 🔴 `fwidth(u), fwidth(v)`로 만든 반경은 등방이 **아니다**. 그건 야코비안의 대각 성분만
                //    쓴 근사라, 가로로 긴 판에서 좌우 8px·상하 14px로 갈린다(실측).
                //    화면에서 진짜 원을 그리려면 두 미분 벡터를 각각 cos·sin으로 섞어야 한다.
                float2 duvdx = ddx(IN.texcoord);
                float2 duvdy = ddy(IN.texcoord);

                // 판 안쪽은 그릴 게 없다. 여기서 먼저 버려야 아래 이중 루프를 테 둘레에서만 돈다.
                clip(0.5 - Solid(IN.texcoord));

                // 가장 가까운 실루엣까지의 거리(0~1로 정규화, 못 찾으면 1).
                float nearest = 1.0;
                [unroll]
                for (int r = 1; r <= OUTLINE_RINGS; r++)
                {
                    float f = (float)r / (float)OUTLINE_RINGS;
                    float px = _OutlineWidth * f;   // 화면 픽셀 반경
                    float hit = 0.0;
                    [unroll]
                    for (int i = 0; i < OUTLINE_DIRS; i++)
                    {
                        float a = 6.28318530718 * (float)i / (float)OUTLINE_DIRS;
                        float2 uv = IN.texcoord + (duvdx * cos(a) + duvdy * sin(a)) * px;
                        hit = max(hit, Solid(uv));
                    }
                    nearest = min(nearest, lerp(1.0, f, hit));
                }

                // 안쪽은 꽉 찬 노랑, 바깥 _OutlineSoftness 구간만 서서히 사라진다.
                float dist = nearest * _OutlineWidth;
                fixed4 color = IN.color;
                color.a *= 1.0 - smoothstep(max(_OutlineWidth - _OutlineSoftness, 0.0), _OutlineWidth, dist);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                clip(color.a - 0.001);
                return color;
            }
        ENDCG
        }
    }
}
