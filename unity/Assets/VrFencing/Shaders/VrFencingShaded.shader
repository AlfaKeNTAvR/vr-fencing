// The look of the Dummy's suit and mask and of the épée's hilt and blade (PER-41): a cheap, light-independent shading
// for passthrough on the Quest, where the scene has no lights that match the real room. One unlit-cost pass:
// - a soft "sky above, floor below" fill plus a head-on fill from the viewer, so every shape reads in the round;
// - a rim on the silhouette (fabric reads soft, the body stands out against the room);
// - for metal, a reflection of a fixed studio environment and a sharp highlight;
// - the suit's regions: shoes, the sword glove and the head under the mask, as spheres and capsules in world space
//   that DummySuitRegions keeps on the rig's bones (radius 0 turns a region off).
// Colour = _BaseColor x _BaseMap x vertex colour; alpha the same. The material picks opaque or transparent through the
// blend, depth-write and queue fields. _BaseColor is what the Dummy and the Opponent set per renderer.
Shader "VrFencing/Shaded"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (1, 1, 1, 1)
        _BaseMap("Base map", 2D) = "white" {}
        _ShadowBrightness("Brightness facing away", Range(0, 1)) = 0.55
        _RimStrength("Rim", Range(0, 1)) = 0.25
        _Metal("Metal (environment reflection)", Range(0, 1)) = 0
        _Highlight("Highlight strength", Range(0, 4)) = 0
        _HighlightSharpness("Highlight sharpness", Range(2, 512)) = 64
        _ShoeColor("Shoe colour", Color) = (0.17, 0.17, 0.19, 1)
        _GloveColor("Glove colour", Color) = (0.93, 0.9, 0.84, 1)
        _HeadColor("Head under the mask", Color) = (0.12, 0.12, 0.13, 1)
        _HeadSphere("Head sphere (xyz, radius)", Vector) = (0, 0, 0, 0)
        _GloveStart("Glove capsule start (xyz, radius)", Vector) = (0, 0, 0, 0)
        _GloveEnd("Glove capsule end", Vector) = (0, 0, 0, 0)
        _ShoeAStart("Shoe A start (xyz, radius)", Vector) = (0, 0, 0, 0)
        _ShoeAEnd("Shoe A end (xyz, top height)", Vector) = (0, 0, 0, 0)
        _ShoeBStart("Shoe B start (xyz, radius)", Vector) = (0, 0, 0, 0)
        _ShoeBEnd("Shoe B end (xyz, top height)", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float _ShadowBrightness;
            float _RimStrength;
            float _Metal;
            float _Highlight;
            float _HighlightSharpness;
            float4 _ShoeColor;
            float4 _GloveColor;
            float4 _HeadColor;
            float4 _HeadSphere;
            float4 _GloveStart;
            float4 _GloveEnd;
            float4 _ShoeAStart;
            float4 _ShoeAEnd;
            float4 _ShoeBStart;
            float4 _ShoeBEnd;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            // 1 inside, 0 outside, with a soft edge a few millimetres wide so the boundary never aliases.
            float InsideSphere(float3 position, float4 sphere)
            {
                if (sphere.w <= 0.0)
                {
                    return 0.0;
                }

                return 1.0 - smoothstep(sphere.w - 0.004, sphere.w + 0.004, distance(position, sphere.xyz));
            }

            float DistanceToSegment(float3 position, float3 start, float3 end)
            {
                float3 axis = end - start;
                float along = saturate(dot(position - start, axis) / max(dot(axis, axis), 1e-8));
                return distance(position, start + (axis * along));
            }

            float InsideCapsule(float3 position, float4 start, float4 end)
            {
                if (start.w <= 0.0)
                {
                    return 0.0;
                }

                return 1.0 - smoothstep(start.w - 0.004, start.w + 0.004, DistanceToSegment(position, start.xyz, end.xyz));
            }

            // A shoe: a capsule from the heel to the toe, cut off at the ankle (end.w is the shoe's top in world y).
            float InsideShoe(float3 position, float4 start, float4 end)
            {
                return InsideCapsule(position, start, end) * (1.0 - smoothstep(end.w - 0.004, end.w + 0.004, position.y));
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 albedo = _BaseColor * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * input.color;

                // Regions of the suit, strongest last: the head (under the mask) wins over a glove or shoe.
                float shoe = max(InsideShoe(input.positionWS, _ShoeAStart, _ShoeAEnd), InsideShoe(input.positionWS, _ShoeBStart, _ShoeBEnd));
                albedo.rgb = lerp(albedo.rgb, albedo.rgb * _ShoeColor.rgb, shoe);
                albedo.rgb = lerp(albedo.rgb, albedo.rgb * _GloveColor.rgb, InsideCapsule(input.positionWS, _GloveStart, _GloveEnd));
                albedo.rgb = lerp(albedo.rgb, albedo.rgb * _HeadColor.rgb, InsideSphere(input.positionWS, _HeadSphere));

                float3 normal = normalize(input.normalWS);
                float3 toViewer = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = saturate(dot(normal, toViewer));

                // Fill: half from above (sky over floor), half head-on from the viewer.
                float fromAbove = (normal.y * 0.5) + 0.5;
                float fill = lerp(_ShadowBrightness, 1.0, (0.55 * fromAbove) + (0.45 * facing));
                float3 diffuse = albedo.rgb * fill;

                // Metal: a fixed studio, bright overhead and a dim floor, plus one sharp highlight up and to the side.
                float3 reflected = reflect(-toViewer, normal);
                float3 studio = lerp(float3(0.22, 0.22, 0.24), float3(1.15, 1.15, 1.2), smoothstep(-0.35, 0.55, reflected.y));
                studio += pow(saturate(dot(reflected, normalize(float3(0.35, 0.85, 0.4)))), _HighlightSharpness) * _Highlight;
                float3 metal = albedo.rgb * studio;

                float3 color = lerp(diffuse, metal, _Metal);
                color += pow(1.0 - facing, 3.0) * _RimStrength;
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        // Opaque parts (the hilt) also go into the depth prepass when URP makes one; transparent ones are never drawn
        // by it (URP only draws the opaque queues there).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
