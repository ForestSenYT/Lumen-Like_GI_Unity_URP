Shader "SEGI/SEGIRenderSunDepth" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB)", 2D) = "white" {}
	_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
}
SubShader 
{
	Pass
	{
	
		CGPROGRAM
			
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0
			
			#include "UnityCG.cginc"
			
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float _Cutoff;
			
			
			struct v2f
			{
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 normal : TEXCOORD1;
				half4 color : COLOR;
			};
			
			
			v2f vert (appdata_full v)
			{
				v2f o;
				
				o.pos = UnityObjectToClipPos(v.vertex);
				
				float3 pos = o.pos;
				
				o.pos.xy = (o.pos.xy);
				
				
				o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);
				o.normal = UnityObjectToWorldNormal(v.normal);
				
				o.color = v.color;
				
				return o;
			}
			
			
			sampler2D GILightCookie;
			float4x4 GIProjection;
			
			float4 frag (v2f input) : SV_Target
			{
				float depth = input.pos.z;
				
				return depth;
			}
			
		ENDCG
	}
}

Fallback "Legacy Shaders/VertexLit"
}

/*
Shader "SEGI/SEGIRenderSunDepth"{
	Properties
	{
		_Color("Main Color", Color) = (1,1,1,1)
		_MainTex("Base (RGB)", 2D) = "white" {}
		_Cutoff("Alpha Cutoff", Range(0,1)) = 0.333
	}

		SubShader
		{
			Pass
			{
				Name "SunDepth"
				Tags { "LightMode" = "UniversalForward" }

				HLSLPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma target 5.0

			// Enable instancing + DOTS
			#pragma multi_compile_instancing
			#pragma multi_compile DOTS_INSTANCING_ON

			#include "UnityCG.cginc"

			// Sampler for the texture
			sampler2D _MainTex;

		// DOTS-compatible instancing buffer
		UNITY_INSTANCING_BUFFER_START(Props)
			UNITY_DEFINE_INSTANCED_PROP(float4, _MainTex_ST)
			UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
			UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
		UNITY_INSTANCING_BUFFER_END(Props)

		struct v2f
		{
			float4 pos : SV_POSITION;
			float4 uv : TEXCOORD0;
			float3 normal : TEXCOORD1;
			half4 color : COLOR;
		};

		v2f vert(appdata_full v)
		{
			v2f o;

			o.pos = UnityObjectToClipPos(v.vertex);

			float4 mainTexST = UNITY_ACCESS_INSTANCED_PROP(Props, _MainTex_ST);
			o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);
			o.normal = UnityObjectToWorldNormal(v.normal);
			o.color = v.color;

			return o;
		}

		float4 frag(v2f input) : SV_Target
		{
			float depth = input.pos.z;

		// Optional: If you want to apply cutoff based on alpha
		float4 col = tex2D(_MainTex, input.uv.xy);
		float alpha = col.a * UNITY_ACCESS_INSTANCED_PROP(Props, _Color).a;
		float cutoff = UNITY_ACCESS_INSTANCED_PROP(Props, _Cutoff);

		//clip(alpha - cutoff);

		return depth;
	}

	ENDHLSL
}
		}

			Fallback "Hidden/InternalErrorShader"
}
*/