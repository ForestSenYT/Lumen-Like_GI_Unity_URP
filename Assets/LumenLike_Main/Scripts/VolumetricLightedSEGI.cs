#pragma warning disable CS0649
#pragma warning disable CS0672
#pragma warning disable CS0618
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif
//using Unity.Mathematics;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Experimental.Rendering;
#endif

namespace LumenLike
{
    public class VolumetricLightedSEGI : ScriptableRendererFeature
    {
        public enum SurfaceCacheDebugView
        {
            None,
            Radiance,
            Confidence,
            RadianceConfidence
        }

        [System.Serializable]
        public class VolumetricLightScatteringSettings
        {
            [Header("Volumetric Properties")]
            [Range(0.1f, 1f)]
            public float resolutionScale = 0.5f;
            [Range(0.0f, 1.0f)]
            public float intensity = 1.0f;
            [Range(0.0f, 1.0f)]
            public float blurWidth = 0.85f;
            [Range(0.0f, 0.2f)]
            public float fadeRange = 0.85f;
            [Range(50, 200)]
            public uint numSamples = 100;

            [Header("Noise Properties")]
            //public float2 noiseSpeed = 0.5f;
            public Vector2 noiseSpeed = 0.5f * Vector2.one;
            public float noiseScale = 1.0f;
            [Range(0.0f, 1.0f)]
            public float noiseStrength = 0.6f;

            [Header("Surface Cache")]
            public bool enableSurfaceCache = true;
            [Range(0.25f, 1.0f)]
            public float surfaceCacheResolutionScale = 0.5f;
            [Range(0.0f, 0.99f)]
            public float surfaceCacheTemporalBlend = 0.9f;
            [Range(0.0f, 0.99f)]
            public float surfaceCacheNormalReject = 0.25f;
            [Range(0.001f, 2.0f)]
            public float surfaceCacheDepthReject = 0.2f;
            public bool useSurfaceCacheFallback = true;
            [Range(0.0f, 1.0f)]
            public float surfaceCacheGIBlend = 0.35f;
            [Range(0.0f, 1.0f)]
            public float surfaceCacheReflectionBlend = 0.15f;
            [Range(0.0f, 1.0f)]
            public float surfaceCacheMinConfidence = 0.05f;
            public SurfaceCacheDebugView surfaceCacheDebugView = SurfaceCacheDebugView.None;
            [Range(0.1f, 4.0f)]
            public float surfaceCacheDebugExposure = 1.0f;

            //v0.1
            public RenderPassEvent eventA = RenderPassEvent.AfterRenderingSkybox;
        }

        class LightScatteringPass : ScriptableRenderPass
        {
            static readonly int SurfaceCacheRadianceId = Shader.PropertyToID("_LumenLikeSurfaceCacheRadiance");
            static readonly int SurfaceCacheNormalId = Shader.PropertyToID("_LumenLikeSurfaceCacheNormal");
            static readonly int SurfaceCacheDepthId = Shader.PropertyToID("_LumenLikeSurfaceCacheDepth");
            static readonly int CurrentSurfaceCacheDepthId = Shader.PropertyToID("_CurrentSurfaceCacheDepth");
            static readonly int CurrentSurfaceCacheNormalId = Shader.PropertyToID("_CurrentSurfaceCacheNormal");
            static readonly int PrevSurfaceCacheRadianceId = Shader.PropertyToID("_PrevSurfaceCacheRadiance");
            static readonly int PrevSurfaceCacheNormalId = Shader.PropertyToID("_PrevSurfaceCacheNormal");
            static readonly int PrevSurfaceCacheDepthId = Shader.PropertyToID("_PrevSurfaceCacheDepth");
            static readonly int CurrentInvViewProjectionMatrixId = Shader.PropertyToID("_CurrentInvViewProjectionMatrix");
            static readonly int PrevViewProjectionMatrixId = Shader.PropertyToID("_PrevViewProjectionMatrix");
            static readonly int PrevInvViewProjectionMatrixId = Shader.PropertyToID("_PrevInvViewProjectionMatrix");
            static readonly int SurfaceCacheTemporalBlendId = Shader.PropertyToID("_TemporalBlend");
            static readonly int SurfaceCacheNormalRejectId = Shader.PropertyToID("_NormalReject");
            static readonly int SurfaceCacheDepthRejectId = Shader.PropertyToID("_DepthReject");
            static readonly int SurfaceCacheHasHistoryId = Shader.PropertyToID("_HasHistory");
            static readonly int SurfaceCacheDebugExposureId = Shader.PropertyToID("_DebugExposure");
            static readonly int SurfaceCacheDebugModeId = Shader.PropertyToID("_DebugMode");
            static readonly ProfilingSampler VolumetricLightScatteringSampler = new ProfilingSampler("VolumetricLightScattering");
            static readonly int VisibleLightsCountId = Shader.PropertyToID("_visibleLightsCount");
            static readonly int SegiVoxelScaleFactorId = Shader.PropertyToID("SEGIVoxelScaleFactor");
            static readonly int SegiFrameSwitchId = Shader.PropertyToID("SEGIFrameSwitch");
            static readonly int CameraToWorldId = Shader.PropertyToID("CameraToWorld");
            static readonly int WorldToCameraId = Shader.PropertyToID("WorldToCamera");
            static readonly int ProjectionMatrixInverseId = Shader.PropertyToID("ProjectionMatrixInverse");
            static readonly int ProjectionMatrixId = Shader.PropertyToID("ProjectionMatrix");
            static readonly int FrameSwitchId = Shader.PropertyToID("FrameSwitch");
            static readonly int CameraPositionId = Shader.PropertyToID("CameraPosition");
            static readonly int DeltaTimeId = Shader.PropertyToID("DeltaTime");
            static readonly int StochasticSamplingId = Shader.PropertyToID("StochasticSampling");
            static readonly int TraceDirectionsId = Shader.PropertyToID("TraceDirections");
            static readonly int TraceStepsId = Shader.PropertyToID("TraceSteps");
            static readonly int TraceLengthId = Shader.PropertyToID("TraceLength");
            static readonly int ConeSizeId = Shader.PropertyToID("ConeSize");
            static readonly int OcclusionStrengthId = Shader.PropertyToID("OcclusionStrength");
            static readonly int OcclusionPowerId = Shader.PropertyToID("OcclusionPower");
            static readonly int ConeTraceBiasId = Shader.PropertyToID("ConeTraceBias");
            static readonly int GIGainId = Shader.PropertyToID("GIGain");
            static readonly int NearLightGainId = Shader.PropertyToID("NearLightGain");
            static readonly int NearOcclusionStrengthId = Shader.PropertyToID("NearOcclusionStrength");
            static readonly int DoReflectionsId = Shader.PropertyToID("DoReflections");
            static readonly int HalfResolutionId = Shader.PropertyToID("HalfResolution");
            static readonly int ReflectionStepsId = Shader.PropertyToID("ReflectionSteps");
            static readonly int ReflectionOcclusionPowerId = Shader.PropertyToID("ReflectionOcclusionPower");
            static readonly int SkyReflectionIntensityId = Shader.PropertyToID("SkyReflectionIntensity");
            static readonly int FarOcclusionStrengthId = Shader.PropertyToID("FarOcclusionStrength");
            static readonly int FarthestOcclusionStrengthId = Shader.PropertyToID("FarthestOcclusionStrength");
            static readonly int NoiseTextureId = Shader.PropertyToID("NoiseTexture");
            static readonly int BlendWeightId = Shader.PropertyToID("BlendWeight");
            static readonly int ContrastAId = Shader.PropertyToID("contrastA");
            static readonly int ReflectControlId = Shader.PropertyToID("ReflectControl");
            static readonly int DitherControlId = Shader.PropertyToID("ditherControl");
            static readonly int SmoothNormalsId = Shader.PropertyToID("smoothNormals");
            static readonly int UseSurfaceCacheId = Shader.PropertyToID("UseSurfaceCache");
            static readonly int SurfaceCacheGIBlendSettingId = Shader.PropertyToID("SurfaceCacheGIBlend");
            static readonly int SurfaceCacheReflectionBlendSettingId = Shader.PropertyToID("SurfaceCacheReflectionBlend");
            static readonly int SurfaceCacheMinConfidenceId = Shader.PropertyToID("SurfaceCacheMinConfidence");
            static readonly int SurfaceCacheRadianceTextureId = Shader.PropertyToID("SurfaceCacheRadiance");
            static readonly int CurrentDepthTextureId = Shader.PropertyToID("CurrentDepth");
            static readonly int CurrentNormalTextureId = Shader.PropertyToID("CurrentNormal");
            static readonly int PreviousGITextureId = Shader.PropertyToID("PreviousGITexture");
            static readonly int PreviousDepthTextureId = Shader.PropertyToID("PreviousDepth");
            static readonly int ReflectionsTextureId = Shader.PropertyToID("Reflections");
            static readonly int KernelId = Shader.PropertyToID("Kernel");
            static readonly int GITextureId = Shader.PropertyToID("GITexture");
            static readonly int ProjectionPrevId = Shader.PropertyToID("ProjectionPrev");
            static readonly int ProjectionPrevInverseId = Shader.PropertyToID("ProjectionPrevInverse");
            static readonly int WorldToCameraPrevId = Shader.PropertyToID("WorldToCameraPrev");
            static readonly int CameraToWorldPrevId = Shader.PropertyToID("CameraToWorldPrev");
            static readonly int CameraPositionPrevId = Shader.PropertyToID("CameraPositionPrev");

            Material _surfaceCacheMaterial;
            readonly RTHandle[] _surfaceCacheRadianceHistory = new RTHandle[2];
            readonly RTHandle[] _surfaceCacheNormalHistory = new RTHandle[2];
            readonly RTHandle[] _surfaceCacheDepthHistory = new RTHandle[2];
            int _surfaceCacheHistoryIndex;
            bool _surfaceCacheHasHistory;
            int _surfaceCacheLastCameraId = int.MinValue;
            Matrix4x4 _surfaceCachePrevViewProjectionMatrix = Matrix4x4.identity;
            Matrix4x4 _surfaceCachePrevInvViewProjectionMatrix = Matrix4x4.identity;
            RenderTexture _scratchFrameSource;
            RenderTexture _scratchFrameResult;
            RenderTexture _scratchGiTraceA;
            RenderTexture _scratchGiTraceB;
            RenderTexture _scratchGiUpsampleA;
            RenderTexture _scratchGiUpsampleB;
            RenderTexture _scratchReflections;
            RenderTexture _scratchCurrentDepth;
            RenderTexture _scratchCurrentNormal;
            Camera _cachedMainCamera;
            LumenLike _cachedMainSegi;
            LumenLike _sharedSegiMaterialStateOwner;
            Material _sharedSegiMaterialStateMaterial;
            bool _sharedSegiMaterialStateValid;
            SharedSegiMaterialState _sharedSegiMaterialState;
            bool _surfaceCacheMaterialStateValid;
            SurfaceCacheMaterialState _surfaceCacheMaterialState;
            int _lastVisibleLightsCount = int.MinValue;

            struct SharedSegiMaterialState
            {
                public float VoxelScaleFactor;
                public bool StochasticSampling;
                public int TraceDirections;
                public int TraceSteps;
                public float TraceLength;
                public float ConeSize;
                public float OcclusionStrength;
                public float OcclusionPower;
                public float ConeTraceBias;
                public float GIGain;
                public float NearLightGain;
                public float NearOcclusionStrength;
                public bool DoReflections;
                public bool HalfResolution;
                public int ReflectionSteps;
                public float ReflectionOcclusionPower;
                public float SkyReflectionIntensity;
                public float FarOcclusionStrength;
                public float FarthestOcclusionStrength;
                public float BlendWeight;
                public float ContrastA;
                public Vector4 ReflectControl;
                public Vector4 DitherControl;
                public float SmoothNormals;
                public bool UseSurfaceCache;
                public float SurfaceCacheGIBlend;
                public float SurfaceCacheReflectionBlend;
                public float SurfaceCacheMinConfidence;
            }

            struct SurfaceCacheMaterialState
            {
                public float TemporalBlend;
                public float NormalReject;
                public float DepthReject;
                public bool HasHistory;
                public float DebugExposure;
                public SurfaceCacheDebugView DebugMode;
            }

#if UNITY_2023_3_OR_NEWER
            //v0.6
            Matrix4x4 lastFrameViewProjectionMatrix;
            Matrix4x4 viewProjectionMatrix;
            Matrix4x4 lastFrameInverseViewProjectionMatrix;
            public float downSample = 1;
            public float depthDilation = 1;
            public bool enabledTemporalAA = false;
            public float TemporalResponse = 1;
            public float TemporalGain = 1;

            RTHandle _handleA;
            RTHandle _handleB;
            RTHandle _handleC;
            RTHandle previousGIResultRT;
            RTHandle previousCameraDepthRT;
            TextureHandle previousGIResult;
            TextureHandle previousCameraDepth;

            RTHandle _handleTAART;
            TextureHandle _handleTAA;

            string m_ProfilerTag;
            public Material blitMaterial = null;
            bool allowHDR = true;// false; //v0.7
            /// <summary>
            /// ///////// GRAPH
            /// </summary>
            // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
            private class PassData
            {    //v0.1               
                internal TextureHandle src;
                internal TextureHandle tmpBuffer1;
                internal TextureHandle texA;
                internal TextureHandle texB;
                internal TextureHandle texC;
                internal TextureHandle texD;
                internal TextureHandle texE;
                // internal TextureHandle copySourceTexture;
                public Material BlitMaterial { get; set; }
                // public TextureHandle SourceTexture { get; set; }
            }
            private Material m_BlitMaterial;
            TextureHandle tmpBuffer1A;
            TextureHandle tmpBuffer2A;
            TextureHandle tmpBuffer3A;
            TextureHandle previousFrameTextureA;
            TextureHandle previousDepthTextureA;
            TextureHandle currentDepth;
            TextureHandle currentNormal;

            TextureHandle reflectionsRG;
            RTHandle _handlereflectionsRG;
            // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                //LumenLike
                //GRAPH
                string passName = "VisualizeVoxels";
                if (!TryGetMainSegi(out Camera mainCamera, out LumenLike segi))
                {
                    return;
                }

                bool enableGI = !segi.disableGI;
                m_BlitMaterial = blitMaterial;
                m_BlitMaterial = segi.material;
                blitMaterial = segi.material;

                //if (!_occludersMaterial || !_radialBlurMaterial) InitializeMaterials();
                if (RenderSettings.sun == null || !RenderSettings.sun.enabled || !enableGI
                    || (Camera.current != null && Camera.current != mainCamera))
                {
                    return;
                }

                if ((mainCamera.depthTextureMode & DepthTextureMode.Depth) == 0)
                {
                    mainCamera.depthTextureMode |= DepthTextureMode.Depth;
                }

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                    CullingResults cullResults = renderingData.cullResults;
                    SetVisibleLightsCount(cullResults.visibleLights.Length); //v1.6

                    if (cameraData.camera != mainCamera)
                    {
                        //Debug.Log("No cam0");
                        return;
                    }

                    //passData.tmpBuffer1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "tmpBuffer1", false);
                    desc.msaaSamples = 1;
                    desc.depthBufferBits = 0;
                    int rtW = desc.width;
                    int rtH = desc.height;
                    int xres = (int)(rtW / ((float)downSample));
                    int yres = (int)(rtH / ((float)downSample));
                    if (_handleA == null || _handleA.rt.width != xres || _handleA.rt.height != yres)
                    {
                        _handleA = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    if (_handleB == null || _handleB.rt.width != xres || _handleB.rt.height != yres)
                    {
                        _handleB = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    if (_handleC == null || _handleC.rt.width != xres || _handleC.rt.height != yres)
                    {
                        _handleC = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    tmpBuffer2A = renderGraph.ImportTexture(_handleA);
                    previousFrameTextureA = renderGraph.ImportTexture(_handleB);
                    previousDepthTextureA = renderGraph.ImportTexture(_handleC);

                    if (_handlereflectionsRG == null || _handlereflectionsRG.rt.width != xres || _handlereflectionsRG.rt.height != yres)
                    {
                        _handlereflectionsRG = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    reflectionsRG = renderGraph.ImportTexture(_handlereflectionsRG);


                    if (_handleTAART == null || _handleTAART.rt.width != xres || _handleTAART.rt.height != yres)
                    {
                        _handleTAART = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                        _handleTAART.rt.wrapMode = TextureWrapMode.Clamp;
                        _handleTAART.rt.filterMode = FilterMode.Bilinear;
                    }
                    _handleTAA = renderGraph.ImportTexture(_handleTAART);
                  

                    //LumenLike         
                    //previousGIResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                    //previousGIResult.wrapMode = TextureWrapMode.Clamp;
                    //previousGIResult.filterMode = FilterMode.Bilinear;
                    //previousGIResult.useMipMap = true;    
                    //previousGIResult.autoGenerateMips = false;
                    //previousCameraDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    //previousCameraDepth.wrapMode = TextureWrapMode.Clamp;
                    //previousCameraDepth.filterMode = FilterMode.Bilinear;
                    //previousCameraDepth.Create();
                    //previousCameraDepth.hideFlags = HideFlags.HideAndDontSave;
                    if (previousGIResultRT == null || previousGIResultRT.rt.width != xres || previousGIResultRT.rt.height != yres)
                    {
                        previousGIResultRT = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    if (previousCameraDepthRT == null || previousCameraDepthRT.rt.width != xres || previousCameraDepthRT.rt.height != yres)
                    {
                        previousCameraDepthRT = RTHandles.Alloc(xres, yres, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, dimension: TextureDimension.Tex2D);
                    }
                    previousGIResult = renderGraph.ImportTexture(previousGIResultRT);
                    previousCameraDepth = renderGraph.ImportTexture(previousCameraDepthRT);

                    // TextureDesc descA = new TextureDesc(1280, 720);
                    // tmpBuffer1A = renderGraph.CreateTexture(descA);
                    //tmpBuffer2A = renderGraph.CreateTexture(descA);
                    tmpBuffer1A = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "tmpBuffer1A", true);
                    //tmpBuffer2A = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "tmpBuffer2A", true);
                    tmpBuffer3A = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "tmpBuffer3A", true);
                    //previousFrameTextureA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "previousFrameTextureA", true);
                    //previousDepthTextureA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "previousDepthTextureA", true);
                    //desc.depthBufferBits = 16;
                    //desc.depthStencilFormat = GraphicsFormat.
                    currentDepth = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "currentDepth", true);
                    currentNormal = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "currentNormal", true);
                    TextureHandle surfaceCacheCurrentRadiance = default;
                    TextureHandle surfaceCacheCurrentNormal = default;
                    TextureHandle surfaceCacheCurrentDepth = default;
                    TextureHandle surfaceCachePreviousRadiance = default;
                    TextureHandle surfaceCachePreviousNormal = default;
                    TextureHandle surfaceCachePreviousDepth = default;
                    bool surfaceCacheReady = false;
                    int surfaceCacheCurrentIndex = _surfaceCacheHistoryIndex;
                    int surfaceCachePreviousIndex = 1 - surfaceCacheCurrentIndex;
                    if (SurfaceCacheEnabled() && EnsureSurfaceCacheMaterial())
                    {
                        EnsureSurfaceCacheCameraState(cameraData.camera);
                        int cacheWidth = Mathf.Max(1, Mathf.RoundToInt(desc.width * _settings.surfaceCacheResolutionScale));
                        int cacheHeight = Mathf.Max(1, Mathf.RoundToInt(desc.height * _settings.surfaceCacheResolutionScale));
                        EnsureSurfaceCacheHistoryTextures(cacheWidth, cacheHeight);
                        PrepareSurfaceCacheMaterial(cameraData.camera);
                        surfaceCacheCurrentRadiance = renderGraph.ImportTexture(_surfaceCacheRadianceHistory[surfaceCacheCurrentIndex]);
                        surfaceCacheCurrentNormal = renderGraph.ImportTexture(_surfaceCacheNormalHistory[surfaceCacheCurrentIndex]);
                        surfaceCacheCurrentDepth = renderGraph.ImportTexture(_surfaceCacheDepthHistory[surfaceCacheCurrentIndex]);
                        surfaceCachePreviousRadiance = renderGraph.ImportTexture(_surfaceCacheRadianceHistory[surfaceCachePreviousIndex]);
                        surfaceCachePreviousNormal = renderGraph.ImportTexture(_surfaceCacheNormalHistory[surfaceCachePreviousIndex]);
                        surfaceCachePreviousDepth = renderGraph.ImportTexture(_surfaceCacheDepthHistory[surfaceCachePreviousIndex]);
                        surfaceCacheReady = true;
                    }
                    // }
                    TextureHandle sourceTexture = resourceData.activeColorTexture;

                    if (cameraData.camera == mainCamera)
                    {


                        CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

                        RenderTextureDescriptor opaqueDesc = cameraData.cameraTargetDescriptor;
                        opaqueDesc.depthBufferBits = 0;

                        //v0.1
                        ///////////////////////////////////////////////////////////////// RENDER FOG
                        Material _material = blitMaterial;



                        //////////////////////////////////////////////////////////// START LumenLike
                        if (segi != null && segi.enabled)
                        {
                            if (segi.notReadyToRender)
                            {
                                //Blit(cmd, source, source);
                                //Graphics.Blit(source, destination);
                                //v0.1
                                //cmd.Blit(source, destination); /// BLITTER
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BLIT EMPTY 1", out var passData, m_ProfilingSampler))
                                {
                                    passData.BlitMaterial = m_BlitMaterial;
                                    // Similar to the previous pass, however now we set destination texture as input and source as output.
                                    builder.UseTexture(tmpBuffer2A, AccessFlags.Read);
                                    passData.src = tmpBuffer2A;
                                    builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                                    // We use the same BlitTexture API to perform the Blit operation.
                                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                                }
                                return;
                            }

                            //Set parameters
                            if (!EnsureSegiMaterial(segi))
                            {
                                return;
                            }

                            ApplySharedSegiMaterialState(segi, surfaceCacheReady && _settings.useSurfaceCacheFallback);
                            segi.material.SetTexture(SurfaceCacheRadianceTextureId, Texture2D.blackTexture);

                            //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
                            if (segi.visualizeVoxels)
                            {
                                //Blit(cmd, segi.blueNoise[segi.frameCounter % 64], destination);
                                //v0.1
                                //cmd.Blit(source, destination, segi.material, LumenLike.Pass.VisualizeVoxels); //BLITTER
                                passName = "VisualizeVoxels";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = resourceData.activeColorTexture;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    //builder.UseTexture(tmpBuffer1A, AccessFlags.Read);
                                    builder.SetRenderAttachment(tmpBuffer2A, 0, AccessFlags.Write);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = m_BlitMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        ExecuteBlitPass(data, context, LumenLike.Pass.VisualizeVoxels, passData.src));
                                }
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BLIT FINAL 1", out var passData, m_ProfilingSampler))
                                {
                                    passData.BlitMaterial = m_BlitMaterial;
                                    // Similar to the previous pass, however now we set destination texture as input and source as output.
                                    builder.UseTexture(tmpBuffer2A, AccessFlags.Read);
                                    passData.src = tmpBuffer2A;
                                    builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                                    // We use the same BlitTexture API to perform the Blit operation.
                                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                                }
                                return;
                            }

                            //Setup temporary textures
                            //                   RenderTexture gi1 = RenderTexture.GetTemporary(source.width / segi.giRenderRes, source.height / segi.giRenderRes, 0, RenderTextureFormat.ARGBHalf);
                            //                   RenderTexture gi2 = RenderTexture.GetTemporary(source.width / segi.giRenderRes, source.height / segi.giRenderRes, 0, RenderTextureFormat.ARGBHalf);
                            //                   RenderTexture reflections = null;

                            //If reflections are enabled, create a temporary render buffer to hold them
                            //                   if (segi.doReflections)
                            //                    {
                            //                        reflections = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
                            //                    }

                            //Setup textures to hold the current camera depth and normal
                            //                   RenderTexture currentDepth = RenderTexture.GetTemporary(source.width / segi.giRenderRes, source.height / segi.giRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                            //                    currentDepth.filterMode = FilterMode.Point;

                            //                    RenderTexture currentNormal = RenderTexture.GetTemporary(source.width / segi.giRenderRes, source.height / segi.giRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                            //                    currentNormal.filterMode = FilterMode.Point;

                            //Get the camera depth and normals
                            //v0.1
     /////                    cmd.Blit(source, currentDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);//v0.1
                            passName = "GetCameraDepthTexture";
                            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            {
                                passData.src = resourceData.activeColorTexture;
                                desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                //builder.UseTexture(tmpBuffer1A, AccessFlags.Read);
                                builder.SetRenderAttachment(currentDepth, 0, AccessFlags.Write);
                                builder.AllowPassCulling(false);
                                passData.BlitMaterial = m_BlitMaterial;
                                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                   // ExecuteBlitPass(data, context, LumenLike.Pass.GetCameraDepthTexture, tmpBuffer1A));
                                   ExecuteBlitPass(data, context, 15, passData.src));
                            }


                           
                            ////                     segi.material.SetTexture(CurrentDepthTextureId, currentDepth);
                            

                            //v0.1
                            /////               cmd.Blit(source, currentNormal, segi.material, LumenLike.Pass.GetWorldNormals);
                            passName = "GetCameraNormalsTexture";
                            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            {
                                passData.src = resourceData.activeColorTexture;
                                desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                //builder.UseTexture(tmpBuffer1A, AccessFlags.Read);
                                builder.SetRenderAttachment(currentNormal, 0, AccessFlags.Write);
                                builder.AllowPassCulling(false);
                                passData.BlitMaterial = m_BlitMaterial;
                                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                    // ExecuteBlitPass(data, context, LumenLike.Pass.GetWorldNormals, tmpBuffer1A));
                                    ExecuteBlitPass(data, context, 16, passData.src));
                            }

                            /////                      segi.material.SetTexture(CurrentNormalTextureId, currentNormal);


                        

                            //v0.1 - check depths
                            //if (segi.visualizeNORMALS)
                            //{
                            //    //v0.1
                            //    cmd.Blit(currentNormal, destination);
                            //    return;
                            //}

                            //Set the previous GI result and camera depth textures to access them in the shader
                            ////                       segi.material.SetTexture(PreviousGITextureId, segi.previousGIResult);
                            ////                       Shader.SetGlobalTexture(PreviousGITextureId, segi.previousGIResult);
                            ///                       Shader.SetGlobalTexture("PreviousGITexture", previousGIResult);
                            ////                       segi.material.SetTexture(PreviousDepthTextureId, segi.previousCameraDepth);

                            //Render diffuse GI tracing result
                            //v0.1
                            /////                      cmd.Blit(source, gi2, segi.material, LumenLike.Pass.DiffuseTrace);
                            passName = "DiffuseTrace";
                            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            {
                                passData.src = resourceData.activeColorTexture;
                                desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                builder.UseTexture(currentDepth, AccessFlags.Read);
                                builder.UseTexture(currentNormal, AccessFlags.Read);
                                builder.UseTexture(previousGIResult, AccessFlags.Read);
                                builder.UseTexture(previousGIResult, AccessFlags.Read);
                                builder.UseTexture(previousCameraDepth, AccessFlags.Read);
                                builder.SetRenderAttachment(tmpBuffer2A, 0, AccessFlags.Write);
                                builder.AllowPassCulling(false);
                                passData.BlitMaterial = m_BlitMaterial;
                                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                    //ExecuteBlitPassTEX5NAME(data, context, 17,
                                    ExecuteBlitPassTEX5NAME(data, context, 17,
                                    "CurrentDepth", currentDepth,
                                    "CurrentNormal", currentNormal,
                                    "PreviousGITexture", previousGIResult,
                                    "PreviousGITexture", previousGIResult,
                                    "PreviousDepth", previousCameraDepth
                                    ));
                            }

                            if (surfaceCacheReady)
                            {
                                passName = "SurfaceCacheCopyNormal";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = resourceData.activeColorTexture;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    builder.UseTexture(passData.src, AccessFlags.Read);
                                    builder.SetRenderAttachment(surfaceCacheCurrentNormal, 0, AccessFlags.Write);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = _surfaceCacheMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        ExecuteSurfaceCacheCopyPass(data, context, 1));
                                }

                                passName = "SurfaceCacheCopyDepth";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = resourceData.activeColorTexture;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    builder.UseTexture(passData.src, AccessFlags.Read);
                                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                                    builder.SetRenderAttachment(surfaceCacheCurrentDepth, 0, AccessFlags.Write);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = _surfaceCacheMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        ExecuteSurfaceCacheCopyPass(data, context, 2));
                                }

                                passName = "SurfaceCacheUpdateRadiance";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = tmpBuffer2A;
                                    passData.texA = surfaceCacheCurrentDepth;
                                    passData.texB = surfaceCacheCurrentNormal;
                                    passData.texC = surfaceCachePreviousRadiance;
                                    passData.texD = surfaceCachePreviousNormal;
                                    passData.texE = surfaceCachePreviousDepth;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    builder.UseTexture(tmpBuffer2A, AccessFlags.Read);
                                    builder.UseTexture(surfaceCacheCurrentDepth, AccessFlags.Read);
                                    builder.UseTexture(surfaceCacheCurrentNormal, AccessFlags.Read);
                                    builder.UseTexture(surfaceCachePreviousRadiance, AccessFlags.Read);
                                    builder.UseTexture(surfaceCachePreviousNormal, AccessFlags.Read);
                                    builder.UseTexture(surfaceCachePreviousDepth, AccessFlags.Read);
                                    builder.SetRenderAttachment(surfaceCacheCurrentRadiance, 0, AccessFlags.Write);
                                    builder.SetGlobalTextureAfterPass(surfaceCacheCurrentRadiance, SurfaceCacheRadianceId);
                                    builder.SetGlobalTextureAfterPass(surfaceCacheCurrentNormal, SurfaceCacheNormalId);
                                    builder.SetGlobalTextureAfterPass(surfaceCacheCurrentDepth, SurfaceCacheDepthId);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = _surfaceCacheMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        ExecuteSurfaceCacheUpdatePass(data, context));
                                }

                                if (SurfaceCacheDebugEnabled())
                                {
                                    RenderGraphUtils.BlitMaterialParameters debugParams = new(surfaceCacheCurrentRadiance, sourceTexture, _surfaceCacheMaterial, 3);
                                    renderGraph.AddBlitPass(debugParams, "SurfaceCacheDebug");
                                    FinalizeSegiFrameState(segi);
                                    _surfaceCacheHistoryIndex = surfaceCachePreviousIndex;
                                    _surfaceCacheHasHistory = true;
                                    return;
                                }
                            }





                            //if (segi.visualizeDEPTH)
                            //{
                            //    //v0.1
                            //    cmd.Blit(gi2, destination);
                            //    return;
                            //}

                            //if (segi.doReflections)
                            //{
                            //    //Render GI reflections result
                            //    //v0.1
                            //    cmd.Blit(source, reflections, segi.material, LumenLike.Pass.SpecularTrace);
                            //    segi.material.SetTexture(ReflectionsTextureId, reflections);
                            //}
                            if (segi.doReflections)
                            {
                                passName = "doReflections";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = resourceData.activeColorTexture;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    builder.UseTexture(currentDepth, AccessFlags.Read);
                                    builder.UseTexture(currentNormal, AccessFlags.Read);
                                    builder.UseTexture(previousGIResult, AccessFlags.Read);
                                    builder.UseTexture(previousGIResult, AccessFlags.Read);
                                    builder.UseTexture(previousCameraDepth, AccessFlags.Read);
                                    builder.SetRenderAttachment(reflectionsRG, 0, AccessFlags.Write);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = m_BlitMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        //ExecuteBlitPassTEX5NAME(data, context, 17,
                                        ExecuteBlitPassTEX5NAME(data, context, 21,
                                        "CurrentDepth", currentDepth,
                                        "CurrentNormal", currentNormal,
                                        "PreviousGITexture", previousGIResult,
                                        "PreviousGITexture", previousGIResult,
                                        "PreviousDepth", previousCameraDepth
                                        ));
                                }
                            }
                            //TEST REFL TEXTURE
                            //using (var builder = renderGraph.AddRasterRenderPass<PassData>("Color Blit Resolve", out var passData, m_ProfilingSampler))
                            //{
                            //    passData.BlitMaterial = m_BlitMaterial;
                            //    // Similar to the previous pass, however now we set destination texture as input and source as output.
                            //    builder.UseTexture(reflectionsRG, AccessFlags.Read);
                            //    passData.src = reflectionsRG;
                            //    builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                            //    // We use the same BlitTexture API to perform the Blit operation.
                            //    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                            //}

                            //Perform bilateral filtering
                            /*
                            if (segi.useBilateralFiltering)
                            {
                                segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));
                                //v0.1
                                cmd.Blit(gi2, gi1, segi.material, LumenLike.Pass.BilateralBlur);

                                segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                                //v0.1
                                cmd.Blit(gi1, gi2, segi.material, LumenLike.Pass.BilateralBlur);

                                segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));
                                //v0.1
                                cmd.Blit(gi2, gi1, segi.material, LumenLike.Pass.BilateralBlur);

                                segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                                //v0.1
                                cmd.Blit(gi1, gi2, segi.material, LumenLike.Pass.BilateralBlur);
                            }
                            */

                            //If Half Resolution tracing is enabled
                            if (segi.giRenderRes == 2)
                            {
                                /*
                                RenderTexture.ReleaseTemporary(gi1);

                                //Setup temporary textures
                                RenderTexture gi3 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
                                RenderTexture gi4 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);


                                //Prepare the half-resolution diffuse GI result to be bilaterally upsampled
                                gi2.filterMode = FilterMode.Point;
                                //v0.1
                                cmd.Blit(gi2, gi4);

                                RenderTexture.ReleaseTemporary(gi2);

                                gi4.filterMode = FilterMode.Point;
                                gi3.filterMode = FilterMode.Point;


                                //Perform bilateral upsampling on half-resolution diffuse GI result
                                segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                                //v0.1
                                cmd.Blit(gi4, gi3, segi.material, LumenLike.Pass.BilateralUpsample);
                                segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));

                                //Perform temporal reprojection and blending
                                if (segi.temporalBlendWeight < 1.0f)
                                {
                                    //v0.1
                                    cmd.Blit(gi3, gi4);
                                    //v0.1
                                    cmd.Blit(gi4, gi3, segi.material, LumenLike.Pass.TemporalBlend);
                                    //v0.1
                                    cmd.Blit(gi3, segi.previousGIResult);
                                    //v0.1
                                    cmd.Blit(source, segi.previousCameraDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);
                                }

                                //Set the result to be accessed in the shader
                                segi.material.SetTexture(GITextureId, gi3);

                                //Actually apply the GI to the scene using gbuffer data
                                //v0.1
                                cmd.Blit(source, destination, segi.material, segi.visualizeGI ? LumenLike.Pass.VisualizeGI : LumenLike.Pass.BlendWithScene);

                                //Release temporary textures
                                RenderTexture.ReleaseTemporary(gi3);
                                RenderTexture.ReleaseTemporary(gi4);
                                */
                        }
                        else    //If Half Resolution tracing is disabled
                            {
                                //Perform temporal reprojection and blending
                                //if (segi.temporalBlendWeight < 1.0f)
                                //{
                                //    //v0.1
                                //    cmd.Blit(gi2, gi1, segi.material, LumenLike.Pass.TemporalBlend);
                                //    //v0.1
                                //    cmd.Blit(gi1, segi.previousGIResult);
                                //    //v0.1
                                //    cmd.Blit(source, segi.previousCameraDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);
                                //}

                                //Actually apply the GI to the scene using gbuffer data
                                ////                     segi.material.SetTexture("GITexture", segi.temporalBlendWeight < 1.0f ? gi1 : gi2);
                                //v0.1
                                ////                     cmd.Blit(source, destination, segi.material, segi.visualizeGI ? LumenLike.Pass.VisualizeGI : LumenLike.Pass.BlendWithScene);


                                ////                    segi.material.SetTexture("GITexture", tmpBuffer2A);


                                if (segi.doReflections)
                                {
                                    passName = "BlendWithSceneREFLECTIONS";
                                    using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                    {
                                        passData.src = resourceData.activeColorTexture;
                                        desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                        builder.UseTexture(tmpBuffer2A, AccessFlags.Read);
                                        builder.UseTexture(reflectionsRG, AccessFlags.Read);
                                        if (surfaceCacheReady && _settings.useSurfaceCacheFallback)
                                        {
                                            builder.UseTexture(surfaceCacheCurrentRadiance, AccessFlags.Read);
                                        }
                                        builder.SetRenderAttachment(tmpBuffer3A, 0, AccessFlags.Write);
                                        builder.AllowPassCulling(false);
                                        passData.BlitMaterial = m_BlitMaterial;
                                        if (surfaceCacheReady && _settings.useSurfaceCacheFallback)
                                        {
                                            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                                ExecuteBlitPassTEXNAME_THREE(data, context, 18, tmpBuffer2A, "GITexture", reflectionsRG, "Reflections", surfaceCacheCurrentRadiance, "SurfaceCacheRadiance"));
                                        }
                                        else
                                        {
                                            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                                ExecuteBlitPassTEXNAME_TWO(data, context, 18, tmpBuffer2A, "GITexture", reflectionsRG, "Reflections"));
                                        }
                                    }
                                }
                                else
                                {
                                    passName = "BlendWithScene";
                                    using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                    {
                                        passData.src = resourceData.activeColorTexture;
                                        desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                        builder.UseTexture(tmpBuffer2A, AccessFlags.Read);
                                        if (surfaceCacheReady && _settings.useSurfaceCacheFallback)
                                        {
                                            builder.UseTexture(surfaceCacheCurrentRadiance, AccessFlags.Read);
                                        }
                                        builder.SetRenderAttachment(tmpBuffer3A, 0, AccessFlags.Write);
                                        builder.AllowPassCulling(false);
                                        passData.BlitMaterial = m_BlitMaterial;
                                        if (surfaceCacheReady && _settings.useSurfaceCacheFallback)
                                        {
                                            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                                ExecuteBlitPassTEXNAME_TWO(data, context, 18, tmpBuffer2A, "GITexture", surfaceCacheCurrentRadiance, "SurfaceCacheRadiance"));
                                        }
                                        else
                                        {
                                            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                                ExecuteBlitPassTEXNAME(data, context, 18, tmpBuffer2A, "GITexture"));
                                        }
                                    }
                                }

                                //TESTER CODE
                                //using (var builder = renderGraph.AddRasterRenderPass<PassData>("Color Blit Resolve", out var passData, m_ProfilingSampler))
                                //{
                                //    passData.BlitMaterial = m_BlitMaterial;
                                //    // Similar to the previous pass, however now we set destination texture as input and source as output.
                                //    passData.src = builder.UseTexture(tmpBuffer3A, IBaseRenderGraphBuilder.AccessFlags.Read);
                                //    builder.SetRenderAttachment(sourceTexture, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                                //    // We use the same BlitTexture API to perform the Blit operation.
                                //    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                                //}
                                //return;
                                //END TESTER CODE

                                //Release temporary textures
                                //RenderTexture.ReleaseTemporary(gi1);
                                //RenderTexture.ReleaseTemporary(gi2);
                            }

                            //Release temporary textures
                            //                  RenderTexture.ReleaseTemporary(currentDepth);
                            //                  RenderTexture.ReleaseTemporary(currentNormal);

                            //Visualize the sun depth texture
                            //if (segi.visualizeSunDepthTexture)
                            //{
                            //    //v0.1
                            //    cmd.Blit(segi.sunDepthTexture, destination);
                            //}


                            //Release the temporary reflections result texture
                            //if (segi.doReflections)
                            //{
                            //    RenderTexture.ReleaseTemporary(reflections);
                            //}

                            //Set matrices/vectors for use during temporal reprojection
                            FinalizeSegiFrameState(segi);
                            if (surfaceCacheReady)
                            {
                                _surfaceCacheHistoryIndex = surfaceCachePreviousIndex;
                                _surfaceCacheHasHistory = true;
                            }

                            //////////////////////////////////////////////////////////// END LumenLike




                            //v1.9.9.5 - Ethereal v1.1.8
                            //_material.SetInt("_visibleLightsCount", renderingData.cullResults.visibleLights.Length); 
                            //v2.0
                            //updateMaterialKeyword(useOnlyFog, "ONLY_FOG", _material);                    
                            //Debug.Log(_material.HasProperty("controlByColor"));                    
                            //var format = camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default; //v3.4.9
                            var format = allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default; //v3.4.9 //v LWRP //v0.7

                            //CONVERT A1                   
                            //        RecordRenderGraphBLIT1(renderGraph, frameData, desc, cameraData, renderingData, resourceData, _material, ref tmpBuffer1A, 24);// 21);
                            //passName = "BLIT1 Keep Source";
                            //using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            //{
                            //    passData.src = resourceData.activeColorTexture;
                            //    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                            //    builder.UseTexture(passData.src, IBaseRenderGraphBuilder.AccessFlags.Read);
                            //    builder.SetRenderAttachment(tmpBuffer1A, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                            //    builder.AllowPassCulling(false);
                            //    passData.BlitMaterial = m_BlitMaterial;
                            //    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                            //        ExecuteBlitPass(data, context, 14, passData.src));
                            //}

                            ///// MORE 1                  

                            //WORLD RECONSTRUCT        
                            Matrix4x4 camToWorld = mainCamera.cameraToWorldMatrix;
                            _material.SetMatrix("_InverseView", camToWorld);

                            //v0.6                    
                            //RecordRenderGraphBLIT1(renderGraph, frameData, desc, cameraData, renderingData, resourceData, _material, ref tmpBuffer2A, 6);

                            //passName = "BLIT2";
                            //using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            //{
                            //    passData.src = resourceData.activeColorTexture;
                            //    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                            //    builder.UseTexture(tmpBuffer1A, IBaseRenderGraphBuilder.AccessFlags.Read);
                            //    builder.SetRenderAttachment(tmpBuffer2A, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                            //    builder.AllowPassCulling(false);
                            //    passData.BlitMaterial = m_BlitMaterial;
                            //    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                            //        ExecuteBlitPass(data, context, (int)segi.temporalBlendWeight * 14, tmpBuffer1A));
                            //}

                            //passName = "BLIT2";
                            //using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                            //{
                            //    passData.src = resourceData.activeColorTexture;
                            //    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                            //    builder.UseTexture(tmpBuffer1A, IBaseRenderGraphBuilder.AccessFlags.Read);
                            //    builder.SetRenderAttachment(tmpBuffer2A, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
                            //    builder.AllowPassCulling(false);
                            //    passData.BlitMaterial = m_BlitMaterial;
                            //    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                            //        ExecuteBlitPass(data, context, (int)segi.temporalBlendWeight*14, tmpBuffer1A));
                            //}

                            ///// TEMPORAL
                            if (segi.temporalBlendWeight < 1 && Time.fixedTime > 0.05f)
                            //if (enabledTemporalAA && Time.fixedTime > 0.05f)
                            {
                                //NEW
                                _material.SetFloat("_TemporalResponse",2f);
                                _material.SetFloat("_TemporalGain", segi.temporalBlendWeight * 5f);

                                var worldToCameraMatrix = mainCamera.worldToCameraMatrix;
                                var projectionMatrix = GL.GetGPUProjectionMatrix(mainCamera.projectionMatrix, false);
                                _material.SetMatrix("_InverseProjectionMatrix", projectionMatrix.inverse);
                                viewProjectionMatrix = projectionMatrix * worldToCameraMatrix;
                                _material.SetMatrix("_InverseViewProjectionMatrix", viewProjectionMatrix.inverse);
                                _material.SetMatrix("_LastFrameViewProjectionMatrix", lastFrameViewProjectionMatrix);
                                _material.SetMatrix("_LastFrameInverseViewProjectionMatrix", lastFrameInverseViewProjectionMatrix);

                                //https://github.com/CMDRSpirit/URPTemporalAA/blob/86f4d28bc5ee8115bff87ee61afe398a6b03f61a/TemporalAA/TemporalAAFeature.cs#L134
                                Matrix4x4 mt = lastFrameViewProjectionMatrix * cameraData.camera.cameraToWorldMatrix;
                                _material.SetMatrix("_FrameMatrix", mt);

                                passName = "BLIT_TAA";
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
                                {
                                    passData.src = resourceData.activeColorTexture;
                                    desc.msaaSamples = 1; desc.depthBufferBits = 0;
                                    builder.UseTexture(tmpBuffer3A, AccessFlags.Read);
                                    builder.UseTexture(previousFrameTextureA, AccessFlags.Read);
                                    builder.UseTexture(previousDepthTextureA, AccessFlags.Read);
                                    builder.SetRenderAttachment(_handleTAA, 0, AccessFlags.Write);
                                    builder.AllowPassCulling(false);
                                    passData.BlitMaterial = m_BlitMaterial;
                                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                        //ExecuteBlitPassTHREE(data, context, 19, tmpBuffer3A, previousFrameTextureA, previousDepthTextureA));
                                        ExecuteBlitPassTEN(data, context, 19, tmpBuffer3A, previousFrameTextureA, previousDepthTextureA,
                                        "_TemporalResponse", 2f,
                                        "_TemporalGain", segi.temporalBlendWeight * 5f,
                                        "_InverseProjectionMatrix", projectionMatrix.inverse,
                                        "_InverseViewProjectionMatrix", viewProjectionMatrix.inverse,
                                        "_LastFrameViewProjectionMatrix", lastFrameViewProjectionMatrix,
                                        "_LastFrameInverseViewProjectionMatrix", lastFrameInverseViewProjectionMatrix,
                                        "_FrameMatrix", mt
                                        ));
                                }
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BLIT_TAA 1", out var passData, m_ProfilingSampler))
                                {
                                    builder.AllowGlobalStateModification(true);
                                    passData.BlitMaterial = m_BlitMaterial;
                                    builder.UseTexture(_handleTAA, AccessFlags.Read);
                                    passData.src = _handleTAA;
                                    builder.SetRenderAttachment(previousFrameTextureA, 0, AccessFlags.Write);
                                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                                }
                                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BLIT_TAA 2", out var passData, m_ProfilingSampler))
                                {
                                    builder.AllowGlobalStateModification(true);
                                    passData.BlitMaterial = m_BlitMaterial;
                                    builder.UseTexture(_handleTAA, AccessFlags.Read);
                                    passData.src = _handleTAA;
                                    builder.SetRenderAttachment(tmpBuffer3A, 0, AccessFlags.Write);
                                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                                }
                            }

                            //v0.6
                            lastFrameViewProjectionMatrix = viewProjectionMatrix;
                            lastFrameInverseViewProjectionMatrix = viewProjectionMatrix.inverse;
                            /////// END MORE 1 

                            ///////////////////////////////////////////////////////////////// END RENDER FOG
                        }
                    }//END A Connector check

                    // Now we will add another pass to 闂傚倸鍊搁崐鎼佸磹閹间礁纾归柟闂寸绾惧綊鏌熼梻瀵割槮缁炬儳缍婇弻鐔兼⒒鐎靛壊妲紒鐐劤缂嶅﹪寮婚悢鍏尖拻閻庨潧澹婂Σ顔剧磼閻愵剙鍔ょ紓宥咃躬瀵鎮㈤崗灏栨嫽闁诲酣娼ф竟濠偽ｉ鍓х＜闁绘劦鍓欓崝銈囩磽瀹ュ拑韬€殿喖顭烽幃銏ゅ礂鐏忔牗瀚介梺璇查叄濞佳勭珶婵犲伣锝夘敊閸撗咃紲闂佺粯鍔﹂崜娆撳礉閵堝洨纾界€广儱鎷戦煬顒傗偓娈垮枛椤兘骞冮姀銈呯閻忓繑鐗楃€氫粙姊虹拠鏌ュ弰婵炰匠鍕彾濠电姴浼ｉ敐澶樻晩闁告挆鍜冪床闂備胶绮崝锕傚礈濞嗘挸绀夐柕鍫濇川绾剧晫鈧箍鍎遍幏鎴︾叕椤掑倵鍋撳▓鍨灈妞ゎ厾鍏橀獮鍐閵堝懐顦ч柣蹇撶箲閻楁鈧矮绮欏铏规嫚閺屻儱寮板┑鐐板尃閸曨厾褰炬繝鐢靛Т娴硷綁鏁愭径妯绘櫓闂佸憡鎸嗛崪鍐簥闂傚倷鑳剁划顖炲礉閿曞倸绀堟繛鍡樻尭缁€澶愭煏閸繃宸濈痪鍓ф櫕閳ь剙绠嶉崕閬嶅箯閹达妇鍙曟い鎺戝€甸崑鎾斥枔閸喗鐏堝銈庡幘閸忔﹢鐛崘顔碱潊闁靛牆鎳愰ˇ褔鏌ｈ箛鎾剁闁绘顨堥埀顒佺煯缁瑥顫忛搹瑙勫珰闁哄被鍎卞鏉库攽閻愭澘灏冮柛鏇ㄥ幘瑜扮偓绻濋悽闈浶㈠ù纭风秮閺佹劖寰勫Ο缁樻珦闂備礁鎲￠幐鍡涘椽閸愵亜绨ラ梻鍌氬€烽懗鍓佸垝椤栫偛绀夐柨鏇炲€哥粈鍫熺箾閸℃ɑ灏紒鈧径鎰厪闁割偅绻冨婵堢棯閸撗勬珪闁逞屽墮缁犲秹宕曢柆宥呯闁硅揪濡囬崣鏇熴亜閹烘垵鈧敻宕戦幘鏂ユ灁闁割煈鍠楅悘鍫濐渻閵堝骸骞橀柛蹇旓耿閻涱噣宕橀纰辨綂闂侀潧鐗嗛幊鎰八囪閺岋綀绠涢幘鍓侇唹闂佺粯顨嗛〃鍫ュ焵椤掍胶鐓紒顔界懃椤繘鎼圭憴鍕彴闂佸搫琚崕鍗烆嚕閺夊簱鏀介柣鎰緲鐏忓啴鏌涢弴銊ュ箻鐟滄壆鍋撶换婵嬫偨闂堟刀銏犆圭涵椋庣М闁轰焦鍔栧鍕熺紒妯荤彟闂傚倷绀侀幉锟犲箰閸℃稑妞介柛鎰典簻缁ㄣ儵姊婚崒姘偓鐑芥嚄閸撲礁鍨濇い鏍仜缁€澶愭煥閺囩偛鈧摜绮堥崼鐔虹闁糕剝蓱鐏忣厾绱掗埀顒佸緞閹邦厾鍘梺鍓插亝缁诲啫顔忓┑鍫㈡／闁告挆鍕彧闂侀€炲苯澧紒鐘茬Ч瀹曟洟鏌嗗鍛唵闂佺鎻俊鍥矗閺囩喆浜滈柟鐑樺灥閳ь剛鏁诲畷鎴﹀箻閺傘儲鐏侀梺鍓茬厛閸犳鎮橀崼婵愭富闁靛牆楠搁獮姗€鏌涜箛鏃撹€块柣娑卞櫍瀹曟﹢顢欑喊杈ㄧ秱闂備線娼ч悧鍡涘箠閹板叓鍥樄闁哄矉缍€缁犳盯骞橀崜渚囧敼闂備胶绮〃鍡涖€冮崼銉ョ劦妞ゆ帊鑳堕悡顖滅磼椤旂晫鎳冩い顐㈢箻閹煎湱鎲撮崟顐ゅ酱闂備礁鎼悮顐﹀磿閸楃儐鍤曢柡澶婄氨閺€浠嬫煟閹邦厽绶查悘蹇撳暣閺屾盯寮撮妸銉ョ閻熸粍澹嗛崑鎾舵崲濠靛鍋ㄩ梻鍫熷垁閵忕妴鍦兜妞嬪海袦闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰熼梻鍌氬€风粈渚€骞夐敓鐘茬闁糕剝绋戝浠嬫煕閹板吀绨荤紒銊ｅ劦濮婂宕掑顑藉亾閻戣姤鍤勯柛鎾茬閸ㄦ繃銇勯弽顐粶缂佲偓婢舵劖鐓ラ柡鍥╁仜閳ь剙鎽滅划鍫ュ醇閻旇櫣顔曢梺绯曞墲钃遍悘蹇ｅ幘缁辨帡鍩€椤掍礁绶為柟閭﹀幘閸橆亪姊洪崜鎻掍簼缂佽鍟蹇撯攽閸垺锛忛梺鍛婃寙閸曨剛褰ч梻渚€鈧偛鑻晶顔剧磼閻樿尙效鐎规洘娲熼弻鍡楊吋閸涱垼鍞甸梻浣侯攰閹活亝淇婇崶顒€鐭楅柡鍥╁枂娴滄粓鏌熼悜妯虹仴闁逞屽墰閺佽鐣烽幋锕€绠婚柡鍌樺劜閻忎線姊洪崜鑼帥闁哥姵顨婇幃姗€宕煎┑鎰瘜闂侀潧鐗嗘鎼佺嵁濮椻偓閺屾稖绠涢弮鎾光偓鍧楁煟濞戝崬娅嶇€规洘锕㈤、娆戝枈鏉堛劎绉遍梻鍌欑窔濞佳囨偋閸℃稑绠犻柟鏉垮彄閸ヮ亶妯勯梺鍝勭焿缂嶁偓缂佺姵鐩獮姗€宕滄笟鍥ф暭闂傚倷鑳剁划顖炪€冮崱娑栤偓鍐醇閵夈儳鍔﹀銈嗗笂閼冲爼鎮￠婊呯＜妞ゆ梻鏅幊鍐┿亜椤愩垻绠婚柟鐓庢贡閹叉挳宕熼銈呴叡闂傚倷绀侀幖顐ゆ偖椤愶箑纾块柛妤冨剱閸ゆ洟鏌℃径濠勬皑闁衡偓娴犲鐓熼柟閭﹀幗缁舵煡鎮樿箛鎾虫殻闁哄本鐩鎾Ω閵夈儳顔掗柣鐔哥矋婢瑰棝宕戦幘鑸靛床婵犻潧顑嗛崑銊╂⒒閸喎鍨侀柕蹇曞Υ閸︻厽鍏滃瀣捣琚﹂梻浣芥〃閻掞箓宕濋弽褜鍤楅柛鏇ㄥ€犻悢铏圭＜婵☆垵宕佃ぐ鐔兼⒒閸屾艾鈧绮堟笟鈧獮澶愭晸閻樿尙顔囬梺绯曞墲缁嬫垵顪冩禒瀣厱闁规澘鍚€缁ㄨ崵绱掗妸锝呭姦婵﹤顭峰畷鎺戭潩椤戣棄浜鹃柣鎴ｅГ閸ゅ嫰鏌涢幘鑼槮闁搞劍绻冮妵鍕冀椤愵澀绮剁紓浣插亾濠㈣埖鍔栭悡銉╂煛閸モ晛浠滈柍褜鍓欓幗婊呭垝閸儱閱囬柣鏃囨椤旀洟姊虹化鏇炲⒉閽冮亶鎮樿箛锝呭箻缂佽鲸甯￠崺鈧い鎺嶇缁剁偤鏌熼柇锕€骞橀柛姗嗕邯濮婃椽宕滈幓鎺嶇凹缂備浇顕ч崯顐︻敊韫囨挴鏀介柛顐犲灪閿涘繘姊洪崨濠冨瘷闁告洦鍓涜ぐ褔姊绘笟鈧埀顒傚仜閼活垱鏅堕鍓х＜闁绘灏欐晥閻庤娲樺ú鐔煎蓟閸℃鍚嬮柛娑卞灱閸炵敻姊绘担渚敯闁规椿浜濇穱濠囧炊椤掆偓閻鏌￠崶鈺佹瀭濞存粍绮撻弻鐔兼倻濡櫣浠撮梺閫炲苯澧伴柡浣筋嚙閻ｇ兘骞嬮敃鈧粻濠氭煛閸屾ê鍔滈柣蹇庣窔濮婃椽宕滈懠顒€甯ラ梺鍝ュУ椤ㄥ﹪骞冨鈧畷鐓庘攽閹邦厼鐦滈梻渚€娼ч悧鍡橆殽閹间胶宓佹慨妞诲亾闁哄本绋栫粻娑㈠籍閹惧厜鍋撻崸妤佺厽婵炴垵宕▍宥団偓瑙勬礃閿曘垽銆佸▎鎴濇瀳閺夊牄鍔庣粔閬嶆⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鏌熼弶鍨絼闁搞儺浜跺Ο鍕⒑閸濆嫮鐒跨紓宥佸亾缂備胶濮甸惄顖炵嵁濮椻偓瀹曟粍绗熼崶褎鏆梻鍌氬€烽懗鍓佸垝椤栫偑鈧啴宕卞☉鏍ゅ亾閸愵喖閱囬柕澶堝劤閸旓箑顪冮妶鍡楃瑐闁煎啿鐖奸幃锟狀敍濠婂懐锛滃銈嗘⒒閺咁偊骞婇崘鈹夸簻妞ゆ劑鍨荤粻濠氭煙閻撳孩璐￠柟鍙夋尦瀹曠喖顢曢妶鍛村彙闂傚倸鍊风粈渚€骞栭銈嗗仏妞ゆ劧绠戠壕鍧楁煕濡ゅ啫鍓遍柛銈嗘礋閺屸剝寰勬繝鍕殤闂佽　鍋撳ù鐘差儐閻撶喖鏌熼柇锕€骞楃紒鑸电叀閹绠涢幘铏闂佸搫鏈惄顖氼嚕閹绢喖惟闁靛鍎抽鎺楁煟閻斿摜鐭嬪鐟版瀹曟垿骞橀懜闈涘簥濠电娀娼ч鍡涘磻閵娾晜鈷掗柛顐ゅ枎閸ㄤ線鏌曡箛鏇炍ラ柛锔诲幗閸忔粌顪冪€ｎ亝鎹ｇ悮锕傛⒒娴ｈ姤銆冮柣鎺炵畵瀹曟繈寮借閸ゆ鏌涢弴銊モ偓鐘绘偄閾忓湱锛滃┑鈽嗗灥瀹曚絻銇愰悢鍏尖拻闁稿本鑹鹃埀顒勵棑缁牊绗熼埀顒勭嵁婢舵劖鏅搁柣妯垮皺椤斿洭姊虹化鏇炲⒉缂佸鍨甸—鍐╃鐎ｎ偆鍘遍梺瑙勬緲閸氣偓缂併劏宕电槐鎺楀Ω閿濆懎濮﹀┑顔硷工椤嘲鐣烽幒鎴僵妞ゆ垼妫勬禍楣冩煟閹达絽袚闁搞倕瀚伴弻娑㈠箻閼碱剦妲梺鎼炲妽缁诲啴濡甸崟顖氬唨妞ゆ劦婢€缁墎绱撴担鎻掍壕婵犮垼鍩栭崝鏍偂閵夆晜鐓涢柛銉㈡櫅娴犳粓鏌嶈閸撴瑩骞楀鍛灊闁割偁鍎遍柋鍥煟閺冨洦顏犳い鏃€娲熷铏瑰寲閺囩偛鈷夐柦鍐憾閹绠涢敐鍛缂備浇椴哥敮锟犲春閳ь剚銇勯幒宥囶槮缂佸墎鍋ら弻鐔兼焽閿曗偓楠炴﹢鏌涘鍡曢偗婵﹥妞藉畷婊堝箵閹哄秶鍑圭紓鍌欐祰椤曆囧疮椤愶富鏁婇煫鍥ㄦ尨閺€浠嬫煕椤愮姴鐏柨娑樼箻濮婃椽宕ㄦ繝鍐ㄧ樂闂佸憡娲﹂崜娑溿亹瑜斿缁樼瑹閳ь剟鍩€椤掑倸浠滈柤娲诲灡閺呭爼顢欓崜褏锛滈梺缁橆焾鐏忔瑦鏅ラ柣搴ゎ潐濞叉ê煤閻旂厧钃熼柛鈩冾殢閸氬鏌涘☉鍗炵伇闁哥偟鏁诲缁樻媴娓氼垱鏁梺瑙勬た娴滄繃绌辨繝鍥х倞妞ゆ巻鍋撻柣顓燁殜閺岋繝宕堕埡浣圭€繛瀛樼矋缁捇寮婚悢鍏煎€绘俊顖濇娴犳挳姊洪柅鐐茶嫰婢т即鏌℃担绛嬪殭闁伙絿鍏橀獮瀣晝閳ь剛绮绘繝姘厸闁稿本锚閸旀粓鏌ｈ箛搴ｇ獢婵﹥妞藉畷婊堟嚑椤掆偓鐢儵姊洪崫銉バｆい銊ワ躬楠炲啫顫滈埀顒€鐣烽悡搴樻斀闁归偊鍘奸惁婊堟⒒娓氣偓濞佳囨偋閸℃﹩娈介柟闂磋兌瀹撲焦鎱ㄥ璇蹭壕闂佸搫鏈粙鏍不濞戙垹绫嶉柟鎯у帨閸嬫捇宕稿Δ浣哄幗濠电偞鍨靛畷顒€鈻嶅鍥ｅ亾鐟欏嫭绀冮柨鏇樺灲閵嗕礁鈻庨幋婵囩€抽柡澶婄墑閸斿海绮旈柆宥嗏拻闁稿本鐟х粣鏃€绻涙担鍐叉处閸嬪鏌涢埄鍐槈缂佺姷濞€閺岀喖寮堕崹顔肩导闂佹悶鍎烘禍璺何熼崟顖涒拺闁告繂瀚悞璺ㄧ磽瀹ヤ礁浜剧紓鍌欒兌婵敻鎯勯鐐靛祦婵せ鍋撶€规洘绮嶇粭鐔煎炊瑜庨弳顓㈡⒒閸屾艾鈧兘鎳楅崜浣稿灊妞ゆ牜鍋戦埀顒€鍟村畷鍗炩槈閺嶃倕浜鹃柛鎰靛枛闁卞洭鏌曟径鍫濆姢闁告棑绠戦—鍐Χ閸℃鐟愰梺鐓庡暱閻栧ジ骞冮敓鐙€鏁嶆繝濠傛噽閿涙粌顪冮妶鍡樷拻闁哄拋鍋婇獮濠囧礃閳瑰じ绨诲銈嗗姂閸ㄨ崵绮绘繝姘厵闁绘挸瀛╃拹锟犳煙閸欏灏︾€规洜鍠栭、鏇㈠Χ閸涙潙褰欓梻鍌氬€搁崐椋庣矆娓氣偓楠炴牠顢曢妶鍡椾粡濡炪倖鍔х粻鎴犲閸ф鐓曟俊銈呭暙閸撻亶鏌涢妶鍡樼闁哄备鍓濆鍕節閸曨剛娈ら梻浣姐€€閸嬫挸霉閻樺樊鍎愰柣鎾跺枛閺岀喖鏌囬敃鈧獮妤冪磼閼哥數鍙€闁诡喗顭堢粻娑㈠箻閾忣偅顔掗梻浣告惈閺堫剟鎯勯姘煎殨闁圭虎鍠栨儫闂侀潧顦崕鎶筋敊閹烘鐓熼柣鏂挎憸閻顭块悷鐗堫棦鐎规洘鍨块獮姗€鎳滈棃娑樼哎婵犵數濞€濞佳囶敄閸℃稑纾婚柕濞炬櫆閳锋帡鏌涢銈呮瀻闁搞劋绶氶弻鏇＄疀閺囩倣銉╂煕閿旇骞楅柛蹇旂矊椤啰鈧綆浜濋幑锝囨偖閿曞倹鈷掑ù锝堝Г閵嗗啴鏌ｉ幒鐐电暤鐎规洘绻堝鍊燁檨闁搞倖顨婇弻娑㈠即閵娿儮鍋撻悩缁樺亜闁惧繐婀遍敍婊冣攽閳藉棗鐏ョ€规洑绲婚妵鎰版偄閸忓皷鎷婚梺绋挎湰閼归箖鍩€椤掑嫷妫戠紒顔肩墛缁楃喖鍩€椤掑嫮宓佸鑸靛姈閺呮悂鏌ｅΟ鍨毢妞ゆ柨娲铏瑰寲閺囩偛鈷夌紓浣割儐閸ㄧ敻鈥旈崘鈺冾浄閻庯綆鍋嗛崢閬嶆⒑鐟欏嫭绶查柛姘ｅ亾缂備降鍔忓畷鐢垫閹烘惟闁挎繂鎳庢慨锕€鈹戦纭峰伐妞ゎ厼鍢查悾鐑藉箳閹搭厽鍍靛銈嗗灱濡嫭绂嶆ィ鍐╃厽闁硅揪绲借闂佹悶鍊曠€氫即寮诲☉銏犵闁肩⒈鍓﹀Σ顕€姊洪幖鐐插缂佽鍟存俊鐢稿礋椤栨艾鍞ㄥ銈嗗姦濠⑩偓缂侇喖澧庣槐鎾存媴閹绘帊澹曞┑鐘灱閸╂牠宕濋弽顓熷亗闁靛鏅滈悡娑㈡煕閵夈垺娅呭ù鐘崇矒閺屽秷顧侀柛鎾寸懇楠炴顭ㄩ崨顓炵亰闂佸壊鍋侀崕閬嶆煁閸ヮ剚鐓熼柡鍐ㄦ处椤忕姵銇勯弮鈧ú鐔奉潖閾忕懓瀵查柡鍥╁仜閳峰鏌﹂崘顔绘喚闁诡喗顭堢粻娑㈡晲閸涱厾顔掔紓鍌欐祰妞村摜鏁敓鐘叉槬闁逞屽墯閵囧嫰骞掑鍥舵М缂備礁澧庨崑銈夊蓟閻斿吋鐒介柨鏇楀亾妤犵偞锕㈤弻娑橆潨閸℃洟鍋楀┑顔硷攻濡炶棄鐣峰鍫濈闁瑰搫绉堕崙鍦磽閸屾瑦绁版い鏇嗗洤鐤い鎰跺瘜閺佸﹪鐓崶銊﹀皑闁衡偓娴犲鐓曟い鎰Т閻忣亪鏌ㄥ☉妯肩婵﹥妞藉畷銊︾節閸愩劎妯傞梻浣告啞濞诧箓宕滃☉婧夸汗鐟滄柨顫忕紒妯肩懝闁逞屽墴閸┾偓妞ゆ帒鍊告禒婊堟煠濞茶鐏￠柡鍛埣椤㈡瑩宕滆閿涙粓姊虹紒姗嗙劸閻忓繑鐟﹂弲銉╂煟鎼淬値娼愭繛鍙壝叅闁绘梻鍘ч拑鐔兼煕閳╁喚娈㈤柛姘儔閺屾稑鈽夐崡鐐典紘闂佸摜鍋熼弫璇差潖缂佹ɑ濯村〒姘煎灡閺侇垶姊虹憴鍕仧濞存粍绻冪粚杈ㄧ節閸パ呭€炲銈嗗坊閸嬫捇鏌ｉ鐕佹疁闁哄矉绻濆畷鍫曞煛娴ｅ湱鈧厽绻涚€涙鐭嬬紒顔芥崌瀵鎮㈤崗鐓庘偓缁樹繆椤栨繃顏犲ù鐘虫尦濮婃椽鏌呴悙鑼跺濠⒀傚嵆閺屸剝鎷呯粵瀣闂佷紮绲块崗姗€鐛€ｎ喗鏅濋柍褜鍓涚划缁樼節濮橆厾鍘搁梺绋挎湰閿氶柛鏃€绮撻弻锝堢疀鎼达絿鐛㈠┑顔硷攻濡炶棄鐣峰鍫熷殤妞ゆ巻鍋撻悽顖樺劦濮婃椽宕妷銉愶絾銇勯妸銉含妤犵偛鍟～婊堝焵椤掆偓閻ｇ兘鎮℃惔妯绘杸闂佸綊鍋婇崢濂告儊濠婂牊鈷掑〒姘ｅ亾婵炰匠鍥ㄥ亱闁糕剝锕╁▓浠嬫煙闂傜鍏岀€规挷鐒﹂幈銊ヮ渻鐠囪弓澹曢梻浣告惈閺堫剛绮欓弽顐や笉婵炴垯鍨瑰Λ姗€鏌涢埦鈧弲娆撴焽椤栨稏浜滈柕蹇娾偓鍐叉懙闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰囬梻鍌氬€烽悞锔锯偓绗涘厾鍝勵吋婢跺﹦锛涢梺瑙勫劤婢у海澹曟總鍛婄厽婵☆垵娅ｉ敍宥夋煃椤栨稒绀嬮柡灞炬礋瀹曟儼顦叉い蹇ｅ幗椤ㄣ儵鎮欓幖顓犲姺闂佸湱鎳撶€氼厼顭囬鍫熷亱闁割偅绻勯崢杈╃磽閸屾艾鈧悂宕愰悜鑺ュ€块柨鏇炲€哥粈澶嬩繆閵堝懏鍣圭痪鎯ь煼閺岋綁骞囬鍌欑驳閻庤娲栧鍓佹崲濠靛顥堟繛鎴濆船閸撻亶姊虹粙娆惧剭闁告梹鍨甸～蹇旂節濮橆剟鍞堕梺缁樻煥閸㈡煡鎮楅鍕拺闂傚牊绋撶粻鐐烘煕婵犲啰澧电€规洘鍔欏畷褰掝敃閿濆懎浼庢繝纰樻閸ㄤ即骞栭锔藉殝鐟滅増甯楅悡鏇熶繆椤栨瑨顒熼柛銈囧枛閺屽秷顧侀柛鎾寸箞閿濈偞寰勬繛鎺楃細缁犳稑鈽夊Ο纭风吹闂傚倸鍊搁悧濠勭矙閹捐姹查柨鏇炲€归悡蹇撯攽閻愭垟鍋撻柛瀣崌閺屾稓鈧綆鍋呯亸顓㈡煃閽樺妲搁柍璇茬У濞煎繘濡搁妷銉︽嚈婵°倗濮烽崑娑氭崲閹烘梹顫曢柟鐑樺殾閻斿吋鍤冮柍鍝勶工閺咁參姊绘担鍛婃儓妞わ富鍨堕幃褔宕卞Ο缁樼彿婵炲濮撮鍛不閺嵮€鏀介柛灞剧閸熺偤鏌ｉ幘瀵告噰闁哄睙鍡欑杸闁挎繂鎳嶇花濂告倵鐟欏嫭绀€闁哄牜鍓熸俊鐢稿礋椤栨凹娼婇梻鍕处缁旂喎螣濮瑰洣绨诲銈嗘尰缁本鎱ㄩ崒婧惧亾鐟欏嫭纾搁柛鏃€鍨块妴浣糕槈濮楀棛鍙嗛梺褰掑亰閸犳牜鑺遍妷鈺傗拻闁稿本鐟︾粊鎵偓瑙勬礀閻忔岸骞堥妸鈺佺骇闁圭偨鍔嶅浠嬪极閸愨晜濯撮柛蹇撴啞閻繘姊绘担鍛婂暈闁告棑绠撳畷浼村冀椤撶喎鈧潡鏌涢…鎴濅簴濞存粍绮撻弻鐔煎传閸曨剦妫炴繛瀛樼矋閸庢娊鍩為幋锔藉€烽柣鎰帨閸嬫挾鈧綆鍓氬畷鍙夌節闂堟侗鍎忕紒鐘崇墪闇夐柛蹇撳悑閸庢鏌℃担闈╄含闁哄矉绠戣灒濞撴凹鍨辨缂傚倷鐒﹂崝妤呭磻閻愬灚宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩闁瑰鍏樺铏圭矙濞嗘儳鍓遍梺鍦嚀濞差參寮幇鐗堝€风€瑰壊鍠栭幃鎴炵節閵忥絾纭炬い鎴濆€块獮蹇撁洪鍛嫽闂佺鏈銊︽櫠濞戞氨纾奸悗锝庡亝鐏忕數绱掗纰辩吋鐎规洘锚闇夐悗锝庡亝閺夊憡淇婇悙顏勨偓鏍ь潖瑜版帒纾块柟鎯版閸屻劑鎮楀☉娅辨粍绂嶅鍫熺厪闊洢鍎崇壕鍧楁煙閸愬弶澶勬い銊ｅ劦閹瑩寮堕幋鐐剁檨闁诲孩顔栭崳顕€宕抽敐鍛殾闁圭儤鍩堥悡銉╂煙闁箑娅嶆俊顐ゅ厴濮婂宕掑顑藉亾閻戣姤鍤勯柛顐ｆ磵閳ь剨绠撳畷濂稿Ψ閵夈儳褰夋俊鐐€栫敮鎺斺偓姘煎弮瀹曟垿鏁嶉崟顒€鏋戦梺鍝勫€藉▔鏇㈠汲閿旂晫绡€闂傚牊绋掗敍宥夋煕濮橆剦鍎旈柡灞剧洴閸╁嫰宕橀妸銉綇缂傚倷闄嶉崝蹇涱敋瑜旈垾鏃堝礃椤斿槈褔鏌涢埄鍏狀亪寮冲Δ鍛拺缂佸顑欓崕鎴︽煕鐎ｃ劌鈧洟鎮鹃悜钘夌骇閻犲洤澧介崰鎾寸閹间礁鍐€鐟滃本绔熼弴銏＄厽闁绘柨鎽滈幊鍐倵濮樼厧骞樺瑙勬礋楠炴牗鎷呴崷顓炲箞闂備線娼ч…鍫ュ磹濡ゅ懏鍎楁繛鍡樺灍閸嬫挸鈻撻崹顔界亪濡炪値鍘奸崲鏌ユ偩閻戣棄纭€闁绘劕绉靛鍦崲濠靛纾兼繛鎴炃氶崑鎾活敍閻愮补鎷绘繛杈剧秬濞咃綁濡存繝鍥ㄧ厓鐟滄粓宕滃▎鎴濐棜妞ゆ挾濮锋稉宥夋煛鐏炶鍔滈柛濠傜仛閹便劌顫滈崱妤€骞嶉梺绋款儍閸ㄦ椽骞堥妸锔剧瘈闁稿被鍊楅崥瀣倵鐟欏嫭绀冮悽顖涘浮閿濈偛鈹戠€ｅ灚鏅為梺鑺ッˇ顔界珶閺囥垺鈷戠憸鐗堝笚閿涚喖鏌ｉ幒鐐电暤鐎规洘鍨归埀顒婄秵娴滄牠寮ㄦ禒瀣厽婵☆垵顕х徊缁樸亜韫囷絽浜伴柡宀嬬秮椤㈡﹢鎮㈤悜妯烘珰闂備礁鎼惉濂稿窗閺嶎厾宓侀柛鈩冨嚬濡查箖姊洪崷顓熸珪濠殿喚鏁搁幑銏犫槈閵忕姴绐涘銈嗙墬閸╁啴寮搁崨瀛樷拺闁告繂瀚悞璺ㄧ磼缂佹绠撻柣锝囧厴瀹曞ジ寮撮悙宥冨姂閺屾洘绔熼姘偓璇参涢鐐粹拻闁稿本鐟ㄩ崗宀€绱掗鍛仸妤犵偞鍔栫换婵嗩潩椤撶偘鐢婚梻浣稿暱閹碱偊骞婃惔銊﹀珔闁绘柨鎽滅粻楣冩煙鐎涙鎳冮柣蹇婃櫇缁辨帡鎮╁畷鍥ｅ闂侀潧娲ょ€氫即鐛Ο鍏煎磯闁烩晜甯囬崹濠氬焵椤掍緡鍟忛柛鐘虫崌瀹曟繈骞嬪┑鎰稁濠电偛妯婃禍婊冾啅濠靛棌鏀介柣妯诲絻椤忣亪鏌涢敍鍗炴处閻撶喖骞栧ǎ顒€鐏╅柛銈庡墴閺屾稑鈻庤箛鎾存婵烇絽娲ら敃顏堝箖閻ｅ瞼鐭欓悹渚厛濡茶淇婇悙顏勨偓鏍偋濡ゅ懎绀勯柣鐔煎亰閻掕棄鈹戦悩鍙夊闁抽攱鍨块弻锟犲磼濡搫濮曞┑鐐叉噹閹虫﹢寮诲☉銏″亹閻庡湱濮撮ˉ婵嬫煣閼姐倕浠遍柡灞剧洴楠炲洭鎮介棃娑樺紬婵犵數鍋為幐鎼佲€﹂悜钘夎摕闁哄洢鍨归柋鍥ㄧ節闂堟稒顥炴繛鍫弮濮婅櫣鎷犻崣澶岊洶婵炲瓨绮犳禍顏勵嚕婵犳艾惟闁宠桨绀佸畵鍡椻攽閳藉棗鐏ユ繛鍜冪稻缁傛帗銈ｉ崘鈹炬嫼闂備緡鍋呯粙鎾诲煘閹烘鐓曢柡鍐ｅ亾闁搞劌鐏濋悾宄懊洪鍕姦濡炪倖甯婇梽宥嗙濠婂牊鐓欓柣鎴灻悘銉︺亜韫囷絼閭柡宀嬬秮楠炴﹢宕￠悙鎻掝潥缂傚倷鑳剁划顖炴儎椤栫偟宓侀悗锝庡枟閸婅埖绻涢懠棰濆敽闂侇収鍨遍妵鍕閳╁喚妫冮梺绯曟櫔缁绘繂鐣烽幒妤€围闁糕檧鏅涢弲顒勬⒒閸屾瑨鍏岀紒顕呭灦瀹曟繈寮借閻掕姤绻涢崱妯哄闁告瑥绻掗埀顒€绠嶉崕鍗炍涘▎鎾崇煑闊洦鎸撮弨浠嬫煟濡搫绾ч柛锝囧劋閵囧嫯绠涢弴鐐╂瀰闂佸搫鐬奸崰鎰八囬悧鍫熷劅闁抽敮鍋撻柡瀣嚇濮婃椽鎮烽幍顕嗙礊婵犵數鍋愰崑鎾愁渻閵堝啫鐏い銊ワ工閻ｇ兘骞掑Δ鈧洿闂佸憡渚楅崰妤€袙瀹€鍕拻闁稿本鑹鹃埀顒佹倐瀹曟劙鎮滈懞銉ユ畱闂佸壊鍋呭ú宥夊焵椤掑﹦鐣电€规洖銈告俊鐑筋敊閼恒儲鐝楅梻鍌欒兌绾爼宕滃┑瀣仭鐟滄棃骞嗙仦瑙ｆ瀻闁规儳顕崢鐢告⒑缂佹ê鐏﹂柨鏇楁櫅閳绘捇寮崼鐔哄幗闂侀潧鐗嗛ˇ閬嶆偩濞差亝鐓涢悘鐐插⒔濞插瓨銇勯姀鈩冪闁轰焦鍔欏畷鍗炩枎濡亶鐐烘⒒娴ｇ瓔鍤欐繛瀵稿厴瀵偊宕ㄦ繝鍐ㄥ伎闂佸湱铏庨崰鎺楀焵椤掑﹤顩柟鐟板婵℃悂鏁冮埀顒勫几閸岀偞鈷戦柛娑橈攻婢跺嫰鏌涘鈧粻鏍箖妤ｅ啫绠氱憸澶愬绩娴犲鐓熼柟閭﹀墮缁狙囨倵濮橆剚鍣界紒杈ㄦ尭椤撳ジ宕熼鐘靛床闁诲氦顫夊ú妯侯渻娴犲鏄ラ柍褜鍓氶妵鍕箳瀹ュ顎栨繛瀛樼矋缁捇寮婚悢鐓庝紶闁告洦鍘滈妷鈺傜厱闊洦鎸鹃悞鎼佹煛鐏炵晫效妞ゃ垺绋戦埥澶娾枎閹搭厽效闂傚倷绀佸﹢閬嶎敆閼碱剛绀婇柍褜鍓氶妵鍕晜閻ｅ苯寮ㄥ┑鈽嗗亗閻掞箑顕ラ崟顓涘亾閿濆骸澧伴柣锕€鐗婄换婵嬫偨闂堟刀銏ゆ煕婵犲啯鍊愭い銏℃椤㈡洟鏁傞悾灞藉箺婵＄偑鍊栭幐楣冨窗鎼粹埗褰掝敋閳ь剟寮婚垾宕囨殕閻庯綆鍓涢敍鐔哥箾鐎电顎撶紒鐘虫崌楠炲啫鈻庨幙鍐╂櫌闂佺琚崐鏍ф毄缂傚倸鍊搁崐鎼佸磹閻戣姤鍤勯柤绋跨仛閸欏繘鏌ｉ姀銏℃毄闁活厽鐟╅弻鐔兼倻濡儵鎷归悗瑙勬礀瀵墎鎹㈠┑瀣棃婵炴垵宕崜鎵磼閻愵剙鍔ら柛姘儔楠炲牓濡搁妷顔藉缓闂佸壊鍋侀崹鏄忔懌闂備浇顕х€涒晛顫濋妸鈺佺獥闁规崘鍩栭～鏇㈡煙閹规劦鍤欑痪鎯у悑閹便劌顫滈崱妤€骞嬮梺绋款儐閹稿藝閻楀牊鍎熼柕蹇曞閳ь剚鐩娲传閸曨噮娼堕梺绋挎唉娴滎剚绔熼弴鐔虹瘈婵﹩鍘鹃崢鐢告⒑閸涘﹥瀵欓柛娑卞幘椤愬ジ姊绘担渚劸闁挎洩绠撳顐ｇ節濮橆剝鎽曢梺闈涱焾閸庮噣寮ㄦ禒瀣厱闁斥晛鍟╃欢閬嶆煃瑜滈崜姘躲€冩繝鍥ц摕闁挎稑瀚ч崑鎾绘晲鎼粹€愁潻闂佸搫顑嗛惄顖炲蓟閳ュ磭鏆嗛悗锝庡墰琚︽俊銈囧Х閸嬬偤鈥﹂崶顒€鐒垫い鎺戝€归弳鈺冪棯椤撯剝纭鹃崡閬嶆煕椤愮姴鍔滈柍閿嬪浮閺屾盯濡烽幋婵囨拱闁宠鐗撳娲传閸曨噮娼堕梺鍛婃煥閻倿宕洪悙鍝勭闁挎洍鍋撴鐐灪娣囧﹪顢涘▎鎺濆妳濠碘剝鐓℃禍璺侯潖缂佹ɑ濯撮悷娆忓娴犫晠姊虹粙鍖℃敾闁告梹鐟ラ悾鐑藉箣閿曗偓缁犲鏌￠崒妯哄姕闁哄倵鍋撻梻鍌欒兌缁垵鎽梺鍛婃尰瀹€绋跨暦椤栨繄鐤€婵炴垶鐟ч崢閬嶆⒑缂佹◤顏嗗椤撶喐娅犻弶鍫氭櫇绾惧吋銇勯弮鍥撴い銉ョ墢閳ь剝顫夊ú鏍Χ缁嬫鍤曢柟缁㈠枟閸婄兘姊婚崼鐔衡槈鐞氭繃绻濈喊澶岀？闁稿鍨垮畷鎰板冀椤愶絾娈伴梺鍦劋椤ㄥ懐绮ｅΔ浣瑰弿婵妫楁晶濠氭煟閹哄秶鐭欓柡宀€鍠栭弻鍥晝閳ь剟鐛Ο鑲╃＜闁绘宕甸悾娲煛鐏炲墽鈽夐柍璇叉唉缁犳盯鏁愰崰鑸妽缁绘繈鍩涢埀顒備沪婵傜浠愰梻浣告惈閻绱炴笟鈧顐﹀箛閺夎法鍊為悷婊冪Ч閻涱喚鈧綆浜跺〒濠氭煏閸繂鏆欓柣蹇ｄ簼閵囧嫰濡搁妷顖氫紣闂佷紮绲块崗姗€骞冮姀銏犳瀳閺夊牄鍔嶅▍鍥⒒娓氣偓濞佳囨偋閸℃稑绠犻幖杈剧悼娑撳秹鏌熼幆鏉啃撻柍閿嬪笒闇夐柨婵嗗椤掔喖鏌￠埀顒佸鐎涙鍘遍柣搴秵閸嬪嫭鎱ㄦ径鎰厵妞ゆ棁顕у畵鍡椻攽閿涘嫬鍘撮柛鈹惧墲閹峰懘宕妷銏犱壕妞ゆ挾鍠嶇换鍡涙煏閸繄绠抽柛鎺嶅嵆閺屾盯鎮ゆ担鍝ヤ桓閻庤娲橀崹鍨暦閻旂⒈鏁嶆繛鎴炶壘楠炴姊绘担绛嬫綈鐎规洘锚閳诲秹寮撮姀鐘殿唹闂佹寧绻傞ˇ浼存偂閻斿吋鐓ユ繝闈涙椤ユ粓鏌嶇紒妯荤闁哄瞼鍠栭、娆戠驳鐎ｎ偆鏉归梻浣虹帛娓氭宕抽敐鍛殾鐟滅増甯╅弫濠囨煟閹惧啿鐦ㄦ繛鍏肩☉閳规垿鎮╁▓鎸庢瘜濠碘剝褰冮幊妯虹暦閹达箑宸濋悗娑櫭禒顓㈡⒑閸愬弶鎯堥柟鍐叉捣缁顫濋懜鐢靛幈闂侀€涘嵆濞佳囧几濞戞氨纾奸柣娆忔噽缁夘噣鏌″畝瀣埌閾伙綁鏌涜箛鎾虫倯婵絽瀚板娲箰鎼达絺濮囩紓渚囧枟閹瑰洤顕ｆ繝姘労闁告劑鍔庣粣鐐寸節閻㈤潧孝閻庢凹鍓熼妴鍌炲醇閺囩啿鎷洪梺鍛婄☉閿曘儵鎮￠妷褏纾煎璺侯儐鐏忥箓鏌熼钘夊姢闁伙綇绻濋獮宥夘敊閼恒儳鏆﹂梻鍌欑窔閳ь剛鍋涢懟顖涙櫠閹绢喗鐓曢柍瑙勫劤娴滅偓淇婇悙顏勨偓鏍暜閹烘绐楁慨姗嗗墻閻掍粙鏌熼柇锕€骞樼紒鐘荤畺閺屾稑鈻庤箛锝嗏枔闂佺粯甯婄划娆撳蓟閳╁啰鐟归柛銉戝嫮褰庢俊銈囧Х閸嬬偟鏁幒鏇犱簷闂備線鈧偛鑻晶鎾煙椤斿厜鍋撻弬銉︽杸闁诲函缍嗘禍鐐侯敊閹邦兘鏀介柣鎴濇川缁夌敻鏌涢幘瀵告噭濞ｅ洤锕幊鏍煛閸愵亷绱查梻浣虹帛閿氶柛鐔锋健閸┿垽寮撮姀锛勫幐閻庡厜鍋撻柍褜鍓熷畷浼村箻鐠囪尙鍔﹀銈嗗笂閼冲爼鍩婇弴鐔翠簻妞ゆ挾鍋炲婵堢磼椤旂⒈鐓兼鐐搭焽缁辨帒螣閻撳骸绠婚梻鍌欑婢瑰﹪鎮￠崼銉ョ；闁告洦鍓氶崣蹇曗偓骞垮劚濡瑩宕ｈ箛鎾斀闁绘ɑ褰冩禍鐐烘煟閹剧懓浜归柍褜鍓濋～澶娒哄Ο鍏兼殰闁圭儤顨呴悡婵嬪箹濞ｎ剙濡肩紒鐘冲▕閺岀喓鈧稒顭囩粻鎾绘煠閸偄濮囬柍瑙勫灴閺佸秹宕熼鈩冩線闂備胶顭堥…鍫ュ磹濠靛棛鏆﹂柕蹇ョ磿闂勫嫰鏌涘☉姗堝伐濞存粓浜跺娲捶椤撶偘澹曢梺娲讳簻缂嶅﹪宕洪埀顒併亜閹达絾纭剁紒鎰⒒閳ь剚顔栭崰鏇犲垝濞嗘挶鈧礁顫滈埀顒勫箖濞嗘挻鍤戦柛銊︾☉娴滈箖鏌涢…鎴濇灀闁衡偓娴犲鐓熼柟閭﹀幗缂嶆垿鏌ｈ箛銉╂闁靛洤瀚伴、姗€鎮欓弶鎴炵亷婵＄偑鍊戦崹娲偡閿曗偓椤曘儵宕熼姘辩杸濡炪倖鎸荤粙鎺斺偓姘偢濮婄粯鎷呮笟顖涙暞闂佺顑嗛崝娆忕暦閹存績妲堥柕蹇曞Х椤︻噣姊洪柅鐐茶嫰婢у瓨鎱ㄦ繝鍐┿仢鐎规洏鍔嶇换婵囨媴閾忓湱鐣抽梻鍌欑閹芥粍鎱ㄩ悽鎼炩偓鍐╃節閸ャ儮鍋撴担绯曟瀻闁规儳鍟垮畵鍡涙⒑闂堟稓绠氭俊鐙欏洤绠紓浣诡焽缁犻箖寮堕崼婵嗏挃闁告帊鍗抽弻鐔烘嫚瑜忕弧鈧Δ鐘靛仜濡繂鐣锋總鍛婂亜闁惧繗顕栭崯搴ㄦ⒑鐠囪尙绠抽柛瀣洴瀹曟劙骞栨担鐟扳偓鍫曟煠绾板崬鍘撮柛瀣尵閹叉挳宕熼鍌ゆК缂傚倸鍊哥粔鎾晝閵堝鍋╅柣鎴ｅГ閸婅崵绱掑☉姗嗗剱闁哄拑缍佸铏圭磼濮楀棛鍔告繛瀛樼矤閸撶喖骞冨畡鎵虫斀闁搞儻濡囩粻姘舵⒑缂佹ê濮﹀ù婊勭矒閸┾偓妞ゆ帊鑳舵晶顏堟偂閵堝洠鍋撻獮鍨姎妞わ富鍨虫竟鏇熺節濮橆厾鍘甸梺鍛婃寙閸涱厾顐奸梻浣虹帛閹稿鎮烽敃鍌毼﹂柛鏇ㄥ灠缁秹鏌涚仦鎹愬濞寸姵锚閳规垿鍩勯崘銊хシ濡炪値鍘鹃崗妯侯嚕鐠囧樊鍚嬮柛銉ｅ妼閻濅即姊洪崫鍕潶闁稿孩妞介崺鈧い鎺嶇贰濞堟粓鏌＄仦鐣屝ら柟椋庡█瀵濡烽妷銉ュ挤濠德板€楁慨鐑藉磻濞戙埄鏁勯柛娑欐綑閻撴﹢鏌熸潏鍓х暠闂佸崬娲弻鏇＄疀閺囩倫銏㈢磼閻橀潧鈻堟慨濠傤煼瀹曟帒顫濋钘変壕闁归棿绀佺壕褰掓煟閹达絽袚闁搞倕瀚伴弻娑㈩敃閿濆棛锛曢梺閫炲苯澧剧紒鐘虫尭閻ｉ攱绺界粙璇俱劍銇勯弮鍥撴繛鍛墦濮婄粯鎷呴搹骞库偓濠囨煕閹惧绠樼紒顔界懇楠炲鎮╅崗鍝ョ憹闂備礁鎼悮顐﹀磿閺屻儲鍋傞柡鍥ュ灪閻撳繐顭块懜寰楊亪寮稿☉姗嗙唵鐟滄粓宕抽敐澶婅摕闁挎稑瀚ч崑鎾绘晲閸屾稒鐝栫紓浣瑰姈濡啴寮婚敍鍕勃闁告挆鍕灡婵°倗濮烽崑娑樏洪鐐嶆盯宕橀妸銏☆潔濠电偛妫欓崹鎶芥焽椤栨粎纾介柛灞剧懆閸忓矂鏌涚€ｎ偅宕岀€规洘娲熼獮搴ㄦ寠婢跺巩鏇㈡煟鎼搭垳绉甸柛鎾磋壘閻ｇ兘宕ｆ径宀€顔曢梺鐟扮摠閻熴儵鎮橀埡鍐＜闁绘瑥鎳愮粔顕€鏌″畝瀣М妤犵偛娲、妤呭焵椤掑嫬姹查柣鎰暯閸嬫捇宕楁径濠佸缂傚倷绀侀鍫濃枖閺囥垹姹查柨鏇炲€归悡娆撳级閸繂鈷旈柣锝堜含缁辨帡鎮╁顔惧悑闂佽鍠楅〃濠偽涢崘銊㈡婵炲棙鍔曢弸鎴︽⒒娴ｉ涓茬紒鐘冲灴閹囧幢濞戞鍘撮梺纭呮彧鐎靛矂寮繝鍥ㄧ厸闁稿本锚閳ь剚鎸惧▎銏ゆ偨閸涘ň鎷洪梺鍛婄☉椤參鎳撶捄銊х＜闁绘ê纾晶顏堟煟閿濆洤鍘撮柡浣瑰姈瀵板嫭绻濋崟鍨為梻鍌欑窔濞佳団€﹂崼銉ュ瀭婵炲樊浜滃Λ姗€鏌嶈閸撶喎顫忕紒妯诲闁告縿鍎查悗顔尖攽閳藉棗浜滈悗姘煎墲閻忓鈹戞幊閸婃洟骞婅箛娑樼９闁割偅娲橀悡鐔兼煛閸屾氨浠㈤柟顔藉灴閺屾稓鈧絽澧庣粔顕€鏌＄仦鐣屝ユい褌绶氶弻娑㈠箻鐠虹儤鐏堥悗娈垮櫘閸嬪﹤鐣峰鈧、娆撴嚃閳轰礁袝濠碉紕鍋戦崐鏍ь啅婵犳艾纾婚柟鎯х亪閸嬫挾鎲撮崟顒€纰嶉柣搴㈠嚬閸橀箖骞戦姀鐘斀閻庯綆鍋掑Λ鍐ㄢ攽閻愭潙鐏﹂懣銈夋煕鐎ｎ偅宕屾鐐寸墬閹峰懘宕妷褎鐎梻鍌欐祰濞夋洟宕伴幘瀛樺弿闁汇垻顭堢壕濠氭煕鐏炲墽銆掔紒鐘荤畺閺屾盯鍩勯崗鈺傚灩娴滃憡瀵肩€涙鍘介梺瑙勫礃濞夋盯鍩㈤崼銉︾厸鐎光偓閳ь剟宕伴弽顓溾偓浣割潨閳ь剟骞冮姀鈽嗘Ч閹艰揪绲鹃崳顖炴⒒閸屾瑧顦﹂柟娴嬧偓鎰佹綎闁革富鍘奸崹婵嬪箹鏉堝墽绋绘い顐ｆ礋閺岀喖鎮滃鍡樼暥闂佺粯甯掗悘姘跺Φ閸曨垰绠抽柟瀛樼妇閸嬫捇鎮界粙璺紱闂佺懓澧界划顖炲疾閺屻儱绠圭紒顔煎帨閸嬫捇鎳犻鈧崵顒勬⒒閸屾瑧顦﹂柟璇х節閹兘濡疯瀹曟煡鏌熼悧鍫熺凡闁绘挻锕㈤弻鐔告綇妤ｅ啯顎嶉梺绋款儐閸旀瑩寮婚悢铏圭＜婵☆垵娅ｉ悷鎻掆攽閻愯尙澧涢柣鎾偓鎰佹綎婵炲樊浜濋ˉ鍫熺箾閹寸偟鎳冩い锔规櫇缁辨挻鎷呯粵瀣闁诲孩鐭崡鍐茬暦濞差亜鐒垫い鎺嶉檷娴滄粓鏌熼悜妯虹仴闁哄鍊栫换娑㈠礂閻撳骸顫嶇紓浣虹帛缁嬫捇骞忛悩渚Ь闂佷紮绲块弫鎼佸焵椤掑喚娼愭繛鍙夛耿瀹曞綊宕滄担鐟板簥濠电娀娼ч鍛閸忚偐绡€濠电姴鍊搁顏呫亜閺冣偓濞茬喎顫忕紒妯诲闁惧繒鎳撶粭鈥斥攽椤旂》宸ユい顓犲厴瀹曞搫鈽夐姀鐘诲敹闂侀潧顦崕鏌ユ倵椤掑嫭鈷戠紒顖涙礀婢ц尙绱掔€ｎ偄娴柟顕嗙節瀹曟﹢顢旈崨顓熺€炬繝鐢靛Т閿曘倝鎮ч崱娆戠焼闁割偆鍠撶粻楣冩煙鐎电浠╁瑙勶耿閺屾盯濡搁敂鍓х杽闂佸搫琚崝宀勫煘閹达箑骞㈡俊顖滃劋閻濆瓨绻濈喊妯哄⒉鐟滄澘鍟撮幃褔骞樼拠鑼舵憰闂佸搫娴勭槐鏇㈡偪閳ь剚绻濋悽闈浶㈤柛鐘冲哺閸┾偓妞ゆ巻鍋撴俊顐ｇ箞瀵鎮㈤崗鑲╁弳闁诲函缍嗛崑浣圭閵忕姭鏀芥い鏃傘€嬮弨缁樹繆閻愯埖顥夐柣锝囧厴婵℃悂鏁傞崜褏妲囬梻浣告啞濞诧箓宕㈤崜褏鐜绘俊銈呮噺閳锋帒霉閿濆嫯顒熼柣鎺斿亾閹便劌顫滈崼銉︻€嶉梺闈涙处濡啴鐛弽銊﹀闁荤喖顣︾純鏇㈡⒑閻熸澘鎮戦柣锝庝邯瀹曟繂顓兼径濠勫幈闂佸湱鍎ら崵姘炽亹閹烘挻娅滈梺鍛婁緱閸犳牠寮抽崼銉︾厽闁绘ê鍟挎慨鈧梺鍛婃尰缁诲牊淇婇悽绋跨妞ゆ牭绲鹃弲锝夋⒑缂佹ê濮嶆繛浣冲毝鐑藉焵椤掑嫭鈷掑ù锝呮啞閹牓鏌熼搹顐ｅ磳鐎规洘妞藉浠嬵敃閵堝洨鍔归梻浣告贡閸庛倝寮婚敓鐘茬；闁瑰墽绮弲鏌ュ箹鐎涙绠橀柡浣圭墪椤啴濡惰箛鎾舵В闂佺顑呴敃銈夛綖韫囨拋娲敂閸曨剙绁舵俊鐐€栭幐楣冨磻濞戞瑤绻嗛柛婵嗗閺€鑺ャ亜閺冨倶鈧螞濮樻墎鍋撶憴鍕闁诲繑姘ㄧ划鈺呮偄閻撳骸宓嗛梺闈涚箳婵兘藝瑜斿濠氬磼濮橆兘鍋撻幖渚囨晪妞ゆ挾濮锋稉宥嗘叏濮楀棗澧绘繛鎾愁煼閺屾洟宕煎┑鍥舵￥婵犫拃灞藉缂佽鲸甯￠、娆撳箚瑜夐弸鍛存⒑鐠団€虫灓闁稿鍊濋悰顕€宕卞☉鎺嗗亾閺嶎厽鏅柛鏇ㄥ墮濞堝苯顪冮妶搴″箲闁告梹鍨甸悾鐑芥偄绾拌鲸鏅ｉ梺缁樏悘姘熆閺嵮€鏀介柣妯诲墯閸熷繘鏌涢悩宕囧⒌闁轰礁鍟存俊鐑藉Ψ椤旇棄鐦滈梻浣藉Г閿氭い锕備憾瀵娊鏁冮崒娑氬帾婵犵數濮寸换鎰般€呴鍌滅＜闁抽敮鍋撻柛瀣崌濮婄粯鎷呮笟顖滃姼闂佸搫鐗滈崜鐔风暦濞嗘挻鍋￠梺顓ㄥ閸旈攱绻濋悽闈浶㈡繛璇х畵閸╂盯骞掗幊銊ョ秺閺佹劙宕ㄩ鍏兼畼闂備浇顕栭崹浼存偋閹捐钃熼柣鏃傚帶缁€鍐煠绾板崬澧版い鏂匡躬濮婃椽鎮烽幍顔炬殯闂佹悶鍔忓Λ鍕偩閻戣姤鍋傞幖瀛樕戦弬鈧梻浣稿閸嬪棝宕伴幇閭︽晩闁哄洢鍨洪埛鎺戙€掑锝呬壕濠电偘鍖犻崟鍨啍闂婎偄娲﹀ú姗€锝為弴銏＄厸闁搞儴鍩栨繛鍥煃瑜滈崜娆撳箠濮椻偓楠炲啫顭ㄩ崼鐔锋疅闂侀潧顦崕顕€宕戦幘缁樺仺闁告稑锕﹂崣鍡椻攽閻樼粯娑ф俊顐ｇ箞椤㈡挸螖閳ь剟婀佸┑鐘诧工鐎氼剚鎱ㄥ澶嬬厸鐎光偓閳ь剟宕伴弽顓炶摕闁靛ě鈧崑鎾绘晲鎼粹€茬按婵炲濮伴崹褰掑煘閹达富鏁婄痪顓炲槻婵稓绱撴笟鍥ф珮闁搞劌纾崚鎺楊敇閵忊€充簻闂佺粯鎸稿ù鐑藉磹閻愮儤鍋℃繝濠傚暟閻忛亶鏌ゅú顏冩喚闁硅櫕鐗犻崺锟犲礃椤忓海闂繝鐢靛仩閹活亞寰婇懞銉х彾濠电姴娲ょ壕鍧楁煙閹殿喖顣奸柣鎾存礋閺屾洘绻涜鐎氼厼袙閸儲鈷戦悹鍥ｂ偓铏彎缂備胶濮甸悧鐘荤嵁閸愵喗鍋╃€光偓閳ь剟鎯屽▎鎾寸厱妞ゎ厽鍨甸弸锕傛煃瑜滈崜娆撴倶濠靛鐓橀柟杈剧畱閻擄繝鏌涢埄鍐炬畼濞寸姵鎸冲铏圭矙濞嗘儳鍓遍梺鍛婃⒐閻熲晠濡存担绯曟瀻闁规儳鍟垮畵鍡涙⒑缂佹ɑ顥嗘繛鍜冪秮椤㈡瑩寮撮姀锛勫幐婵犮垼娉涢敃锔芥櫠閺囩儐鐔嗙憸宀€鍒掑▎蹇曟殾婵﹩鍘奸閬嶆倵濞戞瑯鐒介柛姗€浜跺铏圭磼濡儵鎷荤紓渚囧櫘閸ㄥ啿鈽夐悽绋块唶闁哄洨鍠撻崢鎾绘偡濠婂嫮鐭掔€规洘绮岄～婵堟崉閾忚妲遍柣鐔哥矌婢ф鏁幒妤€纾奸柕濞垮劗閺€浠嬫煕鐏炲墽鐭ら柣鎺楃畺閺岋綁骞樼€靛摜鐣奸梺閫炲苯澧紒鐘茬Ч瀹曟洟鏌嗗畵銉ユ处鐎靛ジ寮堕幋鐙呯幢闂備浇顫夋竟鍡樻櫠濡ゅ懏鍋傞柣鏂垮悑閻撴瑩姊洪銊х暠妤犵偞鐗犻弻锝堢疀閹垮嫬濮涚紓浣介哺鐢剝淇婇幖浣肝ㄦい鏃€鍎抽幃鎴炵節濞堝灝鏋涢柨鏇樺劚椤啴鎸婃径灞炬濡炪倖鍔х粻鎴犵矆鐎ｎ偁浜滈柟鎯ь嚟閳洟鏌ｆ惔銊ゆ喚婵﹦绮幏鍛瑹椤栨粌濮兼俊鐐€栭崹鐢稿箠閹版澘鐒垫い鎺嶇閸ゎ剟鏌涘Ο鍝勨挃闁告帗甯為埀顒婄秵閸犳牜绮婚搹顐＄箚闁靛牆瀚崝宥嗙箾閸涱厽鍠樻慨濠呮缁瑩骞愭惔銏″缂傚倷绀侀鍡涘箲閸パ呮殾闁靛骏绱曢々鐑芥倵閿濆骸浜愰柟閿嬫そ閺岋綁鎮㈤崫銉﹀櫑闁诲孩鍑归崜娑氬垝閸儱绀冮柍鍝勫暟椤旀洘绻濋姀锝嗙【妞ゆ垵娲ょ叅閻庣數纭堕崑鎾斥枔閸喗鐏嶉梺璇″枛閸婂潡鎮伴閿亾閿濆骸鏋熼柛瀣姍閺岋綁濮€閵忊晜娈扮紓浣界簿鐏忔瑧妲愰幘璇茬＜婵﹩鍏橀崑鎾绘倻閼恒儱鈧潡鏌ㄩ弴鐐测偓鐢稿焵椤掑﹦鐣电€规洖鐖奸崺锟犲礃瑜忛悷婵嬫⒒娴ｈ櫣甯涢柛鏃€顨堥幑銏犫攽閸喎搴婇梺绯曞墲鑿уù婊勭矒閺岀喖骞嶉搹顐ｇ彅闂佽绻嗛弲鐘诲蓟閿熺姴骞㈡俊顖氬悑閸ｄ即姊洪崫鍕缂佸缍婇獮鎰節閸愩劎绐炲┑鈽嗗灡娴滃爼鏁冮崒娑掓嫼闁诲骸婀辨刊顓㈠吹濞嗘劖鍙忔慨妤€鐗嗛々顒傜磼椤旀鍤欓柍钘夘樀婵偓闁斥晛鍟悵鎶芥⒒娴ｅ憡鎲稿┑顔肩仛缁旂喖宕卞☉妯肩崶闂佸搫璇炵仦鍓х▉濠电姷鏁告慨鐢告嚌閸撗冾棜闁稿繗鍋愮粻楣冩煕閳╁厾顏堟倶閵夈儮鏀介梽鍥ㄦ叏閵堝洦宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩缂侇喖鐖煎娲箰鎼淬垹顦╂繛瀛樼矋缁捇銆佸鑸垫櫜濠㈣泛谩閳哄懏鐓ラ柡鍐ㄥ€瑰▍鏇犵磼婢跺﹦鍩ｆ慨濠冩そ瀹曟﹢宕ｆ径瀣壍闂備胶顭堥敃锕傚箠閹捐鐓濋柟鐐綑椤曢亶鎮楀☉娅辨岸骞忛搹鍦＝闁稿本鐟ч崝宥夋煥濮橆兘鏀芥い鏂垮悑閸犳﹢鏌″畝瀣К缂佺姵鐩顒勫垂椤旇姤鍤堥梻鍌欑劍鐎笛呮崲閸曨垰纾婚柕鍫濇媼閸ゆ洟鏌熺紒銏犳灈妞ゎ偄鎳橀弻銊モ槈濡警浠煎Δ鐘靛仜缁夌懓顫忕紒妯诲闁惧繒鎳撶粭锟犳⒑閹肩偛濡介柛搴°偢楠炴顢曢敂鐣屽幗闂婎偄娲﹂幐鍓х不閹绘帩鐔嗛柣鐔哄椤ョ姵淇婇崣澶婂妤犵偞锕㈤獮鍥ㄦ媴閸涘﹤鈧垶姊绘担鍛婂暈濞撴碍顨婂畷浼村冀椤撶偛鍤戦梺缁橆焽缁垶鍩涢幋锔界厱婵犻潧妫楅顏呯節閳ь剟鎮ч崼銏㈩啎闂佸憡鐟ラˇ浠嬫倿娴犲鐓欐鐐茬仢閻忊晠鏌嶉挊澶樻█濠殿喒鍋撻梺缁橆焾鐏忔瑩鍩€椤掑骞栨い顏勫暣婵″爼宕卞Ο閿嬪闂備礁鎲￠…鍡涘炊妞嬪海鈼ゆ繝娈垮枟椤牓宕洪弽顓炵厱闁硅揪闄勯悡鐘测攽椤旇棄濮囬柍褜鍓氬ú鏍偤椤撶喓绡€闁汇垽娼ф禒婊堟煟椤忓啫宓嗙€规洘绻堥獮瀣攽閸喐顔曢梻浣侯攰閹活亞绮婚幋婵囩函闂傚倷绀侀幉锛勫垝閸儲鍊块柨鏂垮⒔缁€濠囧箹濞ｎ剙濡介柍閿嬪灴閺岀喖顢涢崱妤佸櫧妞ゆ梹鍨甸—鍐Χ鎼粹€茬盎濡炪倧绠撴禍鍫曞春閳ь剚銇勯幒宥堝厡妞わ綀灏欓埀顒€鍘滈崑鎾绘煙闂傚顦﹂柦鍐枔閳ь剙绠嶉崕閬嵥囬姘殰闂傚倷绶氬褔鈥﹂鐔剁箚闁搞儺浜楁禍鍦喐閺傛娼栭柧蹇氼潐鐎氭岸鏌ょ喊鍗炲妞ゆ柨娲ㄧ槐鎾存媴閸撳弶楔闂佺娅曢幑鍥晲閻愬墎鐤€闁瑰彞鐒﹀浠嬨€侀弮鍫濆窛妞ゆ挾鍣ラ崥瀣⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鐓崶銊﹀瘱闁圭儤顨呯粻娑㈡煟濡も偓閻楀啴骞忓ú顏呪拺闁告稑锕︾粻鎾绘倵濮樼厧寮€规洘濞婇幐濠冨緞閸℃ɑ鏉搁梻浣虹帛閸斿繘寮插☉娆戭洸闁诡垎鈧弨浠嬫煟濡椿鍟忛柡鍡╁灦閺屽秷顧侀柛鎾寸懅婢规洟顢橀姀鐘殿啈闂佺粯顭囩划顖炴偂濞嗘垟鍋撻悷鏉款伀濠⒀勵殜瀹曠敻宕堕浣哄幍闂佹儳娴氶崑鍡樻櫠椤忓牊鐓冮悷娆忓閻忓鈧娲栭悥濂稿灳閿曞倸绠ｉ柣鎴濇椤ュ牓姊绘担绛嬪殭婵﹫绠撻敐鐐村緞婵炴帡缂氱粻娑樷槈濡⒈妲烽梻浣侯攰閹活亞鎷归悢鐓庣劦妞ゆ垼娉曢ˇ锔姐亜椤愶絿鐭掗柛鈹惧亾濡炪倖甯掔€氼剛绮婚弽顓熺厓闁告繂瀚崳鍦磼閻樺灚鏆柡宀€鍠撻幏鐘诲灳閸忓懐鍑规繝鐢靛仜瀵爼鎮ч悩璇叉槬闁绘劕鎼粻锝夋煥閺冨洤袚婵炲懏鐗犲娲川婵犲啫顦╅梺绋款儏閸婂灝顕ｉ崼鏇ㄦ晣闁靛繆妾ч幏铏圭磽娴ｅ壊鍎忛悘蹇撴嚇瀵劍绻濆顓犲幗濠德板€撻懗鍫曟儗閹烘柡鍋撳▓鍨珮闁稿锕妴浣割潩鐠鸿櫣鍔﹀銈嗗笒鐎氼喖鐣垫笟鈧弻鐔兼倻濮楀棙鐣跺┑鈽嗗亝閿曘垽寮诲☉妯锋斀闁糕剝顨忔禒瑙勭節閵忥綆娼愭繛鍙夌墵閸╃偤骞嬮敂缁樻櫓缂備焦绋戦鍥礉閻戣姤鈷戦柛娑橆煬閻掑ジ鏌涢弴銊ュ闁绘繄鍏樺铏规喆閸曢潧鏅遍梺鍝ュУ濮樸劍绔熼弴銏犵缂佹妗ㄧ花濠氭⒑閸濆嫬鏆欐繛灞傚€栫粋宥夋偡闁妇鍞甸悷婊冩捣缁瑩骞樼拠鑼姦濡炪倖宸婚崑鎾绘煟韫囨棁澹樻い顓炵仢铻ｉ悘蹇旂墪娴滅偓鎱ㄥ鍡椾簻鐎规挸妫濋弻锝呪槈閸楃偞鐝濋悗瑙勬礀閻栧ジ銆佸Δ浣哥窞閻庯綆鍋呴悵婵嬫⒒閸屾瑨鍏岀紒顕呭灥閹筋偊鎮峰鍕凡闁哥噥鍨辩粚杈ㄧ節閸パ呭€炲銈嗗笂鐠佹煡骞忔繝姘拺缂佸瀵у﹢浼存煟閻旀繂娉氶崶顒佹櫇闁逞屽墴閳ワ箓宕稿Δ浣镐画闁汇埄鍨奸崰娑㈠触椤愶附鍊甸悷娆忓缁€鍐煕閵娿儳浠㈤柣锝囧厴婵℃悂鍩℃繝鍐╂珫婵犵數鍋為崹鍫曟晪缂備降鍔婇崕闈涱潖缂佹ɑ濯撮柛娑橈工閺嗗牓姊洪崨濠冾棖缂佺姵鎸搁悾鐑筋敍閻愭潙浜滅紓浣割儓濞夋洜绮ｉ悙鐑樷拺鐟滅増甯掓禍浼存煕濡搫鎮戠紒鍌氱Х閵囨劙骞掗幘顖涘婵犳鍠氶幊鎾趁洪妶澶嬪€舵い鏇楀亾闁哄备鍓濋幏鍛喆閸曨偀鍋撻幇鐗堢厸濞达絽鎽滃瓭濡炪値鍘归崝鎴濈暦婵傚憡鍋勯柛娆忣槸椤忓湱绱撻崒姘偓鎼佸磹閻戣姤鍊块柨鏇炲€歌繚闂佺粯鍔曢幖顐ょ不閿濆鐓ユ繝闈涙閺嗘洘鎱ㄧ憴鍕垫疁婵﹥妞藉畷銊︾節閸屾鏇㈡⒑閸濄儱校闁绘濞€楠炲啴鏁撻悩鎻掑祮闂侀潧楠忕槐鏇㈠储閹间焦鈷戦柛娑橈工婵倿鏌涢弬鍨劉闁哄懓娉涢埥澶愬閿涘嫬骞楅梻浣筋潐瀹曟ê鈻斿☉銏犲嚑婵炴垯鍨洪悡娑㈡倶閻愰潧浜剧紒鈧崘顏佸亾閸偅绶查悗姘煎墲閻忔帡姊虹紒妯虹仸妞ゎ厼娲︾粋宥夋倷閻戞ǚ鎷婚梺绋挎湰閻熝呮嫻娴煎瓨鐓曟繛鍡楃箻閸欏嫮鈧娲栫紞濠囧蓟閸℃鍚嬮柛娑樺亰缁犳捇寮诲☉銏犲嵆闁靛鍎虫禒鈺侇渻閵堝骸浜滈柟铏耿瀵顓兼径瀣檮婵犮垼娉涢鍛枔瀹€鍕拺闂侇偆鍋涢懟顖涙櫠椤曗偓閺岋綀绠涢弮鍌滅杽闂佺硶鏅濋崑銈夌嵁鐎ｎ喗鏅滅紓浣股戝▍鎾绘⒒娴ｈ棄鍚归柛鐘崇墵閵嗗倿鏁傞悾宀€褰鹃梺鍝勬储閸ㄦ椽鍩涢幒妤佺厱閻忕偛澧介幊鍛亜閿旇偐鐣甸柡灞剧洴閹垽鏌ㄧ€ｎ亙娣俊銈囧Х閸嬬偤鎮ч悩姹団偓渚€寮撮姀鈩冩珖闂侀€炲苯澧板瑙勬礃缁绘繂顫濋鐘插箞闂備焦鏋奸弲娑㈠疮閵婏妇顩叉俊銈勮兌缁犻箖鏌熺€涙鎳冮柣蹇婃櫊閺屾盯骞囬妸銉ゅ婵犵绱曢崑鎴﹀磹閺嶎厼绀夐柟杈剧畱绾捐淇婇妶鍛櫤闁哄拋鍓氱换婵嬫濞戝崬鍓遍梺鎶芥敱閸ㄥ湱妲愰幒鏂哄亾閿濆骸浜滄い鏇熺矋缁绘繈鍩€椤掍礁顕遍悗娑欘焽閸樹粙姊虹紒妯烩拹婵炲吋鐟﹂幈銊╁磼濞戞牔绨婚梺闈涢獜缁辨洟骞婇崟顓涘亾閸偅绶查悗姘煎櫍閸┾偓妞ゆ帒锕﹀畝娑㈡煛閸涱喚绠樼紒顔款嚙椤繈鎳滅喊妯诲缂傚倸鍊烽悞锕傗€﹂崶顒€绠犻柛鏇ㄥ灡閻撶喖鏌ｉ弮鈧娆撳礉濮樿埖鍋傞柕鍫濐槹閻撳繘鏌涢锝囩畺闁瑰吋鍔栭妵鍕Ψ閿曚礁顥濋梺瀹狀潐閸ㄥ潡骞冮埡浣烘殾闁搞儴鍩栧▓褰掓⒒娴ｈ櫣甯涢柟绋款煼閹兘鍩￠崨顓℃憰濠电偞鍨崹鍦不缂佹ü绻嗛柕鍫濆€告禍楣冩⒑閻熸澘鏆辨繛鎾棑閸掓帡顢橀姀鈩冩闂佺粯蓱閺嬪ジ骞忛搹鍦＝闁稿本鐟ч崝宥夋嫅闁秵鍊堕煫鍥ㄦ礃閺嗩剟鏌＄仦鍓ф创闁诡喒鏅涢悾鐑藉炊瑜夐幏浼存⒒娴ｈ櫣甯涘〒姘殜瀹曟娊鏁愰崪浣告婵炲濮撮鍌炲焵椤掍胶澧靛┑锛勫厴婵＄兘鍩℃担绋垮挤濠碉紕鍋戦崐鎴﹀垂濞差亗鈧啯绻濋崶褎妲梺閫炲苯澧柕鍥у楠炴帡骞嬪┑鎰棯闂備胶顭堥敃銉р偓绗涘懏宕叉繛鎴烇供閸熷懏銇勯弮鍥у惞闁告垵缍婂铏圭磼濡闉嶅┑鐐跺皺閸犳牕顕ｆ繝姘櫜濠㈣泛锕﹂惈鍕⒑閸撴彃浜介柛瀣嚇钘熼柡宥庡亞绾捐棄霉閿濆懏鎯堢€涙繂顪冮妶搴″箻闁稿繑锚椤曪絾绻濆顓熸珫闂佸憡娲︽禍婵嬪礋閸愵喗鈷戦柛娑橈攻鐎垫瑩鏌涢弴銊ヤ簻闁诲骏绻濆濠氬磼濞嗘劗銈板銈嗘礃閻楃姴鐣锋导鏉戠婵°倐鍋撻柣銈囧亾缁绘盯骞嬪▎蹇曚痪闂佺顑傞弲鐘诲蓟閿濆绠ｉ柨婵嗘－濡嫰姊虹紒妯哄闁挎洩绠撻獮澶愬川婵犲倻绐為梺褰掑亰閸撴盯顢欓崱妯肩閻庣數顭堢敮鍫曟煟鎺抽崝鎴﹀春濞戙垹绠抽柟鐐藉妼缂嶅﹪寮幇鏉垮窛妞ゆ棁妫勯崜闈涒攽閻愬樊鍤熷┑顕€娼ч—鍐╃鐎ｎ亣鎽曞┑鐐村灦閻燂箓宕曢悢鍏肩厪闁割偅绻冮崳褰掓煙椤栨粌浠х紒杈ㄦ尰缁楃喖宕惰閻忓牆顪冮妶搴″箻闁稿繑锕㈤幃浼搭敊閸㈠鍠栧畷妤呮偂鎼达絽閰遍梻鍌欐祰婢瑰牊绔熼崼銉ユ槬闁哄稁鍘肩粻鐔访归悡搴ｆ憼闁抽攱甯掗妴鎺戭潩閿濆懍澹曟繝鐢靛仒閸栫娀宕卞Δ渚囦哗闂傚倸鍊风欢姘跺焵椤掑倸浠滈柤娲诲灡閺呭墎鈧數纭堕崑鎾舵喆閸曨剛顦ㄩ梺鎼炲妼濞硷繝鎮伴鍢夌喖鎳栭埡鍐跨床婵犵妲呴崹宕囧垝椤栫偞鍋熼柟鎯板Г閳锋帒霉閿濆嫯顒熼柣鎺楃畺閺岋繝宕奸銏狀潾缂備緡鍠楅悷鈺呯嵁閹烘嚦鏃€鎷呴梹鎰煕闂傚倷绀侀幖顐ょ矙娓氣偓瀹曟垿宕卞▎蹇ｆ闂佽姤锚椤ャ垼銇愰幒鎾存珳闂佸憡渚楅崰妤呭磿閹剧粯鐓涚€广儱绻掔弧鈧梺璇″枟椤ㄥ﹤鐣峰Δ浣哥窞濠电姳绀侀ˉ姘辩磽閸屾艾鈧绮堟笟鈧、鏍幢濞戣鲸鏅炲┑鐐叉濞存岸鎮炴禒瀣厸鐎广儱鍟俊璺ㄧ磼閻樻彃鈷旈柍褜鍓涢幊鎾寸珶婵犲洤绐楅柡鍥╁Ь婵娊鏌ゆ慨鎰偓妤冨婵傚憡鐓冪憸婊堝礈閻旈鏆﹀┑鍌滎焾鍞梺鎸庢⒐閸庢娊鐛鍥╃＝闁稿本鑹鹃埀顒傚厴閹偤鏁冮崒娑欐珨缂傚倸鍊烽悞锕傘€冮崨鏉戠９闁煎鍊栭～鏇㈡煙閻戞﹩娈旈幆鐔兼偡濠婂啰肖闁轰緡鍣ｅ畷濂稿即閻斿搫甯楅梺鑽ゅ枑閻熴儳鈧凹鍓熼幃姗€骞橀鐣屽幍濡炪倖姊婚弲顐﹀箠閸ヮ煈娈介柣鎰摠瀹曞本顨ラ悙鏉戞诞鐎殿噮鍣ｅ畷濂稿閻樻妲堕梻鍌氬€搁崐鎼佸磹閻戣姤鍤勯柛鎾茬閸ㄦ繃銇勯弽顐杭闁逞屽墮閸熸潙鐣锋總鍛婂亜闁惧繗顕栭崯搴ㄦ⒒娓氣偓閳ь剛鍋涢懟顖涙櫠椤栨稏浜滈柕濠忕到閸旓箓鏌熼鐣屾噰妤犵偞鎹囬獮鎺楀箻閹碱厼鏁抽梻鍌氬€风粈渚€鎮块崶顒夋晪鐟滄棃寮绘繝鍥ㄦ櫜濠㈣泛鑻粊锕傛⒑閹肩偛鍔撮柛鎿勭畵瀵偅绻濋崶銊у幈闂佸湱鍋撻〃鍛偓姘槻椤洭骞樼紒妯锋嫽闂佺鏈懝楣冨焵椤掑嫷妫戠紒顔肩墛缁楃喖鍩€椤掑嫮宓佸鑸靛姇閻忔娊鎮洪幒宥囧妽婵＄偘绮欏顐﹀礃椤旇偐锛滃┑顔斤耿绾危閼哥數绡€闁汇垽娼ф禒婊勪繆椤愶絿鎳囨鐐村姍楠炴牗鎷呴崫銉П闂備胶绮…鍥╁垝椤栫偞鍋傛繛鍡樻尭缁狙囨煕椤愮姴鍔ら柣锝囧劋娣囧﹦绱掗姀鐘崇彎闂佸搫鐭夌紞渚€鐛Ο灏栧亾闂堟稒鍟為柛锝勫嵆濮婃椽宕崟顒佹嫳缂備礁顑嗛悧婊呭垝鐠囨祴妲堟慨妤€妫欓崓闈涱渻閵堝棗鐏﹂悗绗涘洦鍎撻煫鍥ㄧ⊕閳锋帒霉閿濆懏鍟為柟顖氱墛娣囧﹪顢曢敐鍥ㄥ櫚闂佽桨绀侀崯瀛樻叏閳ь剟鏌曢崼婵囧殌闁硅姤娲栭埞鎴︽倷閺夋垹浠ч梺鎼炲妿閹虫捁鐏嬪┑顔姐仜閸嬫捇鏌熼鑲╃Ш妤犵偘绶氶獮鎺楀箣濠垫劖顥忛梺鑽ゅ枑缁瞼鎹㈠鈧畷娲焵椤掍降浜滈柟鐑樺灥閺嬨倖绻涢崗鐓庡缂佺粯鐩畷锝嗗緞濞戞壕鍋撶捄銊㈠亾鐟欏嫭绀冪紒璇插€块崺銏℃償閵娿儳顔掗悗瑙勬礀濞层劑寮抽鈶╂斀闁绘劕妯婇崵鐔封攽椤旇姤灏︽い銏＄墵瀹曘劑顢樿閺呯姴鈹戦悩缁樻锭婵☆偄鐭傚銊︾鐎ｎ偆鍘藉┑鈽嗗灥椤曆呭緤缂佹ǜ浜滈煫鍥э攻濞呭棙銇勯妸锝呭姦闁诡喗鐟╅幊鐘活敄椤愬骸瀚崣蹇撯攽閻樻彃顏悽顖涚洴閺岀喎鐣￠悧鍫濇缂備緡鍠氶弫鎼佸焵椤掑﹦绉靛ù婊勭箞閹偓瀵肩€涙ǚ鎷绘繛杈剧秬椤宕戦悩缁樼厱闁哄倽鍎荤€氫即鏌ｉ敐鍥у幋妞ゃ垺顨婂畷鐔煎Ω閵忕姳澹曢梺鎸庢礀閸婃悂宕归崒娑栦簻闁哄倽顕ч崝銈夋煕閻樻煡鍙勯柛鈹垮灪閹棃濡搁敃鈧埀顒勬敱閵囧嫰骞掑鍫濆帯濠碘€虫▕娴滎亜顫忔繝姘＜婵﹩鍏橀崑鎾诲箹娴ｅ摜锛欓梺褰掓？缁€浣哄瑜版帗鐓熼柟杈剧到琚氶梺绋匡工濞硷繝寮婚妸鈺佺睄闁稿本绋掗悵顏嗙磽娴ｅ搫校闁稿孩濞婇崺鐐哄箣閿旇棄浜归梺鍛婄懃椤︿即骞冨▎鎴犵＝闁稿本鐟ㄩ澶愭煛閸涱垰孝妞ゎ偄绻愮叅妞ゅ繐绉甸弲銏ゆ⒑闁偛鑻晶鎵磼椤旇姤顥堟鐐达耿椤㈡瑩鎮℃惔锝傚亾椤撱垺鈷戠紒瀣硶閻忛亶鏌涚€ｎ剙浠遍柟顕嗙節瀵挳鎮╅崘鍙夌€鹃梺鐟板悑閻ｎ亪宕濇繝鍥х劦妞ゆ巻鍋撻柛鐔告綑閻ｇ兘濡歌閸嬫挸鈽夊▍顓т邯閹敻寮崼鐔叉嫼缂傚倷鐒﹁摫闁绘挶鍎甸弻鐔兼嚍閵壯呯厒缂備緡鍠氱划顖炲Χ閿濆绀冮柍鍝勫暙楠炲秹姊虹拠鍙夋崳闁轰焦鎮傞垾锕傚醇閵夈儳鍔﹀銈嗗笂缁€渚€鎮橀妷锔轰簻妞ゆ劧绲跨粻鐐淬亜閵忥紕澧甸柛鈺嬬磿閳ь剨缍嗛崜娑溾叺闂佽姘﹂～澶娒洪弽顓炍ч柟闂寸缁犳牠鏌曡箛瀣伄闁活厽鐟╅弻娑⑩€﹂幋婵囩亞缂備浇浜崰搴ㄥ煘閹达附鍋愰柤纰卞墯椤ュ姊洪崫鍕垫Ц缂併劑浜跺畷鎴﹀箻鐠囪尙鐤€闂佸搫顦冲▔鏇㈡晬濞戙垺鈷戦柣鐔告緲閳锋梻绱掔紒妯肩畼闁瑰箍鍨藉畷鍗炍熼梹鎰泿闂備浇顫夊妯绘櫠鎼淬劌绠栭柛蹇氬亹缁犻箖鏌熺紒妯轰刊闁搞倗鍠栭弻宥堫檨闁告挻绻堥敐鐐村緞婵炴帗妞介崺锟犲礃椤忓棭妲稿┑鐐舵彧缁茶姤绔熸繝鍥у惞闁哄洢鍨洪悡娆戠磽娴ｉ潧鐏╅柡瀣枛閺屾稒绻濋崟顐㈠箣闂佸搫鑻粔鐟扮暦椤愶箑绀嬫い鎰枎娴滃墽鎲搁悧鍫濈瑨缂佺嫏鍥ㄧ厵闂傚倸顕崝宥夋煃闁垮绗掗棁澶愭煥濠靛棛澧涙い蹇曞█閹粙顢涘☉姘垱闂佸搫鏈惄顖氼嚕椤曗偓閸┾偓妞ゆ帒瀚ч埀顒婄畵瀹曞爼濡歌濞插摜绱撴担鍓插剰缂併劑浜跺鍛婃償閵婏妇鍘甸梻渚囧弿缁犳垿鐛Δ鍛厽婵犻潧妫楀Σ濠氭煃鐟欏嫬鐏撮柟顔界懇瀵爼骞嬮悩杈ㄥ煕闂傚倷鐒﹂幃鍫曞礉瀹ュ拋娓诲ù鐘差儏缁犳牠鏌ㄩ悢鍝勑㈤崬顖炴⒑閸愬弶鎯堥柛濠傤煼瀹曘垽骞栨担鍏夋嫼闂佺鍋愰崑娑㈠礉閹绢喗鐓熼柟鎹愬皺缁愭梹銇勯姀鈩冾棃鐎规洖銈搁幃銏焊娴ｉ晲澹曢梺鍓插亝濞叉﹢鎮￠悩鐢垫／闁哄鐏濋懜褰掓煕濡粯灏﹂柡灞剧〒閳ь剨缍嗛崑鍛焊椤撱垺鐓冪紓浣股戝畷宀€鈧娲栫紞濠囥€佸▎鎾村仼閻忕偠鍋愰惌姘節绾板纾块柛瀣灴瀹曟劙寮借閺嬫牠鏌ㄩ弴鐐测偓鎼佹倷婵犲嫭鍠愰煫鍥ㄧ☉閻鏌嶈閸撴瑩鍩為幋锔藉亹妞ゆ棁鍋愭导鍥р攽閻愬樊妲归柣鈺婂灦閸ㄩ箖鏁冮崒銈嗘櫌闂侀€炲苯澧撮柍銉畵瀹曞爼顢楅埀顒勬偂濞戙垺鐓曟繛鎴濆船閺嬨倝鏌涘▎蹇旑棞闁宠鍨块幃鈺冪磼濡鏁繝纰樻閸嬪懘鎮烽埡浣烘殾鐎规洖娲ㄩ惌娆愮箾閸℃鈷旈柛鐐垫暬閺岋綁鎮㈤崫銉﹀櫑闁诲孩鍑归崢鍓у垝閸懇鍋撻敐搴′簼闁告瑥绻愰埞鎴︽偐閹绘帗娈查梺闈涙处缁诲啴銆冮妷鈺傚€烽柡澶嬪灥椤秹姊虹拠鈥崇仭婵☆偄鍟村顐﹀箛閺夎法鍊為梺鍐叉惈閸婂綊顢欓幘鏂ユ斀闁绘灏欏Λ鍕煛婢跺﹦姘ㄩ柛瀣尭铻栧ù锝囨嚀瀵潡姊洪崷顓℃闁哥姵鐗滅划濠氭偄閸忚偐鍙嗗┑鐐村灦閿氭い蹇ｅ亰閺岋綁顢橀悜鍡樞ㄩ梺閫炲苯澧紒鐘茬Ч瀹曟洟鏌嗗畵銉ユ处鐎靛ジ寮堕幋鐙呯串闂備胶绮崹鍏兼叏閵堝纾归柣銏犳啞閳锋帡鏌涢銈呮灁闁愁垱娲熼弻鐔煎箵閹烘挸鏆堥梺闈涙搐鐎氭澘顕ｉ鈧崺鈧い鎺嗗亾閻撱倝鏌ｉ弬娆炬疇婵炲吋鐗楃换娑橆啅椤旇崵鐩庨悗鐟版啞缁诲倿鍩為幋锔藉亹闁圭粯甯╂禒楣冩⒑闂堚晜鈧儵宕橀悙顒€鐦滈梻渚€娼ч悧鍡欐崲濡警鐎舵い鏃傗拡閻斿棝鏌ｉ悢鍝勵暭妞ゃ儳鍋熺槐鎺楀磼濮樻瘷褎顨ラ悙瀵稿闁瑰嘲鎳愰幉鎾礋椤掑倸绲块梻鍌氬€烽懗鍫曗€﹂崼銉ュ珘妞ゆ帒鍊婚惌娆撴煙閹规劦鍤欓柛灞诲姂閺岀喖姊荤€电濡介梺缁樻尵閸犳牠鎮￠锕€鐐婇柕濠忓椤︺儲绻涚€电袥闁哄懏绮撴俊鐢稿礋椤栨艾宓嗛梺绯曞墲椤ㄥ懘顢旇缁辨帞鎷犻崣澶樻闂佸疇顫夐崹鍨暦閹烘埈娼╂い鎾跺仦濞堟﹢姊绘担铏瑰笡闁瑰憡鎮傚畷顖炲箻椤旇偐鐣洪悗鐟板婢瑰寮告惔銊у彄闁搞儯鍔嶉幆鍕归悩灞傚仮婵﹨娅ｉ幏鐘绘嚑椤掑偆鍞舵俊鐐€栧ú锕傚矗閸愵喖鏄ラ柍褜鍓氶妵鍕箳閹存繃鐏撳┑鐐插悑閸旀牜鎹㈠☉銏犵煑濠㈣泛鐬奸悡澶愭倵鐟欏嫭纾搁柛銊ャ偢钘濈憸鐗堝笚閻撴洟鏌嶉崫鍕殭濞寸姾椴搁〃銉╂倷瀹割喖鍓堕梺杞扮閸熸挳宕洪埀顒併亜閹哄棗浜惧銈嗘煥缁绘劙鍩為幋锕€鐐婇柕澶堝€楅惄搴ㄦ⒒娴ｅ憡鎯堟繛灞傚姂瀹曟垿鎮欓崫鍕７闂佹儳绻愬﹢杈╁閻ｅ备鍋撻獮鍨姎婵☆偒鍘奸埢鎾诲即閻樼數锛滅紓鍌欑劍椤洨绮诲鈧弻娑㈡偐鐠囇勬暰閻庡灚婢樼€氼剟鎮惧┑瀣劦妞ゆ帒瀚弲顒勬煟閺傚灝鎮戦柍閿嬪灩缁辨帞鈧綆鍋掗崕銉╂煕鎼淬垹濮嶉柡灞剧洴婵℃悂濡疯閾忓酣姊烘导娆戞偧闁稿繑锕㈤悰顕€宕堕鈧痪褔鎮规笟顖滃帥闁告艾鎳樺缁樻媴閻熼偊鍤嬪┑鐐村絻缁夌懓鐣烽幋锕€绠荤紓浣诡焽閸橀亶姊洪棃娑辩劸闁稿酣浜堕崺鈧い鎺嗗亾婵炲皷鈧剚鍤曢悹鍥ㄧゴ濡插牊淇婇鐐存暠闁哄倵鍋撻梻鍌欒兌绾爼宕滃┑瀣︽繛鎴欏灩閸戠姵绻涢崱妤冪畾闁衡偓娴犲鐓熼柟閭﹀墮缁狙囨煃缂佹ɑ绀€闂囧绻濇繝鍌氼伀缂佺姷鍋熼埀顒冾潐濞叉﹢宕归崹顔炬殾闁绘梻鈷堥弫宥嗙箾閹寸偛绗氶悗姘矙濮婄粯绗熼埀顒勫焵椤掑倸浠滈柤娲诲灡閺呭墎鈧稒锕╁▓浠嬫煟閹邦剙绾ч柛鐘成戦妵鍕閿涘嫭鍣伴悗瑙勬礃缁繘藝閾忣偅鍙忛柨婵嗘嚇閸欏嫭鎱ㄦ繝鍌ょ吋鐎规洘甯掗～婵嬵敄閽樺澹曟俊鐐差儏濞寸兘鎯岄崱妞曞綊鏁愰崨顔跨闂佺粯绻嗗▔娑㈠煘閹达附鍋愰柛娆忣槹閹瑧绱掗悙顒€鍔ゆい顓犲厴瀵鈽夊Ο閿嬵潔闂佸憡顨堥崑娑氱懅濠电姷鏁搁崑娑樜熸繝鍐洸婵犲﹤鐗嗙粈鍐煃瑜滈崜娆撯€旈崘顔嘉ч柛鈩兠弳妤呮⒑閸濄儱孝闂佸府缍佸畷娲焵椤掍降浜滈柟鍝勬娴滈箖姊洪幖鐐茬仾闁绘搫绻濆畷娲倷閸濆嫮顓洪梺鎸庢濡嫭绂嶈ぐ鎺撯拺闁告稑锕﹂幊鍐┿亜閿旇鐏﹂挊婵嬫煃閸濆嫭鍣洪柍閿嬪灩缁辨挻鎷呴懖鈩冨灩娴滄悂顢橀姀锛勫幈闂佺粯鏌ㄩ幉锛勪焊閿旈敮鍋撳▓鍨灕妞ゆ泦鍥х叀濠㈣泛谩閻斿吋鐓ラ悗锝庡亝濠㈡垿姊婚崒娆掑厡缁绢厼鐖煎畷婊冣攽鐎ｃ劉鍋撻崘顓犵杸闁哄倹顑欓崵銈夋倵閸忓浜鹃梺鍛婃处閸忔﹢骞忛搹鍦＝濞达絽澹婇崕蹇涙煟韫囨梻绠炴い銏☆殜婵偓闁靛牆妫涢崢閬嶆⒑闂堟侗鐒鹃柛搴ゆ珪缁傛帡濮€鎺虫禍婊勩亜閹扳晛鐏紒鐘哄皺缁辨帞绱掑Ο鑲╃暤濡炪値鍋呯换鍫ャ€佸Δ鍛＜闁挎梹鍎崇花銉╂⒒閸屾艾鈧娆㈠顒夌劷鐟滄棃骞冭瀹曞崬鈽夊Ο鑲╂瀮闂備礁缍婇崑濠囧储妤ｅ啨鈧懘鏌ㄧ€ｃ劋绨婚梺瑙勫礃濞夋稑鏆╃紓浣哄亾瀹曟ê螞閸曨垱绠掗梻浣瑰缁诲倿鎮ф繝鍥舵晜闁绘绮崑銊︺亜閺嶃劎銆掗柕鍡樺笧缁辨帗娼忛妸銉ь儌闂佸綊顥撴繛鈧柟宕囧█椤㈡鍩€椤掑嫬鐒婚梻鍫熷厷閺冨牊鍋愰梻鍫熺◥缁爼姊洪崫銉ユ珡闁搞劌纾崚鎺斺偓锝庡枛缁犳娊鏌￠崒姘儓濞存粓绠栭弻銊モ攽閸℃侗鈧霉濠婂棗袚濞ｅ洤锕、鏇㈠閻樿櫕顔勯梻浣哥枃椤宕归崸妤€绠栨繛鍡樻尭缁狙囨煙鐎电小婵℃鎸绘穱濠囨倷椤忓嫧鍋撻幋锕€绀夌€光偓閸曨偆鐤囧┑顔姐仜閸嬫捇鏌ｅ☉鍗炴灈閾绘牠鏌涢幇鍓佸埌濞存粓绠栭弻銊モ攽閸℃侗鈧鏌＄€ｎ剙鏋涢柡灞界Ч閺屻劎鈧綆浜為悷銊╂⒒閸パ屾█闁哄被鍔岄埞鎴﹀幢濡櫣鐛╅梻浣侯攰濡嫰宕愰崷顓熷床婵炴垯鍨圭粈鍌炴煟閹炬娊顎楁い顐㈢У缁绘盯鏁愰崨顔芥倷闂佹寧娲︽禍顏堢嵁閸愩劉鏋庨柟鎯х－妤犲洭姊虹憴鍕剹闁告鍘ч埢鎾斥攽閸垻锛濋梺绋挎湰閻熝囁囬敃鍌涚厱濠电姴鍊归幉鍝ョ磼椤旇姤顥嗛柕鍥ㄥ姍楠炴帡骞嬮悩鍨瘒濠电姷鏁搁崑娑樜涘Δ鈧湁婵娉涢悿顕€鏌涘☉鍗炴灁濞存粍绮撻弻锟犲磼濮樺彉铏庨梺璇″枟閸ㄥ爼濡甸崟顖氭闁割煈鍠掗崑鎾诲箹娴ｅ摜鍘撮梺纭呮彧闂勫嫰宕戦幇鐗堢厱妞ゎ厽鍨垫禍婵囨叏閿濆懎顏紒缁樼箓閳绘捇宕归鐣屼憾闂備礁婀遍…鍫ュ疮閸ф鍋╅柣鎴犵摂閺佸秵绻涢幋鐑囦緵闁搞們鍊曢埞鎴︽倻閸モ晝校闂佺绻戦敃銏犵暦閹达箑绠婚悹鍥ㄥ絻缁愭盯鏌ｆ惔銏⑩姇闁挎岸鏌嶈閸撴瑩鈥﹂悜钘夎摕闁靛牆顦粻鎺楁煙閻戞ê鐏ュ┑顔奸叄濮婃椽鏌呴悙鑼跺濠⒀傚嵆閺屸剝鎷呴崜鑼悑閻庢鍠栭…閿嬩繆閹间礁唯闁靛繆鍓濋弶鎼佹⒒娴ｈ櫣銆婇柛鎾寸箞閹兘濡烽埡浣告優濡炪倖甯掗崐鑽ゅ閽樺褰掓晲婢跺閿繝娈垮枓閸嬫挸鈹戦悩鎰佸晱闁哥姵鎹囧畷鎰攽閸ワ妇绠氶梺姹囧灮椤牏绮堢€ｎ偁浜滈柡宥冨妽閻ㄦ垿鏌ｉ銏狀伃婵﹥妞介弻鍛存倷閼艰泛顏繝鈷€鍐惧殶闁逞屽墲椤煤濡吋宕查柛顐ｇ箥閸ゆ洟鏌熺紒銏犳灈闁绘挻鍨块弻宥囨喆閸曨偄濮㈡繛瀛樼矒缁犳牠寮婚敓鐘茬闁靛ě鍐炬毇缂傚倷璁查崑鎾绘煕閹般劍鏉哄ù婊勭矒閺岋繝宕掑☉姗嗗殝濡炪値鍋勭粔鍫曞箟閹间礁绫嶉柛顐ｇ箚閹芥洖鈹戦悙鏉戠仧闁搞劌缍婇幃鐐裁洪鍛幈闂佸綊鍋婇崰鏍ㄧ妤ｅ啯鐓熼柨婵嗘噽閸╋綁鏌熼鎸庣【闁宠棄顦…銊╁礃瑜庨惈蹇曠磽閸屾瑦绁板瀛樻倐楠炴垿宕惰閺嗭箓鏌熼悜妯虹亶闁哄閰ｉ弻鐔煎箹椤撶偛绠洪悶姘剧畵濮婅櫣鎷犻幓鎺濆妷濡炪倖姊归悧鐘茬暦娴兼潙鍐€闁靛闄勯悵鐑芥煙閸忚偐鏆橀柛鏂跨焸閹偤宕归鐘辩盎闂佸湱鍎ら崹鐢稿焵椤掆偓椤兘鐛繝鍋芥棃宕ㄩ鎯у箞闂備線娼чˇ浼村垂閻㈢绠犻煫鍥ㄧ⊕閸嬪倿鏌ㄥ┑鍡╂Ч闁抽攱甯掗湁闁挎繂娴傞悞鐐亜閵夈儳绠婚柡灞剧〒閳ь剨缍嗘禍婵嬪闯瑜版帗鍋傞柕鍫濐槹閻撴稓鈧箍鍎辨鎼佺嵁濡ゅ懏鐓曞┑鐘插€婚崺锝夋煛瀹€鈧崰鏍嵁閸℃稑绾ч柛鐔峰暞閹瑰洭寮婚敐澶婄閻庨潧鎲￠崚娑㈡⒑閸濆嫭婀扮紒瀣尰缁傛帡鏁傞悾灞告灆闂佸憡绮堝鎺楁焽椤栨壕鍋撶憴鍕缂佽鍊块垾锕傚Ω閳轰線鍞堕梺缁樻煥閹碱偊鐛幇鐗堚拻濞达絼璀﹂悞楣冩煛閸偄澧撮柕鍡楀暣瀹曘劑顢欑紒銏￠敜濠德板€х徊浠嬪疮椤栫偛鐓曢柟鐑橆殕閻撴洟鎮橀悙鏉戠濠㈣锕㈤弻宥堫檨闁告挻鐩畷妤€顫滈埀顒勭嵁婵犲伣鏃堝川椤撶媭妲跺┑鐐舵彧缁插潡鈥﹂崼銉﹀€跺┑鐘插亞濞撳鏌曢崼婵囶棤濠⒀屽墰缁辨帞鎷犻幓鎺撴濡ょ姷鍋涢敃銈夋偩濠靛绀嬫い鎴ｅГ鐎氳棄鈹戦悙鑸靛涧缂佽弓绮欓獮澶愭晸閻樿尙鏌堥梺缁樺姉閸庛倝鎮″▎鎰弿婵犻潧妫涢悞楣冩煕閵堝棛鎳囬柡宀嬬秮閺佹劙宕卞▎鎴犳毉缂傚倷绶￠崰妤呮偡瑜旈獮鎴﹀礋椤栨鈺呮煥閺冨倻鎽傞柛鐔烽閳规垿鏁嶉崟顐℃澀闂佺锕ラ悧鐘诲箖瑜嶈灃闁告劏鏅涙惔濠囨⒑閸涘﹥澶勯柛銊╀憾瀹曟垿顢旈崼鐔哄帗閻熸粍绮撳畷婊冣枎閹惧磭鍘撮梺纭呮彧闂勫嫰宕愰悜鑺ョ厸濠㈣泛顑呴悘鈺伱归悩铏€愭慨濠冩そ閹筹繝濡堕崨顔界暚闂備焦鎮堕崕婊堝礃閵娿倖鍠氶梻鍌氬€搁崐椋庢閿熺姴绀堟繛鍡樺灩閻捇鏌ｉ姀銏╃劸闁绘挻娲熼弻鐔衡偓鐢殿焾鍟哥紒鐐礃濡嫰婀侀梺鎸庣箓閹冲海鏁妸锔跨箚妞ゆ劗濮存俊浠嬫煏閸パ冾伂缂佺姵鐩獮姗€骞栭鐕佹＇濠碉紕鍋戦崐鎴﹀磿閼碱剙鏋堢€广儱顦闂佸憡娲﹂崹鎵不婵犳碍鍋ｉ柧蹇曟嚀閸斿鏌ｆ惔銏犲姢闁宠鍨块幃娆撳矗婢跺﹥顏ゅ┑鐘垫暩閸嬫劙宕戦幘缁樷拺闁告繂瀚悞璺ㄧ磽瀹ュ嫮鍔嶉柟渚垮姂閸┾偓妞ゆ帒瀚悡鐔镐繆椤栨碍鎯堥柡鍡涗憾閺岋繝宕卞Δ鍐ㄢ叺濠殿喖锕ㄥ▍锝囨閹烘嚦鐔烘嫚閺屻儳鈧櫣绱撻崒娆戭槮妞ゆ垵妫濆畷褰掑垂椤曞棜鈧灝銆掑锝呬壕濠殿喖锕ュ浠嬬嵁閹邦厽鍎熼柨婵嗘川閺嗐倖绻濋悽闈涗沪婵炲吋鐟╁畷鎰板锤濡も偓缁犳牠鏌ｉ幇顓犳殬闁稿鎳橀弻娑㈠箛閵婏附鐝栭梺鍛娚戦幃鍌氼潖閾忕懓瀵查柡鍥╁櫏濞兼垿姊洪崨濠忚€跨紒鐘崇墪閻ｅ嘲顫濋鐑嗗殼闁诲孩绋掗…鍥储閹剧粯鐓熼柣鏂挎憸閹冲啴鎮楀鐓庡⒋闁诡喗锕㈤獮姗€顢欓悾灞藉箞闂備線娼ч…顓犵不閹达箑绀夐柕鍫濐槹閻撶喖鏌ㄥ┑鍡樻悙闁告ê顕埀顒冾潐濞叉﹢宕曟總鏉嗗洭骞橀鐣屽幈濠碘槅鍨板﹢閬嶆儗濞嗘挻鐓涚€光偓鐎ｎ剛蓱闂佽鍨卞Λ鍐╀繆閼稿灚鍎熼柕蹇嬪灮鍟告繝鐢靛Х閺佹悂宕戝☉姗嗗殨闁割偅娲橀崑瀣煟濡鍤欑紒鐘崇墵閺屾洘寰勫☉姗嗘喘闂傚倸瀚€氼喚妲愰幒鏂哄亾閿濆骸浜滄い鏇熺矋娣囧﹤顔忛鐓庘拫濠殿喖锕ㄥ▍锝夊箲閸曨垰惟闁靛濡囨禍鏍р攽閻樻剚鍟忛柛鐘崇墵椤㈡岸顢橀悩鎻掔亰闂佸壊鍋侀崕杈╃矆閸愨斂浜滈柡鍐ㄥ€瑰▍鏇㈡煕濡湱鐭欐慨濠冩そ閺屽懎鈽夊Ο铏广偖婵犵數鍋橀崠鐘诲炊瑜忛ˇ顔碱渻閵堝棙纾甸柛瀣崌閺岋紕浠︾拠鎻掝潎閻庢鍣崳锝呯暦閹烘垟妲堟繛鍡樺灥濞懷呯磽閸屾艾鈧鎷嬮幓鎺濈€剁憸鏂跨暦閹达箑绠荤紓浣骨氶幏缁樼箾鏉堝墽鍒版繝鈧崡鐑囪€垮ù鐘差儐閻撴洟鏌曟繛鍨妞ゃ儱顦伴妵鍕敇閻愬鈹涘銈忛檮閻擄繝寮婚敓鐘插耿闊洦姊归悵姘攽椤旂》鍔熺紒顕呭灦楠炲繘宕ㄧ€涙ê娈熼梺闈涱檧缁叉椽宕戦幘璇茬妞ゅ繐妫涢敍婊堟⒑闁偛鑻晶顖滅磼濡ゅ啫鏋涢柛鈹惧亾濡炪倖甯掔€氼喖鐣垫担鍓茬唵闁兼悂娼ф慨鍫ユ煛閸涱喗鍊愰柡宀嬬到铻ｉ柛顭戝枤濮ｃ垽姊哄Ч鍥р偓鏇犫偓姘緲椤繐煤椤忓拋妫冨┑鐐寸暘閸庨亶鎮ч幘鎰佸殨闁圭粯宸诲Σ鍫熸叏濡搫缍佺紒妤€顦辩槐鎾诲磼濞嗘垵濡介柤鍨﹀厾鐟邦煥閸曨厾鐓夐梺鍝勭焿缁绘繂鐣峰鈧俊鎼佹晜閼恒儱鈧嘲鈹戦悙鑼憼缂侇喖鐬肩槐鐐寸節閸パ嗘憰闂佹寧绋戠€氼亜鈻介鍫熺厽闁挎繂鎳撶€氫即鏌嶉鍫熸锭闁宠鍨块、娆戞兜闁垮鏆版繝纰夌磿閸嬬姴螞閸曨垱鍋╃€瑰嫰鍋婂銊╂煃瑜滈崜娆撴偩閻戣姤鏅查柛鈩冾殘缁愮偤鏌ｆ惔顖滅У濞存粍绮嶇粩鐔奉吋婢跺鎷婚梺绋挎湰閻熝囁囬敃鍌涚厵闁兼亽鍎抽惌鎺斺偓瑙勬礃婵炲﹥淇婇悜钘夌厸濞达絽鎽滈埀顒夊幗缁绘稓鈧數顭堢敮鍫曟煟鎺抽崝鎴濈暦閹达箑鐓涢柛娑卞枤閸樹粙姊虹涵鍛涧缂佹煡绠栬棢濠㈣埖鍔栭悡銏ゆ煕閹板吀绨婚柡瀣洴閺岋紕浠﹂悙顒傤槰閻庡灚婢樼€氼剟鍩ユ径濠庢建闁糕剝锚閺嬬娀姊婚崒娆戭槮闁硅绻濋弫鍐閿涘嫷娲搁柣鐘烘〃鐠€锕€顭囬弽銊х鐎瑰壊鍠曠花缁樹繆椤愶綇鑰块柡灞炬礃瀵板嫬鈽夐姀鈽嗏偓宥夋⒑缂佹绠栨俊顐㈠暙椤繐煤椤忓嫪绱堕梺闈涱槶閸庢煡鎮楀ú顏呪拺闁告稑顭▓鏇炩攽閻愯宸ラ柣锝囧厴閹粌螣閼测晝妲囬梻浣规偠閸庢粓宕橀妸褋鍋栭梻鍌氬€风欢姘焽瑜庨〃銉ㄧ疀閺囩噥娼熼梺鍝勬储閸ㄥ綊宕掗妸銉冨綊鎮╁顔煎壉闂佹娊鏀遍崹鍧楀箖瀹勬壋鏋庨煫鍥ㄦ惄娴犺偐绱撴担鎻掍壕闂佸憡鍔忛弲婵堢不閹岀唵閻犺桨璀﹂崕鎴炵箾閹绘帩鍤熼柍褜鍓氶鏍窗濮樺灈鈧箓宕奸妷銉у弨婵犮垼鍩栭崝鏇綖閸涘瓨鐓ユ繛鎴灻鈺傤殽閻愭潙濮嶆慨濠呮閹风娀鎳犻鍌ゅ敽闂備胶顭堥鍥窗閺嵮呮殾闁硅揪绠戠粻濠氭煕閹捐尪鍏岄柣鎺戝悑缁绘繈濮€閿濆棛銆愰梺娲诲墲閸嬫劖绔熼弴鐔洪檮闁告稑锕﹂崢钘夆攽閳藉棗鐏ユ繛鍜冪秮閺佸秴顓兼径瀣帾闂佺硶鍓濋敋婵炴惌鍣ｉ弻锛勪沪閻ｅ睗銉︺亜瑜岀欢姘跺蓟濞戙垹绠婚柛妤冨仜椤绱撴担鎴掑惈闁稿鍊曢悾鐑藉础閻愬秶鍠撻崰濠冩綇閵娾晜鏆呴梻鍌氬€烽懗鍓佸垝椤栫偛绀夐柡鍥ュ灩缁犺銇勯幇鈺佲偓妤呮儗閸℃鐔嗛柤鎼佹涧婵牓鏌ｉ幘瀵告噮缂佽鲸鎸婚幏鍛存濞戞矮鎮ｉ梻浣告惈椤戝洭宕伴弽顓炶摕鐎广儱顦伴悡銉╂倵閿濆簼绨藉ù鐘哄亹缁辨挻鎷呮禒瀣懙闂佸湱顭堥…鐑界嵁韫囨稑宸濇い鏃囨瀵潡姊虹憴鍕剹闁告娅ｉ懞閬嶆偩瀹€鈧壕鍏笺亜閺囩偞鍣圭€殿噮鍠楅〃銉╂倷閺夋垶璇炲Δ鐘靛仜椤戝銆佸鈧幃鈺呭矗婢跺鐦庨梻鍌欐祰瀹曠敻宕伴幇顔煎灊鐎光偓閳ь剟骞戦姀鐘栫喐绗熼姘吙闂備礁鎼悮顐﹀磿濞差亜鏋侀柛鎰靛枟閻撳啴鏌ょ粙璺ㄤ粵闁诲浚鍠氱槐鎾愁吋閸曨厾鐛㈤梺缁樹緱閸ｏ絽鐣烽崼鏇ㄦ晢闁逞屽墰閻ヮ亣顦归柟顔款潐缁楃喖顢涘鍐ㄥ紬闂備礁鎽滈崑鐔煎磿閵堝洦宕叉繝闈涙－閸ゆ姊洪崹顕呭剱缂佽埖鐓￠弻宥堫檨闁告挻宀稿畷婵嗏枎韫囨洘娈鹃梺纭呮彧缁犳垹绮婚懡銈囩＝濞达綀顕栭悞浠嬫煕濡湱鐭欐慨濠冩そ瀹曠兘顢橀悙鎻掝瀱闂備焦鎮堕崝蹇涳綖婢跺瞼鐭夐柟鐑樺灍濡插牓鏌曡箛銉х？闁告﹩浜娲箹閻愭彃濡ч梺鍛婂姇瑜扮偟妲愰弮鈧穱濠囧Χ閸ヮ灝銉╂煕鐎ｎ剙浠ч柡渚囧櫍閺佹捇鎮╅棃娑氥偊闂佽鍑界紞鍡樼閺嶎厼缁╁ù鐘差儐閻撴洟鏌熼幍铏珔濠碘€虫惈椤法鎲撮崟顒傤槹濠殿喖锕ら幖顐ｆ櫏闂佹悶鍎滈崨顒傜？濠碉紕鍋戦崐銈夊储婵傚憡鍋嬮煫鍥ㄦ⒒閻濆爼鏌￠崶鈺佹灁缂佲檧鍋撴繝娈垮枟閿曗晠宕㈡ィ鍐ㄥ偍闁汇垹鎲￠埛鎴︽煕濞戞﹫鏀婚悗鍨懇閺屾稑鈽夐崡鐐茬濡炪倖鍔曢妶绋款潖缂佹ɑ濯撮柛娑橈攻閸庢捇姊洪悷鏉挎毐缂佺粯锕㈤獮鍐晸閻樿櫕娅㈤梺缁橈耿濞佳呯矈閿曞倹鈷戦柛婵嗗閳诲鏌涚€ｎ亜顏€规洜鎳撻埞鎴犫偓锝庡亞閸橀亶妫呴銏″闁瑰皷鏅涘嵄缂佸锛曢悷閭︾叆闁割偁鍨婚弳顐︽⒑閸濆嫯顫﹂柛濠冾殜瀹曠増绻濋崶褏顢呴梺缁樺姇濡﹤顭囨禒瀣拺閻犲洦鐓￠妤呮煟韫囨梹鐨戦柛鎺撳笒椤撳ジ宕堕妸銉ョ哎婵犵數濞€濞佳囶敄閸℃稑纾婚柕濞炬櫆閳锋帡鏌涢銈呮灁闁崇鍎崇槐鎺楊敊閼测晛鐓熷┑顔硷龚濞咃綁鍩€椤掑倹顏熼梻鍕椤灝螣鐠佸磭绠氶梺绉嗗嫷娈曢柣鎾存礋閺岀喖鎮滃Ο璇茬缂備焦顨嗙敮妤呭Φ閸曨垰绠婚柣鎰娴狀噣鎮楃憴鍕闁稿骸銈歌棟闁哄被鍎查悡鐔肩叓閸ャ劍鎯堥棅顒夊墯閹便劍绻濋崨顕呬哗闂佺懓寮堕幐鍐茬暦閻旂⒈鏁冮柕鍫濆琚ㄦ繝纰夌磿閸嬫垿宕愰弽褉鏋栭柨鏇炲€搁悙濠囨煏婵炲灝鍔ょ憸鏉挎嚇濮婄粯鎷呴搹鐟扮闂佸搫琚崝鎴濈暦閵壯€鍋撻敐搴℃灍闁稿鍊块弻娑㈠箛闂堟稒鐏嶉梺缁樻尭缁绘劙鈥﹂崸妤佸殝闁汇垻鍋ｉ埀顒佸浮閺岋綁鏁愰崨顓″煘濡炪値鍙€閸庡篓娓氣偓閺屾盯濡搁妷褍鐓熼悗娈垮枦椤曆囧煡婢跺娼ㄩ柛鈩兠崗濠冧繆閻愵亜鈧牜鏁幒妞濆洭顢涢悙鑼槶闂佸壊鍋呭ú姗€鍩涢幋锔界厱婵犻潧妫楅顏呯節閳ь剚瀵肩€涙鍘介梺鍐叉惈閿曘倝鎮橀垾鍩庡酣宕惰闊剟鏌熼鐣岀煉闁瑰磭鍋ゆ俊鐑芥晜閻ｅ苯绀侀梻鍌氬€风粈渚€骞夐埄鍐懝婵°倕鎳庨惌妤呭箹濞ｎ剙濡介柛濠勬暬閹鈽夊▎妯煎姺缂備讲妾ч崑鎾寸節閻㈤潧浠滈柣妤€妫濋幃妯衡攽閸″繑鐎哄┑鐐叉閸ㄥ湱澹曟總鍛婄厾缁炬澘宕晶顖涚箾閸涱喗绀嬮柡灞界Х椤т線鏌涢幘鏉戝摵鐎规洘鐟ㄩ妵鎰板箳閹寸姷宕舵繝娈垮枟閿曗晠宕曢悽鍛婃櫜濠㈣泛锕﹂悿鈧梻浣告惈椤﹀啿鈻旈弴銏╂晩闁圭儤顨嗛悡鐔煎箹濞ｎ剙鐏柍顖涙礋閺屻劌顫濋婊€绨婚柟鍏肩暘閸ㄥ搫鐣风仦鐐弿濠电姴鍟妵婵堚偓瑙勬磸閸斿秶鎹㈠┑瀣闁靛ň鏅濋埀顒佹そ閺岋綁鎮㈤崫銉х厑濠电姰鍨洪敃銏犵暦椤栫偛绾ч柟瀵稿У濞堟儳鈹戦悩璇у伐闁绘妫涢惀顏囶槼缂佺粯鐩畷鍗炩枎韫囧骸顥氶梺璇查叄濞佳囨儗閸屾凹娼栨繛宸簼椤ュ牊绻涢幋鐐跺妞わ絿鍎ゆ穱濠囧Χ閸ヮ灝锝夋煙椤旂厧鈧潡骞冩导鎼晩缁炬媽椴稿娲⒑闁偛鑻晶瀵糕偓娈垮枦椤曆囧煡婢跺鐓ラ柛娑卞灡濠㈡垿姊绘担鍛婃儓闁稿﹦顭堣灋妞ゆ挾鍠撻々鍙夈亜韫囨挾澧戦柍褜鍏涚粈渚€锝炲┑瀣殝闁割煈鍋呴悵鎶芥⒒娴ｈ櫣銆婇柛鎾寸箞閹柉顦归柟顖欑窔瀹曠厧鈹戦崘鈺傛澑婵＄偑鍊栧褰掑几缂佹鐟规繛鎴欏灪閻撴洘淇婇娑橆嚋妞ゃ儱顦甸弻宥囨嫚閼碱儷褍鈹戦鐟颁壕闂備線娼ч悧鍡椢涘畝鍕闁惧繐婀辩壕浠嬫煕鐏炲墽鎳呴柛鏂跨Ч閺屾洟宕卞▎蹇庢濡ょ姷鍋涢澶愬极閹版澘绀冪憸搴ㄦ儓韫囨稒鈷掑ù锝呮啞閸熺偤鏌涢弮鎾绘缂佸倸绉归幃娆撴倻濡桨铏庣紓鍌氬€烽悞锕傗€﹂崶鈺冧笉濡わ絽鍟悡娆撴倵閻㈡鐒鹃柛鎾冲暱閵嗘帒顫濋鐘缂備胶绮惄顖炵嵁濮椻偓楠炲洦鎷呴崫鍕€梻鍌欑閹碱偊寮甸鈧叅闁绘棃顥撻弳锕傛煟閹惧磭宀搁柡鈧禒瀣厱闁靛鍔岄悡鎰版煥濞戞绐旀慨濠勭帛閹峰懘鎼归悷鎵偧闂備礁婀遍…鍫ユ偉閻撳海鏆﹂柡鍥ュ灪閻掕偐鈧箍鍎扮拋鏌ュ磻閹剧粯鏅濋柛灞剧☉濞堟垵顪冮妶鍡欏缂佽尙鍋撻〃娆戠磽閸屾艾鈧悂宕愭搴㈩偨闁跨喓濮寸粣妤佷繆閵堝懏鍣洪柛瀣剁節閺屽秹宕崟顒€娅ч悗瑙勬尫缁舵岸寮诲☉銏犵疀闂傚牊绋掗悘鍫ユ⒑缂佹ê绗氭俊鐐扮矙瀵鈽夐姀鐘电杸闂佸疇妗ㄧ粈渚€鈥栫€ｎ剛纾藉ù锝囨嚀婵鏌涚€ｎ剙浠ч柟骞垮灩閳规垹鈧綆鍋勬禒娲⒒閸屾氨澧涚紒瀣姍楠炲鎮滈懞銉㈡嫼缂備礁顑嗛娆撳磿閹扮増鐓欓柣鐔哄閹兼劖銇勯弴顏嗙М妞ゃ垺顨婂畷鐔碱敃閵堝懎袝濠碉紕鍋戦崐鏍暜婵犲偆娓婚柟鐑樻⒐瀹曟煡鏌涢埄鍐姇闁绘挾鍠栭弻鐔煎级閸喗鍊庣紓浣靛妼椤兘寮诲鍫闂佸憡鎸诲畝鎼佸箖瑜斿鎾綖椤斿墽鈼ら梻濠庡亜濞诧箑顫忛懡銈呭К闁逞屽墮閳规垿顢欓弬銈勭返闂佺娴烽弫缁樹繆閻戣棄鐓涢柛灞惧焹閸嬫捇鎮介崨濠勫弳濠电娀娼уΛ婵嬵敁濡ゅ懏鐓冪憸婊堝礈濞戞瑦鍙忛柟缁㈠枛閻撯€愁熆鐠轰警鐓繛绗哄姂閺屾盯鍩勯崗鐙€浜畷妤佺節閸愌呯畾闂佺粯鍔︽禍婊堝焵椤掍胶澧垫鐐村姍閹瑩顢楁担绋夸紟闂備胶绮崹褰掓偤閺冨牆鏋佸┑鐘叉处閻撴盯鏌涢妷锝呭姎闁诲浚浜弻宥夋煥鐎ｎ亞浼岄梺鍝勬湰缁嬫垿鍩ユ径濠庢建闁割偅绻傞～鐘绘⒒娴ｈ銇熼柛妯恒偢閺佸啴顢旈崼婵婃憰濠电偞鍨崹娲磻閸曨厾纾奸悗锝庡亽閸庛儲淇婇銏☆棤缂佽鲸鎸荤粭鐔煎炊瑜庨悘宥呪攽閳藉棗浜滈柛鐔告尦婵″瓨鎷呯化鏇熺€婚梺瑙勫劤椤曨參宕ｉ崱妤婃富闁靛牆妫涙晶顒勬煟椤撗冩灍缂佹梻鍠栧鎾閿涘嫬骞楅梻浣虹帛閺屻劑骞楀鍫濈疇闁告劦鍠楅崐鍨叏濡厧甯舵鐐搭焽缁辨帡顢欓懖鈺佲叺閻庤娲滈崢褔鍩為幋锕€绀冮柍鍝勫€瑰鎴︽⒒閸屾瑨鍏岀紒顕呭灦瀹曟繆绠涘☉妯兼煣濠电偞鍨堕悷褔銆呴幓鎹ㄦ棃鏁愰崨顓熸闂佺粯鎸鹃崰鏍嵁閺嶎灔搴敆閳ь剟鎮炲ú顏呯厱闁绘棃顥撶粻濠氭煛鐏炲墽娲撮柟顔规櫊閹煎綊顢曢妶搴⑿ら梻鍌欑閹诧紕鏁Δ鍐╂殰闁圭儤顨呴悡婵嬪箹濞ｎ剙濡肩紒鐘差煼閺岀喖宕楅崫銉М婵炲瓨绮嶉崕鎶芥箒闂佹寧绻傞幊搴ㄋ夎箛鏃傜缂佹鎼慨鍌炴煛鐏炵晫啸妞ぱ傜窔閺屾盯骞樼捄鐑樼亪闂佺粯渚楅崳锝呯暦濮椻偓婵℃悂濮€閿涘嫧鍋撴繝姘拺閻熸瑥瀚崝銈囩棯缂併垹寮い銏＄懃閻ｆ繈宕熼鑺ュ闂備線娼荤€靛矂宕㈡總绋跨閻庯綆鍠楅悡娑氣偓鍏夊亾閻庯綆鍓涜ⅵ闁诲氦顫夊ú姗€宕归崹顕呭殨濞寸姴顑愰弫鍥煟閺傚灝绾ч悗姘矙濮婄粯鎷呮笟顖滃姼闂佸搫鐗滈崜鐔煎箠濠靛洢鍋呴柛鎰╁妿閸旓箑顪冮妶鍡楃瑨闁稿﹥鎮傚畷銏ゅ箻椤旂晫鍘甸梺鎯ф禋閸嬫帒鈻撳鍛亾鐟欏嫭绀冪紒顔肩Ч楠炲繘宕ㄩ婊呯厯闁荤姵浜介崝瀣窗婵犲倵鏀介柨娑樺娴滃ジ鏌涙繝鍐⒌闁诡啫鍐剧叆闁割偅绻勯敍娑樷攽閻愭潙鐏︽慨妯稿姂閹矂宕卞Δ濠勫數闂佸吋鎮傚褎鎱ㄩ崼銉︾厽闁规儳顕妴鎺楁煃瑜滈崜娆戠不瀹ュ纾块弶鍫厛濞堜粙鏌涘☉姗堝姛妞も晜鐓￠弻锝夊箛椤旂厧濡洪梺缁樻尵閸犳劙濡甸崟顖氱閻犻缚妗ㄩ幋閿嬬節閳封偓瀹ュ洨鏆ら梺鍝勫閳ь剙纾弳鍡涙煃瑜滈崜鐔风暦娴兼潙绠婚柤鍛婎問濞肩喖姊洪崷顓炲妺妞ゃ劌鎳橀敐鐐哄川鐎涙鍘藉┑鈽嗗灥濞咃綁鏁嶅鍚ょ懓顭ㄦ惔婵堟晼缂備浇椴搁幑鍥х暦閹烘垟鏋庨柟鐑樺灥鐢垰鈹戦悩鎰佸晱闁搞劌銈稿畷锝夊礃閵娧勬閻熸粎澧楃敮鎺楁煁閸ヮ剚鐓涢柛銉㈡櫅娴犫晠鏌熼懞銉︾婵﹥妞藉畷顐﹀礋椤掆偓閸嬪秹姊洪幖鐐插缂傚秴锕鑽も偓锝庡枛閻愬﹪鏌曟繛褍鎳愬Σ鍥⒒閸屾艾鈧悂鎮ф繝鍕煓闁圭儤姊婚惌鍫ユ煃閸濆嫬鈧鎹㈤崱妯镐簻闁规澘澧庨幃濂告煏閸℃ê绗х紒杈ㄥ笚濞煎繘濡搁敃鈧棄宥夋⒑閻熸澘妲婚柟铏姉閸掓帡鎮界喊妯轰壕闁挎繂绨肩花鍏笺亜閺傚灝顏紒缁樼箘閸犲﹤螣濞茬粯缍夐梻浣侯焾椤戝懘骞婇幇鏉跨闁靛繈鍊曢柋鍥ㄧ節閸偄濮囨繛鍫涘妽缁绘繈鎮介棃娴讹絿鐥弶璺ㄐх€殿喗鐓￠獮鎾诲箳濠靛牆鏁搁梺鑽ゅЬ濞咃絿浜搁妸鈺佺闁绘柨鍚嬮悡鐔兼煏閸繂鈧憡绂嶆ィ鍐┾拻闁稿本鑹鹃埀顒傚厴閹虫宕奸弴妞诲亾閿曞倸閱囬柕蹇娾偓鍐茬哎闂備礁婀辨晶妤€顭垮Ο鑲╀笉闁绘垶顭囩弧鈧繝鐢靛Т閸婄粯鏅跺☉銏＄厱閹艰揪绻濋崣鍕殽閻愬澧懣鎰亜閹哄棗浜炬繝纰樷偓鑼煓闁哄苯绉归弻銊р偓锝庝簽娴煎苯鈹戦纭锋敾婵＄偘绮欓妴渚€寮撮姀鐘栄囨煕濞戝崬澧伴柟瑙勬礋濮婄粯鎷呯粙鎸庡€┑锛勫仜濞尖€崇暦瑜版帗鐒肩€广儱鎳愰弻褔姊洪崜鎻掍簼婵炶绠撳顐㈩吋婢跺鍘介梺褰掑亰閸擄箓宕甸悢鍏肩厱婵せ鍋撳ù婊嗘硾椤繐煤椤忓拋妫冨┑鐐村灱娴滎剟宕濋幖浣光拺缂佸瀵ч崬澶嬬箾閸涱喗绀堢紒顔碱儔楠炴帒螖閳ь剟锝為崨瀛樼厪闁割偅绻冮ˉ鐘绘煕濡湱鐭欐慨濠冩そ楠炴劖鎯旈敐鍥╂殼闂備胶鎳撻崯鎸庮殽缁嬪灝绁┑鐘垫暩婵數鍠婂澶嬪亗闁告劦鍠楅崑鈩冪箾閸℃绠版い蹇婃櫊閺岋綀绠涢幙鍐ㄦ闂侀€炲苯澧叉い顐㈩槸鐓ら柡宓懏娈惧銈嗗笒鐎氼剟鎮″┑瀣婵烇綆鍓欐俊鑲╃磼閻樺磭鈯曢柕鍥у楠炴鎹勯惄鎺炵稻缁绘盯宕奸妷褏鏆┑顔硷工椤嘲鐣烽幒鎴僵妞ゆ垼妫勬禍鎯ь渻鐎ｎ亝鎹ｉ柣顓炴閵嗘帒顫濋敐鍛婵°倗濮烽崑娑⑺囬悽绋垮瀭闁告挆鍕劚婵炶揪缍€椤濡靛┑瀣厸濞达絿顭堥弳锝団偓瑙勬礃鐢帡锝炲┑鍥舵綑闁哄秲鍓遍妶鍛斀闁绘ê鐏氶弳鈺呮煕鐎ｎ偆鈽夐摶鐐寸箾閸℃ɑ灏紒鈧径瀣╃箚闁靛牆鎳忛崳鐑芥煃瑜滈崜娆撳箠濡警鍤曞┑鐘宠壘鍞梺鎸庢椤鈧艾鍚嬫穱濠囨倷椤忓嫧鍋撻弽褜鍟呭┑鐘宠壘绾惧鏌熼幆褍顣崇痪鍓у帶椤法鎹勯悜妯绘嫳闂佸搫妫崜鐔煎蓟濞戙垹鐓涢柛灞剧矋閸掓稑螖閻橀潧浠滄い鎴濐樀瀵偊宕掗悙鏉戜患闁诲繒鍋犲Λ鍕不閻愮儤鈷掗柛灞剧懅閸斿秹鎮楃粭娑樺悩濞戞瑧鐟归柍褜鍓熼妴浣割潩閹颁焦鈻岄梻浣虹帛娓氭宕抽敐鍜佸殨濞寸姴顑傞埀顒佺墵閺佸秹宕熼鈧▓灞筋渻閵堝骸骞戦柛鏃€鍨甸悾閿嬬附缁嬫娼婇梺闈涚箚閸撴繈鎯侀幒妤佲拻闁稿本鐟чˇ锕傛煙绾板崬浜伴柟顖氭湰瀵板嫬鐣濋埀顒傜矆婵犲倶鈧帒顫濋敐鍛闂備線娼уΛ鏃傛濮橆剦鍤曟い鏇楀亾鐎规洜鍘ч埞鎴﹀醇椤愶及褍鈹戦敍鍕杭闁稿﹥鐗犻獮鎰版倷椤掍礁寮块悗鍏夊亾闁告洦鍊犻埡鍛厓闁告繂瀚埀顒傛暬楠炲繘鎼归崷顓狅紳婵炴挻鑹惧ú銈夋倶閳╁啰绠鹃柛娆忣檧閼拌法鈧鍠楅崕濂稿Φ閹版澘绠抽柟鎹愭硾楠炲牓姊虹拠鎻掑毐缂傚秴妫濆畷鎴﹀川椤栨瑧鍓ㄥ銈嗘尵閸犲棙绂嶅鍕╀簻闊洦鎸搁鈺呮煛閸☆厾鍒伴柍瑙勫灴閸ㄦ儳鐣烽崶褏鍘介柣搴ゎ潐濞叉牕鐣烽鍐簷闂備礁鎲￠崜顒勫川椤栵絾袨婵犵數濮烽弫鍛婃叏閻戣棄鏋侀柟闂寸绾剧粯绻涢幋鏃€鍤嶉柛銉墮缁狙勪繆椤愩垻浠涙俊鐐扮矙楠炲啴濮€閵忋垻锛滃┑顔筋殔濡瑩鏁嶉崼銉︹拻闁稿本鑹鹃埀顒傚厴閹偤鏁傞柨顖氫壕缂佹绋戦崢鎯洪鍕敤濡炪倖鎸鹃崑鐔兼晬濞嗘挻鍋℃繝濠傛噹椤ｅジ鎮介娑辨疁鐎规洘鍨垮畷鎺楁倷鐎电骞堥梻渚€鈧稑宓嗘繛浣冲洤鍑犻柣鏂垮悑閻撴洟鎮楅敐鍛暢缂佸鍣ｉ弻锛勪沪閸撗勫垱婵犵绱曢崗姗€寮幇鏉垮窛妞ゆ挆鍛亾閻戣姤鈷掑ù锝呮啞閸熺偤鏌ｉ悢鏉戠伈鐎规洘鍨块獮妯肩磼濡攱瀚藉┑鐐舵彧缁蹭粙骞夐敓鐘茬柈闁绘劗鍎ら悡鐔兼煟閺冣偓濞兼瑩宕濋妶澶嬬厓鐟滄粓宕滃璺虹鐟滅増甯掗惌妤呮煕閹伴潧鏋涙潻婵嬫⒑閸涘﹤濮﹂柛鐘崇墪閺侇噣姊绘担绋挎毐闁圭⒈鍋婂畷鎰板箹娴ｅ摜鍔﹀銈嗗笒閸婃悂宕㈢€电硶鍋撳▓鍨灈闁绘牜鍘ч悾閿嬬附閸涘﹤浜滄俊鐐差儏鐎垫帒危娴煎瓨鈷掑〒姘ｅ亾闁逞屽墰閸嬫盯鎳熼娑欐珷妞ゆ牜鍋為崐鐢电磼濡や胶鈽夋繛灞傚灲閸┾偓妞ゆ巻鍋撻柛鐔风摠娣囧﹪鎮滈挊澶屽幐闂佺鏈崺鍐磻閹剧粯鍊婚柤鎭掑劤閸樻悂鏌ｈ箛鏇炰哗妞ゆ泦鍥х婵ɑ澧庨崑鎾斥枔閸喗鐏嗛梺鍛婎殔閸熷潡锝炶箛鎾佹椽顢旈崨顓濈敾闂備浇顫夐鏍闯椤栨粍顫曢柣鎰劋閳锋垹绱掔€ｎ偒鍎ラ柛搴＄Ч閺屾稒绻濋崘顏勵潷缂備礁鐭佹ご鍝ユ崲濠靛鐐婇柕濞垮劗閸嬫捇鎳￠妶鍥╋紲濠电偞鍨堕敃鈺呭磿閹扮増鐓涢柛婊€绀佹晶顕€鏌嶇憴鍕伌闁诡喒鏅濋幏鐘侯槻濞村吋鍔曢埞鎴﹀煡閸℃ぞ绨煎銈冨妼閿曨亪鐛崘顔肩伋婵犮垹瀚崰鎾诲箖娴兼潙鐒洪柛鎰ㄦ櫆濠㈡垿姊婚崒娆掑厡缂侇噮鍨堕弫瀣⒑鐠囪尙绠茬€光偓缁嬫鍤曢柟鎯版閻撴稑霉閿濆懎绾ч柡鍌楀亾濠碉紕鍋戦崐鏍ь潖瑜版帒纾块柟鎯版閸屻劑鎮楅悽鐢点€婇柛瀣尭閳绘捇宕归鐣屽蒋闂備胶顭堥鍛存晝閵堝鍋╅柣鎴ｆ鎯熼梺鎸庢煥婢т粙鎯侀崼銉︹拺闁告稑锕ゆ慨鍌炴煕閺傛寧婀伴悗闈涘悑閹棃濡搁敂瑙勫濠电偠鎻紞鈧繛鍜冪悼閺侇喖鈽夐姀锛勫幈闂侀潧臎閸曨剚鐦撻梻浣瑰缁诲嫰宕戦妶鍜佸殨闁圭虎鍠栭～鍛存煟濮椻偓濞佳勬叏閿旀垝绻嗛柣鎰典簻閳ь剚鐗曢蹇旂節濮橆剛锛涢梺鐟板⒔缁垶鎮￠弴鐐╂斀闁绘ɑ褰冮埀顒€婀遍幏褰掓晬閸曨厾锛滃銈嗘⒒閳峰牓宕曢弮鈧〃銉╂倷閺夋垶璇為悗娈垮櫘閸撶喎鐣烽锕€绀嬫い蹇撳濡粓姊婚崒娆戝妽閻庣瑳鍛床闁稿本绮忔慨鎶芥煠濞村娅堝┑顔煎暱闇夐柣妯烘▕閸庢劙鏌ｉ幘瀛樼闁哄矉绻濆畷姗€鍩￠崒姘闂備椒绱紞鈧瀛樻倐婵＄敻宕熼锝嗘櫆闂佸憡娲﹂崑鍛枔缂佹绡€缁剧増菤閸嬫挸鐣烽崶褏鍘介柣搴ゎ潐濞叉牕鐣烽鍐簷闂備礁鎲￠崝锔界濠靛闂い鏍仦閳锋帡鏌涚仦鍓ф噯闁稿繐鏈妵鍕敇閻愰潧鈪甸梺杞扮閸熷瓨淇婇懜闈涚窞濠电姴瀚弸鈧梻鍌欑劍鐎笛呯矙閹寸姭鍋撳鐓庢珝闁诡垰鏈鍕箛椤撶姴骞嶉梻浣虹帛閸ㄥ爼鏁嬪┑鐐茬墱閸犳鎹㈠☉姗嗗晠妞ゆ梻绮崰姘舵⒑鏉炴壆顦﹂柛濠傛健瀵鈽夊Ο閿嬵潔闂佸憡顨堥崑鐔哥閳哄懏鐓熼柕蹇婃櫅閻忊剝銇勯妸銉含鐎殿喖顭烽弫鎰緞鐎ｎ亙绨婚梻浣告啞缁哄潡宕曢幎鑺ュ亗濞达綀娅ｇ壕钘夈€掑顒佹悙闁哄鍠栭弻鐔煎川婵犲啫鈧劙鏌熼钘夊姢闁伙綇绻濋弻鍥晜閹冩辈闂傚倷绀侀幉锟犲礉濡ゅ懎纾婚柟鎹愵嚙绾句粙鏌ｉ姀鐘冲暈闁绘挾鍠栭弻锝呂熼幐搴ｅ涧闂侀潧娲︾换鍕閹捐纾兼繛鎴炪仦鐎涒晛顪冮妶鍐ㄢ偓鏇㈠磹閸噮娼栧┑鐘宠壘绾惧吋绻涢崱妯虹瑨闁告ǚ鈧剚娓婚柕鍫濋楠炴牠鏌ｅΔ浣瑰磳妤犵偛妫欏鍕偓锝庡墴濡绢喚绱撴担鍓插創婵炲娲熼弫宥呪槈閵忊檧鎷虹紓鍌欑劍閿氬┑顔兼喘閺岀喓鍠婇崡鐐板枈闂?the modified color buffer we have to the pipelinebuffer by doing the reverse blit, from destination to source. Later in this tutorial we will
                    // explore some alternatives that we can do to optimize this second blit away and avoid the round trip.
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Color Blit Resolve", out var passData, m_ProfilingSampler))
                    {
                        passData.BlitMaterial = m_BlitMaterial;
                        // Similar to the previous pass, however now we set destination texture as input and source as output.
                        builder.UseTexture(tmpBuffer3A, AccessFlags.Read);
                        passData.src = tmpBuffer3A;
                        builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                        // We use the same BlitTexture API to perform the Blit operation.
                        builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
                    }                
            }

            //temporal
            static void ExecuteBlitPassTEN(PassData data, RasterGraphContext context, int pass,
                TextureHandle tmpBuffer1, TextureHandle tmpBuffer2, TextureHandle tmpBuffer3,
                string varname1, float var1,
                string varname2, float var2,
                string varname3, Matrix4x4 var3,
                string varname4, Matrix4x4 var4,
                string varname5, Matrix4x4 var5,
                string varname6, Matrix4x4 var6,
                string varname7, Matrix4x4 var7
                )
            {
                data.BlitMaterial.SetTexture("_ColorBuffer", tmpBuffer1);
                data.BlitMaterial.SetTexture("_PreviousColor", tmpBuffer2);
                data.BlitMaterial.SetTexture("_PreviousDepth", tmpBuffer3);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
                //lastFrameViewProjectionMatrix = viewProjectionMatrix;
                //lastFrameInverseViewProjectionMatrix = viewProjectionMatrix.inverse;
            }

            static void ExecuteBlitPassTHREE(PassData data, RasterGraphContext context, int pass,
                TextureHandle tmpBuffer1, TextureHandle tmpBuffer2, TextureHandle tmpBuffer3)
            {
                data.BlitMaterial.SetTexture("_ColorBuffer", tmpBuffer1);
                data.BlitMaterial.SetTexture("_PreviousColor", tmpBuffer2);
                data.BlitMaterial.SetTexture("_PreviousDepth", tmpBuffer3);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }
            static void ExecuteBlitPass(PassData data, RasterGraphContext context, int pass, TextureHandle tmpBuffer1aa)
            {
                data.BlitMaterial.SetTexture("_MainTex", tmpBuffer1aa);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }
            static void ExecuteBlitPassTEXNAME(PassData data, RasterGraphContext context, int pass, TextureHandle tmpBuffer1aa, string texname)
            {
                data.BlitMaterial.SetTexture(texname, tmpBuffer1aa);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }
            static void ExecuteBlitPassTEXNAME_TWO(PassData data, RasterGraphContext context, int pass, TextureHandle tmpBuffer1aa, string texname
                , TextureHandle tmpBuffer1aaa, string texnamea)
            {
                data.BlitMaterial.SetTexture(texname, tmpBuffer1aa);
                data.BlitMaterial.SetTexture(texnamea, tmpBuffer1aaa);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }
            static void ExecuteBlitPassTEXNAME_THREE(PassData data, RasterGraphContext context, int pass, TextureHandle tmpBuffer1aa, string texname
                , TextureHandle tmpBuffer1aaa, string texnamea, TextureHandle tmpBuffer1aaaa, string texnameb)
            {
                data.BlitMaterial.SetTexture(texname, tmpBuffer1aa);
                data.BlitMaterial.SetTexture(texnamea, tmpBuffer1aaa);
                data.BlitMaterial.SetTexture(texnameb, tmpBuffer1aaaa);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }
            static void ExecuteBlitPassTEX5NAME(PassData data, RasterGraphContext context, int pass,
                string texname1, TextureHandle tmpBuffer1,
                string texname2, TextureHandle tmpBuffer2, 
                string texname3, TextureHandle tmpBuffer3, 
                string texname4, TextureHandle tmpBuffer4,
                string texname5, TextureHandle tmpBuffer5
                )
            {
                data.BlitMaterial.SetTexture(texname1, tmpBuffer1);
                data.BlitMaterial.SetTexture(texname2, tmpBuffer2);
                data.BlitMaterial.SetTexture(texname3, tmpBuffer3);
                data.BlitMaterial.SetTexture(texname4, tmpBuffer4);
                data.BlitMaterial.SetTexture(texname5, tmpBuffer5);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }

            static void ExecuteSurfaceCacheUpdatePass(PassData data, RasterGraphContext context)
            {
                data.BlitMaterial.SetTexture(CurrentSurfaceCacheDepthId, data.texA);
                data.BlitMaterial.SetTexture(CurrentSurfaceCacheNormalId, data.texB);
                data.BlitMaterial.SetTexture(PrevSurfaceCacheRadianceId, data.texC);
                data.BlitMaterial.SetTexture(PrevSurfaceCacheNormalId, data.texD);
                data.BlitMaterial.SetTexture(PrevSurfaceCacheDepthId, data.texE);
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, 0);
            }

            static void ExecuteSurfaceCacheCopyPass(PassData data, RasterGraphContext context, int pass)
            {
                Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, pass);
            }

            // It is static to avoid using member variables which could cause unintended behaviour.
            static void ExecutePass(PassData data, RasterGraphContext rgContext)
            {
                Blitter.BlitTexture(rgContext.cmd, data.src, new Vector4(1, 1, 0, 0), data.BlitMaterial, 13);
            }
            //private Material m_BlitMaterial;
            private ProfilingSampler m_ProfilingSampler = new ProfilingSampler("After Opaques");
            ////// END GRAPH
#endif





            //v0.1
            //private readonly RenderTargetHandle _occluders = RenderTargetHandle.CameraTarget;
            //private readonly RenderTargetHandle _occluders = RenderTargetHandle.CameraTarget;
            //if (destination == renderingData.cameraData.renderer.cameraColorTargetHandle)//  UnityEngine.Rendering.Universal.RenderTargetHandle.CameraTarget) //v0.1


            private readonly VolumetricLightScatteringSettings _settings;
            private readonly List<ShaderTagId> _shaderTagIdList = new List<ShaderTagId>();
            //private Material _occludersMaterial;
            //private Material _radialBlurMaterial;
            private FilteringSettings _filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            private RenderTargetIdentifier _cameraColorTargetIdent;

                        private RenderTargetIdentifier source;

            public LightScatteringPass(VolumetricLightScatteringSettings settings)
            {
                ///_occluders.Init("_OccludersMap");//v0.1
                _settings = settings;

                _shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                _shaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
                _shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                _shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            }

            void EnsureSurfaceCacheCameraState(Camera camera)
            {
                int cameraId = camera != null ? camera.GetInstanceID() : int.MinValue;
                if (cameraId != _surfaceCacheLastCameraId)
                {
                    _surfaceCacheLastCameraId = cameraId;
                    _surfaceCacheHasHistory = false;
                }
            }

            bool EnsureSurfaceCacheMaterial()
            {
                if (_surfaceCacheMaterial != null)
                {
                    return true;
                }

                Shader surfaceCacheShader = Shader.Find("Hidden/LumenLike/SurfaceCache");
                if (surfaceCacheShader == null)
                {
                    return false;
                }

                _surfaceCacheMaterial = CoreUtils.CreateEngineMaterial(surfaceCacheShader);
                return _surfaceCacheMaterial != null;
            }

            bool EnsureSurfaceCacheHistoryTextures(int width, int height)
            {
                bool recreated = false;
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheRadianceHistory[0], width, height, GraphicsFormat.R16G16B16A16_SFloat, "_LumenLikeSurfaceCacheRadianceA", FilterMode.Bilinear);
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheRadianceHistory[1], width, height, GraphicsFormat.R16G16B16A16_SFloat, "_LumenLikeSurfaceCacheRadianceB", FilterMode.Bilinear);
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheNormalHistory[0], width, height, GraphicsFormat.R16G16B16A16_SFloat, "_LumenLikeSurfaceCacheNormalA", FilterMode.Point);
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheNormalHistory[1], width, height, GraphicsFormat.R16G16B16A16_SFloat, "_LumenLikeSurfaceCacheNormalB", FilterMode.Point);
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheDepthHistory[0], width, height, GraphicsFormat.R32_SFloat, "_LumenLikeSurfaceCacheDepthA", FilterMode.Point);
                recreated |= EnsureSurfaceCacheHistoryTexture(ref _surfaceCacheDepthHistory[1], width, height, GraphicsFormat.R32_SFloat, "_LumenLikeSurfaceCacheDepthB", FilterMode.Point);

                if (recreated)
                {
                    _surfaceCacheHasHistory = false;
                }

                return recreated;
            }

            static bool EnsureSurfaceCacheHistoryTexture(ref RTHandle handle, int width, int height, GraphicsFormat format, string name, FilterMode filterMode)
            {
                if (handle != null && handle.rt != null && handle.rt.width == width && handle.rt.height == height && handle.rt.graphicsFormat == format)
                {
                    return false;
                }

                handle?.Release();
                handle = RTHandles.Alloc(width, height, colorFormat: format, dimension: TextureDimension.Tex2D, name: name);
                handle.rt.wrapMode = TextureWrapMode.Clamp;
                handle.rt.filterMode = filterMode;
                return true;
            }

            void PrepareSurfaceCacheMaterial(Camera camera)
            {
                Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
                // Surface-cache reprojection samples camera depth textures, not a manually flipped RT path.
                // Using the RT-flipped projection here mirrors reprojection vertically and collapses confidence
                // to a thin horizontal band near screen center.
                Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
                Matrix4x4 currentViewProjectionMatrix = projectionMatrix * viewMatrix;
                Matrix4x4 currentInvViewProjectionMatrix = currentViewProjectionMatrix.inverse;
                SurfaceCacheMaterialState nextState = new SurfaceCacheMaterialState
                {
                    TemporalBlend = _settings.surfaceCacheTemporalBlend,
                    NormalReject = _settings.surfaceCacheNormalReject,
                    DepthReject = _settings.surfaceCacheDepthReject,
                    HasHistory = _surfaceCacheHasHistory,
                    DebugExposure = _settings.surfaceCacheDebugExposure,
                    DebugMode = _settings.surfaceCacheDebugView
                };

                _surfaceCacheMaterial.SetMatrix(CurrentInvViewProjectionMatrixId, currentInvViewProjectionMatrix);
                _surfaceCacheMaterial.SetMatrix(PrevViewProjectionMatrixId, _surfaceCachePrevViewProjectionMatrix);
                _surfaceCacheMaterial.SetMatrix(PrevInvViewProjectionMatrixId, _surfaceCachePrevInvViewProjectionMatrix);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.TemporalBlend != nextState.TemporalBlend) _surfaceCacheMaterial.SetFloat(SurfaceCacheTemporalBlendId, nextState.TemporalBlend);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.NormalReject != nextState.NormalReject) _surfaceCacheMaterial.SetFloat(SurfaceCacheNormalRejectId, nextState.NormalReject);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.DepthReject != nextState.DepthReject) _surfaceCacheMaterial.SetFloat(SurfaceCacheDepthRejectId, nextState.DepthReject);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.HasHistory != nextState.HasHistory) _surfaceCacheMaterial.SetFloat(SurfaceCacheHasHistoryId, nextState.HasHistory ? 1.0f : 0.0f);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.DebugExposure != nextState.DebugExposure) _surfaceCacheMaterial.SetFloat(SurfaceCacheDebugExposureId, nextState.DebugExposure);
                if (!_surfaceCacheMaterialStateValid || _surfaceCacheMaterialState.DebugMode != nextState.DebugMode) _surfaceCacheMaterial.SetFloat(SurfaceCacheDebugModeId, (float)nextState.DebugMode);

                _surfaceCacheMaterialState = nextState;
                _surfaceCacheMaterialStateValid = true;
                _surfaceCachePrevViewProjectionMatrix = currentViewProjectionMatrix;
                _surfaceCachePrevInvViewProjectionMatrix = currentInvViewProjectionMatrix;
            }
            bool SurfaceCacheEnabled()
            {
                return _settings.enableSurfaceCache;
            }

            bool SurfaceCacheDebugEnabled()
            {
                return _settings.enableSurfaceCache && _settings.surfaceCacheDebugView != SurfaceCacheDebugView.None;
            }

            static RenderTexture EnsureScratchTexture(ref RenderTexture texture, int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite, FilterMode filterMode, string name)
            {
                bool needsRecreate = texture == null
                    || texture.width != width
                    || texture.height != height
                    || texture.depth != depth
                    || texture.format != format;

                if (needsRecreate)
                {
                    ReleaseScratchTexture(ref texture);
                    texture = new RenderTexture(width, height, depth, format, readWrite)
                    {
                        name = name,
                        filterMode = filterMode,
                        wrapMode = TextureWrapMode.Clamp,
                        useMipMap = false,
                        autoGenerateMips = false,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    texture.Create();
                    return texture;
                }

                texture.filterMode = filterMode;
                texture.wrapMode = TextureWrapMode.Clamp;
                if (!texture.IsCreated())
                {
                    texture.Create();
                }

                return texture;
            }

            static void ReleaseScratchTexture(ref RenderTexture texture)
            {
                if (texture == null)
                {
                    return;
                }

                texture.Release();
                CoreUtils.Destroy(texture);
                texture = null;
            }

            void ReleaseScratchTextures()
            {
                ReleaseScratchTexture(ref _scratchFrameSource);
                ReleaseScratchTexture(ref _scratchFrameResult);
                ReleaseScratchTexture(ref _scratchGiTraceA);
                ReleaseScratchTexture(ref _scratchGiTraceB);
                ReleaseScratchTexture(ref _scratchGiUpsampleA);
                ReleaseScratchTexture(ref _scratchGiUpsampleB);
                ReleaseScratchTexture(ref _scratchReflections);
                ReleaseScratchTexture(ref _scratchCurrentDepth);
                ReleaseScratchTexture(ref _scratchCurrentNormal);
            }

            public void ReleaseSurfaceCacheResources()
            {
                ReleaseScratchTextures();
                ReleaseSurfaceCacheHistoryArray(_surfaceCacheRadianceHistory);
                ReleaseSurfaceCacheHistoryArray(_surfaceCacheNormalHistory);
                ReleaseSurfaceCacheHistoryArray(_surfaceCacheDepthHistory);
                _surfaceCacheHasHistory = false;
                _surfaceCacheLastCameraId = int.MinValue;
                _surfaceCacheMaterialStateValid = false;
                _sharedSegiMaterialStateValid = false;
                _sharedSegiMaterialStateOwner = null;
                _sharedSegiMaterialStateMaterial = null;
                _cachedMainCamera = null;
                _cachedMainSegi = null;
                _lastVisibleLightsCount = int.MinValue;
                CoreUtils.Destroy(_surfaceCacheMaterial);
                _surfaceCacheMaterial = null;
            }

            static void ReleaseSurfaceCacheHistoryArray(RTHandle[] handles)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i]?.Release();
                    handles[i] = null;
                }
            }

            bool TryGetMainSegi(out Camera mainCamera, out LumenLike segi)
            {
                mainCamera = _cachedMainCamera;
                if (mainCamera == null || !mainCamera.isActiveAndEnabled || !mainCamera.CompareTag("MainCamera"))
                {
                    mainCamera = Camera.main;
                    _cachedMainCamera = mainCamera;
                    _cachedMainSegi = null;
                }

                if (mainCamera == null)
                {
                    segi = null;
                    return false;
                }

                if (_cachedMainSegi == null || _cachedMainSegi.gameObject != mainCamera.gameObject)
                {
                    _cachedMainSegi = mainCamera.GetComponent<LumenLike>();
                }

                segi = _cachedMainSegi;
                return segi != null;
            }

            static Vector4 GetClampedDitherControl(LumenLike segi)
            {
                return new Vector4(
                    segi.DitherControl.x,
                    Mathf.Clamp(segi.DitherControl.y, 0.1f, 10.0f),
                    Mathf.Clamp(segi.DitherControl.z, 0.1f, 10.0f),
                    Mathf.Clamp(segi.DitherControl.w, 0.1f, 10.0f));
            }

            void SetVisibleLightsCount(int visibleLightsCount)
            {
                if (_lastVisibleLightsCount == visibleLightsCount)
                {
                    return;
                }

                Shader.SetGlobalInt(VisibleLightsCountId, visibleLightsCount);
                _lastVisibleLightsCount = visibleLightsCount;
            }

            static bool EnsureSegiMaterial(LumenLike segi)
            {
                if (!segi.material)
                {
                    segi.material = new Material(Shader.Find("Hidden/SEGI"));
                }

                return segi.material != null;
            }

            void ApplySharedSegiMaterialState(LumenLike segi, bool useSurfaceCache)
            {
                if (_sharedSegiMaterialStateOwner != segi)
                {
                    _sharedSegiMaterialStateOwner = segi;
                    _sharedSegiMaterialStateMaterial = segi.material;
                    _sharedSegiMaterialStateValid = false;
                }
                else if (_sharedSegiMaterialStateMaterial != segi.material)
                {
                    _sharedSegiMaterialStateMaterial = segi.material;
                    _sharedSegiMaterialStateValid = false;
                }

                SharedSegiMaterialState nextState = new SharedSegiMaterialState
                {
                    VoxelScaleFactor = segi.voxelScaleFactor,
                    StochasticSampling = segi.stochasticSampling,
                    TraceDirections = segi.cones,
                    TraceSteps = segi.coneTraceSteps,
                    TraceLength = segi.coneLength,
                    ConeSize = segi.coneWidth,
                    OcclusionStrength = segi.occlusionStrength,
                    OcclusionPower = segi.occlusionPower,
                    ConeTraceBias = segi.coneTraceBias,
                    GIGain = segi.giGain,
                    NearLightGain = segi.nearLightGain,
                    NearOcclusionStrength = segi.nearOcclusionStrength,
                    DoReflections = segi.doReflections,
                    HalfResolution = segi.halfResolution,
                    ReflectionSteps = segi.reflectionSteps,
                    ReflectionOcclusionPower = segi.reflectionOcclusionPower,
                    SkyReflectionIntensity = segi.skyReflectionIntensity,
                    FarOcclusionStrength = segi.farOcclusionStrength,
                    FarthestOcclusionStrength = segi.farthestOcclusionStrength,
                    BlendWeight = segi.temporalBlendWeight,
                    ContrastA = segi.contrastA,
                    ReflectControl = segi.ReflectControl,
                    DitherControl = GetClampedDitherControl(segi),
                    SmoothNormals = segi.smoothNormals,
                    UseSurfaceCache = useSurfaceCache,
                    SurfaceCacheGIBlend = _settings.surfaceCacheGIBlend,
                    SurfaceCacheReflectionBlend = _settings.surfaceCacheReflectionBlend,
                    SurfaceCacheMinConfidence = _settings.surfaceCacheMinConfidence
                };

                segi.material.SetMatrix(CameraToWorldId, segi.attachedCamera.cameraToWorldMatrix);
                segi.material.SetMatrix(WorldToCameraId, segi.attachedCamera.worldToCameraMatrix);
                segi.material.SetMatrix(ProjectionMatrixInverseId, segi.attachedCamera.projectionMatrix.inverse);
                segi.material.SetMatrix(ProjectionMatrixId, segi.attachedCamera.projectionMatrix);
                segi.material.SetInt(FrameSwitchId, segi.frameCounter);
                Shader.SetGlobalInt(SegiFrameSwitchId, segi.frameCounter);
                segi.material.SetVector(CameraPositionId, segi.transform.position);
                segi.material.SetFloat(DeltaTimeId, Time.deltaTime);
                segi.material.SetTexture(NoiseTextureId, segi.blueNoise[segi.frameCounter % 64]);

                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.VoxelScaleFactor != nextState.VoxelScaleFactor) Shader.SetGlobalFloat(SegiVoxelScaleFactorId, nextState.VoxelScaleFactor);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.StochasticSampling != nextState.StochasticSampling) segi.material.SetInt(StochasticSamplingId, nextState.StochasticSampling ? 1 : 0);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.TraceDirections != nextState.TraceDirections) segi.material.SetInt(TraceDirectionsId, nextState.TraceDirections);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.TraceSteps != nextState.TraceSteps) segi.material.SetInt(TraceStepsId, nextState.TraceSteps);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.TraceLength != nextState.TraceLength) segi.material.SetFloat(TraceLengthId, nextState.TraceLength);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ConeSize != nextState.ConeSize) segi.material.SetFloat(ConeSizeId, nextState.ConeSize);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.OcclusionStrength != nextState.OcclusionStrength) segi.material.SetFloat(OcclusionStrengthId, nextState.OcclusionStrength);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.OcclusionPower != nextState.OcclusionPower) segi.material.SetFloat(OcclusionPowerId, nextState.OcclusionPower);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ConeTraceBias != nextState.ConeTraceBias) segi.material.SetFloat(ConeTraceBiasId, nextState.ConeTraceBias);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.GIGain != nextState.GIGain) segi.material.SetFloat(GIGainId, nextState.GIGain);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.NearLightGain != nextState.NearLightGain) segi.material.SetFloat(NearLightGainId, nextState.NearLightGain);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.NearOcclusionStrength != nextState.NearOcclusionStrength) segi.material.SetFloat(NearOcclusionStrengthId, nextState.NearOcclusionStrength);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.DoReflections != nextState.DoReflections) segi.material.SetInt(DoReflectionsId, nextState.DoReflections ? 1 : 0);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.HalfResolution != nextState.HalfResolution) segi.material.SetInt(HalfResolutionId, nextState.HalfResolution ? 1 : 0);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ReflectionSteps != nextState.ReflectionSteps) segi.material.SetInt(ReflectionStepsId, nextState.ReflectionSteps);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ReflectionOcclusionPower != nextState.ReflectionOcclusionPower) segi.material.SetFloat(ReflectionOcclusionPowerId, nextState.ReflectionOcclusionPower);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.SkyReflectionIntensity != nextState.SkyReflectionIntensity) segi.material.SetFloat(SkyReflectionIntensityId, nextState.SkyReflectionIntensity);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.FarOcclusionStrength != nextState.FarOcclusionStrength) segi.material.SetFloat(FarOcclusionStrengthId, nextState.FarOcclusionStrength);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.FarthestOcclusionStrength != nextState.FarthestOcclusionStrength) segi.material.SetFloat(FarthestOcclusionStrengthId, nextState.FarthestOcclusionStrength);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.BlendWeight != nextState.BlendWeight) segi.material.SetFloat(BlendWeightId, nextState.BlendWeight);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ContrastA != nextState.ContrastA) segi.material.SetFloat(ContrastAId, nextState.ContrastA);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.ReflectControl != nextState.ReflectControl) segi.material.SetVector(ReflectControlId, nextState.ReflectControl);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.DitherControl != nextState.DitherControl) segi.material.SetVector(DitherControlId, nextState.DitherControl);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.SmoothNormals != nextState.SmoothNormals) segi.material.SetFloat(SmoothNormalsId, nextState.SmoothNormals);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.UseSurfaceCache != nextState.UseSurfaceCache) segi.material.SetInt(UseSurfaceCacheId, nextState.UseSurfaceCache ? 1 : 0);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.SurfaceCacheGIBlend != nextState.SurfaceCacheGIBlend) segi.material.SetFloat(SurfaceCacheGIBlendSettingId, nextState.SurfaceCacheGIBlend);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.SurfaceCacheReflectionBlend != nextState.SurfaceCacheReflectionBlend) segi.material.SetFloat(SurfaceCacheReflectionBlendSettingId, nextState.SurfaceCacheReflectionBlend);
                if (!_sharedSegiMaterialStateValid || _sharedSegiMaterialState.SurfaceCacheMinConfidence != nextState.SurfaceCacheMinConfidence) segi.material.SetFloat(SurfaceCacheMinConfidenceId, nextState.SurfaceCacheMinConfidence);

                _sharedSegiMaterialState = nextState;
                _sharedSegiMaterialStateValid = true;
            }

            void UpdateSurfaceCacheNonRenderGraph(CommandBuffer cmd, RenderTexture sourceTexture, RenderTexture currentGI, int currentIndex, int previousIndex)
            {
                cmd.Blit(sourceTexture, _surfaceCacheNormalHistory[currentIndex].rt, _surfaceCacheMaterial, 1);
                cmd.Blit(sourceTexture, _surfaceCacheDepthHistory[currentIndex].rt, _surfaceCacheMaterial, 2);

                _surfaceCacheMaterial.SetTexture(CurrentSurfaceCacheDepthId, _surfaceCacheDepthHistory[currentIndex].rt);
                _surfaceCacheMaterial.SetTexture(CurrentSurfaceCacheNormalId, _surfaceCacheNormalHistory[currentIndex].rt);
                _surfaceCacheMaterial.SetTexture(PrevSurfaceCacheRadianceId, _surfaceCacheRadianceHistory[previousIndex].rt);
                _surfaceCacheMaterial.SetTexture(PrevSurfaceCacheNormalId, _surfaceCacheNormalHistory[previousIndex].rt);
                _surfaceCacheMaterial.SetTexture(PrevSurfaceCacheDepthId, _surfaceCacheDepthHistory[previousIndex].rt);
                cmd.Blit(currentGI, _surfaceCacheRadianceHistory[currentIndex].rt, _surfaceCacheMaterial, 0);

                cmd.SetGlobalTexture(SurfaceCacheRadianceId, _surfaceCacheRadianceHistory[currentIndex].rt);
                cmd.SetGlobalTexture(SurfaceCacheNormalId, _surfaceCacheNormalHistory[currentIndex].rt);
                cmd.SetGlobalTexture(SurfaceCacheDepthId, _surfaceCacheDepthHistory[currentIndex].rt);
            }

            void FinalizeSurfaceCacheHistory(int previousIndex)
            {
                _surfaceCacheHistoryIndex = previousIndex;
                _surfaceCacheHasHistory = true;
            }

            static void FinalizeSegiFrameState(LumenLike segi)
            {
                segi.material.SetMatrix(ProjectionPrevId, segi.attachedCamera.projectionMatrix);
                segi.material.SetMatrix(ProjectionPrevInverseId, segi.attachedCamera.projectionMatrix.inverse);
                segi.material.SetMatrix(WorldToCameraPrevId, segi.attachedCamera.worldToCameraMatrix);
                segi.material.SetMatrix(CameraToWorldPrevId, segi.attachedCamera.cameraToWorldMatrix);
                segi.material.SetVector(CameraPositionPrevId, segi.transform.position);
                segi.frameCounter = (segi.frameCounter + 1) % 64;
            }

            public void SetCameraColorTarget(RenderTargetIdentifier _cameraColorTargetIdent)
              => this._cameraColorTargetIdent = _cameraColorTargetIdent;

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
#if UNITY_2020_2_OR_NEWER
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                // get a copy of the current camera闂傚倸鍊搁崐鎼佸磹閹间礁纾归柟闂寸绾惧綊鏌熼梻瀵割槮缁炬儳缍婇弻鐔兼⒒鐎靛壊妲紒鐐劤缂嶅﹪寮婚悢鍏尖拻閻庨潧澹婂Σ顔剧磼閻愵剙鍔ょ紓宥咃躬瀵鎮㈤崗灏栨嫽闁诲酣娼ф竟濠偽ｉ鍓х＜闁绘劦鍓欓崝銈囩磽瀹ュ拑韬€殿喖顭烽幃銏ゅ礂鐏忔牗瀚介梺璇查叄濞佳勭珶婵犲伣锝夘敊閸撗咃紲闂佺粯鍔﹂崜娆撳礉閵堝洨纾界€广儱鎷戦煬顒傗偓娈垮枛椤兘骞冮姀銈呯閻忓繑鐗楃€氫粙姊虹拠鏌ュ弰婵炰匠鍕彾濠电姴浼ｉ敐澶樻晩闁告挆鍜冪床闂備胶绮崝锕傚礈濞嗘挸绀夐柕鍫濇川绾剧晫鈧箍鍎遍幏鎴︾叕椤掑倵鍋撳▓鍨灈妞ゎ厾鍏橀獮鍐閵堝懐顦ч柣蹇撶箲閻楁鈧矮绮欏铏规嫚閺屻儱寮板┑鐐板尃閸曨厾褰炬繝鐢靛Т娴硷綁鏁愭径妯绘櫓闂佸憡鎸嗛崪鍐簥闂傚倷鑳剁划顖炲礉閿曞倸绀堟繛鍡樻尭缁€澶愭煏閸繃宸濈痪鍓ф櫕閳ь剙绠嶉崕閬嶅箯閹达妇鍙曟い鎺戝€甸崑鎾斥枔閸喗鐏堝銈庡幘閸忔﹢鐛崘顔碱潊闁靛牆鎳愰ˇ褔鏌ｈ箛鎾剁闁绘顨堥埀顒佺煯缁瑥顫忛搹瑙勫珰闁哄被鍎卞鏉库攽閻愭澘灏冮柛鏇ㄥ幘瑜扮偓绻濋悽闈浶㈠ù纭风秮閺佹劖寰勫Ο缁樻珦闂備礁鎲￠幐鍡涘椽閸愵亜绨ラ梻鍌氬€烽懗鍓佸垝椤栫偛绀夐柨鏇炲€哥粈鍫熺箾閸℃ɑ灏紒鈧径鎰厪闁割偅绻冨婵堢棯閸撗勬珪闁逞屽墮缁犲秹宕曢柆宥呯闁硅揪濡囬崣鏇熴亜閹烘垵鈧敻宕戦幘鏂ユ灁闁割煈鍠楅悘鍫濐渻閵堝骸骞橀柛蹇旓耿閻涱噣宕橀纰辨綂闂侀潧鐗嗛幊鎰八囪閺岋綀绠涢幘鍓侇唹闂佺粯顨嗛〃鍫ュ焵椤掍胶鐓紒顔界懃椤繘鎼圭憴鍕彴闂佸搫琚崕鍗烆嚕閺夊簱鏀介柣鎰緲鐏忓啴鏌涢弴銊ュ箻鐟滄壆鍋撶换婵嬫偨闂堟刀銏犆圭涵椋庣М闁轰焦鍔栧鍕熺紒妯荤彟闂傚倷绀侀幉锟犲箰閸℃稑妞介柛鎰典簻缁ㄣ儵姊婚崒姘偓鐑芥嚄閸撲礁鍨濇い鏍仜缁€澶愭煥閺囩偛鈧摜绮堥崼鐔虹闁糕剝蓱鐏忣厾绱掗埀顒佸緞閹邦厾鍘梺鍓插亝缁诲啫顔忓┑鍫㈡／闁告挆鍕彧闂侀€炲苯澧紒鐘茬Ч瀹曟洟鏌嗗鍛唵闂佺鎻俊鍥矗閺囩喆浜滈柟鐑樺灥閳ь剛鏁诲畷鎴﹀箻閺傘儲鐏侀梺鍓茬厛閸犳鎮橀崼婵愭富闁靛牆楠搁獮姗€鏌涜箛鏃撹€块柣娑卞櫍瀹曟﹢顢欑喊杈ㄧ秱闂備線娼ч悧鍡涘箠閹板叓鍥樄闁哄矉缍€缁犳盯骞橀崜渚囧敼闂備胶绮〃鍡涖€冮崼銉ョ劦妞ゆ帊鑳堕悡顖滅磼椤旂晫鎳冩い顐㈢箻閹煎湱鎲撮崟顐ゅ酱闂備礁鎼悮顐﹀磿閸楃儐鍤曢柡澶婄氨閺€浠嬫煟閹邦厽绶查悘蹇撳暣閺屾盯寮撮妸銉ョ閻熸粍澹嗛崑鎾舵崲濠靛鍋ㄩ梻鍫熷垁閵忕妴鍦兜妞嬪海袦闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰熼梻鍌氬€风粈渚€骞夐敓鐘茬闁糕剝绋戝浠嬫煕閹板吀绨荤紒銊ｅ劦濮婂宕掑顑藉亾閻戣姤鍤勯柛鎾茬閸ㄦ繃銇勯弽顐粶缂佲偓婢舵劖鐓ラ柡鍥╁仜閳ь剙鎽滅划鍫ュ醇閻旇櫣顔曢梺绯曞墲钃遍悘蹇ｅ幘缁辨帡鍩€椤掍礁绶為柟閭﹀幘閸橆亪姊洪崜鎻掍簼缂佽鍟蹇撯攽閸垺锛忛梺鍛婃寙閸曨剛褰ч梻渚€鈧偛鑻晶顔剧磼閻樿尙效鐎规洘娲熼弻鍡楊吋閸涱垼鍞甸梻浣侯攰閹活亝淇婇崶顒€鐭楅柡鍥╁枂娴滄粓鏌熼悜妯虹仴闁逞屽墰閺佽鐣烽幋锕€绠婚柡鍌樺劜閻忎線姊洪崜鑼帥闁哥姵顨婇幃姗€宕煎┑鎰瘜闂侀潧鐗嗘鎼佺嵁濮椻偓閺屾稖绠涢弮鎾光偓鍧楁煟濞戝崬娅嶇€规洘锕㈤、娆戝枈鏉堛劎绉遍梻鍌欑窔濞佳囨偋閸℃稑绠犻柟鏉垮彄閸ヮ亶妯勯梺鍝勭焿缂嶁偓缂佺姵鐩獮姗€宕滄笟鍥ф暭闂傚倷鑳剁划顖炪€冮崱娑栤偓鍐醇閵夈儳鍔﹀銈嗗笂閼冲爼鎮￠婊呯＜妞ゆ梻鏅幊鍐┿亜椤愩垻绠婚柟鐓庢贡閹叉挳宕熼銈呴叡闂傚倷绀侀幖顐ゆ偖椤愶箑纾块柛妤冨剱閸ゆ洟鏌℃径濠勬皑闁衡偓娴犲鐓熼柟閭﹀幗缁舵煡鎮樿箛鎾虫殻闁哄本鐩鎾Ω閵夈儳顔掗柣鐔哥矋婢瑰棝宕戦幘鑸靛床婵犻潧顑嗛崑銊╂⒒閸喎鍨侀柕蹇曞Υ閸︻厽鍏滃瀣捣琚﹂梻浣芥〃閻掞箓宕濋弽褜鍤楅柛鏇ㄥ€犻悢铏圭＜婵☆垵宕佃ぐ鐔兼⒒閸屾艾鈧绮堟笟鈧獮澶愭晸閻樿尙顔囬梺绯曞墲缁嬫垵顪冩禒瀣厱闁规澘鍚€缁ㄨ崵绱掗妸锝呭姦婵﹤顭峰畷鎺戭潩椤戣棄浜鹃柣鎴ｅГ閸ゅ嫰鏌涢幘鑼槮闁搞劍绻冮妵鍕冀椤愵澀绮剁紓浣插亾濠㈣埖鍔栭悡銉╂煛閸モ晛浠滈柍褜鍓欓幗婊呭垝閸儱閱囬柣鏃囨椤旀洟姊虹化鏇炲⒉閽冮亶鎮樿箛锝呭箻缂佽鲸甯￠崺鈧い鎺嶇缁剁偤鏌熼柇锕€骞橀柛姗嗕邯濮婃椽宕滈幓鎺嶇凹缂備浇顕ч崯顐︻敊韫囨挴鏀介柛顐犲灪閿涘繘姊洪崨濠冨瘷闁告洦鍓涜ぐ褔姊绘笟鈧埀顒傚仜閼活垱鏅堕鍓х＜闁绘灏欐晥閻庤娲樺ú鐔煎蓟閸℃鍚嬮柛娑卞灱閸炵敻姊绘担渚敯闁规椿浜濇穱濠囧炊椤掆偓閻鏌￠崶鈺佹瀭濞存粍绮撻弻鐔兼倻濡櫣浠撮梺閫炲苯澧伴柡浣筋嚙閻ｇ兘骞嬮敃鈧粻濠氭煛閸屾ê鍔滈柣蹇庣窔濮婃椽宕滈懠顒€甯ラ梺鍝ュУ椤ㄥ﹪骞冨鈧畷鐓庘攽閹邦厼鐦滈梻渚€娼ч悧鍡橆殽閹间胶宓佹慨妞诲亾闁哄本绋栫粻娑㈠籍閹惧厜鍋撻崸妤佺厽婵炴垵宕▍宥団偓瑙勬礃閿曘垽銆佸▎鎴濇瀳閺夊牄鍔庣粔閬嶆⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鏌熼弶鍨絼闁搞儺浜跺Ο鍕⒑閸濆嫮鐒跨紓宥佸亾缂備胶濮甸惄顖炵嵁濮椻偓瀹曟粍绗熼崶褎鏆梻鍌氬€烽懗鍓佸垝椤栫偑鈧啴宕卞☉鏍ゅ亾閸愵喖閱囬柕澶堝劤閸旓箑顪冮妶鍡楃瑐闁煎啿鐖奸幃锟狀敍濠婂懐锛滃銈嗘⒒閺咁偊骞婇崘鈹夸簻妞ゆ劑鍨荤粻濠氭煙閻撳孩璐￠柟鍙夋尦瀹曠喖顢曢妶鍛村彙闂傚倸鍊风粈渚€骞栭銈嗗仏妞ゆ劧绠戠壕鍧楁煕濡ゅ啫鍓遍柛銈嗘礋閺屸剝寰勬繝鍕殤闂佽　鍋撳ù鐘差儐閻撶喖鏌熼柇锕€骞楃紒鑸电叀閹绠涢幘铏闂佸搫鏈惄顖氼嚕閹绢喖惟闁靛鍎抽鎺楁煟閻斿摜鐭嬪鐟版瀹曟垿骞橀懜闈涘簥濠电娀娼ч鍡涘磻閵娾晜鈷掗柛顐ゅ枎閸ㄤ線鏌曡箛鏇炍ラ柛锔诲幗閸忔粌顪冪€ｎ亝鎹ｇ悮锕傛⒒娴ｈ姤銆冮柣鎺炵畵瀹曟繈寮借閸ゆ鏌涢弴銊モ偓鐘绘偄閾忓湱锛滃┑鈽嗗灥瀹曚絻銇愰悢鍏尖拻闁稿本鑹鹃埀顒勵棑缁牊绗熼埀顒勭嵁婢舵劖鏅搁柣妯垮皺椤斿洭姊虹化鏇炲⒉缂佸鍨甸—鍐╃鐎ｎ偆鍘遍梺瑙勬緲閸氣偓缂併劏宕电槐鎺楀Ω閿濆懎濮﹀┑顔硷工椤嘲鐣烽幒鎴僵妞ゆ垼妫勬禍楣冩煟閹达絽袚闁搞倕瀚伴弻娑㈠箻閼碱剦妲梺鎼炲妽缁诲啴濡甸崟顖氬唨妞ゆ劦婢€缁墎绱撴担鎻掍壕婵犮垼鍩栭崝鏍偂閵夆晜鐓涢柛銉㈡櫅娴犳粓鏌嶈閸撴瑩骞楀鍛灊闁割偁鍎遍柋鍥煟閺冨洦顏犳い鏃€娲熷铏瑰寲閺囩偛鈷夐柦鍐憾閹绠涢敐鍛缂備浇椴哥敮锟犲春閳ь剚銇勯幒宥囶槮缂佸墎鍋ら弻鐔兼焽閿曗偓楠炴﹢鏌涘鍡曢偗婵﹥妞藉畷婊堝箵閹哄秶鍑圭紓鍌欐祰椤曆囧疮椤愶富鏁婇煫鍥ㄦ尨閺€浠嬫煕椤愮姴鐏柨娑樼箻濮婃椽宕ㄦ繝鍐ㄧ樂闂佸憡娲﹂崜娑溿亹瑜斿缁樼瑹閳ь剟鍩€椤掑倸浠滈柤娲诲灡閺呭爼顢欓崜褏锛滈梺缁橆焾鐏忔瑦鏅ラ柣搴ゎ潐濞叉ê煤閻旂厧钃熼柛鈩冾殢閸氬鏌涘☉鍗炵伇闁哥偟鏁诲缁樻媴娓氼垱鏁梺瑙勬た娴滄繃绌辨繝鍥х倞妞ゆ巻鍋撻柣顓燁殜閺岋繝宕堕埡浣圭€繛瀛樼矋缁捇寮婚悢鍏煎€绘俊顖濇娴犳挳姊洪柅鐐茶嫰婢т即鏌℃担绛嬪殭闁伙絿鍏橀獮瀣晝閳ь剛绮绘繝姘厸闁稿本锚閸旀粓鏌ｈ箛搴ｇ獢婵﹥妞藉畷婊堟嚑椤掆偓鐢儵姊洪崫銉バｆい銊ワ躬楠炲啫顫滈埀顒€鐣烽悡搴樻斀闁归偊鍘奸惁婊堟⒒娓氣偓濞佳囨偋閸℃﹩娈介柟闂磋兌瀹撲焦鎱ㄥ璇蹭壕闂佸搫鏈粙鏍不濞戙垹绫嶉柟鎯у帨閸嬫捇宕稿Δ浣哄幗濠电偞鍨靛畷顒€鈻嶅鍥ｅ亾鐟欏嫭绀冮柨鏇樺灲閵嗕礁鈻庨幋婵囩€抽柡澶婄墑閸斿海绮旈柆宥嗏拻闁稿本鐟х粣鏃€绻涙担鍐叉处閸嬪鏌涢埄鍐槈缂佺姷濞€閺岀喖寮堕崹顔肩导闂佹悶鍎烘禍璺何熼崟顖涒拺闁告繂瀚悞璺ㄧ磽瀹ヤ礁浜剧紓鍌欒兌婵敻鎯勯鐐靛祦婵せ鍋撶€规洘绮嶇粭鐔煎炊瑜庨弳顓㈡⒒閸屾艾鈧兘鎳楅崜浣稿灊妞ゆ牜鍋戦埀顒€鍟村畷鍗炩槈閺嶃倕浜鹃柛鎰靛枛闁卞洭鏌曟径鍫濆姢闁告棑绠戦—鍐Χ閸℃鐟愰梺鐓庡暱閻栧ジ骞冮敓鐙€鏁嶆繝濠傛噽閿涙粌顪冮妶鍡樷拻闁哄拋鍋婇獮濠囧礃閳瑰じ绨诲銈嗗姂閸ㄨ崵绮绘繝姘厵闁绘挸瀛╃拹锟犳煙閸欏灏︾€规洜鍠栭、鏇㈠Χ閸涙潙褰欓梻鍌氬€搁崐椋庣矆娓氣偓楠炴牠顢曢妶鍡椾粡濡炪倖鍔х粻鎴犲閸ф鐓曟俊銈呭暙閸撻亶鏌涢妶鍡樼闁哄备鍓濆鍕節閸曨剛娈ら梻浣姐€€閸嬫挸霉閻樺樊鍎愰柣鎾跺枛閺岀喖鏌囬敃鈧獮妤冪磼閼哥數鍙€闁诡喗顭堢粻娑㈠箻閾忣偅顔掗梻浣告惈閺堫剟鎯勯姘煎殨闁圭虎鍠栨儫闂侀潧顦崕鎶筋敊閹烘鐓熼柣鏂挎憸閻顭块悷鐗堫棦鐎规洘鍨块獮姗€鎳滈棃娑樼哎婵犵數濞€濞佳囶敄閸℃稑纾婚柕濞炬櫆閳锋帡鏌涢銈呮瀻闁搞劋绶氶弻鏇＄疀閺囩倣銉╂煕閿旇骞楅柛蹇旂矊椤啰鈧綆浜濋幑锝囨偖閿曞倹鈷掑ù锝堝Г閵嗗啴鏌ｉ幒鐐电暤鐎规洘绻堝鍊燁檨闁搞倖顨婇弻娑㈠即閵娿儮鍋撻悩缁樺亜闁惧繐婀遍敍婊冣攽閳藉棗鐏ョ€规洑绲婚妵鎰版偄閸忓皷鎷婚梺绋挎湰閼归箖鍩€椤掑嫷妫戠紒顔肩墛缁楃喖鍩€椤掑嫮宓佸鑸靛姈閺呮悂鏌ｅΟ鍨毢妞ゆ柨娲铏瑰寲閺囩偛鈷夌紓浣割儐閸ㄧ敻鈥旈崘鈺冾浄閻庯綆鍋嗛崢閬嶆⒑鐟欏嫭绶查柛姘ｅ亾缂備降鍔忓畷鐢垫閹烘惟闁挎繂鎳庢慨锕€鈹戦纭峰伐妞ゎ厼鍢查悾鐑藉箳閹搭厽鍍靛銈嗗灱濡嫭绂嶆ィ鍐╃厽闁硅揪绲借闂佹悶鍊曠€氫即寮诲☉銏犵闁肩⒈鍓﹀Σ顕€姊洪幖鐐插缂佽鍟存俊鐢稿礋椤栨艾鍞ㄥ銈嗗姦濠⑩偓缂侇喖澧庣槐鎾存媴閹绘帊澹曞┑鐘灱閸╂牠宕濋弽顓熷亗闁靛鏅滈悡娑㈡煕閵夈垺娅呭ù鐘崇矒閺屽秷顧侀柛鎾寸懇楠炴顭ㄩ崨顓炵亰闂佸壊鍋侀崕閬嶆煁閸ヮ剚鐓熼柡鍐ㄦ处椤忕姵銇勯弮鈧ú鐔奉潖閾忕懓瀵查柡鍥╁仜閳峰鏌﹂崘顔绘喚闁诡喗顭堢粻娑㈡晲閸涱厾顔掔紓鍌欐祰妞村摜鏁敓鐘叉槬闁逞屽墯閵囧嫰骞掑鍥舵М缂備礁澧庨崑銈夊蓟閻斿吋鐒介柨鏇楀亾妤犵偞锕㈤弻娑橆潨閸℃洟鍋楀┑顔硷攻濡炶棄鐣峰鍫濈闁瑰搫绉堕崙鍦磽閸屾瑦绁版い鏇嗗洤鐤い鎰跺瘜閺佸﹪鐓崶銊﹀皑闁衡偓娴犲鐓曟い鎰Т閻忣亪鏌ㄥ☉妯肩婵﹥妞藉畷銊︾節閸愩劎妯傞梻浣告啞濞诧箓宕滃☉婧夸汗鐟滄柨顫忕紒妯肩懝闁逞屽墴閸┾偓妞ゆ帒鍊告禒婊堟煠濞茶鐏￠柡鍛埣椤㈡瑩宕滆閿涙粓姊虹紒姗嗙劸閻忓繑鐟﹂弲銉╂煟鎼淬値娼愭繛鍙壝叅闁绘梻鍘ч拑鐔兼煕閳╁喚娈㈤柛姘儔閺屾稑鈽夐崡鐐典紘闂佸摜鍋熼弫璇差潖缂佹ɑ濯村〒姘煎灡閺侇垶姊虹憴鍕仧濞存粍绻冪粚杈ㄧ節閸パ呭€炲銈嗗坊閸嬫捇鏌ｉ鐕佹疁闁哄矉绻濆畷鍫曞煛娴ｅ湱鈧厽绻涚€涙鐭嬬紒顔芥崌瀵鎮㈤崗鐓庘偓缁樹繆椤栨繃顏犲ù鐘虫尦濮婃椽鏌呴悙鑼跺濠⒀傚嵆閺屸剝鎷呯粵瀣闂佷紮绲块崗姗€鐛€ｎ喗鏅濋柍褜鍓涚划缁樼節濮橆厾鍘搁梺绋挎湰閿氶柛鏃€绮撻弻锝堢疀鎼达絿鐛㈠┑顔硷攻濡炶棄鐣峰鍫熷殤妞ゆ巻鍋撻悽顖樺劦濮婃椽宕妷銉愶絾銇勯妸銉含妤犵偛鍟～婊堝焵椤掆偓閻ｇ兘鎮℃惔妯绘杸闂佸綊鍋婇崢濂告儊濠婂牊鈷掑〒姘ｅ亾婵炰匠鍥ㄥ亱闁糕剝锕╁▓浠嬫煙闂傜鍏岀€规挷鐒﹂幈銊ヮ渻鐠囪弓澹曢梻浣告惈閺堫剛绮欓弽顐や笉婵炴垯鍨瑰Λ姗€鏌涢埦鈧弲娆撴焽椤栨稏浜滈柕蹇娾偓鍐叉懙闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰囬梻鍌氬€烽悞锔锯偓绗涘厾鍝勵吋婢跺﹦锛涢梺瑙勫劤婢у海澹曟總鍛婄厽婵☆垵娅ｉ敍宥夋煃椤栨稒绀嬮柡灞炬礋瀹曟儼顦叉い蹇ｅ幗椤ㄣ儵鎮欓幖顓犲姺闂佸湱鎳撶€氼厼顭囬鍫熷亱闁割偅绻勯崢杈╃磽閸屾艾鈧悂宕愰悜鑺ュ€块柨鏇炲€哥粈澶嬩繆閵堝懏鍣圭痪鎯ь煼閺岋綁骞囬鍌欑驳閻庤娲栧鍓佹崲濠靛顥堟繛鎴濆船閸撻亶姊虹粙娆惧剭闁告梹鍨甸～蹇旂節濮橆剟鍞堕梺缁樻煥閸㈡煡鎮楅鍕拺闂傚牊绋撶粻鐐烘煕婵犲啰澧电€规洘鍔欏畷褰掝敃閿濆懎浼庢繝纰樻閸ㄤ即骞栭锔藉殝鐟滅増甯楅悡鏇熶繆椤栨瑨顒熼柛銈囧枛閺屽秷顧侀柛鎾寸箞閿濈偞寰勬繛鎺楃細缁犳稑鈽夊Ο纭风吹闂傚倸鍊搁悧濠勭矙閹捐姹查柨鏇炲€归悡蹇撯攽閻愭垟鍋撻柛瀣崌閺屾稓鈧綆鍋呯亸顓㈡煃閽樺妲搁柍璇茬У濞煎繘濡搁妷銉︽嚈婵°倗濮烽崑娑氭崲閹烘梹顫曢柟鐑樺殾閻斿吋鍤冮柍鍝勶工閺咁參姊绘担鍛婃儓妞わ富鍨堕幃褔宕卞Ο缁樼彿婵炲濮撮鍛不閺嵮€鏀介柛灞剧閸熺偤鏌ｉ幘瀵告噰闁哄睙鍡欑杸闁挎繂鎳嶇花濂告倵鐟欏嫭绀€闁哄牜鍓熸俊鐢稿礋椤栨凹娼婇梻鍕处缁旂喎螣濮瑰洣绨诲銈嗘尰缁本鎱ㄩ崒婧惧亾鐟欏嫭纾搁柛鏃€鍨块妴浣糕槈濮楀棛鍙嗛梺褰掑亰閸犳牜鑺遍妷鈺傗拻闁稿本鐟︾粊鎵偓瑙勬礀閻忔岸骞堥妸鈺佺骇闁圭偨鍔嶅浠嬪极閸愨晜濯撮柛蹇撴啞閻繘姊绘担鍛婂暈闁告棑绠撳畷浼村冀椤撶喎鈧潡鏌涢…鎴濅簴濞存粍绮撻弻鐔煎传閸曨剦妫炴繛瀛樼矋閸庢娊鍩為幋锔藉€烽柣鎰帨閸嬫挾鈧綆鍓氬畷鍙夌節闂堟侗鍎忕紒鐘崇墪闇夐柛蹇撳悑閸庢鏌℃担闈╄含闁哄矉绠戣灒濞撴凹鍨辨缂傚倷鐒﹂崝妤呭磻閻愬灚宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩闁瑰鍏樺铏圭矙濞嗘儳鍓遍梺鍦嚀濞差參寮幇鐗堝€风€瑰壊鍠栭幃鎴炵節閵忥絾纭炬い鎴濆€块獮蹇撁洪鍛嫽闂佺鏈銊︽櫠濞戞氨纾奸悗锝庡亝鐏忕數绱掗纰辩吋鐎规洘锚闇夐悗锝庡亝閺夊憡淇婇悙顏勨偓鏍ь潖瑜版帒纾块柟鎯版閸屻劑鎮楀☉娅辨粍绂嶅鍫熺厪闊洢鍎崇壕鍧楁煙閸愬弶澶勬い銊ｅ劦閹瑩寮堕幋鐐剁檨闁诲孩顔栭崳顕€宕抽敐鍛殾闁圭儤鍩堥悡銉╂煙闁箑娅嶆俊顐ゅ厴濮婂宕掑顑藉亾閻戣姤鍤勯柛顐ｆ磵閳ь剨绠撳畷濂稿Ψ閵夈儳褰夋俊鐐€栫敮鎺斺偓姘煎弮瀹曟垿鏁嶉崟顒€鏋戦梺鍝勫€藉▔鏇㈠汲閿旂晫绡€闂傚牊绋掗敍宥夋煕濮橆剦鍎旈柡灞剧洴閸╁嫰宕橀妸銉綇缂傚倷闄嶉崝蹇涱敋瑜旈垾鏃堝礃椤斿槈褔鏌涢埄鍏狀亪寮冲Δ鍛拺缂佸顑欓崕鎴︽煕鐎ｃ劌鈧洟鎮鹃悜钘夌骇閻犲洤澧介崰鎾寸閹间礁鍐€鐟滃本绔熼弴銏＄厽闁绘柨鎽滈幊鍐倵濮樼厧骞樺瑙勬礋楠炴牗鎷呴崷顓炲箞闂備線娼ч…鍫ュ磹濡ゅ懏鍎楁繛鍡樺灍閸嬫挸鈻撻崹顔界亪濡炪値鍘奸崲鏌ユ偩閻戣棄纭€闁绘劕绉靛鍦崲濠靛纾兼繛鎴炃氶崑鎾活敍閻愮补鎷绘繛杈剧秬濞咃綁濡存繝鍥ㄧ厓鐟滄粓宕滃▎鎴濐棜妞ゆ挾濮锋稉宥夋煛鐏炶鍔滈柛濠傜仛閹便劌顫滈崱妤€骞嶉梺绋款儍閸ㄦ椽骞堥妸锔剧瘈闁稿被鍊楅崥瀣倵鐟欏嫭绀冮悽顖涘浮閿濈偛鈹戠€ｅ灚鏅為梺鑺ッˇ顔界珶閺囥垺鈷戠憸鐗堝笚閿涚喖鏌ｉ幒鐐电暤鐎规洘鍨归埀顒婄秵娴滄牠寮ㄦ禒瀣厽婵☆垵顕х徊缁樸亜韫囷絽浜伴柡宀嬬秮椤㈡﹢鎮㈤悜妯烘珰闂備礁鎼惉濂稿窗閺嶎厾宓侀柛鈩冨嚬濡查箖姊洪崷顓熸珪濠殿喚鏁搁幑銏犫槈閵忕姴绐涘銈嗙墬閸╁啴寮搁崨瀛樷拺闁告繂瀚悞璺ㄧ磼缂佹绠撻柣锝囧厴瀹曞ジ寮撮悙宥冨姂閺屾洘绔熼姘偓璇参涢鐐粹拻闁稿本鐟ㄩ崗宀€绱掗鍛仸妤犵偞鍔栫换婵嗩潩椤撶偘鐢婚梻浣稿暱閹碱偊骞婃惔銊﹀珔闁绘柨鎽滅粻楣冩煙鐎涙鎳冮柣蹇婃櫇缁辨帡鎮╁畷鍥ｅ闂侀潧娲ょ€氫即鐛Ο鍏煎磯闁烩晜甯囬崹濠氬焵椤掍緡鍟忛柛鐘虫崌瀹曟繈骞嬪┑鎰稁濠电偛妯婃禍婊冾啅濠靛棌鏀介柣妯诲絻椤忣亪鏌涢敍鍗炴处閻撶喖骞栧ǎ顒€鐏╅柛銈庡墴閺屾稑鈻庤箛鎾存婵烇絽娲ら敃顏堝箖閻ｅ瞼鐭欓悹渚厛濡茶淇婇悙顏勨偓鏍偋濡ゅ懎绀勯柣鐔煎亰閻掕棄鈹戦悩鍙夊闁抽攱鍨块弻锟犲磼濡搫濮曞┑鐐叉噹閹虫﹢寮诲☉銏″亹閻庡湱濮撮ˉ婵嬫煣閼姐倕浠遍柡灞剧洴楠炲洭鎮介棃娑樺紬婵犵數鍋為幐鎼佲€﹂悜钘夎摕闁哄洢鍨归柋鍥ㄧ節闂堟稒顥炴繛鍫弮濮婅櫣鎷犻崣澶岊洶婵炲瓨绮犳禍顏勵嚕婵犳艾惟闁宠桨绀佸畵鍡椻攽閳藉棗鐏ユ繛鍜冪稻缁傛帗銈ｉ崘鈹炬嫼闂備緡鍋呯粙鎾诲煘閹烘鐓曢柡鍐ｅ亾闁搞劌鐏濋悾宄懊洪鍕姦濡炪倖甯婇梽宥嗙濠婂牊鐓欓柣鎴灻悘銉︺亜韫囷絼閭柡宀嬬秮楠炴﹢宕￠悙鎻掝潥缂傚倷鑳剁划顖炴儎椤栫偟宓侀悗锝庡枟閸婅埖绻涢懠棰濆敽闂侇収鍨遍妵鍕閳╁喚妫冮梺绯曟櫔缁绘繂鐣烽幒妤€围闁糕檧鏅涢弲顒勬⒒閸屾瑨鍏岀紒顕呭灦瀹曟繈寮借閻掕姤绻涢崱妯哄闁告瑥绻掗埀顒€绠嶉崕鍗炍涘▎鎾崇煑闊洦鎸撮弨浠嬫煟濡搫绾ч柛锝囧劋閵囧嫯绠涢弴鐐╂瀰闂佸搫鐬奸崰鎰八囬悧鍫熷劅闁抽敮鍋撻柡瀣嚇濮婃椽鎮烽幍顕嗙礊婵犵數鍋愰崑鎾愁渻閵堝啫鐏い銊ワ工閻ｇ兘骞掑Δ鈧洿闂佸憡渚楅崰妤€袙瀹€鍕拻闁稿本鑹鹃埀顒佹倐瀹曟劙鎮滈懞銉ユ畱闂佸壊鍋呭ú宥夊焵椤掑﹦鐣电€规洖銈告俊鐑筋敊閼恒儲鐝楅梻鍌欒兌绾爼宕滃┑瀣仭鐟滄棃骞嗙仦瑙ｆ瀻闁规儳顕崢鐢告⒑缂佹ê鐏﹂柨鏇楁櫅閳绘捇寮崼鐔哄幗闂侀潧鐗嗛ˇ閬嶆偩濞差亝鐓涢悘鐐插⒔濞插瓨銇勯姀鈩冪闁轰焦鍔欏畷鍗炩枎濡亶鐐烘⒒娴ｇ瓔鍤欐繛瀵稿厴瀵偊宕ㄦ繝鍐ㄥ伎闂佸湱铏庨崰鎺楀焵椤掑﹤顩柟鐟板婵℃悂鏁冮埀顒勫几閸岀偞鈷戦柛娑橈攻婢跺嫰鏌涘鈧粻鏍箖妤ｅ啫绠氱憸澶愬绩娴犲鐓熼柟閭﹀墮缁狙囨倵濮橆剚鍣界紒杈ㄦ尭椤撳ジ宕熼鐘靛床闁诲氦顫夊ú妯侯渻娴犲鏄ラ柍褜鍓氶妵鍕箳瀹ュ顎栨繛瀛樼矋缁捇寮婚悢鐓庝紶闁告洦鍘滈妷鈺傜厱闊洦鎸鹃悞鎼佹煛鐏炵晫效妞ゃ垺绋戦埥澶娾枎閹搭厽效闂傚倷绀佸﹢閬嶎敆閼碱剛绀婇柍褜鍓氶妵鍕晜閻ｅ苯寮ㄥ┑鈽嗗亗閻掞箑顕ラ崟顓涘亾閿濆骸澧伴柣锕€鐗婄换婵嬫偨闂堟刀銏ゆ煕婵犲啯鍊愭い銏℃椤㈡洟鏁傞悾灞藉箺婵＄偑鍊栭幐楣冨窗鎼粹埗褰掝敋閳ь剟寮婚垾宕囨殕閻庯綆鍓涢敍鐔哥箾鐎电顎撶紒鐘虫崌楠炲啫鈻庨幙鍐╂櫌闂佺琚崐鏍ф毄缂傚倸鍊搁崐鎼佸磹閻戣姤鍤勯柤绋跨仛閸欏繘鏌ｉ姀銏℃毄闁活厽鐟╅弻鐔兼倻濡儵鎷归悗瑙勬礀瀵墎鎹㈠┑瀣棃婵炴垵宕崜鎵磼閻愵剙鍔ら柛姘儔楠炲牓濡搁妷顔藉缓闂佸壊鍋侀崹鏄忔懌闂備浇顕х€涒晛顫濋妸鈺佺獥闁规崘鍩栭～鏇㈡煙閹规劦鍤欑痪鎯у悑閹便劌顫滈崱妤€骞嬮梺绋款儐閹稿藝閻楀牊鍎熼柕蹇曞閳ь剚鐩娲传閸曨噮娼堕梺绋挎唉娴滎剚绔熼弴鐔虹瘈婵﹩鍘鹃崢鐢告⒑閸涘﹥瀵欓柛娑卞幘椤愬ジ姊绘担渚劸闁挎洩绠撳顐ｇ節濮橆剝鎽曢梺闈涱焾閸庮噣寮ㄦ禒瀣厱闁斥晛鍟╃欢閬嶆煃瑜滈崜姘躲€冩繝鍥ц摕闁挎稑瀚ч崑鎾绘晲鎼粹€愁潻闂佸搫顑嗛惄顖炲蓟閳ュ磭鏆嗛悗锝庡墰琚︽俊銈囧Х閸嬬偤鈥﹂崶顒€鐒垫い鎺戝€归弳鈺冪棯椤撯剝纭鹃崡閬嶆煕椤愮姴鍔滈柍閿嬪浮閺屾盯濡烽幋婵囨拱闁宠鐗撳娲传閸曨噮娼堕梺鍛婃煥閻倿宕洪悙鍝勭闁挎洍鍋撴鐐灪娣囧﹪顢涘▎鎺濆妳濠碘剝鐓℃禍璺侯潖缂佹ɑ濯撮悷娆忓娴犫晠姊虹粙鍖℃敾闁告梹鐟ラ悾鐑藉箣閿曗偓缁犲鏌￠崒妯哄姕闁哄倵鍋撻梻鍌欒兌缁垵鎽梺鍛婃尰瀹€绋跨暦椤栨繄鐤€婵炴垶鐟ч崢閬嶆⒑缂佹◤顏嗗椤撶喐娅犻弶鍫氭櫇绾惧吋銇勯弮鍥撴い銉ョ墢閳ь剝顫夊ú鏍Χ缁嬫鍤曢柟缁㈠枟閸婄兘姊婚崼鐔衡槈鐞氭繃绻濈喊澶岀？闁稿鍨垮畷鎰板冀椤愶絾娈伴梺鍦劋椤ㄥ懐绮ｅΔ浣瑰弿婵妫楁晶濠氭煟閹哄秶鐭欓柡宀€鍠栭弻鍥晝閳ь剟鐛Ο鑲╃＜闁绘宕甸悾娲煛鐏炲墽鈽夐柍璇叉唉缁犳盯鏁愰崰鑸妽缁绘繈鍩涢埀顒備沪婵傜浠愰梻浣告惈閻绱炴笟鈧顐﹀箛閺夎法鍊為悷婊冪Ч閻涱喚鈧綆浜跺〒濠氭煏閸繂鏆欓柣蹇ｄ簼閵囧嫰濡搁妷顖氫紣闂佷紮绲块崗姗€骞冮姀銏犳瀳閺夊牄鍔嶅▍鍥⒒娓氣偓濞佳囨偋閸℃稑绠犻幖杈剧悼娑撳秹鏌熼幆鏉啃撻柍閿嬪笒闇夐柨婵嗗椤掔喖鏌￠埀顒佸鐎涙鍘遍柣搴秵閸嬪嫭鎱ㄦ径鎰厵妞ゆ棁顕у畵鍡椻攽閿涘嫬鍘撮柛鈹惧墲閹峰懘宕妷銏犱壕妞ゆ挾鍠嶇换鍡涙煏閸繄绠抽柛鎺嶅嵆閺屾盯鎮ゆ担鍝ヤ桓閻庤娲橀崹鍨暦閻旂⒈鏁嶆繛鎴炶壘楠炴姊绘担绛嬫綈鐎规洘锚閳诲秹寮撮姀鐘殿唹闂佹寧绻傞ˇ浼存偂閻斿吋鐓ユ繝闈涙椤ユ粓鏌嶇紒妯荤闁哄瞼鍠栭、娆戠驳鐎ｎ偆鏉归梻浣虹帛娓氭宕抽敐鍛殾鐟滅増甯╅弫濠囨煟閹惧啿鐦ㄦ繛鍏肩☉閳规垿鎮╁▓鎸庢瘜濠碘剝褰冮幊妯虹暦閹达箑宸濋悗娑櫭禒顓㈡⒑閸愬弶鎯堥柟鍐叉捣缁顫濋懜鐢靛幈闂侀€涘嵆濞佳囧几濞戞氨纾奸柣娆忔噽缁夘噣鏌″畝瀣埌閾伙綁鏌涜箛鎾虫倯婵絽瀚板娲箰鎼达絺濮囩紓渚囧枟閹瑰洤顕ｆ繝姘労闁告劑鍔庣粣鐐寸節閻㈤潧孝閻庢凹鍓熼妴鍌炲醇閺囩啿鎷洪梺鍛婄☉閿曘儵鎮￠妷褏纾煎璺侯儐鐏忥箓鏌熼钘夊姢闁伙綇绻濋獮宥夘敊閼恒儳鏆﹂梻鍌欑窔閳ь剛鍋涢懟顖涙櫠閹绢喗鐓曢柍瑙勫劤娴滅偓淇婇悙顏勨偓鏍暜閹烘绐楁慨姗嗗墻閻掍粙鏌熼柇锕€骞樼紒鐘荤畺閺屾稑鈻庤箛锝嗏枔闂佺粯甯婄划娆撳蓟閳╁啰鐟归柛銉戝嫮褰庢俊銈囧Х閸嬬偟鏁幒鏇犱簷闂備線鈧偛鑻晶鎾煙椤斿厜鍋撻弬銉︽杸闁诲函缍嗘禍鐐侯敊閹邦兘鏀介柣鎴濇川缁夌敻鏌涢幘瀵告噭濞ｅ洤锕幊鏍煛閸愵亷绱查梻浣虹帛閿氶柛鐔锋健閸┿垽寮撮姀锛勫幐閻庡厜鍋撻柍褜鍓熷畷浼村箻鐠囪尙鍔﹀銈嗗笂閼冲爼鍩婇弴鐔翠簻妞ゆ挾鍋炲婵堢磼椤旂⒈鐓兼鐐搭焽缁辨帒螣閻撳骸绠婚梻鍌欑婢瑰﹪鎮￠崼銉ョ；闁告洦鍓氶崣蹇曗偓骞垮劚濡瑩宕ｈ箛鎾斀闁绘ɑ褰冩禍鐐烘煟閹剧懓浜归柍褜鍓濋～澶娒哄Ο鍏兼殰闁圭儤顨呴悡婵嬪箹濞ｎ剙濡肩紒鐘冲▕閺岀喓鈧稒顭囩粻鎾绘煠閸偄濮囬柍瑙勫灴閺佸秹宕熼鈩冩線闂備胶顭堥…鍫ュ磹濠靛棛鏆﹂柕蹇ョ磿闂勫嫰鏌涘☉姗堝伐濞存粓浜跺娲捶椤撶偘澹曢梺娲讳簻缂嶅﹪宕洪埀顒併亜閹达絾纭剁紒鎰⒒閳ь剚顔栭崰鏇犲垝濞嗘挶鈧礁顫滈埀顒勫箖濞嗘挻鍤戦柛銊︾☉娴滈箖鏌涢…鎴濇灀闁衡偓娴犲鐓熼柟閭﹀幗缂嶆垿鏌ｈ箛銉╂闁靛洤瀚伴、姗€鎮欓弶鎴炵亷婵＄偑鍊戦崹娲偡閿曗偓椤曘儵宕熼姘辩杸濡炪倖鎸荤粙鎺斺偓姘偢濮婄粯鎷呮笟顖涙暞闂佺顑嗛崝娆忕暦閹存績妲堥柕蹇曞Х椤︻噣姊洪柅鐐茶嫰婢у瓨鎱ㄦ繝鍐┿仢鐎规洏鍔嶇换婵囨媴閾忓湱鐣抽梻鍌欑閹芥粍鎱ㄩ悽鎼炩偓鍐╃節閸ャ儮鍋撴担绯曟瀻闁规儳鍟垮畵鍡涙⒑闂堟稓绠氭俊鐙欏洤绠紓浣诡焽缁犻箖寮堕崼婵嗏挃闁告帊鍗抽弻鐔烘嫚瑜忕弧鈧Δ鐘靛仜濡繂鐣锋總鍛婂亜闁惧繗顕栭崯搴ㄦ⒑鐠囪尙绠抽柛瀣洴瀹曟劙骞栨担鐟扳偓鍫曟煠绾板崬鍘撮柛瀣尵閹叉挳宕熼鍌ゆК缂傚倸鍊哥粔鎾晝閵堝鍋╅柣鎴ｅГ閸婅崵绱掑☉姗嗗剱闁哄拑缍佸铏圭磼濮楀棛鍔告繛瀛樼矤閸撶喖骞冨畡鎵虫斀闁搞儻濡囩粻姘舵⒑缂佹ê濮﹀ù婊勭矒閸┾偓妞ゆ帊鑳舵晶顏堟偂閵堝洠鍋撻獮鍨姎妞わ富鍨虫竟鏇熺節濮橆厾鍘甸梺鍛婃寙閸涱厾顐奸梻浣虹帛閹稿鎮烽敃鍌毼﹂柛鏇ㄥ灠缁秹鏌涚仦鎹愬濞寸姵锚閳规垿鍩勯崘銊хシ濡炪値鍘鹃崗妯侯嚕鐠囧樊鍚嬮柛銉ｅ妼閻濅即姊洪崫鍕潶闁稿孩妞介崺鈧い鎺嶇贰濞堟粓鏌＄仦鐣屝ら柟椋庡█瀵濡烽妷銉ュ挤濠德板€楁慨鐑藉磻濞戙埄鏁勯柛娑欐綑閻撴﹢鏌熸潏鍓х暠闂佸崬娲弻鏇＄疀閺囩倫銏㈢磼閻橀潧鈻堟慨濠傤煼瀹曟帒顫濋钘変壕闁归棿绀佺壕褰掓煟閹达絽袚闁搞倕瀚伴弻娑㈩敃閿濆棛锛曢梺閫炲苯澧剧紒鐘虫尭閻ｉ攱绺界粙璇俱劍銇勯弮鍥撴繛鍛墦濮婄粯鎷呴搹骞库偓濠囨煕閹惧绠樼紒顔界懇楠炲鎮╅崗鍝ョ憹闂備礁鎼悮顐﹀磿閺屻儲鍋傞柡鍥ュ灪閻撳繐顭块懜寰楊亪寮稿☉姗嗙唵鐟滄粓宕抽敐澶婅摕闁挎稑瀚ч崑鎾绘晲閸屾稒鐝栫紓浣瑰姈濡啴寮婚敍鍕勃闁告挆鍕灡婵°倗濮烽崑娑樏洪鐐嶆盯宕橀妸銏☆潔濠电偛妫欓崹鎶芥焽椤栨粎纾介柛灞剧懆閸忓矂鏌涚€ｎ偅宕岀€规洘娲熼獮搴ㄦ寠婢跺巩鏇㈡煟鎼搭垳绉甸柛鎾磋壘閻ｇ兘宕ｆ径宀€顔曢梺鐟扮摠閻熴儵鎮橀埡鍐＜闁绘瑥鎳愮粔顕€鏌″畝瀣М妤犵偛娲、妤呭焵椤掑嫬姹查柣鎰暯閸嬫捇宕楁径濠佸缂傚倷绀侀鍫濃枖閺囥垹姹查柨鏇炲€归悡娆撳级閸繂鈷旈柣锝堜含缁辨帡鎮╁顔惧悑闂佽鍠楅〃濠偽涢崘銊㈡婵炲棙鍔曢弸鎴︽⒒娴ｉ涓茬紒鐘冲灴閹囧幢濞戞鍘撮梺纭呮彧鐎靛矂寮繝鍥ㄧ厸闁稿本锚閳ь剚鎸惧▎銏ゆ偨閸涘ň鎷洪梺鍛婄☉椤參鎳撶捄銊х＜闁绘ê纾晶顏堟煟閿濆洤鍘撮柡浣瑰姈瀵板嫭绻濋崟鍨為梻鍌欑窔濞佳団€﹂崼銉ュ瀭婵炲樊浜滃Λ姗€鏌嶈閸撶喎顫忕紒妯诲闁告縿鍎查悗顔尖攽閳藉棗浜滈悗姘煎墲閻忓鈹戞幊閸婃洟骞婅箛娑樼９闁割偅娲橀悡鐔兼煛閸屾氨浠㈤柟顔藉灴閺屾稓鈧絽澧庣粔顕€鏌＄仦鐣屝ユい褌绶氶弻娑㈠箻鐠虹儤鐏堥悗娈垮櫘閸嬪﹤鐣峰鈧、娆撴嚃閳轰礁袝濠碉紕鍋戦崐鏍ь啅婵犳艾纾婚柟鎯х亪閸嬫挾鎲撮崟顒€纰嶉柣搴㈠嚬閸橀箖骞戦姀鐘斀閻庯綆鍋掑Λ鍐ㄢ攽閻愭潙鐏﹂懣銈夋煕鐎ｎ偅宕屾鐐寸墬閹峰懘宕妷褎鐎梻鍌欐祰濞夋洟宕伴幘瀛樺弿闁汇垻顭堢壕濠氭煕鐏炲墽銆掔紒鐘荤畺閺屾盯鍩勯崗鈺傚灩娴滃憡瀵肩€涙鍘介梺瑙勫礃濞夋盯鍩㈤崼銉︾厸鐎光偓閳ь剟宕伴弽顓溾偓浣割潨閳ь剟骞冮姀鈽嗘Ч閹艰揪绲鹃崳顖炴⒒閸屾瑧顦﹂柟娴嬧偓鎰佹綎闁革富鍘奸崹婵嬪箹鏉堝墽绋绘い顐ｆ礋閺岀喖鎮滃鍡樼暥闂佺粯甯掗悘姘跺Φ閸曨垰绠抽柟瀛樼妇閸嬫捇鎮界粙璺紱闂佺懓澧界划顖炲疾閺屻儱绠圭紒顔煎帨閸嬫捇鎳犻鈧崵顒勬⒒閸屾瑧顦﹂柟璇х節閹兘濡疯瀹曟煡鏌熼悧鍫熺凡闁绘挻锕㈤弻鐔告綇妤ｅ啯顎嶉梺绋款儐閸旀瑩寮婚悢铏圭＜婵☆垵娅ｉ悷鎻掆攽閻愯尙澧涢柣鎾偓鎰佹綎婵炲樊浜濋ˉ鍫熺箾閹寸偟鎳冩い锔规櫇缁辨挻鎷呯粵瀣闁诲孩鐭崡鍐茬暦濞差亜鐒垫い鎺嶉檷娴滄粓鏌熼悜妯虹仴闁哄鍊栫换娑㈠礂閻撳骸顫嶇紓浣虹帛缁嬫捇骞忛悩渚Ь闂佷紮绲块弫鎼佸焵椤掑喚娼愭繛鍙夛耿瀹曞綊宕滄担鐟板簥濠电娀娼ч鍛閸忚偐绡€濠电姴鍊搁顏呫亜閺冣偓濞茬喎顫忕紒妯诲闁惧繒鎳撶粭鈥斥攽椤旂》宸ユい顓犲厴瀹曞搫鈽夐姀鐘诲敹闂侀潧顦崕鏌ユ倵椤掑嫭鈷戠紒顖涙礀婢ц尙绱掔€ｎ偄娴柟顕嗙節瀹曟﹢顢旈崨顓熺€炬繝鐢靛Т閿曘倝鎮ч崱娆戠焼闁割偆鍠撶粻楣冩煙鐎电浠╁瑙勶耿閺屾盯濡搁敂鍓х杽闂佸搫琚崝宀勫煘閹达箑骞㈡俊顖滃劋閻濆瓨绻濈喊妯哄⒉鐟滄澘鍟撮幃褔骞樼拠鑼舵憰闂佸搫娴勭槐鏇㈡偪閳ь剚绻濋悽闈浶㈤柛鐘冲哺閸┾偓妞ゆ巻鍋撴俊顐ｇ箞瀵鎮㈤崗鑲╁弳闁诲函缍嗛崑浣圭閵忕姭鏀芥い鏃傘€嬮弨缁樹繆閻愯埖顥夐柣锝囧厴婵℃悂鏁傞崜褏妲囬梻浣告啞濞诧箓宕㈤崜褏鐜绘俊銈呮噺閳锋帒霉閿濆嫯顒熼柣鎺斿亾閹便劌顫滈崼銉︻€嶉梺闈涙处濡啴鐛弽銊﹀闁荤喖顣︾純鏇㈡⒑閻熸澘鎮戦柣锝庝邯瀹曟繂顓兼径濠勫幈闂佸湱鍎ら崵姘炽亹閹烘挻娅滈梺鍛婁緱閸犳牠寮抽崼銉︾厽闁绘ê鍟挎慨鈧梺鍛婃尰缁诲牊淇婇悽绋跨妞ゆ牭绲鹃弲锝夋⒑缂佹ê濮嶆繛浣冲毝鐑藉焵椤掑嫭鈷掑ù锝呮啞閹牓鏌熼搹顐ｅ磳鐎规洘妞藉浠嬵敃閵堝洨鍔归梻浣告贡閸庛倝寮婚敓鐘茬；闁瑰墽绮弲鏌ュ箹鐎涙绠橀柡浣圭墪椤啴濡惰箛鎾舵В闂佺顑呴敃銈夛綖韫囨拋娲敂閸曨剙绁舵俊鐐€栭幐楣冨磻濞戞瑤绻嗛柛婵嗗閺€鑺ャ亜閺冨倶鈧螞濮樻墎鍋撶憴鍕闁诲繑姘ㄧ划鈺呮偄閻撳骸宓嗛梺闈涚箳婵兘藝瑜斿濠氬磼濮橆兘鍋撻幖渚囨晪妞ゆ挾濮锋稉宥嗘叏濮楀棗澧绘繛鎾愁煼閺屾洟宕煎┑鍥舵￥婵犫拃灞藉缂佽鲸甯￠、娆撳箚瑜夐弸鍛存⒑鐠団€虫灓闁稿鍊濋悰顕€宕卞☉鎺嗗亾閺嶎厽鏅柛鏇ㄥ墮濞堝苯顪冮妶搴″箲闁告梹鍨甸悾鐑芥偄绾拌鲸鏅ｉ梺缁樏悘姘熆閺嵮€鏀介柣妯诲墯閸熷繘鏌涢悩宕囧⒌闁轰礁鍟存俊鐑藉Ψ椤旇棄鐦滈梻浣藉Г閿氭い锕備憾瀵娊鏁冮崒娑氬帾婵犵數濮寸换鎰般€呴鍌滅＜闁抽敮鍋撻柛瀣崌濮婄粯鎷呮笟顖滃姼闂佸搫鐗滈崜鐔风暦濞嗘挻鍋￠梺顓ㄥ閸旈攱绻濋悽闈浶㈡繛璇х畵閸╂盯骞掗幊銊ョ秺閺佹劙宕ㄩ鍏兼畼闂備浇顕栭崹浼存偋閹捐钃熼柣鏃傚帶缁€鍐煠绾板崬澧版い鏂匡躬濮婃椽鎮烽幍顔炬殯闂佹悶鍔忓Λ鍕偩閻戣姤鍋傞幖瀛樕戦弬鈧梻浣稿閸嬪棝宕伴幇閭︽晩闁哄洢鍨洪埛鎺戙€掑锝呬壕濠电偘鍖犻崟鍨啍闂婎偄娲﹀ú姗€锝為弴銏＄厸闁搞儴鍩栨繛鍥煃瑜滈崜娆撳箠濮椻偓楠炲啫顭ㄩ崼鐔锋疅闂侀潧顦崕顕€宕戦幘缁樺仺闁告稑锕﹂崣鍡椻攽閻樼粯娑ф俊顐ｇ箞椤㈡挸螖閳ь剟婀佸┑鐘诧工鐎氼剚鎱ㄥ澶嬬厸鐎光偓閳ь剟宕伴弽顓炶摕闁靛ě鈧崑鎾绘晲鎼粹€茬按婵炲濮伴崹褰掑煘閹达富鏁婄痪顓炲槻婵稓绱撴笟鍥ф珮闁搞劌纾崚鎺楊敇閵忊€充簻闂佺粯鎸稿ù鐑藉磹閻愮儤鍋℃繝濠傚暟閻忛亶鏌ゅú顏冩喚闁硅櫕鐗犻崺锟犲礃椤忓海闂繝鐢靛仩閹活亞寰婇懞銉х彾濠电姴娲ょ壕鍧楁煙閹殿喖顣奸柣鎾存礋閺屾洘绻涜鐎氼厼袙閸儲鈷戦悹鍥ｂ偓铏彎缂備胶濮甸悧鐘荤嵁閸愵喗鍋╃€光偓閳ь剟鎯屽▎鎾寸厱妞ゎ厽鍨甸弸锕傛煃瑜滈崜娆撴倶濠靛鐓橀柟杈剧畱閻擄繝鏌涢埄鍐炬畼濞寸姵鎸冲铏圭矙濞嗘儳鍓遍梺鍛婃⒐閻熲晠濡存担绯曟瀻闁规儳鍟垮畵鍡涙⒑缂佹ɑ顥嗘繛鍜冪秮椤㈡瑩寮撮姀锛勫幐婵犮垼娉涢敃锔芥櫠閺囩儐鐔嗙憸宀€鍒掑▎蹇曟殾婵﹩鍘奸閬嶆倵濞戞瑯鐒介柛姗€浜跺铏圭磼濡儵鎷荤紓渚囧櫘閸ㄥ啿鈽夐悽绋块唶闁哄洨鍠撻崢鎾绘偡濠婂嫮鐭掔€规洘绮岄～婵堟崉閾忚妲遍柣鐔哥矌婢ф鏁幒妤€纾奸柕濞垮劗閺€浠嬫煕鐏炲墽鐭ら柣鎺楃畺閺岋綁骞樼€靛摜鐣奸梺閫炲苯澧紒鐘茬Ч瀹曟洟鏌嗗畵銉ユ处鐎靛ジ寮堕幋鐙呯幢闂備浇顫夋竟鍡樻櫠濡ゅ懏鍋傞柣鏂垮悑閻撴瑩姊洪銊х暠妤犵偞鐗犻弻锝堢疀閹垮嫬濮涚紓浣介哺鐢剝淇婇幖浣肝ㄦい鏃€鍎抽幃鎴炵節濞堝灝鏋涢柨鏇樺劚椤啴鎸婃径灞炬濡炪倖鍔х粻鎴犵矆鐎ｎ偁浜滈柟鎯ь嚟閳洟鏌ｆ惔銊ゆ喚婵﹦绮幏鍛瑹椤栨粌濮兼俊鐐€栭崹鐢稿箠閹版澘鐒垫い鎺嶇閸ゎ剟鏌涘Ο鍝勨挃闁告帗甯為埀顒婄秵閸犳牜绮婚搹顐＄箚闁靛牆瀚崝宥嗙箾閸涱厽鍠樻慨濠呮缁瑩骞愭惔銏″缂傚倷绀侀鍡涘箲閸パ呮殾闁靛骏绱曢々鐑芥倵閿濆骸浜愰柟閿嬫そ閺岋綁鎮㈤崫銉﹀櫑闁诲孩鍑归崜娑氬垝閸儱绀冮柍鍝勫暟椤旀洘绻濋姀锝嗙【妞ゆ垵娲ょ叅閻庣數纭堕崑鎾斥枔閸喗鐏嶉梺璇″枛閸婂潡鎮伴閿亾閿濆骸鏋熼柛瀣姍閺岋綁濮€閵忊晜娈扮紓浣界簿鐏忔瑧妲愰幘璇茬＜婵﹩鍏橀崑鎾绘倻閼恒儱鈧潡鏌ㄩ弴鐐测偓鐢稿焵椤掑﹦鐣电€规洖鐖奸崺锟犲礃瑜忛悷婵嬫⒒娴ｈ櫣甯涢柛鏃€顨堥幑銏犫攽閸喎搴婇梺绯曞墲鑿уù婊勭矒閺岀喖骞嶉搹顐ｇ彅闂佽绻嗛弲鐘诲蓟閿熺姴骞㈡俊顖氬悑閸ｄ即姊洪崫鍕缂佸缍婇獮鎰節閸愩劎绐炲┑鈽嗗灡娴滃爼鏁冮崒娑掓嫼闁诲骸婀辨刊顓㈠吹濞嗘劖鍙忔慨妤€鐗嗛々顒傜磼椤旀鍤欓柍钘夘樀婵偓闁斥晛鍟悵鎶芥⒒娴ｅ憡鎲稿┑顔肩仛缁旂喖宕卞☉妯肩崶闂佸搫璇炵仦鍓х▉濠电姷鏁告慨鐢告嚌閸撗冾棜闁稿繗鍋愮粻楣冩煕閳╁厾顏堟倶閵夈儮鏀介梽鍥ㄦ叏閵堝洦宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩缂侇喖鐖煎娲箰鎼淬垹顦╂繛瀛樼矋缁捇銆佸鑸垫櫜濠㈣泛谩閳哄懏鐓ラ柡鍐ㄥ€瑰▍鏇犵磼婢跺﹦鍩ｆ慨濠冩そ瀹曟﹢宕ｆ径瀣壍闂備胶顭堥敃锕傚箠閹捐鐓濋柟鐐綑椤曢亶鎮楀☉娅辨岸骞忛搹鍦＝闁稿本鐟ч崝宥夋煥濮橆兘鏀芥い鏂垮悑閸犳﹢鏌″畝瀣К缂佺姵鐩顒勫垂椤旇姤鍤堥梻鍌欑劍鐎笛呮崲閸曨垰纾婚柕鍫濇媼閸ゆ洟鏌熺紒銏犳灈妞ゎ偄鎳橀弻銊モ槈濡警浠煎Δ鐘靛仜缁夌懓顫忕紒妯诲闁惧繒鎳撶粭锟犳⒑閹肩偛濡介柛搴°偢楠炴顢曢敂鐣屽幗闂婎偄娲﹂幐鍓х不閹绘帩鐔嗛柣鐔哄椤ョ姵淇婇崣澶婂妤犵偞锕㈤獮鍥ㄦ媴閸涘﹤鈧垶姊绘担鍛婂暈濞撴碍顨婂畷浼村冀椤撶偛鍤戦梺缁橆焽缁垶鍩涢幋锔界厱婵犻潧妫楅顏呯節閳ь剟鎮ч崼銏㈩啎闂佸憡鐟ラˇ浠嬫倿娴犲鐓欐鐐茬仢閻忊晠鏌嶉挊澶樻█濠殿喒鍋撻梺缁橆焾鐏忔瑩鍩€椤掑骞栨い顏勫暣婵″爼宕卞Ο閿嬪闂備礁鎲￠…鍡涘炊妞嬪海鈼ゆ繝娈垮枟椤牓宕洪弽顓炵厱闁硅揪闄勯悡鐘测攽椤旇棄濮囬柍褜鍓氬ú鏍偤椤撶喓绡€闁汇垽娼ф禒婊堟煟椤忓啫宓嗙€规洘绻堥獮瀣攽閸喐顔曢梻浣侯攰閹活亞绮婚幋婵囩函闂傚倷绀侀幉锛勫垝閸儲鍊块柨鏂垮⒔缁€濠囧箹濞ｎ剙濡介柍閿嬪灴閺岀喖顢涢崱妤佸櫧妞ゆ梹鍨甸—鍐Χ鎼粹€茬盎濡炪倧绠撴禍鍫曞春閳ь剚銇勯幒宥堝厡妞わ綀灏欓埀顒€鍘滈崑鎾绘煙闂傚顦﹂柦鍐枔閳ь剙绠嶉崕閬嵥囬姘殰闂傚倷绶氬褔鈥﹂鐔剁箚闁搞儺浜楁禍鍦喐閺傛娼栭柧蹇氼潐鐎氭岸鏌ょ喊鍗炲妞ゆ柨娲ㄧ槐鎾存媴閸撳弶楔闂佺娅曢幑鍥晲閻愬墎鐤€闁瑰彞鐒﹀浠嬨€侀弮鍫濆窛妞ゆ挾鍣ラ崥瀣⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鐓崶銊﹀瘱闁圭儤顨呯粻娑㈡煟濡も偓閻楀啴骞忓ú顏呪拺闁告稑锕︾粻鎾绘倵濮樼厧寮€规洘濞婇幐濠冨緞閸℃ɑ鏉搁梻浣虹帛閸斿繘寮插☉娆戭洸闁诡垎鈧弨浠嬫煟濡椿鍟忛柡鍡╁灦閺屽秷顧侀柛鎾寸懅婢规洟顢橀姀鐘殿啈闂佺粯顭囩划顖炴偂濞嗘垟鍋撻悷鏉款伀濠⒀勵殜瀹曠敻宕堕浣哄幍闂佹儳娴氶崑鍡樻櫠椤忓牊鐓冮悷娆忓閻忓鈧娲栭悥濂稿灳閿曞倸绠ｉ柣鎴濇椤ュ牓姊绘担绛嬪殭婵﹫绠撻敐鐐村緞婵炴帡缂氱粻娑樷槈濡⒈妲烽梻浣侯攰閹活亞鎷归悢鐓庣劦妞ゆ垼娉曢ˇ锔姐亜椤愶絿鐭掗柛鈹惧亾濡炪倖甯掔€氼剛绮婚弽顓熺厓闁告繂瀚崳鍦磼閻樺灚鏆柡宀€鍠撻幏鐘诲灳閸忓懐鍑规繝鐢靛仜瀵爼鎮ч悩璇叉槬闁绘劕鎼粻锝夋煥閺冨洤袚婵炲懏鐗犲娲川婵犲啫顦╅梺绋款儏閸婂灝顕ｉ崼鏇ㄦ晣闁靛繆妾ч幏铏圭磽娴ｅ壊鍎忛悘蹇撴嚇瀵劍绻濆顓犲幗濠德板€撻懗鍫曟儗閹烘柡鍋撳▓鍨珮闁稿锕妴浣割潩鐠鸿櫣鍔﹀銈嗗笒鐎氼喖鐣垫笟鈧弻鐔兼倻濮楀棙鐣跺┑鈽嗗亝閿曘垽寮诲☉妯锋斀闁糕剝顨忔禒瑙勭節閵忥綆娼愭繛鍙夌墵閸╃偤骞嬮敂缁樻櫓缂備焦绋戦鍥礉閻戣姤鈷戦柛娑橆煬閻掑ジ鏌涢弴銊ュ闁绘繄鍏樺铏规喆閸曢潧鏅遍梺鍝ュУ濮樸劍绔熼弴銏犵缂佹妗ㄧ花濠氭⒑閸濆嫬鏆欐繛灞傚€栫粋宥夋偡闁妇鍞甸悷婊冩捣缁瑩骞樼拠鑼姦濡炪倖宸婚崑鎾绘煟韫囨棁澹樻い顓炵仢铻ｉ悘蹇旂墪娴滅偓鎱ㄥ鍡椾簻鐎规挸妫濋弻锝呪槈閸楃偞鐝濋悗瑙勬礀閻栧ジ銆佸Δ浣哥窞閻庯綆鍋呴悵婵嬫⒒閸屾瑨鍏岀紒顕呭灥閹筋偊鎮峰鍕凡闁哥噥鍨辩粚杈ㄧ節閸パ呭€炲銈嗗笂鐠佹煡骞忔繝姘拺缂佸瀵у﹢浼存煟閻旀繂娉氶崶顒佹櫇闁逞屽墴閳ワ箓宕稿Δ浣镐画闁汇埄鍨奸崰娑㈠触椤愶附鍊甸悷娆忓缁€鍐煕閵娿儳浠㈤柣锝囧厴婵℃悂鍩℃繝鍐╂珫婵犵數鍋為崹鍫曟晪缂備降鍔婇崕闈涱潖缂佹ɑ濯撮柛娑橈工閺嗗牓姊洪崨濠冾棖缂佺姵鎸搁悾鐑筋敍閻愭潙浜滅紓浣割儓濞夋洜绮ｉ悙鐑樷拺鐟滅増甯掓禍浼存煕濡搫鎮戠紒鍌氱Х閵囨劙骞掗幘顖涘婵犳鍠氶幊鎾趁洪妶澶嬪€舵い鏇楀亾闁哄备鍓濋幏鍛喆閸曨偀鍋撻幇鐗堢厸濞达絽鎽滃瓭濡炪値鍘归崝鎴濈暦婵傚憡鍋勯柛娆忣槸椤忓湱绱撻崒姘偓鎼佸磹閻戣姤鍊块柨鏇炲€歌繚闂佺粯鍔曢幖顐ょ不閿濆鐓ユ繝闈涙閺嗘洘鎱ㄧ憴鍕垫疁婵﹥妞藉畷銊︾節閸屾鏇㈡⒑閸濄儱校闁绘濞€楠炲啴鏁撻悩鎻掑祮闂侀潧楠忕槐鏇㈠储閹间焦鈷戦柛娑橈工婵倿鏌涢弬鍨劉闁哄懓娉涢埥澶愬閿涘嫬骞楅梻浣筋潐瀹曟ê鈻斿☉銏犲嚑婵炴垯鍨洪悡娑㈡倶閻愰潧浜剧紒鈧崘顏佸亾閸偅绶查悗姘煎墲閻忔帡姊虹紒妯虹仸妞ゎ厼娲︾粋宥夋倷閻戞ǚ鎷婚梺绋挎湰閻熝呮嫻娴煎瓨鐓曟繛鍡楃箻閸欏嫮鈧娲栫紞濠囧蓟閸℃鍚嬮柛娑樺亰缁犳捇寮诲☉銏犲嵆闁靛鍎虫禒鈺侇渻閵堝骸浜滈柟铏耿瀵顓兼径瀣檮婵犮垼娉涢鍛枔瀹€鍕拺闂侇偆鍋涢懟顖涙櫠椤曗偓閺岋綀绠涢弮鍌滅杽闂佺硶鏅濋崑銈夌嵁鐎ｎ喗鏅滅紓浣股戝▍鎾绘⒒娴ｈ棄鍚归柛鐘崇墵閵嗗倿鏁傞悾宀€褰鹃梺鍝勬储閸ㄦ椽鍩涢幒妤佺厱閻忕偛澧介幊鍛亜閿旇偐鐣甸柡灞剧洴閹垽鏌ㄧ€ｎ亙娣俊銈囧Х閸嬬偤鎮ч悩姹団偓渚€寮撮姀鈩冩珖闂侀€炲苯澧板瑙勬礃缁绘繂顫濋鐘插箞闂備焦鏋奸弲娑㈠疮閵婏妇顩叉俊銈勮兌缁犻箖鏌熺€涙鎳冮柣蹇婃櫊閺屾盯骞囬妸銉ゅ婵犵绱曢崑鎴﹀磹閺嶎厼绀夐柟杈剧畱绾捐淇婇妶鍛櫤闁哄拋鍓氱换婵嬫濞戝崬鍓遍梺鎶芥敱閸ㄥ湱妲愰幒鏂哄亾閿濆骸浜滄い鏇熺矋缁绘繈鍩€椤掍礁顕遍悗娑欘焽閸樹粙姊虹紒妯烩拹婵炲吋鐟﹂幈銊╁磼濞戞牔绨婚梺闈涢獜缁辨洟骞婇崟顓涘亾閸偅绶查悗姘煎櫍閸┾偓妞ゆ帒锕﹀畝娑㈡煛閸涱喚绠樼紒顔款嚙椤繈鎳滅喊妯诲缂傚倸鍊烽悞锕傗€﹂崶顒€绠犻柛鏇ㄥ灡閻撶喖鏌ｉ弮鈧娆撳礉濮樿埖鍋傞柕鍫濐槹閻撳繘鏌涢锝囩畺闁瑰吋鍔栭妵鍕Ψ閿曚礁顥濋梺瀹狀潐閸ㄥ潡骞冮埡浣烘殾闁搞儴鍩栧▓褰掓⒒娴ｈ櫣甯涢柟绋款煼閹兘鍩￠崨顓℃憰濠电偞鍨崹鍦不缂佹ü绻嗛柕鍫濆€告禍楣冩⒑閻熸澘鏆辨繛鎾棑閸掓帡顢橀姀鈩冩闂佺粯蓱閺嬪ジ骞忛搹鍦＝闁稿本鐟ч崝宥夋嫅闁秵鍊堕煫鍥ㄦ礃閺嗩剟鏌＄仦鍓ф创闁诡喒鏅涢悾鐑藉炊瑜夐幏浼存⒒娴ｈ櫣甯涘〒姘殜瀹曟娊鏁愰崪浣告婵炲濮撮鍌炲焵椤掍胶澧靛┑锛勫厴婵＄兘鍩℃担绋垮挤濠碉紕鍋戦崐鎴﹀垂濞差亗鈧啯绻濋崶褎妲梺閫炲苯澧柕鍥у楠炴帡骞嬪┑鎰棯闂備胶顭堥敃銉р偓绗涘懏宕叉繛鎴烇供閸熷懏銇勯弮鍥у惞闁告垵缍婂铏圭磼濡闉嶅┑鐐跺皺閸犳牕顕ｆ繝姘櫜濠㈣泛锕﹂惈鍕⒑閸撴彃浜介柛瀣嚇钘熼柡宥庡亞绾捐棄霉閿濆懏鎯堢€涙繂顪冮妶搴″箻闁稿繑锚椤曪絾绻濆顓熸珫闂佸憡娲︽禍婵嬪礋閸愵喗鈷戦柛娑橈攻鐎垫瑩鏌涢弴銊ヤ簻闁诲骏绻濆濠氬磼濞嗘劗銈板銈嗘礃閻楃姴鐣锋导鏉戠婵°倐鍋撻柣銈囧亾缁绘盯骞嬪▎蹇曚痪闂佺顑傞弲鐘诲蓟閿濆绠ｉ柨婵嗘－濡嫰姊虹紒妯哄闁挎洩绠撻獮澶愬川婵犲倻绐為梺褰掑亰閸撴盯顢欓崱妯肩閻庣數顭堢敮鍫曟煟鎺抽崝鎴﹀春濞戙垹绠抽柟鐐藉妼缂嶅﹪寮幇鏉垮窛妞ゆ棁妫勯崜闈涒攽閻愬樊鍤熷┑顕€娼ч—鍐╃鐎ｎ亣鎽曞┑鐐村灦閻燂箓宕曢悢鍏肩厪闁割偅绻冮崳褰掓煙椤栨粌浠х紒杈ㄦ尰缁楃喖宕惰閻忓牆顪冮妶搴″箻闁稿繑锕㈤幃浼搭敊閸㈠鍠栧畷妤呮偂鎼达絽閰遍梻鍌欐祰閸嬫劙鍩涢崼銉ョ婵炴垯鍨瑰Ч鍙夈亜閹板墎鎮肩紒鈾€鍋撻梻浣圭湽閸ㄨ棄顭囪閻☆參姊绘担鐟邦嚋婵炴彃绻樺畷鎰攽鐎ｎ亞鐣洪悷婊冩捣閹广垹鈹戠€ｎ亞顦伴梺闈涱焾閸庣増绔熼弴銏♀拺婵懓娲ら悘鍙夌箾娴ｅ啿鍟伴幗銉モ攽閿涘嫬浜奸柛濞垮€濆畷锝夊焵椤掍胶绠惧璺侯儐缁€瀣殽閻愭潙鐏寸€规洜鍠栭、妤呭磼濠婂嫬娈炲┑锛勫亼閸婃牠鎮уΔ鍛殞闁绘劦鍓涢悵鍫曟倵閿濆骸鏋熼柍閿嬪笒閵嗘帒顫濋敐鍛闂備線鈧偛鑻晶浼存煕鐎ｎ偆娲撮柟宕囧枛椤㈡稑鈽夊▎鎰娇濠电姷鏁告慨鐢靛枈瀹ュ鍋傞柡鍥ュ灪閻撴瑩鏌熺憴鍕缁绢參绠栭弻锛勨偓锝庡亞閳洘銇勯妸锝呭姦闁诡喗鐟╅獮鎾诲箳閹炬惌鍞查梻浣稿⒔缁垶鎮ч悩璇茶摕婵炴垶菤濡插牓鏌涘Δ鍐ㄤ户濞寸姭鏅濈槐鎾存媴妞嬪海鐡樼紓浣哄У閹告悂锝炶箛鎾佹椽顢旈崟顓фФ闂備浇鍋愰埛鍫ュ礈濞嗘垶鏆滈柕澶嗘櫆閳锋垿鏌涘┑鍕姎閺嶏繝姊虹紒姗嗘畷妞ゃ劌锕悰顕€宕奸妷銉庘晠鏌ㄩ弮鍥棄闁告柨鎳樺铏光偓鍦У椤ュ淇婇锝囨噮闁逞屽墴濞佳囨儗閸屾凹娼栨繛宸簼椤ュ牊绻涢幋鐐跺妞わ綀娅ｇ槐鎾存媴閾忕懓绗￠梺鍛婃⒐閻熲晠鐛崱妯诲闁告捁灏欓崣鍡涙⒑閸撴彃浜為柛鐘虫崌瀹曘垽鏌嗗鍡忔嫼闂傚倸鐗婃笟妤呮倿妤ｅ啯鐓曢幖娣灩閳绘洟鏌℃担鍝バ㈡い鎾冲悑瀵板嫮鈧綆鍓欓獮鍫ユ⒒娴ｅ憡璐￠柛搴涘€濋獮鎰矙濡潧缍婇、娆撴煥椤栨矮澹曢柣鐔哥懃鐎氼厾绮堥埀顒勬⒑闂堟稓澧涢柟顔煎€搁悾鐑藉传閸曞孩妫冨畷銊╊敇閻樻彃绠版繝鐢靛仩閹活亞寰婃禒瀣簥闁哄被鍎栨径濠庢僵闁煎摜顣介幏缁樼箾鏉堝墽鎮奸柟铏崌椤㈡艾顭ㄩ崨顖滐紲闁荤姴娲﹁ぐ鍐焵椤掆偓缂嶅﹪濡撮崘顔嘉ㄩ柍杞扮缁愭稒绻濋悽闈浶㈤悗姘槻鐓ら柟闂寸劍閳锋垿鏌涘┑鍡楊伀闁宠顦甸弻锝堢疀閺傚灝顫掗梺璇″枤閸忔﹢寮婚崶顒佹櫆闁诡垎鍐╄緢闂傚倸鍊风粈渚€骞夐垾瓒佹椽鏁冮崒姘亶婵°倧绲介崯顖炲磻閸屾凹鐔嗛柤鎼佹涧婵牓鏌嶉柨瀣仼缂佽鲸鎸婚幏鍛存嚃閳╁啫鐏存鐐叉瀹曠喖顢涘☉姘箞闂佽鍑界紞鍡樼瀹勯偊鍟呴柕澶嗘櫆閻撶喖鏌ㄥ┑鍡樻悙闁告ê顕埀顒冾潐濞叉牠濡剁粙娆惧殨闁圭虎鍠楅崐椋庘偓骞垮劙閻掞箑鈻嶆繝鍥ㄧ厸閻忕偠顕ф慨鍌溾偓娈垮枟濞兼瑨鐏冮梺閫炲苯澧紒鍌氱У閵堬綁宕橀埞鐐闂備胶顭堥張顒勫礄閻熸嫈锝夊传閵壯咃紲婵犮垼娉涢張顒勫吹閳ь剙顪冮妶搴′簻缂佺粯鍔楅崣鍛渻閵堝懐绠伴柟宄邦儏铻為柛鎰靛枟閳锋垿鏌涢幇顒€绾ч柟顖氱墦閺屾盯鎮ゆ担鍝ヤ桓閻庤娲橀崹濂杆囩憴鍕弿濠电姴鎳忛鐘崇箾閹寸姵鏆€规洏鍔戦、娆撳箚瑜庨柨顓㈡⒒閸屾瑧顦︽繝鈧柆宥呯疇闁规崘绉ú顏嶆晣闁绘劕鐏氬▓鎯р攽閻愬弶鈻曞ù婊勭箞瀹曟劙鎮滈懞銉у幈濠电娀娼уΛ妤咁敂椤愶絻浜滈柟鎯х摠閵囨繃鎱ㄦ繝鍐┿仢妤犵偞鍔栭幆鏃堝煑閳哄倸顏虹紓鍌氬€风欢锟犲闯椤曗偓瀹曞湱鎹勬笟顖氭闂佸搫琚崕娲极閸℃稒鐓冪憸婊堝礈閻旂厧鏄ラ柣鎰惈缁狅綁鏌ㄩ弮鍥棄闁逞屽墰閸忔﹢寮诲☉妯锋瀻闊浄绲鹃埢鎾斥攽閳藉棗浜為柛瀣枔濡叉劙骞樼€涙ê顎撻梺鎯х箳閹虫挾绮敓鐘斥拺闁告稑锕ラ埛鎰亜閵娿儳澧︾€规洘宀搁獮鎺楀箻閸撲胶妲囬梻浣规偠閸庢挳宕洪弽顓炵柧闁冲搫鎳忛悡鐔肩叓閸ャ劍灏电紒鐘崇叀閺岋絽螖娓氬洦鐤侀悗娈垮櫘閸撶喖宕洪埀顒併亜閹哄秵顦风紒璇叉閺岋綁骞囬崗鍝ョ泿闂侀€炲苯澧柣妤佹礋閿濈偠绠涢弮鍌滅槇濠殿喗锕╅崕鐢稿Ψ閳哄倸鈧敻鏌ㄥ┑鍡涱€楅柡瀣〒缁辨挻鎷呴崣澶嬬彇缂備浇椴搁幐濠氬箯閸涱喗鍙忛柟杈剧到娴滃綊鏌熼獮鍨伈鐎规洖宕埥澶娾枎閹存繂绠洪梻鍌欑缂嶅﹪宕戞繝鍥у偍濠靛倹娼屾径灞稿亾閿濆骸鏋熼柣鎾跺枑閵囧嫰骞樼捄鐑樼亖闂佺懓鍟跨€氼參濡甸崟顖毼╅柨婵嗘噹婵箓姊虹拠鈥虫灍闁荤啙鍥х闁绘ü璀﹀Ο渚€姊洪崨濠傚毈闁稿锕ら～蹇涙倻濡顫￠梺瑙勵問閸犳牗淇婃禒瀣拺闁革富鍘介崵鈧┑鐐叉▕閸欏啴鎮伴鈧畷姗€顢欓懖鈺佸箰闂備礁鎲℃笟妤呭储妤ｅ啯鍋傞柟杈鹃檮閳锋垹绱掔€ｎ厼鍔甸悗姘嵆閺屾盯鎮ゆ担闀愮盎闁绘挶鍊濋弻銊╁即閻愭祴鍋撹ぐ鎺戠；闁稿瞼鍋為悡娑㈡煕閵夈垺娅呴柡瀣⒒缁辨帡鎮╅懡銈囨毇闂佸搫鐬奸崰鎾诲焵椤掑倹鏆╂い顓炵墦閸┾偓妞ゆ帊鑳舵晶鍨殽閻愬樊鍎旈柡浣稿暣閸┾偓妞ゆ帒瀚埛鎺撱亜閺嶎偄浠﹂柣鎾跺枛閺岀喐娼忛崜褍鍩岄悶姘哺濮婅櫣绱掑Ο璇查瀺濠电偠灏欓崰鏍ь嚕婵犳艾鍗抽柣鏃囨椤旀洟姊洪崜鑼帥闁哥姵鐗犻垾鏍ㄥ緞婵犲孩瀵岄梺闈涚墕閹虫劗绮婚崘娴嬫斀闁绘劏鏅涙禍楣冩⒒娴ｅ憡鎯堥柣顒€銈稿畷浼村冀椤撶喎浠掑銈嗘磵閸嬫挾鈧娲栫紞濠囥€佸▎鎾崇煑闁靛绠戞禍婵嬫⒒閸屾艾鈧绮堟笟鈧幃鍧楀炊椤剚鐩畷鐔碱敍閳ь剟宕堕澶嬫櫖闂佺粯鍨靛ú銊х矙韫囨挴鏀介柣妯肩帛濞懷勩亜閹寸偛濮嶇€殿噮鍋婂畷鍫曨敆娴ｅ搫骞楅梻浣筋潐婢瑰棙鏅跺Δ鈧埥澶庮樄婵﹦鍎ょ€佃偐绱欓悩鍐叉灓婵犳鍠栭敃銉ヮ渻娴犲绠犻柨鐔哄Т鍥撮梺鍛婁緱閸犳岸鍩€椤掆偓婢х晫妲愰幘瀵哥懝闁搞儜鍕邯闂備焦妞块崜娆撳Χ閹间胶宓侀柟杈剧畱椤懘鏌ｅ▎灞戒壕濠电偟顑曢崝鎴﹀蓟瀹ュ牜妾ㄩ梺鍛婃尵閸犳牠骞冩导鎼晪闁逞屽墮閻ｅ嘲顫滈埀顒勫春閻愬搫鍨傛い鏃囧吹閸戝綊姊婚崒娆戭槮闁硅绻濆畷婵嬪箣閿曗偓缁€澶愬箹閹碱厾鍘涢柡浣革躬閺屾稑鈽夊Ο鍏兼喖闂佹娊鏀遍崹鍧楀蓟濞戞粎鐤€婵炴垶鐟辩槐鐢告⒑閻撳骸顥忓ù婊庝邯瀵鈽夊▎鎰妳闂侀潧绻掓慨顓㈠几濞嗘挻鍊甸悷娆忓绾炬悂鏌涢妸銈囩煓闁靛棔绶氬顕€宕煎┑瀣暪闂備胶绮弻銊ヮ嚕閸撲讲鍋撳顑惧仮婵﹦绮幏鍛瑹椤栨稒鏆為梻浣侯焾椤戝棝宕濆▎蹇曟殾闁瑰瓨绺惧Σ鍫ユ煏韫囨洖啸妞わ富鍠栭埞鎴︻敊婵劒绮堕梺绋款儐閹瑰洭寮婚敍鍕勃闁告挆鍕灡闁诲孩顔栭崳顕€宕滈悢鑲╁祦闁归偊鍘介崕鐔兼煏韫囧ň鍋撻幇浣风礂闂傚倸鍊搁崐椋庢濮樿泛鐒垫い鎺嶈兌閵嗘帡鏌嶇憴鍕诞闁哄本鐩顒傛崉閵娧冨П婵＄偑鍊戦崹楦挎懌缂備浇娅ｉ弲顐ゅ垝濞嗘垶宕夐柕濞垮劗閸嬫捇鎮欓悜妯锋嫼闂佸湱顭堝ù椋庣不閹剧粯鐓欓柛鎰皺鏍＄紓浣规⒒閸犳牕顕ｉ幘顔碱潊闁挎稑瀚獮妤呮⒒娴ｈ櫣甯涢柡灞诲姂楠炴顭ㄩ崼婵堢枃闂佸憡鍔﹂崰妤呭磹閸偅鍙忔俊顖滃帶鐢埖顨ラ悙鎼劷缂佽鲸甯￠、娆撴嚃閳轰讲鍙洪柣搴ゎ潐濞叉﹢鏁冮姀銈冣偓浣糕枎閹炬潙浠奸柣蹇曞仩濡嫬效閺屻儲鈷掗柛灞剧懅椤︼箓鏌熺喊鍗炰簻閻撱倝鏌ㄩ弴鐐测偓褰掑疾椤忓牊鈷掑ù锝囩摂閸ゆ瑧绱掔紒姗嗘畼濞存粎顭堥埢搴ㄥ箻瀹曞洤濮︽俊鐐€栫敮鎺楀窗濮橆剦鐒介柟閭﹀幘缁犻箖鏌涘▎蹇ｆ＆妞ゅ繐鐗冮埀顒佹瀹曟﹢鍩￠崘鐐カ闂佽鍑界紞鍡樼濠靛鍊垫い鎺戝閳锋垿鏌ｉ悢鍛婄凡闁抽攱姊荤槐鎺楊敋閸涱厾浠搁悗瑙勬礃閸ㄥ潡鐛崶顒佸亱闁割偁鍨归獮妯肩磽娴ｅ搫浜炬繝銏∶悾鐑筋敆娴ｈ鐝烽梺鍛婃寙閸涱垽绱查梻浣告啞椤ㄥ牓宕戝☉姘辨／鐟滃繒妲愰幒鏃傜＜婵☆垵鍋愰悾鐢告⒑瀹曞洨甯涢柟鐟版搐閻ｇ柉銇愰幒婵囨櫓闂佷紮绲芥總鏃堝箟妤ｅ啯鈷掗柛灞剧懅閸斿秹鎮楃粭娑樺幘閸濆嫷鍚嬪璺猴功閺屟囨⒑缂佹﹩鐒鹃悘蹇旂懇瀵娊鏁傞幋鎺旂畾闂侀潧鐗嗛崐鍛婄妤ｅ啯鈷戦悗鍦У椤ュ銇勯敂璇茬仸闁挎繄鍋涢埞鎴犫偓锝呯仛閺咃綁鎮峰鍐╂崳缂佽京鍋涜灃闁告侗鍠掗幏缁樼箾鏉堝墽绉繛鍜冪悼閺侇喖鈽夐姀锛勫幐闁诲繒鍋犻褎鎱ㄩ崒婧惧亾鐟欏嫭纾搁柛搴㈠▕閸┾偓妞ゆ帒锕︾粔鐢告煕閻樺磭澧甸柟顕嗙節瀵挳濮€閿涘嫬骞嶉梻浣虹帛閸ㄥ爼鏁冮埡浣叉灁闁哄洢鍨洪悡鐔兼煙閹呮憼缂佲偓鐎ｎ喗鐓欐い鏃€鏋婚懓鍧楁煙椤旂晫鎳囨鐐存崌楠炴帡宕卞Ο铏规Ш闂傚倸鍊烽懗鍫曞箠閹捐鐤柛褎顨呯粻鐘荤叓閸ャ劎鈽夊鍛存⒑閸涘﹥澶勯柛銊╀憾瀵彃顭ㄩ崼鐔哄幘濠电偞娼欓鍡椻枍閸ヮ剚鐓涢柛鈩冨姇閳ь剚绻傞～蹇撁洪鍕炊闂佸憡娲﹂崢鐐閸ヮ剚鈷戦柛婵勫劚鏍￠梺鍦嚀濞层倝鎮鹃悜鑺ュ亱闁割偁鍨婚惁鍫ユ⒑閹肩偛鍔€闁告粈鐒﹂弫銈夋⒒閸屾瑨鍏岀紒顕呭灦瀹曟繈寮介妸褏褰鹃梺绯曞墲缁嬫帡宕曟惔銊ョ婵烇綆鍓欐俊浠嬫煃闁垮鐏╃紒杈ㄦ尰閹峰懘鎳栭埄鍐ㄧ仼濞存粍鎮傞、鏃堝醇閻斿搫骞堝┑鐘垫暩婵挳宕幍顔句笉闁绘劗顣介崑鎾斥枔閸喗鐏堝銈庡幖閸㈡煡顢氶敐澶婄妞ゆ棁濮ゅ▍鏍⒑閸涘﹥澶勯柛妯挎椤潡骞嬮敂瑙ｆ嫼闁荤姴娲﹁ぐ鍐吹鏉堚晝纾界€广儱鎳忛ˉ鐐电磼閸屾氨效鐎殿喕绮欓、姗€鎮欑喊澶屽耿闂傚倷娴囬～澶婄暦濮椻偓椤㈡俺顦圭€规洩绻濋獮搴ㄦ嚍閵壯冨妇闂傚鍋勫ú锕€煤閺嵮呮懃闂傚倷娴囬褏鈧灚甯″畷锝夊礃椤垶缍庨梺鎯х箰濠€閬嶆儗濞嗘挻鐓欑紒瀣仢椤掋垽鏌熼悿顖欏惈缂佽鲸鎸婚幏鍛村礈閹绘帒澹夐梻浣告贡椤牊鏅舵禒瀣闁圭儤鎸剧弧鈧┑顔斤供閸撴盯藝椤撱垺鍋℃繝濠傚枤濡偓閻庤娲橀崹鍧楃嵁濡偐纾兼俊顖濇〃閻㈠姊绘担鍛婃儓妞わ缚鍗抽、鏍р枎瀵邦偅绋戦悾婵嬪礋椤戣姤瀚奸梻浣告啞缁哄潡宕曟潏銊уⅰ濠电姷鏁搁崑娑橆嚕閸撲礁鍨濋柟鎹愵嚙缁犵娀鏌ｉ幇顒傛憼缂傚秴娲弻鏇熺箾瑜嶉幊鎰ｉ崶銊х瘈婵炲牆鐏濋弸鐔兼煥閺囨娅婄€规洏鍨洪妶锝夊礃閵娿儱浜?RenderTextureDescriptor
                // this descriptor contains all the information you need to create a new texture
                RenderTextureDescriptor cameraTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;

                // disable the depth buffer because we are not going to use it
                cameraTextureDescriptor.depthBufferBits = 0;

                // scale the texture dimensions
                cameraTextureDescriptor.width = Mathf.RoundToInt(cameraTextureDescriptor.width * _settings.resolutionScale);
                cameraTextureDescriptor.height = Mathf.RoundToInt(cameraTextureDescriptor.height * _settings.resolutionScale);

                // create temporary render texture
           //     cmd.GetTemporaryRT(_occluders.id, cameraTextureDescriptor, FilterMode.Bilinear);//v0.1

                // finish configuration
           //     ConfigureTarget(_occluders.Identifier());//v0.1


                //v0.1
                var renderer = renderingData.cameraData.renderer;
                //v0.1
                //source = renderer.cameraColorTarget;
#if UNITY_2022_1_OR_NEWER
                source = renderer.cameraColorTargetHandle;
#else
                source = renderer.cameraColorTarget;
#endif

            }
#else
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                // get a copy of the current camera闂傚倸鍊搁崐鎼佸磹閹间礁纾归柟闂寸绾惧綊鏌熼梻瀵割槮缁炬儳缍婇弻鐔兼⒒鐎靛壊妲紒鐐劤缂嶅﹪寮婚悢鍏尖拻閻庨潧澹婂Σ顔剧磼閻愵剙鍔ょ紓宥咃躬瀵鎮㈤崗灏栨嫽闁诲酣娼ф竟濠偽ｉ鍓х＜闁绘劦鍓欓崝銈囩磽瀹ュ拑韬€殿喖顭烽幃銏ゅ礂鐏忔牗瀚介梺璇查叄濞佳勭珶婵犲伣锝夘敊閸撗咃紲闂佺粯鍔﹂崜娆撳礉閵堝洨纾界€广儱鎷戦煬顒傗偓娈垮枛椤兘骞冮姀銈呯閻忓繑鐗楃€氫粙姊虹拠鏌ュ弰婵炰匠鍕彾濠电姴浼ｉ敐澶樻晩闁告挆鍜冪床闂備胶绮崝锕傚礈濞嗘挸绀夐柕鍫濇川绾剧晫鈧箍鍎遍幏鎴︾叕椤掑倵鍋撳▓鍨灈妞ゎ厾鍏橀獮鍐閵堝懐顦ч柣蹇撶箲閻楁鈧矮绮欏铏规嫚閺屻儱寮板┑鐐板尃閸曨厾褰炬繝鐢靛Т娴硷綁鏁愭径妯绘櫓闂佸憡鎸嗛崪鍐簥闂傚倷鑳剁划顖炲礉閿曞倸绀堟繛鍡樻尭缁€澶愭煏閸繃宸濈痪鍓ф櫕閳ь剙绠嶉崕閬嶅箯閹达妇鍙曟い鎺戝€甸崑鎾斥枔閸喗鐏堝銈庡幘閸忔﹢鐛崘顔碱潊闁靛牆鎳愰ˇ褔鏌ｈ箛鎾剁闁绘顨堥埀顒佺煯缁瑥顫忛搹瑙勫珰闁哄被鍎卞鏉库攽閻愭澘灏冮柛鏇ㄥ幘瑜扮偓绻濋悽闈浶㈠ù纭风秮閺佹劖寰勫Ο缁樻珦闂備礁鎲￠幐鍡涘椽閸愵亜绨ラ梻鍌氬€烽懗鍓佸垝椤栫偛绀夐柨鏇炲€哥粈鍫熺箾閸℃ɑ灏紒鈧径鎰厪闁割偅绻冨婵堢棯閸撗勬珪闁逞屽墮缁犲秹宕曢柆宥呯闁硅揪濡囬崣鏇熴亜閹烘垵鈧敻宕戦幘鏂ユ灁闁割煈鍠楅悘鍫濐渻閵堝骸骞橀柛蹇旓耿閻涱噣宕橀纰辨綂闂侀潧鐗嗛幊鎰八囪閺岋綀绠涢幘鍓侇唹闂佺粯顨嗛〃鍫ュ焵椤掍胶鐓紒顔界懃椤繘鎼圭憴鍕彴闂佸搫琚崕鍗烆嚕閺夊簱鏀介柣鎰緲鐏忓啴鏌涢弴銊ュ箻鐟滄壆鍋撶换婵嬫偨闂堟刀銏犆圭涵椋庣М闁轰焦鍔栧鍕熺紒妯荤彟闂傚倷绀侀幉锟犲箰閸℃稑妞介柛鎰典簻缁ㄣ儵姊婚崒姘偓鐑芥嚄閸撲礁鍨濇い鏍仜缁€澶愭煥閺囩偛鈧摜绮堥崼鐔虹闁糕剝蓱鐏忣厾绱掗埀顒佸緞閹邦厾鍘梺鍓插亝缁诲啫顔忓┑鍫㈡／闁告挆鍕彧闂侀€炲苯澧紒鐘茬Ч瀹曟洟鏌嗗鍛唵闂佺鎻俊鍥矗閺囩喆浜滈柟鐑樺灥閳ь剛鏁诲畷鎴﹀箻閺傘儲鐏侀梺鍓茬厛閸犳鎮橀崼婵愭富闁靛牆楠搁獮姗€鏌涜箛鏃撹€块柣娑卞櫍瀹曟﹢顢欑喊杈ㄧ秱闂備線娼ч悧鍡涘箠閹板叓鍥樄闁哄矉缍€缁犳盯骞橀崜渚囧敼闂備胶绮〃鍡涖€冮崼銉ョ劦妞ゆ帊鑳堕悡顖滅磼椤旂晫鎳冩い顐㈢箻閹煎湱鎲撮崟顐ゅ酱闂備礁鎼悮顐﹀磿閸楃儐鍤曢柡澶婄氨閺€浠嬫煟閹邦厽绶查悘蹇撳暣閺屾盯寮撮妸銉ョ閻熸粍澹嗛崑鎾舵崲濠靛鍋ㄩ梻鍫熷垁閵忕妴鍦兜妞嬪海袦闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰熼梻鍌氬€风粈渚€骞夐敓鐘茬闁糕剝绋戝浠嬫煕閹板吀绨荤紒銊ｅ劦濮婂宕掑顑藉亾閻戣姤鍤勯柛鎾茬閸ㄦ繃銇勯弽顐粶缂佲偓婢舵劖鐓ラ柡鍥╁仜閳ь剙鎽滅划鍫ュ醇閻旇櫣顔曢梺绯曞墲钃遍悘蹇ｅ幘缁辨帡鍩€椤掍礁绶為柟閭﹀幘閸橆亪姊洪崜鎻掍簼缂佽鍟蹇撯攽閸垺锛忛梺鍛婃寙閸曨剛褰ч梻渚€鈧偛鑻晶顔剧磼閻樿尙效鐎规洘娲熼弻鍡楊吋閸涱垼鍞甸梻浣侯攰閹活亝淇婇崶顒€鐭楅柡鍥╁枂娴滄粓鏌熼悜妯虹仴闁逞屽墰閺佽鐣烽幋锕€绠婚柡鍌樺劜閻忎線姊洪崜鑼帥闁哥姵顨婇幃姗€宕煎┑鎰瘜闂侀潧鐗嗘鎼佺嵁濮椻偓閺屾稖绠涢弮鎾光偓鍧楁煟濞戝崬娅嶇€规洘锕㈤、娆戝枈鏉堛劎绉遍梻鍌欑窔濞佳囨偋閸℃稑绠犻柟鏉垮彄閸ヮ亶妯勯梺鍝勭焿缂嶁偓缂佺姵鐩獮姗€宕滄笟鍥ф暭闂傚倷鑳剁划顖炪€冮崱娑栤偓鍐醇閵夈儳鍔﹀銈嗗笂閼冲爼鎮￠婊呯＜妞ゆ梻鏅幊鍐┿亜椤愩垻绠婚柟鐓庢贡閹叉挳宕熼銈呴叡闂傚倷绀侀幖顐ゆ偖椤愶箑纾块柛妤冨剱閸ゆ洟鏌℃径濠勬皑闁衡偓娴犲鐓熼柟閭﹀幗缁舵煡鎮樿箛鎾虫殻闁哄本鐩鎾Ω閵夈儳顔掗柣鐔哥矋婢瑰棝宕戦幘鑸靛床婵犻潧顑嗛崑銊╂⒒閸喎鍨侀柕蹇曞Υ閸︻厽鍏滃瀣捣琚﹂梻浣芥〃閻掞箓宕濋弽褜鍤楅柛鏇ㄥ€犻悢铏圭＜婵☆垵宕佃ぐ鐔兼⒒閸屾艾鈧绮堟笟鈧獮澶愭晸閻樿尙顔囬梺绯曞墲缁嬫垵顪冩禒瀣厱闁规澘鍚€缁ㄨ崵绱掗妸锝呭姦婵﹤顭峰畷鎺戭潩椤戣棄浜鹃柣鎴ｅГ閸ゅ嫰鏌涢幘鑼槮闁搞劍绻冮妵鍕冀椤愵澀绮剁紓浣插亾濠㈣埖鍔栭悡銉╂煛閸モ晛浠滈柍褜鍓欓幗婊呭垝閸儱閱囬柣鏃囨椤旀洟姊虹化鏇炲⒉閽冮亶鎮樿箛锝呭箻缂佽鲸甯￠崺鈧い鎺嶇缁剁偤鏌熼柇锕€骞橀柛姗嗕邯濮婃椽宕滈幓鎺嶇凹缂備浇顕ч崯顐︻敊韫囨挴鏀介柛顐犲灪閿涘繘姊洪崨濠冨瘷闁告洦鍓涜ぐ褔姊绘笟鈧埀顒傚仜閼活垱鏅堕鍓х＜闁绘灏欐晥閻庤娲樺ú鐔煎蓟閸℃鍚嬮柛娑卞灱閸炵敻姊绘担渚敯闁规椿浜濇穱濠囧炊椤掆偓閻鏌￠崶鈺佹瀭濞存粍绮撻弻鐔兼倻濡櫣浠撮梺閫炲苯澧伴柡浣筋嚙閻ｇ兘骞嬮敃鈧粻濠氭煛閸屾ê鍔滈柣蹇庣窔濮婃椽宕滈懠顒€甯ラ梺鍝ュУ椤ㄥ﹪骞冨鈧畷鐓庘攽閹邦厼鐦滈梻渚€娼ч悧鍡橆殽閹间胶宓佹慨妞诲亾闁哄本绋栫粻娑㈠籍閹惧厜鍋撻崸妤佺厽婵炴垵宕▍宥団偓瑙勬礃閿曘垽銆佸▎鎴濇瀳閺夊牄鍔庣粔閬嶆⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鏌熼弶鍨絼闁搞儺浜跺Ο鍕⒑閸濆嫮鐒跨紓宥佸亾缂備胶濮甸惄顖炵嵁濮椻偓瀹曟粍绗熼崶褎鏆梻鍌氬€烽懗鍓佸垝椤栫偑鈧啴宕卞☉鏍ゅ亾閸愵喖閱囬柕澶堝劤閸旓箑顪冮妶鍡楃瑐闁煎啿鐖奸幃锟狀敍濠婂懐锛滃銈嗘⒒閺咁偊骞婇崘鈹夸簻妞ゆ劑鍨荤粻濠氭煙閻撳孩璐￠柟鍙夋尦瀹曠喖顢曢妶鍛村彙闂傚倸鍊风粈渚€骞栭銈嗗仏妞ゆ劧绠戠壕鍧楁煕濡ゅ啫鍓遍柛銈嗘礋閺屸剝寰勬繝鍕殤闂佽　鍋撳ù鐘差儐閻撶喖鏌熼柇锕€骞楃紒鑸电叀閹绠涢幘铏闂佸搫鏈惄顖氼嚕閹绢喖惟闁靛鍎抽鎺楁煟閻斿摜鐭嬪鐟版瀹曟垿骞橀懜闈涘簥濠电娀娼ч鍡涘磻閵娾晜鈷掗柛顐ゅ枎閸ㄤ線鏌曡箛鏇炍ラ柛锔诲幗閸忔粌顪冪€ｎ亝鎹ｇ悮锕傛⒒娴ｈ姤銆冮柣鎺炵畵瀹曟繈寮借閸ゆ鏌涢弴銊モ偓鐘绘偄閾忓湱锛滃┑鈽嗗灥瀹曚絻銇愰悢鍏尖拻闁稿本鑹鹃埀顒勵棑缁牊绗熼埀顒勭嵁婢舵劖鏅搁柣妯垮皺椤斿洭姊虹化鏇炲⒉缂佸鍨甸—鍐╃鐎ｎ偆鍘遍梺瑙勬緲閸氣偓缂併劏宕电槐鎺楀Ω閿濆懎濮﹀┑顔硷工椤嘲鐣烽幒鎴僵妞ゆ垼妫勬禍楣冩煟閹达絽袚闁搞倕瀚伴弻娑㈠箻閼碱剦妲梺鎼炲妽缁诲啴濡甸崟顖氬唨妞ゆ劦婢€缁墎绱撴担鎻掍壕婵犮垼鍩栭崝鏍偂閵夆晜鐓涢柛銉㈡櫅娴犳粓鏌嶈閸撴瑩骞楀鍛灊闁割偁鍎遍柋鍥煟閺冨洦顏犳い鏃€娲熷铏瑰寲閺囩偛鈷夐柦鍐憾閹绠涢敐鍛缂備浇椴哥敮锟犲春閳ь剚銇勯幒宥囶槮缂佸墎鍋ら弻鐔兼焽閿曗偓楠炴﹢鏌涘鍡曢偗婵﹥妞藉畷婊堝箵閹哄秶鍑圭紓鍌欐祰椤曆囧疮椤愶富鏁婇煫鍥ㄦ尨閺€浠嬫煕椤愮姴鐏柨娑樼箻濮婃椽宕ㄦ繝鍐ㄧ樂闂佸憡娲﹂崜娑溿亹瑜斿缁樼瑹閳ь剟鍩€椤掑倸浠滈柤娲诲灡閺呭爼顢欓崜褏锛滈梺缁橆焾鐏忔瑦鏅ラ柣搴ゎ潐濞叉ê煤閻旂厧钃熼柛鈩冾殢閸氬鏌涘☉鍗炵伇闁哥偟鏁诲缁樻媴娓氼垱鏁梺瑙勬た娴滄繃绌辨繝鍥х倞妞ゆ巻鍋撻柣顓燁殜閺岋繝宕堕埡浣圭€繛瀛樼矋缁捇寮婚悢鍏煎€绘俊顖濇娴犳挳姊洪柅鐐茶嫰婢т即鏌℃担绛嬪殭闁伙絿鍏橀獮瀣晝閳ь剛绮绘繝姘厸闁稿本锚閸旀粓鏌ｈ箛搴ｇ獢婵﹥妞藉畷婊堟嚑椤掆偓鐢儵姊洪崫銉バｆい銊ワ躬楠炲啫顫滈埀顒€鐣烽悡搴樻斀闁归偊鍘奸惁婊堟⒒娓氣偓濞佳囨偋閸℃﹩娈介柟闂磋兌瀹撲焦鎱ㄥ璇蹭壕闂佸搫鏈粙鏍不濞戙垹绫嶉柟鎯у帨閸嬫捇宕稿Δ浣哄幗濠电偞鍨靛畷顒€鈻嶅鍥ｅ亾鐟欏嫭绀冮柨鏇樺灲閵嗕礁鈻庨幋婵囩€抽柡澶婄墑閸斿海绮旈柆宥嗏拻闁稿本鐟х粣鏃€绻涙担鍐叉处閸嬪鏌涢埄鍐槈缂佺姷濞€閺岀喖寮堕崹顔肩导闂佹悶鍎烘禍璺何熼崟顖涒拺闁告繂瀚悞璺ㄧ磽瀹ヤ礁浜剧紓鍌欒兌婵敻鎯勯鐐靛祦婵せ鍋撶€规洘绮嶇粭鐔煎炊瑜庨弳顓㈡⒒閸屾艾鈧兘鎳楅崜浣稿灊妞ゆ牜鍋戦埀顒€鍟村畷鍗炩槈閺嶃倕浜鹃柛鎰靛枛闁卞洭鏌曟径鍫濆姢闁告棑绠戦—鍐Χ閸℃鐟愰梺鐓庡暱閻栧ジ骞冮敓鐙€鏁嶆繝濠傛噽閿涙粌顪冮妶鍡樷拻闁哄拋鍋婇獮濠囧礃閳瑰じ绨诲銈嗗姂閸ㄨ崵绮绘繝姘厵闁绘挸瀛╃拹锟犳煙閸欏灏︾€规洜鍠栭、鏇㈠Χ閸涙潙褰欓梻鍌氬€搁崐椋庣矆娓氣偓楠炴牠顢曢妶鍡椾粡濡炪倖鍔х粻鎴犲閸ф鐓曟俊銈呭暙閸撻亶鏌涢妶鍡樼闁哄备鍓濆鍕節閸曨剛娈ら梻浣姐€€閸嬫挸霉閻樺樊鍎愰柣鎾跺枛閺岀喖鏌囬敃鈧獮妤冪磼閼哥數鍙€闁诡喗顭堢粻娑㈠箻閾忣偅顔掗梻浣告惈閺堫剟鎯勯姘煎殨闁圭虎鍠栨儫闂侀潧顦崕鎶筋敊閹烘鐓熼柣鏂挎憸閻顭块悷鐗堫棦鐎规洘鍨块獮姗€鎳滈棃娑樼哎婵犵數濞€濞佳囶敄閸℃稑纾婚柕濞炬櫆閳锋帡鏌涢銈呮瀻闁搞劋绶氶弻鏇＄疀閺囩倣銉╂煕閿旇骞楅柛蹇旂矊椤啰鈧綆浜濋幑锝囨偖閿曞倹鈷掑ù锝堝Г閵嗗啴鏌ｉ幒鐐电暤鐎规洘绻堝鍊燁檨闁搞倖顨婇弻娑㈠即閵娿儮鍋撻悩缁樺亜闁惧繐婀遍敍婊冣攽閳藉棗鐏ョ€规洑绲婚妵鎰版偄閸忓皷鎷婚梺绋挎湰閼归箖鍩€椤掑嫷妫戠紒顔肩墛缁楃喖鍩€椤掑嫮宓佸鑸靛姈閺呮悂鏌ｅΟ鍨毢妞ゆ柨娲铏瑰寲閺囩偛鈷夌紓浣割儐閸ㄧ敻鈥旈崘鈺冾浄閻庯綆鍋嗛崢閬嶆⒑鐟欏嫭绶查柛姘ｅ亾缂備降鍔忓畷鐢垫閹烘惟闁挎繂鎳庢慨锕€鈹戦纭峰伐妞ゎ厼鍢查悾鐑藉箳閹搭厽鍍靛銈嗗灱濡嫭绂嶆ィ鍐╃厽闁硅揪绲借闂佹悶鍊曠€氫即寮诲☉銏犵闁肩⒈鍓﹀Σ顕€姊洪幖鐐插缂佽鍟存俊鐢稿礋椤栨艾鍞ㄥ銈嗗姦濠⑩偓缂侇喖澧庣槐鎾存媴閹绘帊澹曞┑鐘灱閸╂牠宕濋弽顓熷亗闁靛鏅滈悡娑㈡煕閵夈垺娅呭ù鐘崇矒閺屽秷顧侀柛鎾寸懇楠炴顭ㄩ崨顓炵亰闂佸壊鍋侀崕閬嶆煁閸ヮ剚鐓熼柡鍐ㄦ处椤忕姵銇勯弮鈧ú鐔奉潖閾忕懓瀵查柡鍥╁仜閳峰鏌﹂崘顔绘喚闁诡喗顭堢粻娑㈡晲閸涱厾顔掔紓鍌欐祰妞村摜鏁敓鐘叉槬闁逞屽墯閵囧嫰骞掑鍥舵М缂備礁澧庨崑銈夊蓟閻斿吋鐒介柨鏇楀亾妤犵偞锕㈤弻娑橆潨閸℃洟鍋楀┑顔硷攻濡炶棄鐣峰鍫濈闁瑰搫绉堕崙鍦磽閸屾瑦绁版い鏇嗗洤鐤い鎰跺瘜閺佸﹪鐓崶銊﹀皑闁衡偓娴犲鐓曟い鎰Т閻忣亪鏌ㄥ☉妯肩婵﹥妞藉畷銊︾節閸愩劎妯傞梻浣告啞濞诧箓宕滃☉婧夸汗鐟滄柨顫忕紒妯肩懝闁逞屽墴閸┾偓妞ゆ帒鍊告禒婊堟煠濞茶鐏￠柡鍛埣椤㈡瑩宕滆閿涙粓姊虹紒姗嗙劸閻忓繑鐟﹂弲銉╂煟鎼淬値娼愭繛鍙壝叅闁绘梻鍘ч拑鐔兼煕閳╁喚娈㈤柛姘儔閺屾稑鈽夐崡鐐典紘闂佸摜鍋熼弫璇差潖缂佹ɑ濯村〒姘煎灡閺侇垶姊虹憴鍕仧濞存粍绻冪粚杈ㄧ節閸パ呭€炲銈嗗坊閸嬫捇鏌ｉ鐕佹疁闁哄矉绻濆畷鍫曞煛娴ｅ湱鈧厽绻涚€涙鐭嬬紒顔芥崌瀵鎮㈤崗鐓庘偓缁樹繆椤栨繃顏犲ù鐘虫尦濮婃椽鏌呴悙鑼跺濠⒀傚嵆閺屸剝鎷呯粵瀣闂佷紮绲块崗姗€鐛€ｎ喗鏅濋柍褜鍓涚划缁樼節濮橆厾鍘搁梺绋挎湰閿氶柛鏃€绮撻弻锝堢疀鎼达絿鐛㈠┑顔硷攻濡炶棄鐣峰鍫熷殤妞ゆ巻鍋撻悽顖樺劦濮婃椽宕妷銉愶絾銇勯妸銉含妤犵偛鍟～婊堝焵椤掆偓閻ｇ兘鎮℃惔妯绘杸闂佸綊鍋婇崢濂告儊濠婂牊鈷掑〒姘ｅ亾婵炰匠鍥ㄥ亱闁糕剝锕╁▓浠嬫煙闂傜鍏岀€规挷鐒﹂幈銊ヮ渻鐠囪弓澹曢梻浣告惈閺堫剛绮欓弽顐や笉婵炴垯鍨瑰Λ姗€鏌涢埦鈧弲娆撴焽椤栨稏浜滈柕蹇娾偓鍐叉懙闂佽桨鐒﹂崝鏍ь嚗閸曨倠鐔虹磼濡崵褰囬梻鍌氬€烽悞锔锯偓绗涘厾鍝勵吋婢跺﹦锛涢梺瑙勫劤婢у海澹曟總鍛婄厽婵☆垵娅ｉ敍宥夋煃椤栨稒绀嬮柡灞炬礋瀹曟儼顦叉い蹇ｅ幗椤ㄣ儵鎮欓幖顓犲姺闂佸湱鎳撶€氼厼顭囬鍫熷亱闁割偅绻勯崢杈╃磽閸屾艾鈧悂宕愰悜鑺ュ€块柨鏇炲€哥粈澶嬩繆閵堝懏鍣圭痪鎯ь煼閺岋綁骞囬鍌欑驳閻庤娲栧鍓佹崲濠靛顥堟繛鎴濆船閸撻亶姊虹粙娆惧剭闁告梹鍨甸～蹇旂節濮橆剟鍞堕梺缁樻煥閸㈡煡鎮楅鍕拺闂傚牊绋撶粻鐐烘煕婵犲啰澧电€规洘鍔欏畷褰掝敃閿濆懎浼庢繝纰樻閸ㄤ即骞栭锔藉殝鐟滅増甯楅悡鏇熶繆椤栨瑨顒熼柛銈囧枛閺屽秷顧侀柛鎾寸箞閿濈偞寰勬繛鎺楃細缁犳稑鈽夊Ο纭风吹闂傚倸鍊搁悧濠勭矙閹捐姹查柨鏇炲€归悡蹇撯攽閻愭垟鍋撻柛瀣崌閺屾稓鈧綆鍋呯亸顓㈡煃閽樺妲搁柍璇茬У濞煎繘濡搁妷銉︽嚈婵°倗濮烽崑娑氭崲閹烘梹顫曢柟鐑樺殾閻斿吋鍤冮柍鍝勶工閺咁參姊绘担鍛婃儓妞わ富鍨堕幃褔宕卞Ο缁樼彿婵炲濮撮鍛不閺嵮€鏀介柛灞剧閸熺偤鏌ｉ幘瀵告噰闁哄睙鍡欑杸闁挎繂鎳嶇花濂告倵鐟欏嫭绀€闁哄牜鍓熸俊鐢稿礋椤栨凹娼婇梻鍕处缁旂喎螣濮瑰洣绨诲銈嗘尰缁本鎱ㄩ崒婧惧亾鐟欏嫭纾搁柛鏃€鍨块妴浣糕槈濮楀棛鍙嗛梺褰掑亰閸犳牜鑺遍妷鈺傗拻闁稿本鐟︾粊鎵偓瑙勬礀閻忔岸骞堥妸鈺佺骇闁圭偨鍔嶅浠嬪极閸愨晜濯撮柛蹇撴啞閻繘姊绘担鍛婂暈闁告棑绠撳畷浼村冀椤撶喎鈧潡鏌涢…鎴濅簴濞存粍绮撻弻鐔煎传閸曨剦妫炴繛瀛樼矋閸庢娊鍩為幋锔藉€烽柣鎰帨閸嬫挾鈧綆鍓氬畷鍙夌節闂堟侗鍎忕紒鐘崇墪闇夐柛蹇撳悑閸庢鏌℃担闈╄含闁哄矉绠戣灒濞撴凹鍨辨缂傚倷鐒﹂崝妤呭磻閻愬灚宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩闁瑰鍏樺铏圭矙濞嗘儳鍓遍梺鍦嚀濞差參寮幇鐗堝€风€瑰壊鍠栭幃鎴炵節閵忥絾纭炬い鎴濆€块獮蹇撁洪鍛嫽闂佺鏈銊︽櫠濞戞氨纾奸悗锝庡亝鐏忕數绱掗纰辩吋鐎规洘锚闇夐悗锝庡亝閺夊憡淇婇悙顏勨偓鏍ь潖瑜版帒纾块柟鎯版閸屻劑鎮楀☉娅辨粍绂嶅鍫熺厪闊洢鍎崇壕鍧楁煙閸愬弶澶勬い銊ｅ劦閹瑩寮堕幋鐐剁檨闁诲孩顔栭崳顕€宕抽敐鍛殾闁圭儤鍩堥悡銉╂煙闁箑娅嶆俊顐ゅ厴濮婂宕掑顑藉亾閻戣姤鍤勯柛顐ｆ磵閳ь剨绠撳畷濂稿Ψ閵夈儳褰夋俊鐐€栫敮鎺斺偓姘煎弮瀹曟垿鏁嶉崟顒€鏋戦梺鍝勫€藉▔鏇㈠汲閿旂晫绡€闂傚牊绋掗敍宥夋煕濮橆剦鍎旈柡灞剧洴閸╁嫰宕橀妸銉綇缂傚倷闄嶉崝蹇涱敋瑜旈垾鏃堝礃椤斿槈褔鏌涢埄鍏狀亪寮冲Δ鍛拺缂佸顑欓崕鎴︽煕鐎ｃ劌鈧洟鎮鹃悜钘夌骇閻犲洤澧介崰鎾寸閹间礁鍐€鐟滃本绔熼弴銏＄厽闁绘柨鎽滈幊鍐倵濮樼厧骞樺瑙勬礋楠炴牗鎷呴崷顓炲箞闂備線娼ч…鍫ュ磹濡ゅ懏鍎楁繛鍡樺灍閸嬫挸鈻撻崹顔界亪濡炪値鍘奸崲鏌ユ偩閻戣棄纭€闁绘劕绉靛鍦崲濠靛纾兼繛鎴炃氶崑鎾活敍閻愮补鎷绘繛杈剧秬濞咃綁濡存繝鍥ㄧ厓鐟滄粓宕滃▎鎴濐棜妞ゆ挾濮锋稉宥夋煛鐏炶鍔滈柛濠傜仛閹便劌顫滈崱妤€骞嶉梺绋款儍閸ㄦ椽骞堥妸锔剧瘈闁稿被鍊楅崥瀣倵鐟欏嫭绀冮悽顖涘浮閿濈偛鈹戠€ｅ灚鏅為梺鑺ッˇ顔界珶閺囥垺鈷戠憸鐗堝笚閿涚喖鏌ｉ幒鐐电暤鐎规洘鍨归埀顒婄秵娴滄牠寮ㄦ禒瀣厽婵☆垵顕х徊缁樸亜韫囷絽浜伴柡宀嬬秮椤㈡﹢鎮㈤悜妯烘珰闂備礁鎼惉濂稿窗閺嶎厾宓侀柛鈩冨嚬濡查箖姊洪崷顓熸珪濠殿喚鏁搁幑銏犫槈閵忕姴绐涘銈嗙墬閸╁啴寮搁崨瀛樷拺闁告繂瀚悞璺ㄧ磼缂佹绠撻柣锝囧厴瀹曞ジ寮撮悙宥冨姂閺屾洘绔熼姘偓璇参涢鐐粹拻闁稿本鐟ㄩ崗宀€绱掗鍛仸妤犵偞鍔栫换婵嗩潩椤撶偘鐢婚梻浣稿暱閹碱偊骞婃惔銊﹀珔闁绘柨鎽滅粻楣冩煙鐎涙鎳冮柣蹇婃櫇缁辨帡鎮╁畷鍥ｅ闂侀潧娲ょ€氫即鐛Ο鍏煎磯闁烩晜甯囬崹濠氬焵椤掍緡鍟忛柛鐘虫崌瀹曟繈骞嬪┑鎰稁濠电偛妯婃禍婊冾啅濠靛棌鏀介柣妯诲絻椤忣亪鏌涢敍鍗炴处閻撶喖骞栧ǎ顒€鐏╅柛銈庡墴閺屾稑鈻庤箛鎾存婵烇絽娲ら敃顏堝箖閻ｅ瞼鐭欓悹渚厛濡茶淇婇悙顏勨偓鏍偋濡ゅ懎绀勯柣鐔煎亰閻掕棄鈹戦悩鍙夊闁抽攱鍨块弻锟犲磼濡搫濮曞┑鐐叉噹閹虫﹢寮诲☉銏″亹閻庡湱濮撮ˉ婵嬫煣閼姐倕浠遍柡灞剧洴楠炲洭鎮介棃娑樺紬婵犵數鍋為幐鎼佲€﹂悜钘夎摕闁哄洢鍨归柋鍥ㄧ節闂堟稒顥炴繛鍫弮濮婅櫣鎷犻崣澶岊洶婵炲瓨绮犳禍顏勵嚕婵犳艾惟闁宠桨绀佸畵鍡椻攽閳藉棗鐏ユ繛鍜冪稻缁傛帗銈ｉ崘鈹炬嫼闂備緡鍋呯粙鎾诲煘閹烘鐓曢柡鍐ｅ亾闁搞劌鐏濋悾宄懊洪鍕姦濡炪倖甯婇梽宥嗙濠婂牊鐓欓柣鎴灻悘銉︺亜韫囷絼閭柡宀嬬秮楠炴﹢宕￠悙鎻掝潥缂傚倷鑳剁划顖炴儎椤栫偟宓侀悗锝庡枟閸婅埖绻涢懠棰濆敽闂侇収鍨遍妵鍕閳╁喚妫冮梺绯曟櫔缁绘繂鐣烽幒妤€围闁糕檧鏅涢弲顒勬⒒閸屾瑨鍏岀紒顕呭灦瀹曟繈寮借閻掕姤绻涢崱妯哄闁告瑥绻掗埀顒€绠嶉崕鍗炍涘▎鎾崇煑闊洦鎸撮弨浠嬫煟濡搫绾ч柛锝囧劋閵囧嫯绠涢弴鐐╂瀰闂佸搫鐬奸崰鎰八囬悧鍫熷劅闁抽敮鍋撻柡瀣嚇濮婃椽鎮烽幍顕嗙礊婵犵數鍋愰崑鎾愁渻閵堝啫鐏い銊ワ工閻ｇ兘骞掑Δ鈧洿闂佸憡渚楅崰妤€袙瀹€鍕拻闁稿本鑹鹃埀顒佹倐瀹曟劙鎮滈懞銉ユ畱闂佸壊鍋呭ú宥夊焵椤掑﹦鐣电€规洖銈告俊鐑筋敊閼恒儲鐝楅梻鍌欒兌绾爼宕滃┑瀣仭鐟滄棃骞嗙仦瑙ｆ瀻闁规儳顕崢鐢告⒑缂佹ê鐏﹂柨鏇楁櫅閳绘捇寮崼鐔哄幗闂侀潧鐗嗛ˇ閬嶆偩濞差亝鐓涢悘鐐插⒔濞插瓨銇勯姀鈩冪闁轰焦鍔欏畷鍗炩枎濡亶鐐烘⒒娴ｇ瓔鍤欐繛瀵稿厴瀵偊宕ㄦ繝鍐ㄥ伎闂佸湱铏庨崰鎺楀焵椤掑﹤顩柟鐟板婵℃悂鏁冮埀顒勫几閸岀偞鈷戦柛娑橈攻婢跺嫰鏌涘鈧粻鏍箖妤ｅ啫绠氱憸澶愬绩娴犲鐓熼柟閭﹀墮缁狙囨倵濮橆剚鍣界紒杈ㄦ尭椤撳ジ宕熼鐘靛床闁诲氦顫夊ú妯侯渻娴犲鏄ラ柍褜鍓氶妵鍕箳瀹ュ顎栨繛瀛樼矋缁捇寮婚悢鐓庝紶闁告洦鍘滈妷鈺傜厱闊洦鎸鹃悞鎼佹煛鐏炵晫效妞ゃ垺绋戦埥澶娾枎閹搭厽效闂傚倷绀佸﹢閬嶎敆閼碱剛绀婇柍褜鍓氶妵鍕晜閻ｅ苯寮ㄥ┑鈽嗗亗閻掞箑顕ラ崟顓涘亾閿濆骸澧伴柣锕€鐗婄换婵嬫偨闂堟刀銏ゆ煕婵犲啯鍊愭い銏℃椤㈡洟鏁傞悾灞藉箺婵＄偑鍊栭幐楣冨窗鎼粹埗褰掝敋閳ь剟寮婚垾宕囨殕閻庯綆鍓涢敍鐔哥箾鐎电顎撶紒鐘虫崌楠炲啫鈻庨幙鍐╂櫌闂佺琚崐鏍ф毄缂傚倸鍊搁崐鎼佸磹閻戣姤鍤勯柤绋跨仛閸欏繘鏌ｉ姀銏℃毄闁活厽鐟╅弻鐔兼倻濡儵鎷归悗瑙勬礀瀵墎鎹㈠┑瀣棃婵炴垵宕崜鎵磼閻愵剙鍔ら柛姘儔楠炲牓濡搁妷顔藉缓闂佸壊鍋侀崹鏄忔懌闂備浇顕х€涒晛顫濋妸鈺佺獥闁规崘鍩栭～鏇㈡煙閹规劦鍤欑痪鎯у悑閹便劌顫滈崱妤€骞嬮梺绋款儐閹稿藝閻楀牊鍎熼柕蹇曞閳ь剚鐩娲传閸曨噮娼堕梺绋挎唉娴滎剚绔熼弴鐔虹瘈婵﹩鍘鹃崢鐢告⒑閸涘﹥瀵欓柛娑卞幘椤愬ジ姊绘担渚劸闁挎洩绠撳顐ｇ節濮橆剝鎽曢梺闈涱焾閸庮噣寮ㄦ禒瀣厱闁斥晛鍟╃欢閬嶆煃瑜滈崜姘躲€冩繝鍥ц摕闁挎稑瀚ч崑鎾绘晲鎼粹€愁潻闂佸搫顑嗛惄顖炲蓟閳ュ磭鏆嗛悗锝庡墰琚︽俊銈囧Х閸嬬偤鈥﹂崶顒€鐒垫い鎺戝€归弳鈺冪棯椤撯剝纭鹃崡閬嶆煕椤愮姴鍔滈柍閿嬪浮閺屾盯濡烽幋婵囨拱闁宠鐗撳娲传閸曨噮娼堕梺鍛婃煥閻倿宕洪悙鍝勭闁挎洍鍋撴鐐灪娣囧﹪顢涘▎鎺濆妳濠碘剝鐓℃禍璺侯潖缂佹ɑ濯撮悷娆忓娴犫晠姊虹粙鍖℃敾闁告梹鐟ラ悾鐑藉箣閿曗偓缁犲鏌￠崒妯哄姕闁哄倵鍋撻梻鍌欒兌缁垵鎽梺鍛婃尰瀹€绋跨暦椤栨繄鐤€婵炴垶鐟ч崢閬嶆⒑缂佹◤顏嗗椤撶喐娅犻弶鍫氭櫇绾惧吋銇勯弮鍥撴い銉ョ墢閳ь剝顫夊ú鏍Χ缁嬫鍤曢柟缁㈠枟閸婄兘姊婚崼鐔衡槈鐞氭繃绻濈喊澶岀？闁稿鍨垮畷鎰板冀椤愶絾娈伴梺鍦劋椤ㄥ懐绮ｅΔ浣瑰弿婵妫楁晶濠氭煟閹哄秶鐭欓柡宀€鍠栭弻鍥晝閳ь剟鐛Ο鑲╃＜闁绘宕甸悾娲煛鐏炲墽鈽夐柍璇叉唉缁犳盯鏁愰崰鑸妽缁绘繈鍩涢埀顒備沪婵傜浠愰梻浣告惈閻绱炴笟鈧顐﹀箛閺夎法鍊為悷婊冪Ч閻涱喚鈧綆浜跺〒濠氭煏閸繂鏆欓柣蹇ｄ簼閵囧嫰濡搁妷顖氫紣闂佷紮绲块崗姗€骞冮姀銏犳瀳閺夊牄鍔嶅▍鍥⒒娓氣偓濞佳囨偋閸℃稑绠犻幖杈剧悼娑撳秹鏌熼幆鏉啃撻柍閿嬪笒闇夐柨婵嗗椤掔喖鏌￠埀顒佸鐎涙鍘遍柣搴秵閸嬪嫭鎱ㄦ径鎰厵妞ゆ棁顕у畵鍡椻攽閿涘嫬鍘撮柛鈹惧墲閹峰懘宕妷銏犱壕妞ゆ挾鍠嶇换鍡涙煏閸繄绠抽柛鎺嶅嵆閺屾盯鎮ゆ担鍝ヤ桓閻庤娲橀崹鍨暦閻旂⒈鏁嶆繛鎴炶壘楠炴姊绘担绛嬫綈鐎规洘锚閳诲秹寮撮姀鐘殿唹闂佹寧绻傞ˇ浼存偂閻斿吋鐓ユ繝闈涙椤ユ粓鏌嶇紒妯荤闁哄瞼鍠栭、娆戠驳鐎ｎ偆鏉归梻浣虹帛娓氭宕抽敐鍛殾鐟滅増甯╅弫濠囨煟閹惧啿鐦ㄦ繛鍏肩☉閳规垿鎮╁▓鎸庢瘜濠碘剝褰冮幊妯虹暦閹达箑宸濋悗娑櫭禒顓㈡⒑閸愬弶鎯堥柟鍐叉捣缁顫濋懜鐢靛幈闂侀€涘嵆濞佳囧几濞戞氨纾奸柣娆忔噽缁夘噣鏌″畝瀣埌閾伙綁鏌涜箛鎾虫倯婵絽瀚板娲箰鎼达絺濮囩紓渚囧枟閹瑰洤顕ｆ繝姘労闁告劑鍔庣粣鐐寸節閻㈤潧孝閻庢凹鍓熼妴鍌炲醇閺囩啿鎷洪梺鍛婄☉閿曘儵鎮￠妷褏纾煎璺侯儐鐏忥箓鏌熼钘夊姢闁伙綇绻濋獮宥夘敊閼恒儳鏆﹂梻鍌欑窔閳ь剛鍋涢懟顖涙櫠閹绢喗鐓曢柍瑙勫劤娴滅偓淇婇悙顏勨偓鏍暜閹烘绐楁慨姗嗗墻閻掍粙鏌熼柇锕€骞樼紒鐘荤畺閺屾稑鈻庤箛锝嗏枔闂佺粯甯婄划娆撳蓟閳╁啰鐟归柛銉戝嫮褰庢俊銈囧Х閸嬬偟鏁幒鏇犱簷闂備線鈧偛鑻晶鎾煙椤斿厜鍋撻弬銉︽杸闁诲函缍嗘禍鐐侯敊閹邦兘鏀介柣鎴濇川缁夌敻鏌涢幘瀵告噭濞ｅ洤锕幊鏍煛閸愵亷绱查梻浣虹帛閿氶柛鐔锋健閸┿垽寮撮姀锛勫幐閻庡厜鍋撻柍褜鍓熷畷浼村箻鐠囪尙鍔﹀銈嗗笂閼冲爼鍩婇弴鐔翠簻妞ゆ挾鍋炲婵堢磼椤旂⒈鐓兼鐐搭焽缁辨帒螣閻撳骸绠婚梻鍌欑婢瑰﹪鎮￠崼銉ョ；闁告洦鍓氶崣蹇曗偓骞垮劚濡瑩宕ｈ箛鎾斀闁绘ɑ褰冩禍鐐烘煟閹剧懓浜归柍褜鍓濋～澶娒哄Ο鍏兼殰闁圭儤顨呴悡婵嬪箹濞ｎ剙濡肩紒鐘冲▕閺岀喓鈧稒顭囩粻鎾绘煠閸偄濮囬柍瑙勫灴閺佸秹宕熼鈩冩線闂備胶顭堥…鍫ュ磹濠靛棛鏆﹂柕蹇ョ磿闂勫嫰鏌涘☉姗堝伐濞存粓浜跺娲捶椤撶偘澹曢梺娲讳簻缂嶅﹪宕洪埀顒併亜閹达絾纭剁紒鎰⒒閳ь剚顔栭崰鏇犲垝濞嗘挶鈧礁顫滈埀顒勫箖濞嗘挻鍤戦柛銊︾☉娴滈箖鏌涢…鎴濇灀闁衡偓娴犲鐓熼柟閭﹀幗缂嶆垿鏌ｈ箛銉╂闁靛洤瀚伴、姗€鎮欓弶鎴炵亷婵＄偑鍊戦崹娲偡閿曗偓椤曘儵宕熼姘辩杸濡炪倖鎸荤粙鎺斺偓姘偢濮婄粯鎷呮笟顖涙暞闂佺顑嗛崝娆忕暦閹存績妲堥柕蹇曞Х椤︻噣姊洪柅鐐茶嫰婢у瓨鎱ㄦ繝鍐┿仢鐎规洏鍔嶇换婵囨媴閾忓湱鐣抽梻鍌欑閹芥粍鎱ㄩ悽鎼炩偓鍐╃節閸ャ儮鍋撴担绯曟瀻闁规儳鍟垮畵鍡涙⒑闂堟稓绠氭俊鐙欏洤绠紓浣诡焽缁犻箖寮堕崼婵嗏挃闁告帊鍗抽弻鐔烘嫚瑜忕弧鈧Δ鐘靛仜濡繂鐣锋總鍛婂亜闁惧繗顕栭崯搴ㄦ⒑鐠囪尙绠抽柛瀣洴瀹曟劙骞栨担鐟扳偓鍫曟煠绾板崬鍘撮柛瀣尵閹叉挳宕熼鍌ゆК缂傚倸鍊哥粔鎾晝閵堝鍋╅柣鎴ｅГ閸婅崵绱掑☉姗嗗剱闁哄拑缍佸铏圭磼濮楀棛鍔告繛瀛樼矤閸撶喖骞冨畡鎵虫斀闁搞儻濡囩粻姘舵⒑缂佹ê濮﹀ù婊勭矒閸┾偓妞ゆ帊鑳舵晶顏堟偂閵堝洠鍋撻獮鍨姎妞わ富鍨虫竟鏇熺節濮橆厾鍘甸梺鍛婃寙閸涱厾顐奸梻浣虹帛閹稿鎮烽敃鍌毼﹂柛鏇ㄥ灠缁秹鏌涚仦鎹愬濞寸姵锚閳规垿鍩勯崘銊хシ濡炪値鍘鹃崗妯侯嚕鐠囧樊鍚嬮柛銉ｅ妼閻濅即姊洪崫鍕潶闁稿孩妞介崺鈧い鎺嶇贰濞堟粓鏌＄仦鐣屝ら柟椋庡█瀵濡烽妷銉ュ挤濠德板€楁慨鐑藉磻濞戙埄鏁勯柛娑欐綑閻撴﹢鏌熸潏鍓х暠闂佸崬娲弻鏇＄疀閺囩倫銏㈢磼閻橀潧鈻堟慨濠傤煼瀹曟帒顫濋钘変壕闁归棿绀佺壕褰掓煟閹达絽袚闁搞倕瀚伴弻娑㈩敃閿濆棛锛曢梺閫炲苯澧剧紒鐘虫尭閻ｉ攱绺界粙璇俱劍銇勯弮鍥撴繛鍛墦濮婄粯鎷呴搹骞库偓濠囨煕閹惧绠樼紒顔界懇楠炲鎮╅崗鍝ョ憹闂備礁鎼悮顐﹀磿閺屻儲鍋傞柡鍥ュ灪閻撳繐顭块懜寰楊亪寮稿☉姗嗙唵鐟滄粓宕抽敐澶婅摕闁挎稑瀚ч崑鎾绘晲閸屾稒鐝栫紓浣瑰姈濡啴寮婚敍鍕勃闁告挆鍕灡婵°倗濮烽崑娑樏洪鐐嶆盯宕橀妸銏☆潔濠电偛妫欓崹鎶芥焽椤栨粎纾介柛灞剧懆閸忓矂鏌涚€ｎ偅宕岀€规洘娲熼獮搴ㄦ寠婢跺巩鏇㈡煟鎼搭垳绉甸柛鎾磋壘閻ｇ兘宕ｆ径宀€顔曢梺鐟扮摠閻熴儵鎮橀埡鍐＜闁绘瑥鎳愮粔顕€鏌″畝瀣М妤犵偛娲、妤呭焵椤掑嫬姹查柣鎰暯閸嬫捇宕楁径濠佸缂傚倷绀侀鍫濃枖閺囥垹姹查柨鏇炲€归悡娆撳级閸繂鈷旈柣锝堜含缁辨帡鎮╁顔惧悑闂佽鍠楅〃濠偽涢崘銊㈡婵炲棙鍔曢弸鎴︽⒒娴ｉ涓茬紒鐘冲灴閹囧幢濞戞鍘撮梺纭呮彧鐎靛矂寮繝鍥ㄧ厸闁稿本锚閳ь剚鎸惧▎銏ゆ偨閸涘ň鎷洪梺鍛婄☉椤參鎳撶捄銊х＜闁绘ê纾晶顏堟煟閿濆洤鍘撮柡浣瑰姈瀵板嫭绻濋崟鍨為梻鍌欑窔濞佳団€﹂崼銉ュ瀭婵炲樊浜滃Λ姗€鏌嶈閸撶喎顫忕紒妯诲闁告縿鍎查悗顔尖攽閳藉棗浜滈悗姘煎墲閻忓鈹戞幊閸婃洟骞婅箛娑樼９闁割偅娲橀悡鐔兼煛閸屾氨浠㈤柟顔藉灴閺屾稓鈧絽澧庣粔顕€鏌＄仦鐣屝ユい褌绶氶弻娑㈠箻鐠虹儤鐏堥悗娈垮櫘閸嬪﹤鐣峰鈧、娆撴嚃閳轰礁袝濠碉紕鍋戦崐鏍ь啅婵犳艾纾婚柟鎯х亪閸嬫挾鎲撮崟顒€纰嶉柣搴㈠嚬閸橀箖骞戦姀鐘斀閻庯綆鍋掑Λ鍐ㄢ攽閻愭潙鐏﹂懣銈夋煕鐎ｎ偅宕屾鐐寸墬閹峰懘宕妷褎鐎梻鍌欐祰濞夋洟宕伴幘瀛樺弿闁汇垻顭堢壕濠氭煕鐏炲墽銆掔紒鐘荤畺閺屾盯鍩勯崗鈺傚灩娴滃憡瀵肩€涙鍘介梺瑙勫礃濞夋盯鍩㈤崼銉︾厸鐎光偓閳ь剟宕伴弽顓溾偓浣割潨閳ь剟骞冮姀鈽嗘Ч閹艰揪绲鹃崳顖炴⒒閸屾瑧顦﹂柟娴嬧偓鎰佹綎闁革富鍘奸崹婵嬪箹鏉堝墽绋绘い顐ｆ礋閺岀喖鎮滃鍡樼暥闂佺粯甯掗悘姘跺Φ閸曨垰绠抽柟瀛樼妇閸嬫捇鎮界粙璺紱闂佺懓澧界划顖炲疾閺屻儱绠圭紒顔煎帨閸嬫捇鎳犻鈧崵顒勬⒒閸屾瑧顦﹂柟璇х節閹兘濡疯瀹曟煡鏌熼悧鍫熺凡闁绘挻锕㈤弻鐔告綇妤ｅ啯顎嶉梺绋款儐閸旀瑩寮婚悢铏圭＜婵☆垵娅ｉ悷鎻掆攽閻愯尙澧涢柣鎾偓鎰佹綎婵炲樊浜濋ˉ鍫熺箾閹寸偟鎳冩い锔规櫇缁辨挻鎷呯粵瀣闁诲孩鐭崡鍐茬暦濞差亜鐒垫い鎺嶉檷娴滄粓鏌熼悜妯虹仴闁哄鍊栫换娑㈠礂閻撳骸顫嶇紓浣虹帛缁嬫捇骞忛悩渚Ь闂佷紮绲块弫鎼佸焵椤掑喚娼愭繛鍙夛耿瀹曞綊宕滄担鐟板簥濠电娀娼ч鍛閸忚偐绡€濠电姴鍊搁顏呫亜閺冣偓濞茬喎顫忕紒妯诲闁惧繒鎳撶粭鈥斥攽椤旂》宸ユい顓犲厴瀹曞搫鈽夐姀鐘诲敹闂侀潧顦崕鏌ユ倵椤掑嫭鈷戠紒顖涙礀婢ц尙绱掔€ｎ偄娴柟顕嗙節瀹曟﹢顢旈崨顓熺€炬繝鐢靛Т閿曘倝鎮ч崱娆戠焼闁割偆鍠撶粻楣冩煙鐎电浠╁瑙勶耿閺屾盯濡搁敂鍓х杽闂佸搫琚崝宀勫煘閹达箑骞㈡俊顖滃劋閻濆瓨绻濈喊妯哄⒉鐟滄澘鍟撮幃褔骞樼拠鑼舵憰闂佸搫娴勭槐鏇㈡偪閳ь剚绻濋悽闈浶㈤柛鐘冲哺閸┾偓妞ゆ巻鍋撴俊顐ｇ箞瀵鎮㈤崗鑲╁弳闁诲函缍嗛崑浣圭閵忕姭鏀芥い鏃傘€嬮弨缁樹繆閻愯埖顥夐柣锝囧厴婵℃悂鏁傞崜褏妲囬梻浣告啞濞诧箓宕㈤崜褏鐜绘俊銈呮噺閳锋帒霉閿濆嫯顒熼柣鎺斿亾閹便劌顫滈崼銉︻€嶉梺闈涙处濡啴鐛弽銊﹀闁荤喖顣︾純鏇㈡⒑閻熸澘鎮戦柣锝庝邯瀹曟繂顓兼径濠勫幈闂佸湱鍎ら崵姘炽亹閹烘挻娅滈梺鍛婁緱閸犳牠寮抽崼銉︾厽闁绘ê鍟挎慨鈧梺鍛婃尰缁诲牊淇婇悽绋跨妞ゆ牭绲鹃弲锝夋⒑缂佹ê濮嶆繛浣冲毝鐑藉焵椤掑嫭鈷掑ù锝呮啞閹牓鏌熼搹顐ｅ磳鐎规洘妞藉浠嬵敃閵堝洨鍔归梻浣告贡閸庛倝寮婚敓鐘茬；闁瑰墽绮弲鏌ュ箹鐎涙绠橀柡浣圭墪椤啴濡惰箛鎾舵В闂佺顑呴敃銈夛綖韫囨拋娲敂閸曨剙绁舵俊鐐€栭幐楣冨磻濞戞瑤绻嗛柛婵嗗閺€鑺ャ亜閺冨倶鈧螞濮樻墎鍋撶憴鍕闁诲繑姘ㄧ划鈺呮偄閻撳骸宓嗛梺闈涚箳婵兘藝瑜斿濠氬磼濮橆兘鍋撻幖渚囨晪妞ゆ挾濮锋稉宥嗘叏濮楀棗澧绘繛鎾愁煼閺屾洟宕煎┑鍥舵￥婵犫拃灞藉缂佽鲸甯￠、娆撳箚瑜夐弸鍛存⒑鐠団€虫灓闁稿鍊濋悰顕€宕卞☉鎺嗗亾閺嶎厽鏅柛鏇ㄥ墮濞堝苯顪冮妶搴″箲闁告梹鍨甸悾鐑芥偄绾拌鲸鏅ｉ梺缁樏悘姘熆閺嵮€鏀介柣妯诲墯閸熷繘鏌涢悩宕囧⒌闁轰礁鍟存俊鐑藉Ψ椤旇棄鐦滈梻浣藉Г閿氭い锕備憾瀵娊鏁冮崒娑氬帾婵犵數濮寸换鎰般€呴鍌滅＜闁抽敮鍋撻柛瀣崌濮婄粯鎷呮笟顖滃姼闂佸搫鐗滈崜鐔风暦濞嗘挻鍋￠梺顓ㄥ閸旈攱绻濋悽闈浶㈡繛璇х畵閸╂盯骞掗幊銊ョ秺閺佹劙宕ㄩ鍏兼畼闂備浇顕栭崹浼存偋閹捐钃熼柣鏃傚帶缁€鍐煠绾板崬澧版い鏂匡躬濮婃椽鎮烽幍顔炬殯闂佹悶鍔忓Λ鍕偩閻戣姤鍋傞幖瀛樕戦弬鈧梻浣稿閸嬪棝宕伴幇閭︽晩闁哄洢鍨洪埛鎺戙€掑锝呬壕濠电偘鍖犻崟鍨啍闂婎偄娲﹀ú姗€锝為弴銏＄厸闁搞儴鍩栨繛鍥煃瑜滈崜娆撳箠濮椻偓楠炲啫顭ㄩ崼鐔锋疅闂侀潧顦崕顕€宕戦幘缁樺仺闁告稑锕﹂崣鍡椻攽閻樼粯娑ф俊顐ｇ箞椤㈡挸螖閳ь剟婀佸┑鐘诧工鐎氼剚鎱ㄥ澶嬬厸鐎光偓閳ь剟宕伴弽顓炶摕闁靛ě鈧崑鎾绘晲鎼粹€茬按婵炲濮伴崹褰掑煘閹达富鏁婄痪顓炲槻婵稓绱撴笟鍥ф珮闁搞劌纾崚鎺楊敇閵忊€充簻闂佺粯鎸稿ù鐑藉磹閻愮儤鍋℃繝濠傚暟閻忛亶鏌ゅú顏冩喚闁硅櫕鐗犻崺锟犲礃椤忓海闂繝鐢靛仩閹活亞寰婇懞銉х彾濠电姴娲ょ壕鍧楁煙閹殿喖顣奸柣鎾存礋閺屾洘绻涜鐎氼厼袙閸儲鈷戦悹鍥ｂ偓铏彎缂備胶濮甸悧鐘荤嵁閸愵喗鍋╃€光偓閳ь剟鎯屽▎鎾寸厱妞ゎ厽鍨甸弸锕傛煃瑜滈崜娆撴倶濠靛鐓橀柟杈剧畱閻擄繝鏌涢埄鍐炬畼濞寸姵鎸冲铏圭矙濞嗘儳鍓遍梺鍛婃⒐閻熲晠濡存担绯曟瀻闁规儳鍟垮畵鍡涙⒑缂佹ɑ顥嗘繛鍜冪秮椤㈡瑩寮撮姀锛勫幐婵犮垼娉涢敃锔芥櫠閺囩儐鐔嗙憸宀€鍒掑▎蹇曟殾婵﹩鍘奸閬嶆倵濞戞瑯鐒介柛姗€浜跺铏圭磼濡儵鎷荤紓渚囧櫘閸ㄥ啿鈽夐悽绋块唶闁哄洨鍠撻崢鎾绘偡濠婂嫮鐭掔€规洘绮岄～婵堟崉閾忚妲遍柣鐔哥矌婢ф鏁幒妤€纾奸柕濞垮劗閺€浠嬫煕鐏炲墽鐭ら柣鎺楃畺閺岋綁骞樼€靛摜鐣奸梺閫炲苯澧紒鐘茬Ч瀹曟洟鏌嗗畵銉ユ处鐎靛ジ寮堕幋鐙呯幢闂備浇顫夋竟鍡樻櫠濡ゅ懏鍋傞柣鏂垮悑閻撴瑩姊洪銊х暠妤犵偞鐗犻弻锝堢疀閹垮嫬濮涚紓浣介哺鐢剝淇婇幖浣肝ㄦい鏃€鍎抽幃鎴炵節濞堝灝鏋涢柨鏇樺劚椤啴鎸婃径灞炬濡炪倖鍔х粻鎴犵矆鐎ｎ偁浜滈柟鎯ь嚟閳洟鏌ｆ惔銊ゆ喚婵﹦绮幏鍛瑹椤栨粌濮兼俊鐐€栭崹鐢稿箠閹版澘鐒垫い鎺嶇閸ゎ剟鏌涘Ο鍝勨挃闁告帗甯為埀顒婄秵閸犳牜绮婚搹顐＄箚闁靛牆瀚崝宥嗙箾閸涱厽鍠樻慨濠呮缁瑩骞愭惔銏″缂傚倷绀侀鍡涘箲閸パ呮殾闁靛骏绱曢々鐑芥倵閿濆骸浜愰柟閿嬫そ閺岋綁鎮㈤崫銉﹀櫑闁诲孩鍑归崜娑氬垝閸儱绀冮柍鍝勫暟椤旀洘绻濋姀锝嗙【妞ゆ垵娲ょ叅閻庣數纭堕崑鎾斥枔閸喗鐏嶉梺璇″枛閸婂潡鎮伴閿亾閿濆骸鏋熼柛瀣姍閺岋綁濮€閵忊晜娈扮紓浣界簿鐏忔瑧妲愰幘璇茬＜婵﹩鍏橀崑鎾绘倻閼恒儱鈧潡鏌ㄩ弴鐐测偓鐢稿焵椤掑﹦鐣电€规洖鐖奸崺锟犲礃瑜忛悷婵嬫⒒娴ｈ櫣甯涢柛鏃€顨堥幑銏犫攽閸喎搴婇梺绯曞墲鑿уù婊勭矒閺岀喖骞嶉搹顐ｇ彅闂佽绻嗛弲鐘诲蓟閿熺姴骞㈡俊顖氬悑閸ｄ即姊洪崫鍕缂佸缍婇獮鎰節閸愩劎绐炲┑鈽嗗灡娴滃爼鏁冮崒娑掓嫼闁诲骸婀辨刊顓㈠吹濞嗘劖鍙忔慨妤€鐗嗛々顒傜磼椤旀鍤欓柍钘夘樀婵偓闁斥晛鍟悵鎶芥⒒娴ｅ憡鎲稿┑顔肩仛缁旂喖宕卞☉妯肩崶闂佸搫璇炵仦鍓х▉濠电姷鏁告慨鐢告嚌閸撗冾棜闁稿繗鍋愮粻楣冩煕閳╁厾顏堟倶閵夈儮鏀介梽鍥ㄦ叏閵堝洦宕叉繝闈涱儐閸嬨劑姊婚崼鐔衡棩缂侇喖鐖煎娲箰鎼淬垹顦╂繛瀛樼矋缁捇銆佸鑸垫櫜濠㈣泛谩閳哄懏鐓ラ柡鍐ㄥ€瑰▍鏇犵磼婢跺﹦鍩ｆ慨濠冩そ瀹曟﹢宕ｆ径瀣壍闂備胶顭堥敃锕傚箠閹捐鐓濋柟鐐綑椤曢亶鎮楀☉娅辨岸骞忛搹鍦＝闁稿本鐟ч崝宥夋煥濮橆兘鏀芥い鏂垮悑閸犳﹢鏌″畝瀣К缂佺姵鐩顒勫垂椤旇姤鍤堥梻鍌欑劍鐎笛呮崲閸曨垰纾婚柕鍫濇媼閸ゆ洟鏌熺紒銏犳灈妞ゎ偄鎳橀弻銊モ槈濡警浠煎Δ鐘靛仜缁夌懓顫忕紒妯诲闁惧繒鎳撶粭锟犳⒑閹肩偛濡介柛搴°偢楠炴顢曢敂鐣屽幗闂婎偄娲﹂幐鍓х不閹绘帩鐔嗛柣鐔哄椤ョ姵淇婇崣澶婂妤犵偞锕㈤獮鍥ㄦ媴閸涘﹤鈧垶姊绘担鍛婂暈濞撴碍顨婂畷浼村冀椤撶偛鍤戦梺缁橆焽缁垶鍩涢幋锔界厱婵犻潧妫楅顏呯節閳ь剟鎮ч崼銏㈩啎闂佸憡鐟ラˇ浠嬫倿娴犲鐓欐鐐茬仢閻忊晠鏌嶉挊澶樻█濠殿喒鍋撻梺缁橆焾鐏忔瑩鍩€椤掑骞栨い顏勫暣婵″爼宕卞Ο閿嬪闂備礁鎲￠…鍡涘炊妞嬪海鈼ゆ繝娈垮枟椤牓宕洪弽顓炵厱闁硅揪闄勯悡鐘测攽椤旇棄濮囬柍褜鍓氬ú鏍偤椤撶喓绡€闁汇垽娼ф禒婊堟煟椤忓啫宓嗙€规洘绻堥獮瀣攽閸喐顔曢梻浣侯攰閹活亞绮婚幋婵囩函闂傚倷绀侀幉锛勫垝閸儲鍊块柨鏂垮⒔缁€濠囧箹濞ｎ剙濡介柍閿嬪灴閺岀喖顢涢崱妤佸櫧妞ゆ梹鍨甸—鍐Χ鎼粹€茬盎濡炪倧绠撴禍鍫曞春閳ь剚銇勯幒宥堝厡妞わ綀灏欓埀顒€鍘滈崑鎾绘煙闂傚顦﹂柦鍐枔閳ь剙绠嶉崕閬嵥囬姘殰闂傚倷绶氬褔鈥﹂鐔剁箚闁搞儺浜楁禍鍦喐閺傛娼栭柧蹇氼潐鐎氭岸鏌ょ喊鍗炲妞ゆ柨娲ㄧ槐鎾存媴閸撳弶楔闂佺娅曢幑鍥晲閻愬墎鐤€闁瑰彞鐒﹀浠嬨€侀弮鍫濆窛妞ゆ挾鍣ラ崥瀣⒒閸屾瑧绐旀繛浣冲洦鍋嬮柛鈩冪☉缁犵娀鐓崶銊﹀瘱闁圭儤顨呯粻娑㈡煟濡も偓閻楀啴骞忓ú顏呪拺闁告稑锕︾粻鎾绘倵濮樼厧寮€规洘濞婇幐濠冨緞閸℃ɑ鏉搁梻浣虹帛閸斿繘寮插☉娆戭洸闁诡垎鈧弨浠嬫煟濡椿鍟忛柡鍡╁灦閺屽秷顧侀柛鎾寸懅婢规洟顢橀姀鐘殿啈闂佺粯顭囩划顖炴偂濞嗘垟鍋撻悷鏉款伀濠⒀勵殜瀹曠敻宕堕浣哄幍闂佹儳娴氶崑鍡樻櫠椤忓牊鐓冮悷娆忓閻忓鈧娲栭悥濂稿灳閿曞倸绠ｉ柣鎴濇椤ュ牓姊绘担绛嬪殭婵﹫绠撻敐鐐村緞婵炴帡缂氱粻娑樷槈濡⒈妲烽梻浣侯攰閹活亞鎷归悢鐓庣劦妞ゆ垼娉曢ˇ锔姐亜椤愶絿鐭掗柛鈹惧亾濡炪倖甯掔€氼剛绮婚弽顓熺厓闁告繂瀚崳鍦磼閻樺灚鏆柡宀€鍠撻幏鐘诲灳閸忓懐鍑规繝鐢靛仜瀵爼鎮ч悩璇叉槬闁绘劕鎼粻锝夋煥閺冨洤袚婵炲懏鐗犲娲川婵犲啫顦╅梺绋款儏閸婂灝顕ｉ崼鏇ㄦ晣闁靛繆妾ч幏铏圭磽娴ｅ壊鍎忛悘蹇撴嚇瀵劍绻濆顓犲幗濠德板€撻懗鍫曟儗閹烘柡鍋撳▓鍨珮闁稿锕妴浣割潩鐠鸿櫣鍔﹀銈嗗笒鐎氼喖鐣垫笟鈧弻鐔兼倻濮楀棙鐣跺┑鈽嗗亝閿曘垽寮诲☉妯锋斀闁糕剝顨忔禒瑙勭節閵忥綆娼愭繛鍙夌墵閸╃偤骞嬮敂缁樻櫓缂備焦绋戦鍥礉閻戣姤鈷戦柛娑橆煬閻掑ジ鏌涢弴銊ュ闁绘繄鍏樺铏规喆閸曢潧鏅遍梺鍝ュУ濮樸劍绔熼弴銏犵缂佹妗ㄧ花濠氭⒑閸濆嫬鏆欐繛灞傚€栫粋宥夋偡闁妇鍞甸悷婊冩捣缁瑩骞樼拠鑼姦濡炪倖宸婚崑鎾绘煟韫囨棁澹樻い顓炵仢铻ｉ悘蹇旂墪娴滅偓鎱ㄥ鍡椾簻鐎规挸妫濋弻锝呪槈閸楃偞鐝濋悗瑙勬礀閻栧ジ銆佸Δ浣哥窞閻庯綆鍋呴悵婵嬫⒒閸屾瑨鍏岀紒顕呭灥閹筋偊鎮峰鍕凡闁哥噥鍨辩粚杈ㄧ節閸パ呭€炲銈嗗笂鐠佹煡骞忔繝姘拺缂佸瀵у﹢浼存煟閻旀繂娉氶崶顒佹櫇闁逞屽墴閳ワ箓宕稿Δ浣镐画闁汇埄鍨奸崰娑㈠触椤愶附鍊甸悷娆忓缁€鍐煕閵娿儳浠㈤柣锝囧厴婵℃悂鍩℃繝鍐╂珫婵犵數鍋為崹鍫曟晪缂備降鍔婇崕闈涱潖缂佹ɑ濯撮柛娑橈工閺嗗牓姊洪崨濠冾棖缂佺姵鎸搁悾鐑筋敍閻愭潙浜滅紓浣割儓濞夋洜绮ｉ悙鐑樷拺鐟滅増甯掓禍浼存煕濡搫鎮戠紒鍌氱Х閵囨劙骞掗幘顖涘婵犳鍠氶幊鎾趁洪妶澶嬪€舵い鏇楀亾闁哄备鍓濋幏鍛喆閸曨偀鍋撻幇鐗堢厸濞达絽鎽滃瓭濡炪値鍘归崝鎴濈暦婵傚憡鍋勯柛娆忣槸椤忓湱绱撻崒姘偓鎼佸磹閻戣姤鍊块柨鏇炲€歌繚闂佺粯鍔曢幖顐ょ不閿濆鐓ユ繝闈涙閺嗘洘鎱ㄧ憴鍕垫疁婵﹥妞藉畷銊︾節閸屾鏇㈡⒑閸濄儱校闁绘濞€楠炲啴鏁撻悩鎻掑祮闂侀潧楠忕槐鏇㈠储閹间焦鈷戦柛娑橈工婵倿鏌涢弬鍨劉闁哄懓娉涢埥澶愬閿涘嫬骞楅梻浣筋潐瀹曟ê鈻斿☉銏犲嚑婵炴垯鍨洪悡娑㈡倶閻愰潧浜剧紒鈧崘顏佸亾閸偅绶查悗姘煎墲閻忔帡姊虹紒妯虹仸妞ゎ厼娲︾粋宥夋倷閻戞ǚ鎷婚梺绋挎湰閻熝呮嫻娴煎瓨鐓曟繛鍡楃箻閸欏嫮鈧娲栫紞濠囧蓟閸℃鍚嬮柛娑樺亰缁犳捇寮诲☉銏犲嵆闁靛鍎虫禒鈺侇渻閵堝骸浜滈柟铏耿瀵顓兼径瀣檮婵犮垼娉涢鍛枔瀹€鍕拺闂侇偆鍋涢懟顖涙櫠椤曗偓閺岋綀绠涢弮鍌滅杽闂佺硶鏅濋崑銈夌嵁鐎ｎ喗鏅滅紓浣股戝▍鎾绘⒒娴ｈ棄鍚归柛鐘崇墵閵嗗倿鏁傞悾宀€褰鹃梺鍝勬储閸ㄦ椽鍩涢幒妤佺厱閻忕偛澧介幊鍛亜閿旇偐鐣甸柡灞剧洴閹垽鏌ㄧ€ｎ亙娣俊銈囧Х閸嬬偤鎮ч悩姹団偓渚€寮撮姀鈩冩珖闂侀€炲苯澧板瑙勬礃缁绘繂顫濋鐘插箞闂備焦鏋奸弲娑㈠疮閵婏妇顩叉俊銈勮兌缁犻箖鏌熺€涙鎳冮柣蹇婃櫊閺屾盯骞囬妸銉ゅ婵犵绱曢崑鎴﹀磹閺嶎厼绀夐柟杈剧畱绾捐淇婇妶鍛櫤闁哄拋鍓氱换婵嬫濞戝崬鍓遍梺鎶芥敱閸ㄥ湱妲愰幒鏂哄亾閿濆骸浜滄い鏇熺矋缁绘繈鍩€椤掍礁顕遍悗娑欘焽閸樹粙姊虹紒妯烩拹婵炲吋鐟﹂幈銊╁磼濞戞牔绨婚梺闈涢獜缁辨洟骞婇崟顓涘亾閸偅绶查悗姘煎櫍閸┾偓妞ゆ帒锕﹀畝娑㈡煛閸涱喚绠樼紒顔款嚙椤繈鎳滅喊妯诲缂傚倸鍊烽悞锕傗€﹂崶顒€绠犻柛鏇ㄥ灡閻撶喖鏌ｉ弮鈧娆撳礉濮樿埖鍋傞柕鍫濐槹閻撳繘鏌涢锝囩畺闁瑰吋鍔栭妵鍕Ψ閿曚礁顥濋梺瀹狀潐閸ㄥ潡骞冮埡浣烘殾闁搞儴鍩栧▓褰掓⒒娴ｈ櫣甯涢柟绋款煼閹兘鍩￠崨顓℃憰濠电偞鍨崹鍦不缂佹ü绻嗛柕鍫濆€告禍楣冩⒑閻熸澘鏆辨繛鎾棑閸掓帡顢橀姀鈩冩闂佺粯蓱閺嬪ジ骞忛搹鍦＝闁稿本鐟ч崝宥夋嫅闁秵鍊堕煫鍥ㄦ礃閺嗩剟鏌＄仦鍓ф创闁诡喒鏅涢悾鐑藉炊瑜夐幏浼存⒒娴ｈ櫣甯涘〒姘殜瀹曟娊鏁愰崪浣告婵炲濮撮鍌炲焵椤掍胶澧靛┑锛勫厴婵＄兘鍩℃担绋垮挤濠碉紕鍋戦崐鎴﹀垂濞差亗鈧啯绻濋崶褎妲梺閫炲苯澧柕鍥у楠炴帡骞嬪┑鎰棯闂備胶顭堥敃銉р偓绗涘懏宕叉繛鎴烇供閸熷懏銇勯弮鍥у惞闁告垵缍婂铏圭磼濡闉嶅┑鐐跺皺閸犳牕顕ｆ繝姘櫜濠㈣泛锕﹂惈鍕⒑閸撴彃浜介柛瀣嚇钘熼柡宥庡亞绾捐棄霉閿濆懏鎯堢€涙繂顪冮妶搴″箻闁稿繑锚椤曪絾绻濆顓熸珫闂佸憡娲︽禍婵嬪礋閸愵喗鈷戦柛娑橈攻鐎垫瑩鏌涢弴銊ヤ簻闁诲骏绻濆濠氬磼濞嗘劗銈板銈嗘礃閻楃姴鐣锋导鏉戠婵°倐鍋撻柣銈囧亾缁绘盯骞嬪▎蹇曚痪闂佺顑傞弲鐘诲蓟閿濆绠ｉ柨婵嗘－濡嫰姊虹紒妯哄闁挎洩绠撻獮澶愬川婵犲倻绐為梺褰掑亰閸撴盯顢欓崱妯肩閻庣數顭堢敮鍫曟煟鎺抽崝鎴﹀春濞戙垹绠抽柟鐐藉妼缂嶅﹪寮幇鏉垮窛妞ゆ棁妫勯崜闈涒攽閻愬樊鍤熷┑顕€娼ч—鍐╃鐎ｎ亣鎽曞┑鐐村灦閻燂箓宕曢悢鍏肩厪闁割偅绻冮崳褰掓煙椤栨粌浠х紒杈ㄦ尰缁楃喖宕惰閻忓牆顪冮妶搴″箻闁稿繑锕㈤幃浼搭敊閸㈠鍠栧畷妤呮偂鎼达絽閰遍梻鍌欐祰閸嬫劙鍩涢崼銉ョ婵炴垯鍨瑰Ч鍙夈亜閹板墎鎮肩紒鈾€鍋撻梻浣圭湽閸ㄨ棄顭囪閻☆參姊绘担鐟邦嚋婵炴彃绻樺畷鎰攽鐎ｎ亞鐣洪悷婊冩捣閹广垹鈹戠€ｎ亞顦伴梺闈涱焾閸庣増绔熼弴銏♀拺婵懓娲ら悘鍙夌箾娴ｅ啿鍟伴幗銉モ攽閿涘嫬浜奸柛濞垮€濆畷锝夊焵椤掍胶绠惧璺侯儐缁€瀣殽閻愭潙鐏寸€规洜鍠栭、妤呭磼濠婂嫬娈炲┑锛勫亼閸婃牠鎮уΔ鍛殞闁绘劦鍓涢悵鍫曟倵閿濆骸鏋熼柍閿嬪笒閵嗘帒顫濋敐鍛闂備線鈧偛鑻晶浼存煕鐎ｎ偆娲撮柟宕囧枛椤㈡稑鈽夊▎鎰娇濠电姷鏁告慨鐢靛枈瀹ュ鍋傞柡鍥ュ灪閻撴瑩鏌熺憴鍕缁绢參绠栭弻锛勨偓锝庡亞閳洘銇勯妸锝呭姦闁诡喗鐟╅獮鎾诲箳閹炬惌鍞查梻浣稿⒔缁垶鎮ч悩璇茶摕婵炴垶菤濡插牓鏌涘Δ鍐ㄤ户濞寸姭鏅濈槐鎾存媴妞嬪海鐡樼紓浣哄У閹告悂锝炶箛鎾佹椽顢旈崟顓фФ闂備浇鍋愰埛鍫ュ礈濞嗘垶鏆滈柕澶嗘櫆閳锋垿鏌涘┑鍕姎閺嶏繝姊虹紒姗嗘畷妞ゃ劌锕悰顕€宕奸妷銉庘晠鏌ㄩ弮鍥棄闁告柨鎳樺铏光偓鍦У椤ュ淇婇锝囨噮闁逞屽墴濞佳囨儗閸屾凹娼栨繛宸簼椤ュ牊绻涢幋鐐跺妞わ綀娅ｇ槐鎾存媴閾忕懓绗￠梺鍛婃⒐閻熲晠鐛崱妯诲闁告捁灏欓崣鍡涙⒑閸撴彃浜為柛鐘虫崌瀹曘垽鏌嗗鍡忔嫼闂傚倸鐗婃笟妤呮倿妤ｅ啯鐓曢幖娣灩閳绘洟鏌℃担鍝バ㈡い鎾冲悑瀵板嫮鈧綆鍓欓獮鍫ユ⒒娴ｅ憡璐￠柛搴涘€濋獮鎰矙濡潧缍婇、娆撴煥椤栨矮澹曢柣鐔哥懃鐎氼厾绮堥埀顒勬⒑闂堟稓澧涢柟顔煎€搁悾鐑藉传閸曞孩妫冨畷銊╊敇閻樻彃绠版繝鐢靛仩閹活亞寰婃禒瀣簥闁哄被鍎栨径濠庢僵闁煎摜顣介幏缁樼箾鏉堝墽鎮奸柟铏崌椤㈡艾顭ㄩ崨顖滐紲闁荤姴娲﹁ぐ鍐焵椤掆偓缂嶅﹪濡撮崘顔嘉ㄩ柍杞扮缁愭稒绻濋悽闈浶㈤悗姘槻鐓ら柟闂寸劍閳锋垿鏌涘┑鍡楊伀闁宠顦甸弻锝堢疀閺傚灝顫掗梺璇″枤閸忔﹢寮婚崶顒佹櫆闁诡垎鍐╄緢闂傚倸鍊风粈渚€骞夐垾瓒佹椽鏁冮崒姘亶婵°倧绲介崯顖炲磻閸屾凹鐔嗛柤鎼佹涧婵牓鏌嶉柨瀣仼缂佽鲸鎸婚幏鍛存嚃閳╁啫鐏存鐐叉瀹曠喖顢涘☉姘箞闂佽鍑界紞鍡樼瀹勯偊鍟呴柕澶嗘櫆閻撶喖鏌ㄥ┑鍡樻悙闁告ê顕埀顒冾潐濞叉牠濡剁粙娆惧殨闁圭虎鍠楅崐椋庘偓骞垮劙閻掞箑鈻嶆繝鍥ㄧ厸閻忕偠顕ф慨鍌溾偓娈垮枟濞兼瑨鐏冮梺閫炲苯澧紒鍌氱У閵堬綁宕橀埞鐐闂備胶顭堥張顒勫礄閻熸嫈锝夊传閵壯咃紲婵犮垼娉涢張顒勫吹閳ь剙顪冮妶搴′簻缂佺粯鍔楅崣鍛渻閵堝懐绠伴柟宄邦儏铻為柛鎰靛枟閳锋垿鏌涢幇顒€绾ч柟顖氱墦閺屾盯鎮ゆ担鍝ヤ桓閻庤娲橀崹濂杆囩憴鍕弿濠电姴鎳忛鐘崇箾閹寸姵鏆€规洏鍔戦、娆撳箚瑜庨柨顓㈡⒒閸屾瑧顦︽繝鈧柆宥呯疇闁规崘绉ú顏嶆晣闁绘劕鐏氬▓鎯р攽閻愬弶鈻曞ù婊勭箞瀹曟劙鎮滈懞銉у幈濠电娀娼уΛ妤咁敂椤愶絻浜滈柟鎯х摠閵囨繃鎱ㄦ繝鍐┿仢妤犵偞鍔栭幆鏃堝煑閳哄倸顏虹紓鍌氬€风欢锟犲闯椤曗偓瀹曞湱鎹勬笟顖氭闂佸搫琚崕娲极閸℃稒鐓冪憸婊堝礈閻旂厧鏄ラ柣鎰惈缁狅綁鏌ㄩ弮鍥棄闁逞屽墰閸忔﹢寮诲☉妯锋瀻闊浄绲鹃埢鎾斥攽閳藉棗浜為柛瀣枔濡叉劙骞樼€涙ê顎撻梺鎯х箳閹虫挾绮敓鐘斥拺闁告稑锕ラ埛鎰亜閵娿儳澧︾€规洘宀搁獮鎺楀箻閸撲胶妲囬梻浣规偠閸庢挳宕洪弽顓炵柧闁冲搫鎳忛悡鐔肩叓閸ャ劍灏电紒鐘崇叀閺岋絽螖娓氬洦鐤侀悗娈垮櫘閸撶喖宕洪埀顒併亜閹哄秵顦风紒璇叉閺岋綁骞囬崗鍝ョ泿闂侀€炲苯澧柣妤佹礋閿濈偠绠涢弮鍌滅槇濠殿喗锕╅崕鐢稿Ψ閳哄倸鈧敻鏌ㄥ┑鍡涱€楅柡瀣〒缁辨挻鎷呴崣澶嬬彇缂備浇椴搁幐濠氬箯閸涱喗鍙忛柟杈剧到娴滃綊鏌熼獮鍨伈鐎规洖宕埥澶娾枎閹存繂绠洪梻鍌欑缂嶅﹪宕戞繝鍥у偍濠靛倹娼屾径灞稿亾閿濆骸鏋熼柣鎾跺枑閵囧嫰骞樼捄鐑樼亖闂佺懓鍟跨€氼參濡甸崟顖毼╅柨婵嗘噹婵箓姊虹拠鈥虫灍闁荤啙鍥х闁绘ü璀﹀Ο渚€姊洪崨濠傚毈闁稿锕ら～蹇涙倻濡顫￠梺瑙勵問閸犳牗淇婃禒瀣拺闁革富鍘介崵鈧┑鐐叉▕閸欏啴鎮伴鈧畷姗€顢欓懖鈺佸箰闂備礁鎲℃笟妤呭储妤ｅ啯鍋傞柟杈鹃檮閳锋垹绱掔€ｎ厼鍔甸悗姘嵆閺屾盯鎮ゆ担闀愮盎闁绘挶鍊濋弻銊╁即閻愭祴鍋撹ぐ鎺戠；闁稿瞼鍋為悡娑㈡煕閵夈垺娅呴柡瀣⒒缁辨帡鎮╅懡銈囨毇闂佸搫鐬奸崰鎾诲焵椤掑倹鏆╂い顓炵墦閸┾偓妞ゆ帊鑳舵晶鍨殽閻愬樊鍎旈柡浣稿暣閸┾偓妞ゆ帒瀚埛鎺撱亜閺嶎偄浠﹂柣鎾跺枛閺岀喐娼忛崜褍鍩岄悶姘哺濮婅櫣绱掑Ο璇查瀺濠电偠灏欓崰鏍ь嚕婵犳艾鍗抽柣鏃囨椤旀洟姊洪崜鑼帥闁哥姵鐗犻垾鏍ㄥ緞婵犲孩瀵岄梺闈涚墕閹虫劗绮婚崘娴嬫斀闁绘劏鏅涙禍楣冩⒒娴ｅ憡鎯堥柣顒€銈稿畷浼村冀椤撶喎浠掑銈嗘磵閸嬫挾鈧娲栫紞濠囥€佸▎鎾崇煑闁靛绠戞禍婵嬫⒒閸屾艾鈧绮堟笟鈧幃鍧楀炊椤剚鐩畷鐔碱敍閳ь剟宕堕澶嬫櫖闂佺粯鍨靛ú銊х矙韫囨挴鏀介柣妯肩帛濞懷勩亜閹寸偛濮嶇€殿噮鍋婂畷鍫曨敆娴ｅ搫骞楅梻浣筋潐婢瑰棙鏅跺Δ鈧埥澶庮樄婵﹦鍎ょ€佃偐绱欓悩鍐叉灓婵犳鍠栭敃銉ヮ渻娴犲绠犻柨鐔哄Т鍥撮梺鍛婁緱閸犳岸鍩€椤掆偓婢х晫妲愰幘瀵哥懝闁搞儜鍕邯闂備焦妞块崜娆撳Χ閹间胶宓侀柟杈剧畱椤懘鏌ｅ▎灞戒壕濠电偟顑曢崝鎴﹀蓟瀹ュ牜妾ㄩ梺鍛婃尵閸犳牠骞冩导鎼晪闁逞屽墮閻ｅ嘲顫滈埀顒勫春閻愬搫鍨傛い鏃囧吹閸戝綊姊婚崒娆戭槮闁硅绻濆畷婵嬪箣閿曗偓缁€澶愬箹閹碱厾鍘涢柡浣革躬閺屾稑鈽夊Ο鍏兼喖闂佹娊鏀遍崹鍧楀蓟濞戞粎鐤€婵炴垶鐟辩槐鐢告⒑閻撳骸顥忓ù婊庝邯瀵鈽夊▎鎰妳闂侀潧绻掓慨顓㈠几濞嗘挻鍊甸悷娆忓绾炬悂鏌涢妸銈囩煓闁靛棔绶氬顕€宕煎┑瀣暪闂備胶绮弻銊ヮ嚕閸撲讲鍋撳顑惧仮婵﹦绮幏鍛瑹椤栨稒鏆為梻浣侯焾椤戝棝宕濆▎蹇曟殾闁瑰瓨绺惧Σ鍫ユ煏韫囨洖啸妞わ富鍠栭埞鎴︻敊婵劒绮堕梺绋款儐閹瑰洭寮婚敍鍕勃闁告挆鍕灡闁诲孩顔栭崳顕€宕滈悢鑲╁祦闁归偊鍘介崕鐔兼煏韫囧ň鍋撻幇浣风礂闂傚倸鍊搁崐椋庢濮樿泛鐒垫い鎺嶈兌閵嗘帡鏌嶇憴鍕诞闁哄本鐩顒傛崉閵娧冨П婵＄偑鍊戦崹楦挎懌缂備浇娅ｉ弲顐ゅ垝濞嗘垶宕夐柕濞垮劗閸嬫捇鎮欓悜妯锋嫼闂佸湱顭堝ù椋庣不閹剧粯鐓欓柛鎰皺鏍＄紓浣规⒒閸犳牕顕ｉ幘顔碱潊闁挎稑瀚獮妤呮⒒娴ｈ櫣甯涢柡灞诲姂楠炴顭ㄩ崼婵堢枃闂佸憡鍔﹂崰妤呭磹閸偅鍙忔俊顖滃帶鐢埖顨ラ悙鎼劷缂佽鲸甯￠、娆撴嚃閳轰讲鍙洪柣搴ゎ潐濞叉﹢鏁冮姀銈冣偓浣糕枎閹炬潙浠奸柣蹇曞仩濡嫬效閺屻儲鈷掗柛灞剧懅椤︼箓鏌熺喊鍗炰簻閻撱倝鏌ㄩ弴鐐测偓褰掑疾椤忓牊鈷掑ù锝囩摂閸ゆ瑧绱掔紒姗嗘畼濞存粎顭堥埢搴ㄥ箻瀹曞洤濮︽俊鐐€栫敮鎺楀窗濮橆剦鐒介柟閭﹀幘缁犻箖鏌涘▎蹇ｆ＆妞ゅ繐鐗冮埀顒佹瀹曟﹢鍩￠崘鐐カ闂佽鍑界紞鍡樼濠靛鍊垫い鎺戝閳锋垿鏌ｉ悢鍛婄凡闁抽攱姊荤槐鎺楊敋閸涱厾浠搁悗瑙勬礃閸ㄥ潡鐛崶顒佸亱闁割偁鍨归獮妯肩磽娴ｅ搫浜炬繝銏∶悾鐑筋敆娴ｈ鐝烽梺鍛婃寙閸涱垽绱查梻浣告啞椤ㄥ牓宕戝☉姘辨／鐟滃繒妲愰幒鏃傜＜婵☆垵鍋愰悾鐢告⒑瀹曞洨甯涢柟鐟版搐閻ｇ柉銇愰幒婵囨櫓闂佷紮绲芥總鏃堝箟妤ｅ啯鈷掗柛灞剧懅閸斿秹鎮楃粭娑樺幘閸濆嫷鍚嬪璺猴功閺屟囨⒑缂佹﹩鐒鹃悘蹇旂懇瀵娊鏁傞幋鎺旂畾闂侀潧鐗嗛崐鍛婄妤ｅ啯鈷戦悗鍦У椤ュ銇勯敂璇茬仸闁挎繄鍋涢埞鎴犫偓锝呯仛閺咃綁鎮峰鍐╂崳缂佽京鍋涜灃闁告侗鍠掗幏缁樼箾鏉堝墽绉繛鍜冪悼閺侇喖鈽夐姀锛勫幐闁诲繒鍋犻褎鎱ㄩ崒婧惧亾鐟欏嫭纾搁柛搴㈠▕閸┾偓妞ゆ帒锕︾粔鐢告煕閻樺磭澧甸柟顕嗙節瀵挳濮€閿涘嫬骞嶉梻浣虹帛閸ㄥ爼鏁冮埡浣叉灁闁哄洢鍨洪悡鐔兼煙閹呮憼缂佲偓鐎ｎ喗鐓欐い鏃€鏋婚懓鍧楁煙椤旂晫鎳囨鐐存崌楠炴帡宕卞Ο铏规Ш闂傚倸鍊烽懗鍫曞箠閹捐鐤柛褎顨呯粻鐘荤叓閸ャ劎鈽夊鍛存⒑閸涘﹥澶勯柛銊╀憾瀵彃顭ㄩ崼鐔哄幘濠电偞娼欓鍡椻枍閸ヮ剚鐓涢柛鈩冨姇閳ь剚绻傞～蹇撁洪鍕炊闂佸憡娲﹂崢鐐閸ヮ剚鈷戦柛婵勫劚鏍￠梺鍦嚀濞层倝鎮鹃悜鑺ュ亱闁割偁鍨婚惁鍫ユ⒑閹肩偛鍔€闁告粈鐒﹂弫銈夋⒒閸屾瑨鍏岀紒顕呭灦瀹曟繈寮介妸褏褰鹃梺绯曞墲缁嬫帡宕曟惔銊ョ婵烇綆鍓欐俊浠嬫煃闁垮鐏╃紒杈ㄦ尰閹峰懘鎳栭埄鍐ㄧ仼濞存粍鎮傞、鏃堝醇閻斿搫骞堝┑鐘垫暩婵挳宕幍顔句笉闁绘劗顣介崑鎾斥枔閸喗鐏堝銈庡幖閸㈡煡顢氶敐澶婄妞ゆ棁濮ゅ▍鏍⒑閸涘﹥澶勯柛妯挎椤潡骞嬮敂瑙ｆ嫼闁荤姴娲﹁ぐ鍐吹鏉堚晝纾界€广儱鎳忛ˉ鐐电磼閸屾氨效鐎殿喕绮欓、姗€鎮欑喊澶屽耿闂傚倷娴囬～澶婄暦濮椻偓椤㈡俺顦圭€规洩绻濋獮搴ㄦ嚍閵壯冨妇闂傚鍋勫ú锕€煤閺嵮呮懃闂傚倷娴囬褏鈧灚甯″畷锝夊礃椤垶缍庨梺鎯х箰濠€閬嶆儗濞嗘挻鐓欑紒瀣仢椤掋垽鏌熼悿顖欏惈缂佽鲸鎸婚幏鍛村礈閹绘帒澹夐梻浣告贡椤牊鏅舵禒瀣闁圭儤鎸剧弧鈧┑顔斤供閸撴盯藝椤撱垺鍋℃繝濠傚枤濡偓閻庤娲橀崹鍧楃嵁濡偐纾兼俊顖濇〃閻㈠姊绘担鍛婃儓妞わ缚鍗抽、鏍р枎瀵邦偅绋戦悾婵嬪礋椤戣姤瀚奸梻浣告啞缁哄潡宕曟潏銊уⅰ濠电姷鏁搁崑娑橆嚕閸撲礁鍨濋柟鎹愵嚙缁犵娀鏌ｉ幇顒傛憼缂傚秴娲弻鏇熺箾瑜嶉幊鎰ｉ崶銊х瘈婵炲牆鐏濋弸鐔兼煥閺囨娅婄€规洏鍨洪妶锝夊礃閵娿儱浜?RenderTextureDescriptor
                // this descriptor contains all the information you need to create a new texture
                //RenderTextureDescriptor cameraTextureDescriptor = cameraTextureDescriptor;// renderingData.cameraData.cameraTargetDescriptor;

                // disable the depth buffer because we are not going to use it
                cameraTextureDescriptor.depthBufferBits = 0;

                // scale the texture dimensions
                cameraTextureDescriptor.width = Mathf.RoundToInt(cameraTextureDescriptor.width * _settings.resolutionScale);
                cameraTextureDescriptor.height = Mathf.RoundToInt(cameraTextureDescriptor.height * _settings.resolutionScale);

                // create temporary render texture
           //     cmd.GetTemporaryRT(_occluders.id, cameraTextureDescriptor, FilterMode.Bilinear);//v0.1

                // finish configuration
           //     ConfigureTarget(_occluders.Identifier());//v0.1

                //v0.1
                //var renderer = renderingData.cameraData.renderer;
                source = _cameraColorTargetIdent; //source = renderer.cameraColorTarget;
            }
#endif

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!TryGetMainSegi(out Camera mainCamera, out LumenLike segi))
                {
                    return;
                }

                SetVisibleLightsCount(renderingData.cullResults.visibleLights.Length); //v1.6
                bool enableGI = !segi.disableGI;

                //if (!_occludersMaterial || !_radialBlurMaterial) InitializeMaterials();
                if (RenderSettings.sun == null || !RenderSettings.sun.enabled || !enableGI
                    || (Camera.current != null && Camera.current != mainCamera))
                {
                    return;
                }

                Camera cameraA = renderingData.cameraData.camera;
                if (cameraA != mainCamera)
                {
                    return;
                }

                // get command buffer pool
                CommandBuffer cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, VolumetricLightScatteringSampler))
                {
                    //if (1 == 0)
                    //{
                    //    // prepares command buffer
                    //    Graphics.ExecuteCommandBuffer(cmd);
                    //    cmd.Clear();

                    //    Camera camera = renderingData.cameraData.camera;
                    //    context.DrawSkybox(camera);

                    //    DrawingSettings drawSettings = CreateDrawingSettings(
                    //      _shaderTagIdList, ref renderingData, SortingCriteria.CommonOpaque
                    //    );
                    //    drawSettings.overrideMaterial = _occludersMaterial;
                    //    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref _filteringSettings);

                    //    // schedule it for execution and release it after the execution
                    //    Graphics.ExecuteCommandBuffer(cmd);
                    //    CommandBufferPool.Release(cmd);

                    //    //float3 sunDirectionWorldSpace = RenderSettings.sun.transform.forward;
                    //    //float3 cameraDirectionWorldSpace = camera.transform.forward;
                    //    //float3 cameraPositionWorldSpace = camera.transform.position;
                    //    //float3 sunPositionWorldSpace = cameraPositionWorldSpace + sunDirectionWorldSpace;
                    //    //float3 sunPositionViewportSpace = camera.WorldToViewportPoint(sunPositionWorldSpace);
                    //    Vector3 sunDirectionWorldSpace = RenderSettings.sun.transform.forward;
                    //    Vector3 cameraDirectionWorldSpace = camera.transform.forward;
                    //    Vector3 cameraPositionWorldSpace = camera.transform.position;
                    //    Vector3 sunPositionWorldSpace = cameraPositionWorldSpace + sunDirectionWorldSpace;
                    //    Vector3 sunPositionViewportSpace = camera.WorldToViewportPoint(sunPositionWorldSpace);

                    //    //float dotProd = math.dot(-cameraDirectionWorldSpace, sunDirectionWorldSpace);
                    //    //dotProd -= math.dot(cameraDirectionWorldSpace, Vector3.down);

                    //    float dotProd = Vector3.Dot(-cameraDirectionWorldSpace, sunDirectionWorldSpace);
                    //    dotProd -= Vector3.Dot(cameraDirectionWorldSpace, Vector3.down);

                    //    float intensityFader = dotProd / _settings.fadeRange;
                    //    intensityFader = Mathf.Clamp01(intensityFader); //intensityFader = math.saturate(intensityFader);

                    //    _radialBlurMaterial.SetColor("_Color", RenderSettings.sun.color);
                    //    _radialBlurMaterial.SetVector("_Center", new Vector4(
                    //      sunPositionViewportSpace.x, sunPositionViewportSpace.y, 0.0f, 0.0f
                    //    ));
                    //    _radialBlurMaterial.SetFloat("_BlurWidth", _settings.blurWidth);
                    //    _radialBlurMaterial.SetFloat("_NumSamples", _settings.numSamples);
                    //    _radialBlurMaterial.SetFloat("_Intensity", _settings.intensity * intensityFader);

                    //    //_radialBlurMaterial.SetVector("_NoiseSpeed", new float4(_settings.noiseSpeed, 0.0f, 0.0f));
                    //    _radialBlurMaterial.SetVector("_NoiseSpeed", new Vector4(_settings.noiseSpeed.x, _settings.noiseSpeed.y, 0.0f, 0.0f));

                    //    _radialBlurMaterial.SetFloat("_NoiseScale", _settings.noiseScale);
                    //    _radialBlurMaterial.SetFloat("_NoiseStrength", _settings.noiseStrength);

                    //    Blit(cmd, _occluders.Identifier(), _cameraColorTargetIdent, _radialBlurMaterial);
                    //}


                    //v0.1
                    //Graphics.ExecuteCommandBuffer(cmd);
                    //cmd.Clear();
                    RenderTextureDescriptor cameraTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;

                    RenderTexture skyA = EnsureScratchTexture(ref _scratchFrameSource, cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, "_LumenLikeFrameSource");

		     //v0.1
		     cmd.Blit(source, skyA);
                    //context.DrawSkybox(cameraA);
                    //https://www.febucci.com/2022/05/custom-post-processing-in-urp/
                    //var renderer = renderingData.cameraData.renderer;
                    //source = renderer.cameraColorTarget;
                    //context.sky
                    //Blit(cmd, renderingData.cameraData.renderer.cameraColorTarget, skyA);// Blit(cmd, _occluders.Identifier(), skyA);

                    //DEBUG 
                    // Blit(cmd, skyA, _cameraColorTargetIdent);
                    // Graphics.ExecuteCommandBuffer(cmd);
                    //CommandBufferPool.Release(cmd);
                    //RenderTexture.ReleaseTemporary(skyA);                   
                    //return;


                    RenderTexture gi1 = EnsureScratchTexture(ref _scratchFrameResult, cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, "_LumenLikeFrameResult");

                    //renderingData.cameraData.camera = Camera.main;

                    OnRenderImage(cameraA, segi, cmd, skyA, gi1);

                    //v0.1
                    cmd.Blit(gi1, source);// _cameraColorTargetIdent); //1.0g
                    //if (cameraA.targetTexture != null)
                    //{
                    //    //Debug.Log(cameraA.targetTexture.name);
                    //    ///cmd.Blit(gi1, source);
                    //    cmd.SetRenderTarget(cameraA.targetTexture);
                    //    cmd.Blit(gi1, cameraA.targetTexture);// renderingData.cameraData.renderer.cameraColorTargetHandle);
                    //}
                    //else
                    //{
                    //    cmd.Blit(gi1, source);// _cameraColorTargetIdent); //1.0g
                    //}

                    Graphics.ExecuteCommandBuffer(cmd);
                    CommandBufferPool.Release(cmd);

                }
            }



            void OnRenderImage(Camera camera, LumenLike segi, CommandBuffer cmd, RenderTexture source, RenderTexture destination)
            {



                // Graphics.Blit(source, destination);
                //Blit(cmd, source, destination);
                //return;

                if (camera == null || segi == null || !segi.enabled)
                {
                    return;
                }

                if ((camera.depthTextureMode & DepthTextureMode.Depth) == 0)
                {
                    camera.depthTextureMode |= DepthTextureMode.Depth;
                }

                if (segi.notReadyToRender)
                {
                    //Blit(cmd, source, source);
                    //Graphics.Blit(source, destination);
                    //v0.1
		     		cmd.Blit( source, destination);
                    return;
                }

                //Set parameters
                        if (!EnsureSegiMaterial(segi))
                        {
                            cmd.Blit(source, destination);
                            return;
                        }

                        bool surfaceCacheReady = false;
                        int surfaceCacheCurrentIndex = _surfaceCacheHistoryIndex;
                        int surfaceCachePreviousIndex = 1 - surfaceCacheCurrentIndex;
                        if (SurfaceCacheEnabled() && EnsureSurfaceCacheMaterial())
                        {
                            EnsureSurfaceCacheCameraState(camera);
                            int cacheWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * _settings.surfaceCacheResolutionScale));
                            int cacheHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * _settings.surfaceCacheResolutionScale));
                            EnsureSurfaceCacheHistoryTextures(cacheWidth, cacheHeight);
                            PrepareSurfaceCacheMaterial(camera);
                            surfaceCacheReady = true;
                        }
                        ApplySharedSegiMaterialState(segi, surfaceCacheReady && _settings.useSurfaceCacheFallback);
                        segi.material.SetTexture(SurfaceCacheRadianceTextureId, Texture2D.blackTexture);
                        bool requiresImmediateSurfaceCache = _settings.useSurfaceCacheFallback || SurfaceCacheDebugEnabled();

                        //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
                        if (segi.visualizeVoxels)
                        {
                            //Blit(cmd, segi.blueNoise[segi.frameCounter % 64], destination);
                            //v0.1
		     cmd.Blit( source, destination, segi.material, LumenLike.Pass.VisualizeVoxels);
                            return;
                        }

                        //Setup temporary textures
                        int giWidth = source.width / segi.giRenderRes;
                        int giHeight = source.height / segi.giRenderRes;
                        RenderTexture gi1 = EnsureScratchTexture(ref _scratchGiTraceA, giWidth, giHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, "_LumenLikeGiTraceA");
                        RenderTexture gi2 = EnsureScratchTexture(ref _scratchGiTraceB, giWidth, giHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, "_LumenLikeGiTraceB");
                        RenderTexture reflections = null;

                        //If reflections are enabled, create a temporary render buffer to hold them
                        if (segi.doReflections)
                        {
                            reflections = EnsureScratchTexture(ref _scratchReflections, source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, "_LumenLikeReflections");
                        }

                        //Setup textures to hold the current camera depth and normal
                        RenderTexture currentDepth = EnsureScratchTexture(ref _scratchCurrentDepth, giWidth, giHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear, FilterMode.Point, "_LumenLikeCurrentDepth");

                        RenderTexture currentNormal = EnsureScratchTexture(ref _scratchCurrentNormal, giWidth, giHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, FilterMode.Point, "_LumenLikeCurrentNormal");

                        //Get the camera depth and normals
                        //v0.1
		     cmd.Blit( source, currentDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);//v0.1
                                                                                                          //Blit(cmd, source, currentDepth, segi.material, VolumeLitSEGI.Pass.GetCameraDepthTexture);


                        segi.material.SetTexture(CurrentDepthTextureId, currentDepth);
                        //v0.1
		     cmd.Blit( source, currentNormal, segi.material, LumenLike.Pass.GetWorldNormals);
                        segi.material.SetTexture(CurrentNormalTextureId, currentNormal);


                        //v0.1 - check depths
                        if (segi.visualizeNORMALS)
                        {
                            //v0.1
		     cmd.Blit( currentNormal, destination);
                            return;
                        }
                        //if (segi.visualizeDEPTH)
                        //{
                        //    Blit(cmd, currentDepth, destination);
                        //    return;
                        //}



                        //Set the previous GI result and camera depth textures to access them in the shader
                        segi.material.SetTexture(PreviousGITextureId, segi.previousGIResult);
                        Shader.SetGlobalTexture(PreviousGITextureId, segi.previousGIResult);
                        segi.material.SetTexture(PreviousDepthTextureId, segi.previousCameraDepth);

                        //Render diffuse GI tracing result
                        //v0.1
		     cmd.Blit( source, gi2, segi.material, LumenLike.Pass.DiffuseTrace);

                        if (segi.visualizeDEPTH)
                        {
                            //v0.1
		     cmd.Blit( gi2, destination);
                            return;
                        }

                        if (segi.doReflections)
                        {
                            //Render GI reflections result
                            //v0.1
		     cmd.Blit( source, reflections, segi.material, LumenLike.Pass.SpecularTrace);
                            segi.material.SetTexture(ReflectionsTextureId, reflections);
                        }


                        //Perform bilateral filtering
                        if (segi.useBilateralFiltering)
                        {
                            segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));
                            //v0.1
		     cmd.Blit( gi2, gi1, segi.material, LumenLike.Pass.BilateralBlur);

                            segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                            //v0.1
		     cmd.Blit( gi1, gi2, segi.material, LumenLike.Pass.BilateralBlur);

                            segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));
                            //v0.1
		     cmd.Blit( gi2, gi1, segi.material, LumenLike.Pass.BilateralBlur);

                            segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                            //v0.1
		     cmd.Blit( gi1, gi2, segi.material, LumenLike.Pass.BilateralBlur);
                        }

                        //If Half Resolution tracing is enabled
                        if (segi.giRenderRes == 2)
                        {
                            //Setup temporary textures
                            RenderTexture gi3 = EnsureScratchTexture(ref _scratchGiUpsampleA, source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Point, "_LumenLikeGiUpsampleA");
                            RenderTexture gi4 = EnsureScratchTexture(ref _scratchGiUpsampleB, source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Point, "_LumenLikeGiUpsampleB");


                            //Prepare the half-resolution diffuse GI result to be bilaterally upsampled
                            gi2.filterMode = FilterMode.Point;
                            //v0.1
		     cmd.Blit( gi2, gi4);

                            gi4.filterMode = FilterMode.Point;
                            gi3.filterMode = FilterMode.Point;


                            //Perform bilateral upsampling on half-resolution diffuse GI result
                            segi.material.SetVector(KernelId, new Vector2(1.0f, 0.0f));
                            //v0.1
		     cmd.Blit( gi4, gi3, segi.material, LumenLike.Pass.BilateralUpsample);
                            segi.material.SetVector(KernelId, new Vector2(0.0f, 1.0f));

                            //Perform temporal reprojection and blending
                            if (segi.temporalBlendWeight < 1.0f)
                            {
                                //v0.1
		     cmd.Blit( gi3, gi4);
                                //v0.1
		     cmd.Blit( gi4, gi3, segi.material, LumenLike.Pass.TemporalBlend);
                                //v0.1
		     cmd.Blit( gi3, segi.previousGIResult);
                                //v0.1
		     cmd.Blit( source, segi.previousCameraDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);
                            }

                            //Set the result to be accessed in the shader
                            segi.material.SetTexture(GITextureId, gi3);
                            if (surfaceCacheReady)
                            {
                                UpdateSurfaceCacheNonRenderGraph(cmd, source, gi3, surfaceCacheCurrentIndex, surfaceCachePreviousIndex);
                                if (requiresImmediateSurfaceCache)
                                {
                                    Graphics.ExecuteCommandBuffer(cmd);
                                    cmd.Clear();
                                    if (_settings.useSurfaceCacheFallback)
                                    {
                                        segi.material.SetTexture(SurfaceCacheRadianceTextureId, _surfaceCacheRadianceHistory[surfaceCacheCurrentIndex].rt);
                                    }
                                    if (SurfaceCacheDebugEnabled())
                                    {
                                        cmd.Blit(_surfaceCacheRadianceHistory[surfaceCacheCurrentIndex].rt, destination, _surfaceCacheMaterial, 3);
                                        FinalizeSegiFrameState(segi);
                                        FinalizeSurfaceCacheHistory(surfaceCachePreviousIndex);
                                        return;
                                    }
                                }
                            }

                            //Actually apply the GI to the scene using gbuffer data
                            //v0.1
		     cmd.Blit( source, destination, segi.material, segi.visualizeGI ? LumenLike.Pass.VisualizeGI : LumenLike.Pass.BlendWithScene);

                        }
                        else    //If Half Resolution tracing is disabled
                        {
                            //Perform temporal reprojection and blending
                            if (segi.temporalBlendWeight < 1.0f)
                            {
                                //v0.1
		     cmd.Blit( gi2, gi1, segi.material, LumenLike.Pass.TemporalBlend);
                                //v0.1
		     cmd.Blit( gi1, segi.previousGIResult);
                                //v0.1
		     cmd.Blit( source, segi.previousCameraDepth, segi.material, LumenLike.Pass.GetCameraDepthTexture);
                            }

                            //Actually apply the GI to the scene using gbuffer data
                            RenderTexture currentGI = segi.temporalBlendWeight < 1.0f ? gi1 : gi2;
                            segi.material.SetTexture(GITextureId, currentGI);
                            if (surfaceCacheReady)
                            {
                                UpdateSurfaceCacheNonRenderGraph(cmd, source, currentGI, surfaceCacheCurrentIndex, surfaceCachePreviousIndex);
                                if (requiresImmediateSurfaceCache)
                                {
                                    Graphics.ExecuteCommandBuffer(cmd);
                                    cmd.Clear();
                                    if (_settings.useSurfaceCacheFallback)
                                    {
                                        segi.material.SetTexture(SurfaceCacheRadianceTextureId, _surfaceCacheRadianceHistory[surfaceCacheCurrentIndex].rt);
                                    }
                                    if (SurfaceCacheDebugEnabled())
                                    {
                                        cmd.Blit(_surfaceCacheRadianceHistory[surfaceCacheCurrentIndex].rt, destination, _surfaceCacheMaterial, 3);
                                        FinalizeSegiFrameState(segi);
                                        FinalizeSurfaceCacheHistory(surfaceCachePreviousIndex);
                                        return;
                                    }
                                }
                            }
                            //v0.1
		     cmd.Blit( source, destination, segi.material, segi.visualizeGI ? LumenLike.Pass.VisualizeGI : LumenLike.Pass.BlendWithScene);

                        }

                        //Visualize the sun depth texture
                        if (segi.visualizeSunDepthTexture){
                            //v0.1
		     cmd.Blit( segi.sunDepthTexture, destination);
		     }


                        //Set matrices/vectors for use during temporal reprojection
                        FinalizeSegiFrameState(segi);
                        if (surfaceCacheReady)
                        {
                            FinalizeSurfaceCacheHistory(surfaceCachePreviousIndex);
                        }
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
#if UNITY_2020_2_OR_NEWER
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                //cmd.ReleaseTemporaryRT(_occluders.id); //v0.1
            }
#else
            /// Cleanup any allocated resources that were created during the execution of this render pass.
            private RenderTargetHandle destination { get; set; }
            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(_occluders.id);
                if (destination != RenderTargetHandle.CameraTarget)
                {
                    cmd.ReleaseTemporaryRT(destination.id);
                    destination = RenderTargetHandle.CameraTarget;
                }                
            }
#endif

            //private void InitializeMaterials()
            //{
            //    _occludersMaterial = new Material(Shader.Find("Hidden/UnlitColor"));
            //    _radialBlurMaterial = new Material(Shader.Find("Hidden/RadialBlur"));
            //}
        }

        private LightScatteringPass _scriptablePass;
        public VolumetricLightScatteringSettings _settings = new VolumetricLightScatteringSettings();

#if UNITY_EDITOR
        bool _editorFeatureRefreshQueued;

        void QueueEditorFeatureRefresh()
        {
            if (Application.isPlaying || _editorFeatureRefreshQueued)
            {
                return;
            }

            _editorFeatureRefreshQueued = true;
            EditorApplication.delayCall -= PerformQueuedEditorFeatureRefresh;
            EditorApplication.delayCall += PerformQueuedEditorFeatureRefresh;
        }

        void PerformQueuedEditorFeatureRefresh()
        {
            _editorFeatureRefreshQueued = false;
            if (this == null || Application.isPlaying)
            {
                return;
            }

            Create();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        void HandleUndoRedo()
        {
            QueueEditorFeatureRefresh();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.undoRedoPerformed += HandleUndoRedo;
            QueueEditorFeatureRefresh();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.delayCall -= PerformQueuedEditorFeatureRefresh;
            _editorFeatureRefreshQueued = false;
        }

        void OnValidate()
        {
            QueueEditorFeatureRefresh();
        }
#endif

        /// <inheritdoc/>
        public override void Create()
        {
            _scriptablePass?.ReleaseSurfaceCacheResources();
            _scriptablePass = new LightScatteringPass(_settings);

            // Configures where the render pass should be injected.
            _scriptablePass.renderPassEvent = _settings.eventA;// RenderPassEvent.BeforeRenderingPostProcessing;
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
renderer.EnqueuePass(_scriptablePass);
            //_scriptablePass.SetCameraColorTarget(renderer.cameraColorTarget);//1.0g
        }

        protected override void Dispose(bool disposing)
        {
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.delayCall -= PerformQueuedEditorFeatureRefresh;
            _editorFeatureRefreshQueued = false;
#endif
            _scriptablePass?.ReleaseSurfaceCacheResources();
        }

    }
}










