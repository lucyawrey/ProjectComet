// A flat color where an object is hidden behind others, drawn as two materials on the object's renderers after the
// opaque pass. The mask material (_ZTest LEqual, _StencilComp Always, _ColorMask 0) marks the stencil where the
// object is in sight, so it never draws over its own front; the silhouette material (_ZTest Greater, _StencilComp
// NotEqual) then colors only hidden, unmarked pixels, marking each as it goes, so overlapping parts blend once.
// The mask needs the lower render queue, so every part's mask is down before any silhouette.
Shader "Comet/Silhouette"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 0.5)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test", Float) = 5
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil test", Float) = 6
        _ColorMask("Color mask", Float) = 15
        _DepthOffset("Depth offset", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Offset [_DepthOffset], [_DepthOffset]
            ColorMask [_ColorMask]
            Cull Back

            // The top bit only, clear of anything else using the stencil.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp [_StencilComp]
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
