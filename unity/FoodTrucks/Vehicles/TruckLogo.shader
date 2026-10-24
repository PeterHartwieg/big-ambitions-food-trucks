Shader "FoodTrucks/TruckLogo"
{
    Properties
    {
        _LogoTexture ("Logo", 2D) = "white" {}
        _LogoColour ("Colour", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry+1" }
        Pass
        {
            Name "Logo"
            Tags { "LightMode"="ForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            TEXTURE2D(_LogoTexture);
            SAMPLER(sampler_LogoTexture);
            CBUFFER_START(UnityPerMaterial)
                float4 _LogoColour;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                // The runtime supplies the calibrated sign tint. No light is spawned; the camera grades
                // this surface with the rest of the scene, and depth still hides it behind other objects.
                return float4(SAMPLE_TEXTURE2D(_LogoTexture, sampler_LogoTexture, input.uv).rgb * _LogoColour.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
