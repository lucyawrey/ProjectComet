// Unlit lines in their vertex colors, for debug views. Each point is pulled a little toward the camera, so lines on a
// surface they outline (collision that matches its model) show instead of fighting it for depth; polygon offset
// doesn't apply to line primitives. _ZTest picks which lines show: LEqual those in sight, Greater those behind
// something, Always every line.
Shader "Comet/Wireframe"
{
    Properties
    {
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Wireframe"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
            CBUFFER_END

            // How far toward the camera points move, as a share of their distance.
            #define PULL 0.004

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += (GetCameraPositionWS() - positionWS) * PULL;
                output.positionCS = TransformWorldToHClip(positionWS);
                // Vertex colors are sRGB; Unity has already made _Color linear.
                output.color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.color *= _Color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(input.color.rgb, input.color.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
