using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
#if UNITY_EDITOR
using UnityEditor;
#endif

// v0.3

namespace LumenLike
{
    [ExecuteInEditMode]
//#if UNITY_5_4_OR_NEWER
//    [ImageEffectAllowedInSceneView]
//#endif
    [RequireComponent(typeof(Camera))]
    public class LumenLike : MonoBehaviour
    {
        static readonly int SegiVoxelAAId = Shader.PropertyToID("SEGIVoxelAA");
        static readonly int SegiVoxelSpaceOriginDeltaId = Shader.PropertyToID("SEGIVoxelSpaceOriginDelta");
        static readonly int WorldToCameraId = Shader.PropertyToID("WorldToCamera");
        static readonly int SegiVoxelViewFrontId = Shader.PropertyToID("SEGIVoxelViewFront");
        static readonly int SegiVoxelViewLeftId = Shader.PropertyToID("SEGIVoxelViewLeft");
        static readonly int SegiVoxelViewTopId = Shader.PropertyToID("SEGIVoxelViewTop");
        static readonly int SegiWorldToVoxelId = Shader.PropertyToID("SEGIWorldToVoxel");
        static readonly int SegiVoxelProjectionId = Shader.PropertyToID("SEGIVoxelProjection");
        static readonly int SegiVoxelProjectionInverseId = Shader.PropertyToID("SEGIVoxelProjectionInverse");
        static readonly int SegiVoxelResolutionId = Shader.PropertyToID("SEGIVoxelResolution");
        static readonly int SegiVoxelToGiProjectionId = Shader.PropertyToID("SEGIVoxelToGIProjection");
        static readonly int SegiSunlightVectorId = Shader.PropertyToID("SEGISunlightVector");
        static readonly int GiSunColorId = Shader.PropertyToID("GISunColor");
        static readonly int SegiSkyColorId = Shader.PropertyToID("SEGISkyColor");
        static readonly int GIGainShaderId = Shader.PropertyToID("GIGain");
        static readonly int SegiSecondaryBounceGainId = Shader.PropertyToID("SEGISecondaryBounceGain");
        static readonly int SegiSoftSunlightId = Shader.PropertyToID("SEGISoftSunlight");
        static readonly int SegiSphericalSkylightId = Shader.PropertyToID("SEGISphericalSkylight");
        static readonly int SegiInnerOcclusionLayersId = Shader.PropertyToID("SEGIInnerOcclusionLayers");
        static readonly int SegiSunDepthId = Shader.PropertyToID("SEGISunDepth");
        static readonly int SegiSunDepthTexelSizeId = Shader.PropertyToID("SEGISunDepth_TexelSize");
        static readonly int CutoffGiId = Shader.PropertyToID("_CutoffGI");
        static readonly int ShadowedLocalPowerId = Shader.PropertyToID("shadowedLocalPower");
        static readonly int ShadowlessLocalPowerId = Shader.PropertyToID("shadowlessLocalPower");
        static readonly int ShadowlessLocalOcclusionId = Shader.PropertyToID("shadowlessLocalOcclusion");
        static readonly int SegiVolumeLevel0Id = Shader.PropertyToID("SEGIVolumeLevel0");
        static readonly int SegiSecondaryConesId = Shader.PropertyToID("SEGISecondaryCones");
        static readonly int SegiSecondaryOcclusionStrengthId = Shader.PropertyToID("SEGISecondaryOcclusionStrength");
        static readonly int SegiVolumeTexture1Id = Shader.PropertyToID("SEGIVolumeTexture1");
        //v1.9
        //v1.7
        [HideInInspector] public int voxelizerID = 1;
        [HideInInspector] public int bouncerID = 1;

        //v1.6
        [HideInInspector] public bool renderSunWithSRP = false; //use Standard pipeline to render sun depth texture
        [HideInInspector] public Vector3 cutoff = new Vector3(1, 1, 0);

        //v1.5
        public bool enableLocalLightGI = false;
        public float shadowedLocalPower = 0;
        public float shadowlessLocalPower = 0;
        public float shadowlessLocalOcclusion = 0;

        //v1.3
        [HideInInspector] public bool proxyNoGeom = false;

        //v1.2
        public float smoothNormals = 0;

        //v0.4
        public bool disableGI = false;
        [HideInInspector] public float contrastA = 0;
        [HideInInspector] public Vector4 ReflectControl = new Vector4(1, 1, 0, 0);

        //v0.7
        [HideInInspector] public Vector4 DitherControl = new Vector4(0, 1, 1, 1);

        //v0.3
        [HideInInspector] public int helperRendererID = 1;//2ond renderer in the pipeline, with render objects renderer feature with preRenderers override material

        //v0.2
        [HideInInspector] public bool colorizeVolume = true;
        public bool updatePerDistance = false;
        public bool updatePerTime = false;
        public float updateTime = 1;
        public float updateDistance = 10;
        float lastUpdateTime = 0;
        Vector3 lastUpdatePos = Vector3.zero;
        Vector3 lastTransformPos = Vector3.zero;
        [HideInInspector] public bool clearBounceCameraTarget = false;

        //v0.1
        [HideInInspector] public Material preRenderers;//feed the material to renderer feature 

        #region Parameters
        [Serializable]
        public enum VoxelResolution
        {
            low = 128,
            high = 256,
            insane = 512
        }

        public bool updateGI = true;
        public LayerMask giCullingMask = 2147483647;
        public float shadowSpaceSize = 50.0f;
        public Light sun;

        public Color skyColor;

        public float voxelSpaceSize = 25.0f;

        public bool useBilateralFiltering = false;

        [Range(0, 2)]
        public int innerOcclusionLayers = 1;


        [Range(0.01f, 1.0f)]
        public float temporalBlendWeight = 0.1f;


        public LumenLike.VoxelResolution voxelResolution = LumenLike.VoxelResolution.high;

        public bool visualizeSunDepthTexture = false;
        public bool visualizeGI = false;
        public bool visualizeVoxels = false;

        public bool visualizeDEPTH = false;
        public bool visualizeNORMALS = false;

        public bool halfResolution = true;
        public bool stochasticSampling = true;
        public bool infiniteBounces = false;
        public Transform followTransform;
        [Range(1, 128)]
        public int cones = 6;
        [Range(1, 32)]
        public int coneTraceSteps = 14;
        [Range(0.1f, 2.0f)]
        public float coneLength = 1.0f;
        [Range(0.5f, 6.0f)]
        public float coneWidth = 5.5f;
        [Range(0.0f, 4.0f)]
        public float occlusionStrength = 1.0f;
        [Range(0.0f, 4.0f)]
        public float nearOcclusionStrength = 0.5f;
        [Range(0.001f, 4.0f)]
        public float occlusionPower = 1.5f;
        [Range(0.0f, 4.0f)]
        public float coneTraceBias = 1.0f;
        [Range(0.0f, 14.0f)]
        public float nearLightGain = 1.0f;
        [Range(0.0f, 14.0f)]
        public float giGain = 1.0f;
        [Range(0.0f, 14.0f)]
        public float secondaryBounceGain = 1.0f;
        [Range(0.0f, 16.0f)]
        public float softSunlight = 0.0f;

        [Range(0.0f, 8.0f)]
        public float skyIntensity = 1.0f;

        public bool doReflections = true;
        [Range(12, 512)]
        public int reflectionSteps = 64;
        [Range(0.001f, 4.0f)]
        public float reflectionOcclusionPower = 1.0f;
        [Range(0.0f, 1.0f)]
        public float skyReflectionIntensity = 1.0f;

        public bool voxelAA = false;

        public bool gaussianMipFilter = false;


        [Range(0.1f, 4.0f)]
        public float farOcclusionStrength = 1.0f;
        [Range(0.1f, 4.0f)]
        public float farthestOcclusionStrength = 1.0f;

        [Range(3, 16)]
        public int secondaryCones = 6;
        [Range(0.1f, 4.0f)]
        public float secondaryOcclusionStrength = 1.0f;

        public bool sphericalSkylight = false;

        #endregion






        #region InternalVariables
        [NonSerialized] public object initChecker;
        [NonSerialized] public bool initChecker2;//v0.2
        [NonSerialized] public Material material;
        [NonSerialized] public Camera attachedCamera;
        [NonSerialized] public Transform shadowCamTransform;
        [NonSerialized] public Camera shadowCam;
        [NonSerialized] public GameObject shadowCamGameObject;
        [NonSerialized] public Texture2D[] blueNoise;

        public int sunShadowResolution = 256;
        [NonSerialized] public int prevSunShadowResolution;

        [NonSerialized] public Shader sunDepthShader;

        public float shadowSpaceDepthRatio = 10.0f;

        [NonSerialized] public int frameCounter = 0;


        [NonSerialized] public RenderTexture sunDepthTexture;
        [NonSerialized] public RenderTexture previousGIResult;
        [NonSerialized] public RenderTexture previousCameraDepth;

        ///<summary>This is a volume texture that is immediately written to in the voxelization shader. The RInt format enables atomic writes to avoid issues where multiple fragments are trying to write to the same voxel in the volume.</summary>
        [NonSerialized] public RenderTexture integerVolume;

        ///<summary>An array of volume textures where each element is a mip/LOD level. Each volume is half the resolution of the previous volume. Separate textures for each mip level are required for manual mip-mapping of the main GI volume texture.</summary>
        [NonSerialized] public RenderTexture[] volumeTextures;

        ///<summary>The secondary volume texture that holds irradiance calculated during the in-volume GI tracing that occurs when Infinite Bounces is enabled. </summary>
        [NonSerialized] public RenderTexture secondaryIrradianceVolume;

        ///<summary>The alternate mip level 0 main volume texture needed to avoid simultaneous read/write errors while performing temporal stabilization on the main voxel volume.</summary>
        [NonSerialized] public RenderTexture volumeTextureB;

        ///<summary>The current active volume texture that holds GI information to be read during GI tracing.</summary>
        [NonSerialized] public RenderTexture activeVolume;

        ///<summary>The volume texture that holds GI information to be read during GI tracing that was used in the previous frame.</summary>
        [NonSerialized] public RenderTexture previousActiveVolume;

        ///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture scales depending on whether Voxel AA is enabled to ensure correct voxelization.</summary>
        [NonSerialized] public RenderTexture dummyVoxelTextureAAScaled;

        ///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture is always the same size whether Voxel AA is enabled or not.</summary>
        [NonSerialized] public RenderTexture dummyVoxelTextureFixed;

        [NonSerialized] public bool notReadyToRender = false;
        bool _loggedMissingSegiShader = false;

        [NonSerialized] public Shader voxelizationShader;
        [NonSerialized] public Shader voxelTracingShader;

        //v1.3
        [NonSerialized] public Shader voxelizationShaderVERT;
        [NonSerialized] public Shader voxelTracingShaderVERT;

        //v1.5
        [NonSerialized] public Shader voxelizationShaderL;
        [NonSerialized] public Shader voxelizationShaderVERTL;
        [NonSerialized] Shader segiShader;

        [NonSerialized] public ComputeShader clearCompute;
        [NonSerialized] public ComputeShader transferIntsCompute;
        [NonSerialized] public ComputeShader mipFilterCompute;

        // public const int numMipLevels = 6;
        [NonSerialized] public int numMipLevels = 6;

        [NonSerialized] public Camera voxelCamera;
        [NonSerialized] public GameObject voxelCameraGO;
        [NonSerialized] public GameObject leftViewPoint;
        [NonSerialized] public GameObject topViewPoint;

        public float voxelScaleFactor
        {
            get
            {
                return (float)voxelResolution / 256.0f;
            }
        }

        [NonSerialized] public Vector3 voxelSpaceOrigin;
        [NonSerialized] public Vector3 previousVoxelSpaceOrigin;
        [NonSerialized] public Vector3 voxelSpaceOriginDelta;


        [NonSerialized] public Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
        [NonSerialized] public Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
        [NonSerialized] public Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);

        [NonSerialized] public int voxelFlipFlop = 0;


        public enum RenderState
        {
            Voxelize,
            Bounce
        }

        [NonSerialized] public RenderState renderState = RenderState.Voxelize;
        #endregion





        #region SupportingObjectsAndProperties
        public struct Pass
        {
            public static int DiffuseTrace = 0;
            public static int BilateralBlur = 1;
            public static int BlendWithScene = 2;
            public static int TemporalBlend = 3;
            public static int SpecularTrace = 4;
            public static int GetCameraDepthTexture = 5;
            public static int GetWorldNormals = 6;
            public static int VisualizeGI = 7;
            public static int WriteBlack = 8;
            public static int VisualizeVoxels = 10;
            public static int BilateralUpsample = 11;
        }

        public struct SystemSupported
        {
            public bool hdrTextures;
            public bool rIntTextures;
            public bool dx11;
            public bool volumeTextures;
            public bool postShader;
            public bool sunDepthShader;
            public bool voxelizationShader;
            public bool tracingShader;

            //v1.3
            public bool voxelizationShaderVERT;
            public bool tracingShaderVERT;

            //v1.5
            public bool voxelizationShaderL;
            public bool voxelizationShaderVERTL;

            public bool fullFunctionality
            {
                get
                {
                    return hdrTextures && rIntTextures && dx11 && volumeTextures && postShader && sunDepthShader && voxelizationShader && tracingShader 
                        && voxelizationShaderVERT && tracingShaderVERT && voxelizationShaderVERTL && voxelizationShaderL;
                }
            }
        }

        /// <summary>
        /// Contains info on system compatibility of required hardware functionality
        /// </summary>
        [NonSerialized] public SystemSupported systemSupported;

        /// <summary>
        /// Estimates the VRAM usage of all the render textures used to render GI.
        /// </summary>
        public float vramUsage
        {
            get
            {
                long v = 0;

                if (sunDepthTexture != null)
                    v += sunDepthTexture.width * sunDepthTexture.height * 16;

                if (previousGIResult != null)
                    v += previousGIResult.width * previousGIResult.height * 16 * 4;

                if (previousCameraDepth != null)
                    v += previousCameraDepth.width * previousCameraDepth.height * 32;

                if (integerVolume != null)
                    v += integerVolume.width * integerVolume.height * integerVolume.volumeDepth * 32;

                if (volumeTextures != null)
                {
                    for (int i = 0; i < volumeTextures.Length; i++)
                    {
                        if (volumeTextures[i] != null)
                            v += volumeTextures[i].width * volumeTextures[i].height * volumeTextures[i].volumeDepth * 16 * 4;
                    }
                }

                if (secondaryIrradianceVolume != null)
                    v += secondaryIrradianceVolume.width * secondaryIrradianceVolume.height * secondaryIrradianceVolume.volumeDepth * 16 * 4;

                if (volumeTextureB != null)
                    v += volumeTextureB.width * volumeTextureB.height * volumeTextureB.volumeDepth * 16 * 4;

                if (dummyVoxelTextureAAScaled != null)
                    v += dummyVoxelTextureAAScaled.width * dummyVoxelTextureAAScaled.height * 8;

                if (dummyVoxelTextureFixed != null)
                    v += dummyVoxelTextureFixed.width * dummyVoxelTextureFixed.height * 8;

                float vram = (v / 8388608.0f);

                return vram;
            }
        }

        public int mipFilterKernel
        {
            get
            {
                return gaussianMipFilter ? 1 : 0;
            }
        }

        //v0.8
        public int downscaleFactor = 2;

        public int dummyVoxelResolution
        {
            get
            {
                return (int)voxelResolution * (voxelAA ? 2 : 1);
            }
        }

        public int giRenderRes
        {
            get
            {
                return halfResolution ? downscaleFactor : 1;// return halfResolution ? 2 : 1;
            }
        }

        #endregion

        void Start()
        {

            InitCheck();
        }

        public void InitCheck()
        {


            if (initChecker == null)
            {
                if (!initChecker2)//v0.2
                {
                    CleanupTextures();//v0.2
                    Init();
                }
            }
        }

        //v0.2a
        bool createdVolumeTexturesPlayMode = false;

        void CreateVolumeTextures()
        {


            if (volumeTextures != null)
            {
                for (int i = 0; i < numMipLevels; i++)
                {
                    if (volumeTextures[i] != null)
                    {
                        volumeTextures[i].DiscardContents();
                        volumeTextures[i].Release();
                        DestroyImmediate(volumeTextures[i]);
                    }
                }
            }

            volumeTextures = new RenderTexture[numMipLevels];

            for (int i = 0; i < numMipLevels; i++)
            {
                int resolution = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i));
                volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
#if UNITY_5_4_OR_NEWER
                volumeTextures[i].dimension = TextureDimension.Tex3D;
#else
			volumeTextures[i].isVolume = true;
#endif
                volumeTextures[i].volumeDepth = resolution;
                volumeTextures[i].enableRandomWrite = true;
                volumeTextures[i].filterMode = FilterMode.Bilinear;
#if UNITY_5_4_OR_NEWER
                volumeTextures[i].autoGenerateMips = false;
#else
			volumeTextures[i].generateMips = false;
#endif
                volumeTextures[i].useMipMap = false;
                volumeTextures[i].Create();
                volumeTextures[i].hideFlags = HideFlags.HideAndDontSave;
            }

            if (volumeTextureB)
            {
                volumeTextureB.DiscardContents();
                volumeTextureB.Release();
                DestroyImmediate(volumeTextureB);
            }
            volumeTextureB = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
#if UNITY_5_4_OR_NEWER
            volumeTextureB.dimension = TextureDimension.Tex3D;
#else
		volumeTextureB.isVolume = true;
#endif
            volumeTextureB.volumeDepth = (int)voxelResolution;
            volumeTextureB.enableRandomWrite = true;
            volumeTextureB.filterMode = FilterMode.Bilinear;
#if UNITY_5_4_OR_NEWER
            volumeTextureB.autoGenerateMips = false;
#else
		volumeTextureB.generateMips = false;
#endif
            volumeTextureB.useMipMap = false;
            volumeTextureB.Create();
            volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

            if (secondaryIrradianceVolume)
            {
                secondaryIrradianceVolume.DiscardContents();
                secondaryIrradianceVolume.Release();
                DestroyImmediate(secondaryIrradianceVolume);
            }
            secondaryIrradianceVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
#if UNITY_5_4_OR_NEWER
            secondaryIrradianceVolume.dimension = TextureDimension.Tex3D;
#else
		secondaryIrradianceVolume.isVolume = true;
#endif
            secondaryIrradianceVolume.volumeDepth = (int)voxelResolution;
            secondaryIrradianceVolume.enableRandomWrite = true;
            secondaryIrradianceVolume.filterMode = FilterMode.Point;
#if UNITY_5_4_OR_NEWER
            secondaryIrradianceVolume.autoGenerateMips = false;
#else
		secondaryIrradianceVolume.generateMips = false;
#endif
            secondaryIrradianceVolume.useMipMap = false;
            secondaryIrradianceVolume.antiAliasing = 1;
            secondaryIrradianceVolume.Create();
            secondaryIrradianceVolume.hideFlags = HideFlags.HideAndDontSave;



            if (integerVolume)
            {
                integerVolume.DiscardContents();
                integerVolume.Release();
                DestroyImmediate(integerVolume);
            }
            integerVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.RInt, RenderTextureReadWrite.Linear);
#if UNITY_5_4_OR_NEWER
            integerVolume.dimension = TextureDimension.Tex3D;
#else
		integerVolume.isVolume = true;
#endif
            integerVolume.volumeDepth = (int)voxelResolution;
            integerVolume.enableRandomWrite = true;
            integerVolume.filterMode = FilterMode.Point;
            integerVolume.Create();
            integerVolume.hideFlags = HideFlags.HideAndDontSave;

            //v0.1
            integerVolume.depth = 0;

            ResizeDummyTexture();

        }

        bool EnsureAttachedCamera()
        {
            if (attachedCamera == null)
            {
                attachedCamera = GetComponent<Camera>();
            }

            if (attachedCamera == null)
            {
                return false;
            }

            DepthTextureMode requiredModes = DepthTextureMode.Depth;
#if UNITY_5_4_OR_NEWER
            requiredModes |= DepthTextureMode.MotionVectors;
#endif
            if ((attachedCamera.depthTextureMode & requiredModes) != requiredModes)
            {
                attachedCamera.depthTextureMode |= requiredModes;
            }
            return true;
        }

        bool IsPrimaryCameraReady()
        {
            return !notReadyToRender && EnsureAttachedCamera();
        }

        GameObject EnsureManagedHelperObject(ref GameObject helperObject, string helperName)
        {
            if (helperObject == null)
            {
                GameObject existingObject = GameObject.Find(helperName);
                helperObject = existingObject != null ? existingObject : new GameObject(helperName);
            }

            helperObject.name = helperName;
            helperObject.hideFlags = HideFlags.HideInHierarchy;
            return helperObject;
        }

        Camera EnsureManagedHelperCamera(ref GameObject helperObject, ref Camera helperCamera, string helperName)
        {
            GameObject helper = EnsureManagedHelperObject(ref helperObject, helperName);
            if (helperCamera == null || helperCamera.gameObject != helper)
            {
                helperCamera = helper.GetComponent<Camera>();
                if (helperCamera == null)
                {
                    helperCamera = helper.AddComponent<Camera>();
                }
            }

            return helperCamera;
        }

        UniversalAdditionalCameraData EnsureHelperCameraData(Camera helperCamera)
        {
            UniversalAdditionalCameraData additionalCameraData = helperCamera.GetComponent<UniversalAdditionalCameraData>();
            if (additionalCameraData == null)
            {
                additionalCameraData = helperCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            additionalCameraData.SetRenderer(helperRendererID);
            return additionalCameraData;
        }

        void ConfigureShadowCamera()
        {
            shadowCam = EnsureManagedHelperCamera(ref shadowCamGameObject, ref shadowCam, "SEGI_SHADOWCAM");
            shadowCam.enabled = false;
            shadowCam.depth = attachedCamera.depth - 1;
            shadowCam.orthographic = true;
            shadowCam.orthographicSize = shadowSpaceSize;
            shadowCam.clearFlags = CameraClearFlags.SolidColor;
            shadowCam.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
            shadowCam.cullingMask = giCullingMask;
            shadowCam.useOcclusionCulling = false;
            shadowCamTransform = shadowCam.transform;
            EnsureHelperCameraData(shadowCam);
        }

        void ConfigureVoxelCamera()
        {
            voxelCamera = EnsureManagedHelperCamera(ref voxelCameraGO, ref voxelCamera, "SEGI_VOXEL_CAMERA");
            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = voxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.useOcclusionCulling = false;
            EnsureHelperCameraData(voxelCamera);
        }

        void EnsureVoxelViewPoints()
        {
            EnsureManagedHelperObject(ref leftViewPoint, "SEGI_LEFT_VOXEL_VIEW");
            EnsureManagedHelperObject(ref topViewPoint, "SEGI_TOP_VOXEL_VIEW");
        }

        void CleanupManagedHelperObject(ref GameObject helperObject)
        {
            if (helperObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(helperObject);
            }
            else
            {
                DestroyImmediate(helperObject);
            }

            helperObject = null;
        }

        void CleanupManagedHelpers()
        {
            shadowCam = null;
            shadowCamTransform = null;
            CleanupManagedHelperObject(ref shadowCamGameObject);

            voxelCamera = null;
            CleanupManagedHelperObject(ref voxelCameraGO);

            CleanupManagedHelperObject(ref leftViewPoint);
            CleanupManagedHelperObject(ref topViewPoint);
        }
        T LoadCachedResource<T>(ref T cachedResource, string resourcePath) where T : UnityEngine.Object
        {
            if (cachedResource == null)
            {
                cachedResource = Resources.Load<T>(resourcePath);
            }

            return cachedResource;
        }

        Shader LoadCachedShader(ref Shader cachedShader, string shaderName)
        {
            if (cachedShader == null)
            {
                cachedShader = Shader.Find(shaderName);
            }

            return cachedShader;
        }

        void EnsureBlueNoiseTextures()
        {
            const int blueNoiseCount = 64;
            if (blueNoise == null || blueNoise.Length != blueNoiseCount)
            {
                blueNoise = new Texture2D[blueNoiseCount];
            }

            for (int i = 0; i < blueNoiseCount; i++)
            {
                if (blueNoise[i] != null)
                {
                    continue;
                }

                string fileName = "LDR_RGBA_" + i.ToString();
                Texture2D blueNoiseTexture = Resources.Load<Texture2D>("Noise Textures/" + fileName);
                if (blueNoiseTexture == null)
                {
                    Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName + "\" for SEGI!");
                }

                blueNoise[i] = blueNoiseTexture;
            }
        }

        void ResizeDummyTexture()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam3b");
                return;
            }

            if (dummyVoxelTextureAAScaled)
            {
                dummyVoxelTextureAAScaled.DiscardContents();
                dummyVoxelTextureAAScaled.Release();
                DestroyImmediate(dummyVoxelTextureAAScaled);
            }
            dummyVoxelTextureAAScaled = new RenderTexture(dummyVoxelResolution, dummyVoxelResolution, 16, RenderTextureFormat.RInt);
            dummyVoxelTextureAAScaled.Create();
            dummyVoxelTextureAAScaled.hideFlags = HideFlags.HideAndDontSave;

            if (dummyVoxelTextureFixed)
            {
                dummyVoxelTextureFixed.DiscardContents();
                dummyVoxelTextureFixed.Release();
                DestroyImmediate(dummyVoxelTextureFixed);
            }
            dummyVoxelTextureFixed = new RenderTexture((int)voxelResolution, (int)voxelResolution, 16, RenderTextureFormat.RInt);
            dummyVoxelTextureFixed.Create();
            dummyVoxelTextureFixed.hideFlags = HideFlags.HideAndDontSave;
        }

        void Init()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                // Debug.Log("No cam3c");
                return;
            }



            //Setup shaders and materials
            sunDepthShader = LoadCachedShader(ref sunDepthShader, "SEGI/SEGIRenderSunDepth");

            clearCompute = LoadCachedResource(ref clearCompute, "SEGIClear");
            transferIntsCompute = LoadCachedResource(ref transferIntsCompute, "SEGITransferInts");
            mipFilterCompute = LoadCachedResource(ref mipFilterCompute, "SEGIMipFilter");

            voxelizationShader = LoadCachedShader(ref voxelizationShader, "SEGI/SEGIVoxelizeScene");
            voxelTracingShader = LoadCachedShader(ref voxelTracingShader, "SEGI/SEGITraceScene");

            //v1.3
            voxelizationShaderVERT = LoadCachedShader(ref voxelizationShaderVERT, "SEGI/SEGIVoxelizeSceneVERT");
            voxelTracingShaderVERT = LoadCachedShader(ref voxelTracingShaderVERT, "SEGI/SEGITraceSceneVERT");

            //v1.5
            voxelizationShaderL = LoadCachedShader(ref voxelizationShaderL, "SEGI/SEGIVoxelizeSceneL");
            voxelizationShaderVERTL = LoadCachedShader(ref voxelizationShaderVERTL, "SEGI/SEGIVoxelizeSceneVERTL");

            segiShader = LoadCachedShader(ref segiShader, "Hidden/SEGI");
            if (segiShader == null)
            {
                if (!_loggedMissingSegiShader)
                {
                    Debug.LogError("LumenLike: missing shader 'Hidden/SEGI'. Reimport Assets/LumenLike_Main/Scripts/SEGI/Resources/SEGI.shader and fix any compile errors first.", this);
                    _loggedMissingSegiShader = true;
                }
                return;
            }

            _loggedMissingSegiShader = false;
            if (!material || material.shader != segiShader)
            {
                material = new Material(segiShader);
                //material.hideFlags = HideFlags.HideAndDontSave;//v0.2
            }

            //Get the camera attached to this game object
            if (!EnsureAttachedCamera())
            {
                return;
            }
            ConfigureShadowCamera();
            ConfigureVoxelCamera();
            EnsureVoxelViewPoints();

            //Get blue noise textures
            EnsureBlueNoiseTextures();

            //Setup sun depth texture
            if (sunDepthTexture)
            {
                sunDepthTexture.DiscardContents();
                sunDepthTexture.Release();
                DestroyImmediate(sunDepthTexture);
            }
            sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
            sunDepthTexture.filterMode = FilterMode.Point;
            sunDepthTexture.Create();
            sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;


            //v0.2a
            if (createdVolumeTexturesPlayMode)
            {
                // return;
            }
            if (Application.isPlaying)
            {
                createdVolumeTexturesPlayMode = true;
            }

            //Create the volume textures
            CreateVolumeTextures();

            initChecker2 = true;//v0.2
            initChecker = new object();
        }

        void CheckSupport()
        {
            if (material == null)
            {
                return;
            }
            systemSupported.hdrTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
            systemSupported.rIntTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RInt);
            systemSupported.dx11 = SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders;
            systemSupported.volumeTextures = SystemInfo.supports3DTextures;

            systemSupported.postShader = material.shader.isSupported;
            systemSupported.sunDepthShader = sunDepthShader.isSupported;
            systemSupported.voxelizationShader = voxelizationShader.isSupported;
            systemSupported.tracingShader = voxelTracingShader.isSupported;

            //1.3
            systemSupported.voxelizationShaderVERT = voxelizationShaderVERT.isSupported;
            systemSupported.tracingShaderVERT = voxelTracingShaderVERT.isSupported;

            //1.5
            systemSupported.voxelizationShaderL = voxelizationShaderL.isSupported;
            systemSupported.voxelizationShaderVERTL = voxelizationShaderVERTL.isSupported;
            

            if (!systemSupported.fullFunctionality)
            {
                Debug.LogWarning("SEGI is not supported on the current platform. Check for shader compile errors in SEGI/Resources");
                enabled = false;
            }
        }

        void OnDrawGizmosSelectedA()
        {
            if (!enabled)
                return;

            Color prevColor = Gizmos.color;
            Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);

            Gizmos.DrawCube(voxelSpaceOrigin, new Vector3(voxelSpaceSize, voxelSpaceSize, voxelSpaceSize));

            Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);

            Gizmos.color = prevColor;
        }

        void CleanupTexture(ref RenderTexture texture)
        {
            if (texture)
            {
                texture.DiscardContents();
                texture.Release();

                if (voxelCamera != null)
                {
                    voxelCamera.targetTexture = null;//v0.2
                }

                DestroyImmediate(texture);
            }
        }

        void CleanupTextures()
        {
            CleanupTexture(ref sunDepthTexture);
            CleanupTexture(ref previousGIResult);
            CleanupTexture(ref previousCameraDepth);
            CleanupTexture(ref integerVolume);
            if (volumeTextures != null)//v0.2
            {
                for (int i = 0; i < volumeTextures.Length; i++)
                {
                    CleanupTexture(ref volumeTextures[i]);
                }
            }
            CleanupTexture(ref secondaryIrradianceVolume);
            CleanupTexture(ref volumeTextureB);
            CleanupTexture(ref dummyVoxelTextureAAScaled);
            CleanupTexture(ref dummyVoxelTextureFixed);
        }

        void Cleanup()
        {
            if (material != null)
            {
                DestroyImmediate(material);
            }
            initChecker = null;
            initChecker2 = false;//v0.2

            CleanupTextures();
            CleanupManagedHelpers();

            //v0.1
            RenderPipelineManager.beginCameraRendering -= ExecuteBeforeCameraRender;
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.delayCall -= PerformQueuedEditorRuntimeRefresh;
            _editorRuntimeRefreshQueued = false;
#endif
        }

#if UNITY_EDITOR
        bool _editorRuntimeRefreshQueued;

        void QueueEditorRuntimeRefresh()
        {
            if (Application.isPlaying || _editorRuntimeRefreshQueued)
            {
                return;
            }

            _editorRuntimeRefreshQueued = true;
            EditorApplication.delayCall -= PerformQueuedEditorRuntimeRefresh;
            EditorApplication.delayCall += PerformQueuedEditorRuntimeRefresh;
        }

        void PerformQueuedEditorRuntimeRefresh()
        {
            _editorRuntimeRefreshQueued = false;
            if (this == null || Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            RebuildRuntimeStateForEditor();
        }

        void HandleUndoRedo()
        {
            QueueEditorRuntimeRefresh();
        }

        void RebuildRuntimeStateForEditor()
        {
            CleanupTextures();
            CleanupManagedHelpers();

            if (material != null)
            {
                DestroyImmediate(material);
                material = null;
            }

            attachedCamera = null;
            shadowCamTransform = null;
            shadowCam = null;
            shadowCamGameObject = null;
            blueNoise = null;
            voxelCamera = null;
            voxelCameraGO = null;
            leftViewPoint = null;
            topViewPoint = null;
            frameCounter = 0;
            voxelFlipFlop = 0;
            voxelSpaceOrigin = Vector3.zero;
            previousVoxelSpaceOrigin = Vector3.zero;
            voxelSpaceOriginDelta = Vector3.zero;
            renderState = RenderState.Voxelize;
            systemSupported = default;
            notReadyToRender = false;
            initChecker = null;
            initChecker2 = false;
            _loggedMissingSegiShader = false;

            RenderPipelineManager.beginCameraRendering -= ExecuteBeforeCameraRender;
            RenderPipelineManager.beginCameraRendering += ExecuteBeforeCameraRender;

            InitCheck();
            ResizeRenderTextures();
            CheckSupport();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        void OnValidate()
        {
            QueueEditorRuntimeRefresh();
        }
#endif

        void OnEnable()
        {
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.undoRedoPerformed += HandleUndoRedo;
#endif
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam3d");
                return;
            }


            //  InitCheck();
            //  ResizeRenderTextures();

            //  CheckSupport();

            //v0.1
            RenderPipelineManager.beginCameraRendering -= ExecuteBeforeCameraRender;
            RenderPipelineManager.beginCameraRendering += ExecuteBeforeCameraRender;
        }

        void OnDisable()
        {
            Cleanup();


        }

        void ResizeRenderTextures()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                // Debug.Log("No cam3e");
                return;
            }

            if (previousGIResult)
            {
                previousGIResult.DiscardContents();
                previousGIResult.Release();
                DestroyImmediate(previousGIResult);
            }

            //v0.6
            int width = (attachedCamera == null || attachedCamera.pixelWidth == 0) ? 2 : attachedCamera.pixelWidth;
            int height = (attachedCamera == null || attachedCamera.pixelHeight == 0) ? 2 : attachedCamera.pixelHeight;
            //int width = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
            //int height = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;

            previousGIResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
            previousGIResult.wrapMode = TextureWrapMode.Clamp;
            previousGIResult.filterMode = FilterMode.Bilinear;
            previousGIResult.useMipMap = true;
#if UNITY_5_4_OR_NEWER
            previousGIResult.autoGenerateMips = false;
#else
		previousResult.generateMips = false;
#endif
            previousGIResult.Create();
            previousGIResult.hideFlags = HideFlags.HideAndDontSave;

            if (previousCameraDepth)
            {
                previousCameraDepth.DiscardContents();
                previousCameraDepth.Release();
                DestroyImmediate(previousCameraDepth);
            }
            previousCameraDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            previousCameraDepth.wrapMode = TextureWrapMode.Clamp;
            previousCameraDepth.filterMode = FilterMode.Bilinear;
            previousCameraDepth.Create();
            previousCameraDepth.hideFlags = HideFlags.HideAndDontSave;
        }

        void ResizeSunShadowBuffer()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam3f");
                return;
            }
            if (sunDepthTexture)
            {
                sunDepthTexture.DiscardContents();
                sunDepthTexture.Release();
                DestroyImmediate(sunDepthTexture);
            }
            sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
            sunDepthTexture.filterMode = FilterMode.Point;
            sunDepthTexture.Create();
            sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        void Update()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam1");
                return;
            }

            if (previousGIResult == null)
            {
                ResizeRenderTextures();
            }

            //v0.6
            if (attachedCamera != null)
            {
                if (previousGIResult.width != attachedCamera.pixelWidth || previousGIResult.height != attachedCamera.pixelHeight)
                {
                    ResizeRenderTextures();
                }
            }

            if ((int)sunShadowResolution != prevSunShadowResolution)
            {
                ResizeSunShadowBuffer();
            }

            prevSunShadowResolution = (int)sunShadowResolution;

            if (volumeTextures[0] != null && volumeTextures[0].width != (int)voxelResolution)
            {
                CreateVolumeTextures();
            }

            if (dummyVoxelTextureAAScaled != null && dummyVoxelTextureAAScaled.width != dummyVoxelResolution)
            {
                ResizeDummyTexture();
            }
        }

        public Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
        {
            //Since the third column of the view matrix needs to be reversed if using reversed z-buffer, do so here
#if UNITY_5_5_OR_NEWER
            if (SystemInfo.usesReversedZBuffer)
            {
                mat[2, 0] = -mat[2, 0];
                mat[2, 1] = -mat[2, 1];
                mat[2, 2] = -mat[2, 2];
                mat[2, 3] = -mat[2, 3];
            }
#endif
            return mat;
        }

        //v0.2
        //v0.4

        static readonly UniversalRenderPipeline.SingleCameraRequest s_SingleCameraRequest = new UniversalRenderPipeline.SingleCameraRequest();

        static void RenderSingleCameraCompat(ScriptableRenderContext context, Camera targetCamera)
        {
            if (targetCamera == null)
            {
                return;
            }

            var destination = targetCamera.targetTexture;
            if (destination != null)
            {
                s_SingleCameraRequest.destination = destination;
                s_SingleCameraRequest.mipLevel = 0;
                s_SingleCameraRequest.face = CubemapFace.Unknown;
                s_SingleCameraRequest.slice = 0;

                if (RenderPipeline.SupportsRenderRequest(targetCamera, s_SingleCameraRequest))
                {
                    RenderPipeline.SubmitRenderRequest(targetCamera, s_SingleCameraRequest);
                    return;
                }
            }

#pragma warning disable CS0618
            UniversalRenderPipeline.RenderSingleCamera(context, targetCamera);
#pragma warning restore CS0618
        }

        void ExecuteBeforeCameraRenderA(ScriptableRenderContext context, Camera camera) //void OnPreRender()
        {
            //return;
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                // Debug.Log("No cam4a");
                return;
            }

            var urpAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
            var urpAsset0 = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
            GraphicsSettings.defaultRenderPipeline = null;
            GraphicsSettings.defaultRenderPipeline = null;
            //v0.3
            var urpAsset2 = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = null;


            //Force reinitialization to make sure that everything is working properly if one of the cameras was unexpectedly destroyed
            if (!voxelCamera || !shadowCam)
                initChecker = null; CleanupTextures(); initChecker2 = false;//v0.2

            InitCheck();

            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam2");
                return;
            }

            if (!updateGI)
            {
                return;
            }

            //Cache the previous active render texture to avoid issues with other Unity rendering going on
            RenderTexture previousActive = RenderTexture.active;

            Shader.SetGlobalInt(SegiVoxelAAId, voxelAA ? 1 : 0);



            //Main voxelization work
            if (renderState == RenderState.Voxelize)
            {
                activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;             //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
                previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

                //float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;			//Calculate the size of a voxel texel in world-space units



                //Setup the voxel volume origin position
                float interval = voxelSpaceSize / 8.0f;                                             //The interval at which the voxel volume will be "locked" in world-space
                Vector3 origin;
                if (followTransform)
                {
                    origin = followTransform.position;
                }
                else
                {
                    //GI is still flickering a bit when the scene view and the game view are opened at the same time
                    origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
                }
                //Lock the voxel volume origin based on the interval
                voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

                //Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
                voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
                Shader.SetGlobalVector(SegiVoxelSpaceOriginDeltaId, voxelSpaceOriginDelta / voxelSpaceSize);

                previousVoxelSpaceOrigin = voxelSpaceOrigin;



                //Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
                voxelCamera.enabled = false;
                voxelCamera.orthographic = true;
                voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
                voxelCamera.nearClipPlane = 0.0f;
                voxelCamera.farClipPlane = voxelSpaceSize;
                voxelCamera.depth = -2;
                voxelCamera.renderingPath = RenderingPath.Forward;
                voxelCamera.clearFlags = CameraClearFlags.Color;
                voxelCamera.backgroundColor = Color.black;
                voxelCamera.cullingMask = giCullingMask;


                //Move the voxel camera game object and other related objects to the above calculated voxel space origin
                voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
                voxelCameraGO.transform.rotation = rotationFront;

                leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
                leftViewPoint.transform.rotation = rotationLeft;
                topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
                topViewPoint.transform.rotation = rotationTop;



                //Set matrices needed for voxelization
                Shader.SetGlobalMatrix(WorldToCameraId, attachedCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelViewFrontId, TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewLeftId, TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewTopId, TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiWorldToVoxelId, voxelCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionId, voxelCamera.projectionMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionInverseId, voxelCamera.projectionMatrix.inverse);

                Shader.SetGlobalInt(SegiVoxelResolutionId, (int)voxelResolution);

                Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
                Shader.SetGlobalMatrix(SegiVoxelToGiProjectionId, voxelToGIProjection);
                Shader.SetGlobalVector(SegiSunlightVectorId, sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);

                //Set paramteters
                Shader.SetGlobalColor(GiSunColorId, sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
                Shader.SetGlobalColor(SegiSkyColorId, new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
                Shader.SetGlobalFloat(GIGainShaderId, giGain);
                Shader.SetGlobalFloat(SegiSecondaryBounceGainId, infiniteBounces ? secondaryBounceGain : 0.0f);
                Shader.SetGlobalFloat(SegiSoftSunlightId, softSunlight);
                Shader.SetGlobalInt(SegiSphericalSkylightId, sphericalSkylight ? 1 : 0);
                Shader.SetGlobalInt(SegiInnerOcclusionLayersId, innerOcclusionLayers);


                //Render the depth texture from the sun's perspective in order to inject sunlight with shadows during voxelization
                if (sun != null)
                {
                    if (renderSunWithSRP)
                    {  
                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;

                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        shadowCam.RenderWithShader(sunDepthShader, "");
                        shadowCam.targetTexture = sunDepthTexture;
                        preRenderers.shader = sunDepthShader;
                        //RenderSingleCameraCompat(context, shadowCam);

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));
                    }
                    else
                    {
                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;


                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        shadowCam.RenderWithShader(sunDepthShader, "");

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));
                    }
                }









                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.SetInt("Res", (int)voxelResolution);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);








                //Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
                Graphics.SetRandomWriteTarget(1, integerVolume);
                voxelCamera.targetTexture = dummyVoxelTextureAAScaled;

                Shader.SetGlobalVector(CutoffGiId, cutoff); //v1.6

                if (proxyNoGeom) //v1.3
                {
                    if (enableLocalLightGI)//v1.5
                    {                        
                        Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                        voxelCamera.RenderWithShader(voxelizationShaderVERTL, "");
                    }
                    else
                    {
                        voxelCamera.RenderWithShader(voxelizationShaderVERT, "");
                    }
                }
                else
                {
                    if (enableLocalLightGI)//v1.5
                    {
                        Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                        voxelCamera.RenderWithShader(voxelizationShaderL, "");
                    }
                    else
                    {
                        voxelCamera.RenderWithShader(voxelizationShader, "");
                    }
                }

                Graphics.ClearRandomWriteTargets();


                //Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
                transferIntsCompute.SetTexture(0, "Result", activeVolume);
                transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
                transferIntsCompute.SetTexture(0, "RG0", integerVolume);
                transferIntsCompute.SetInt("VoxelAA", voxelAA ? 1 : 0);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
                transferIntsCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeLevel0Id, activeVolume);

                //Manually filter/render mip maps
                for (int i = 0; i < numMipLevels - 1; i++)
                {
                    RenderTexture source = volumeTextures[i];

                    if (i == 0)
                    {
                        source = activeVolume;
                    }

                    int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
                    mipFilterCompute.SetInt("destinationRes", destinationRes);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Source", source);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
                    mipFilterCompute.Dispatch(mipFilterKernel, destinationRes / 8, destinationRes / 8, 1);
                    Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
                }

                //Advance the voxel flip flop counter
                voxelFlipFlop += 1;
                voxelFlipFlop = voxelFlipFlop % 2;

                if (infiniteBounces)
                {
                    renderState = RenderState.Bounce;
                }
            }
            else if (renderState == RenderState.Bounce)
            {

                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                //Set secondary tracing parameters
                Shader.SetGlobalInt(SegiSecondaryConesId, secondaryCones);
                Shader.SetGlobalFloat(SegiSecondaryOcclusionStrengthId, secondaryOcclusionStrength);

                //Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
                Graphics.SetRandomWriteTarget(1, integerVolume);
                voxelCamera.targetTexture = dummyVoxelTextureFixed;

                //v1.3
                if (proxyNoGeom)
                {
                    voxelCamera.RenderWithShader(voxelTracingShaderVERT, "");
                }
                else
                {
                    voxelCamera.RenderWithShader(voxelTracingShader, "");
                }

                Graphics.ClearRandomWriteTargets();


                //Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
                transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
                transferIntsCompute.SetTexture(1, "RG0", integerVolume);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeTexture1Id, secondaryIrradianceVolume);

                renderState = RenderState.Voxelize;
            }



            RenderTexture.active = previousActive;


            //v0.3
            if (urpAsset != null)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset;
            }
            if (urpAsset2 != null)
            {
                QualitySettings.renderPipeline = urpAsset2;
            }
            if (urpAsset0 != null)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset0;
            }
        }


        void OnPreRenderA()
        {
            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam4b");
                return;
            }


            //Force reinitialization to make sure that everything is working properly if one of the cameras was unexpectedly destroyed
            if (!voxelCamera || !shadowCam)
                initChecker = null; CleanupTextures(); initChecker2 = false;//v0.2

            InitCheck();

            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                //Debug.Log("No cam3");
                return;
            }

            if (!updateGI)
            {
                return;
            }

            //Cache the previous active render texture to avoid issues with other Unity rendering going on
            RenderTexture previousActive = RenderTexture.active;

            Shader.SetGlobalInt(SegiVoxelAAId, voxelAA ? 1 : 0);



            //Main voxelization work
            if (renderState == RenderState.Voxelize)
            {
                activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;             //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
                previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

                //float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;			//Calculate the size of a voxel texel in world-space units



                //Setup the voxel volume origin position
                float interval = voxelSpaceSize / 8.0f;                                             //The interval at which the voxel volume will be "locked" in world-space
                Vector3 origin;
                if (followTransform)
                {
                    origin = followTransform.position;
                }
                else
                {
                    //GI is still flickering a bit when the scene view and the game view are opened at the same time
                    origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
                }
                //Lock the voxel volume origin based on the interval
                voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

                //Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
                voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
                Shader.SetGlobalVector(SegiVoxelSpaceOriginDeltaId, voxelSpaceOriginDelta / voxelSpaceSize);

                previousVoxelSpaceOrigin = voxelSpaceOrigin;



                //Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
                voxelCamera.enabled = false;
                voxelCamera.orthographic = true;
                voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
                voxelCamera.nearClipPlane = 0.0f;
                voxelCamera.farClipPlane = voxelSpaceSize;
                voxelCamera.depth = -2;
                voxelCamera.renderingPath = RenderingPath.Forward;
                voxelCamera.clearFlags = CameraClearFlags.Color;
                voxelCamera.backgroundColor = Color.black;
                voxelCamera.cullingMask = giCullingMask;


                //Move the voxel camera game object and other related objects to the above calculated voxel space origin
                voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
                voxelCameraGO.transform.rotation = rotationFront;

                leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
                leftViewPoint.transform.rotation = rotationLeft;
                topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
                topViewPoint.transform.rotation = rotationTop;



                //Set matrices needed for voxelization
                Shader.SetGlobalMatrix(WorldToCameraId, attachedCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelViewFrontId, TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewLeftId, TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewTopId, TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiWorldToVoxelId, voxelCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionId, voxelCamera.projectionMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionInverseId, voxelCamera.projectionMatrix.inverse);

                Shader.SetGlobalInt(SegiVoxelResolutionId, (int)voxelResolution);

                Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
                Shader.SetGlobalMatrix(SegiVoxelToGiProjectionId, voxelToGIProjection);
                Shader.SetGlobalVector(SegiSunlightVectorId, sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);

                //Set paramteters
                Shader.SetGlobalColor(GiSunColorId, sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
                Shader.SetGlobalColor(SegiSkyColorId, new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
                Shader.SetGlobalFloat(GIGainShaderId, giGain);
                Shader.SetGlobalFloat(SegiSecondaryBounceGainId, infiniteBounces ? secondaryBounceGain : 0.0f);
                Shader.SetGlobalFloat(SegiSoftSunlightId, softSunlight);
                Shader.SetGlobalInt(SegiSphericalSkylightId, sphericalSkylight ? 1 : 0);
                Shader.SetGlobalInt(SegiInnerOcclusionLayersId, innerOcclusionLayers);


                //Render the depth texture from the sun's perspective in order to inject sunlight with shadows during voxelization
                if (sun != null)
                {
                    if (renderSunWithSRP)
                    {
                        var urpAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                        var urpAsset0 = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                        GraphicsSettings.defaultRenderPipeline = null;
                        GraphicsSettings.defaultRenderPipeline = null;
                        //v0.3
                        var urpAsset2 = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
                        QualitySettings.renderPipeline = null;

                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;

                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        shadowCam.RenderWithShader(sunDepthShader, "");
                        shadowCam.targetTexture = sunDepthTexture;
                        preRenderers.shader = sunDepthShader;
                        //RenderSingleCameraCompat(context, shadowCam);

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));

                        //v0.3
                        if (urpAsset != null)
                        {
                            GraphicsSettings.defaultRenderPipeline = urpAsset;
                        }
                        if (urpAsset2 != null)
                        {
                            QualitySettings.renderPipeline = urpAsset2;
                        }
                        if (urpAsset0 != null)
                        {
                            GraphicsSettings.defaultRenderPipeline = urpAsset0;
                        }
                    }
                    else
                    {
                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;


                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        shadowCam.RenderWithShader(sunDepthShader, "");

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));
                    }
                }









                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.SetInt("Res", (int)voxelResolution);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);








                //Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
                Graphics.SetRandomWriteTarget(1, integerVolume);
                voxelCamera.targetTexture = dummyVoxelTextureAAScaled;

                Shader.SetGlobalVector(CutoffGiId, cutoff); //v1.6

                if (proxyNoGeom) //v1.3
                {
                    if (enableLocalLightGI)//v1.5
                    {
                        Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                        voxelCamera.RenderWithShader(voxelizationShaderVERTL, "");
                    }
                    else
                    {
                        voxelCamera.RenderWithShader(voxelizationShaderVERT, "");
                    }
                }
                else
                {
                    if (enableLocalLightGI)//v1.5
                    {                        
                        Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                        Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                        voxelCamera.RenderWithShader(voxelizationShaderL, "");
                    }
                    else
                    {
                        voxelCamera.RenderWithShader(voxelizationShader, "");
                    }
                }

                Graphics.ClearRandomWriteTargets();


                //Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
                transferIntsCompute.SetTexture(0, "Result", activeVolume);
                transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
                transferIntsCompute.SetTexture(0, "RG0", integerVolume);
                transferIntsCompute.SetInt("VoxelAA", voxelAA ? 1 : 0);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
                transferIntsCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeLevel0Id, activeVolume);

                //Manually filter/render mip maps
                for (int i = 0; i < numMipLevels - 1; i++)
                {
                    RenderTexture source = volumeTextures[i];

                    if (i == 0)
                    {
                        source = activeVolume;
                    }

                    int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
                    mipFilterCompute.SetInt("destinationRes", destinationRes);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Source", source);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
                    mipFilterCompute.Dispatch(mipFilterKernel, destinationRes / 8, destinationRes / 8, 1);
                    Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
                }

                //Advance the voxel flip flop counter
                voxelFlipFlop += 1;
                voxelFlipFlop = voxelFlipFlop % 2;

                if (infiniteBounces)
                {
                    renderState = RenderState.Bounce;
                }
            }
            else if (renderState == RenderState.Bounce)
            {

                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                //Set secondary tracing parameters
                Shader.SetGlobalInt(SegiSecondaryConesId, secondaryCones);
                Shader.SetGlobalFloat(SegiSecondaryOcclusionStrengthId, secondaryOcclusionStrength);

                //Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
                Graphics.SetRandomWriteTarget(1, integerVolume);
                voxelCamera.targetTexture = dummyVoxelTextureFixed;

                //v1.3
                if (proxyNoGeom)
                {
                    voxelCamera.RenderWithShader(voxelTracingShaderVERT, "");
                }
                else
                {
                    voxelCamera.RenderWithShader(voxelTracingShader, "");
                }

                Graphics.ClearRandomWriteTargets();


                //Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
                transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
                transferIntsCompute.SetTexture(1, "RG0", integerVolume);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeTexture1Id, secondaryIrradianceVolume);

                renderState = RenderState.Voxelize;
            }



            RenderTexture.active = previousActive;
        }


        void ExecuteBeforeCameraRender(ScriptableRenderContext context, Camera camera) //void OnPreRender()
        {
            if (!EnsureAttachedCamera())
            {
                return;
            }

            if (integerVolume == null)// || Camera.current != null
            {
                Init();//v0.2
                //Debug.Log("No cam4c");
                return;
            }
            //v0.6
            if (camera != attachedCamera)
            {
                return;
            }

            //Force reinitialization to make sure that everything is working properly if one of the helpers was unexpectedly destroyed
            if (!voxelCamera || !shadowCam || !leftViewPoint || !topViewPoint)
            {
                initChecker = null;
                CleanupTextures();
                CleanupManagedHelpers();
                initChecker2 = false;//v0.2
            }

            InitCheck();

            if (!IsPrimaryCameraReady())// || Camera.current != null)
            {
                // Debug.Log("No cam4");
                return;
            }


            //v0.2
            Vector3 currentTransformPos = transform.position;
            float updateDistanceSqr = updateDistance * updateDistance;
            if (Application.isPlaying && updatePerDistance && (currentTransformPos - lastUpdatePos).sqrMagnitude < updateDistanceSqr && currentTransformPos != lastTransformPos)//v0.3
            {
                lastTransformPos = currentTransformPos;
                return;
            }
            lastUpdatePos = currentTransformPos;

            if (Application.isPlaying && updatePerTime && Time.fixedTime - lastUpdateTime < updateTime)
            {
                return;
            }
            lastUpdateTime = Time.fixedTime;


            if (!updateGI)
            {
                return;
            }

            //     return;

            //Cache the previous active render texture to avoid issues with other Unity rendering going on
            RenderTexture previousActive = RenderTexture.active;

            Shader.SetGlobalInt(SegiVoxelAAId, voxelAA ? 1 : 0);

            //v0.2
            //RenderTexture gi1 = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
            //Camera.main.targetTexture = gi1;
            //UniversalRenderPipeline.RenderSingleCamera(context, Camera.main);
            //preRenderers.SetTexture("_MainTex", gi1);
            //Camera.main.targetTexture = null; 

            //Main voxelization work
            if (renderState == RenderState.Voxelize)
            {
                activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;             //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
                previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

                //float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;			//Calculate the size of a voxel texel in world-space units



                //Setup the voxel volume origin position
                float interval = voxelSpaceSize / 8.0f;                                             //The interval at which the voxel volume will be "locked" in world-space
                Vector3 origin;
                if (followTransform)
                {
                    origin = followTransform.position;
                }
                else
                {
                    //GI is still flickering a bit when the scene view and the game view are opened at the same time
                    origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
                }
                //Lock the voxel volume origin based on the interval
                voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

                //Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
                voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
                Shader.SetGlobalVector(SegiVoxelSpaceOriginDeltaId, voxelSpaceOriginDelta / voxelSpaceSize);

                previousVoxelSpaceOrigin = voxelSpaceOrigin;



                //Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
                voxelCamera.enabled = false;//true;//v0.1 false;
                voxelCamera.orthographic = true;
                voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
                voxelCamera.nearClipPlane = 0.0f;
                voxelCamera.farClipPlane = voxelSpaceSize;
                voxelCamera.depth = -2;
                voxelCamera.renderingPath = RenderingPath.Forward;
                voxelCamera.clearFlags = CameraClearFlags.Color;
                voxelCamera.backgroundColor = Color.black;
                voxelCamera.cullingMask = giCullingMask;


                //Move the voxel camera game object and other related objects to the above calculated voxel space origin
                voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
                voxelCameraGO.transform.rotation = rotationFront;

                leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
                leftViewPoint.transform.rotation = rotationLeft;
                topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
                topViewPoint.transform.rotation = rotationTop;



                //Set matrices needed for voxelization
                Shader.SetGlobalMatrix(WorldToCameraId, attachedCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelViewFrontId, TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewLeftId, TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiVoxelViewTopId, TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
                Shader.SetGlobalMatrix(SegiWorldToVoxelId, voxelCamera.worldToCameraMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionId, voxelCamera.projectionMatrix);
                Shader.SetGlobalMatrix(SegiVoxelProjectionInverseId, voxelCamera.projectionMatrix.inverse);

                Shader.SetGlobalInt(SegiVoxelResolutionId, (int)voxelResolution);

                Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
                Shader.SetGlobalMatrix(SegiVoxelToGiProjectionId, voxelToGIProjection);
                Shader.SetGlobalVector(SegiSunlightVectorId, sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);

                //Set paramteters
                Shader.SetGlobalColor(GiSunColorId, sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
                Shader.SetGlobalColor(SegiSkyColorId, new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
                Shader.SetGlobalFloat(GIGainShaderId, giGain);
                Shader.SetGlobalFloat(SegiSecondaryBounceGainId, infiniteBounces ? secondaryBounceGain : 0.0f);
                Shader.SetGlobalFloat(SegiSoftSunlightId, softSunlight);
                Shader.SetGlobalInt(SegiSphericalSkylightId, sphericalSkylight ? 1 : 0);
                Shader.SetGlobalInt(SegiInnerOcclusionLayersId, innerOcclusionLayers);


                //Render the depth texture from the sun's perspective in order to inject sunlight with shadows during voxelization
                if (sun != null)
                {
                    if (renderSunWithSRP)
                    {
                        var urpAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                        var urpAsset0 = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                        GraphicsSettings.defaultRenderPipeline = null;
                        GraphicsSettings.defaultRenderPipeline = null;
                        //v0.3
                        var urpAsset2 = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
                        QualitySettings.renderPipeline = null;

                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;


                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        shadowCam.RenderWithShader(sunDepthShader, "");
                        shadowCam.targetTexture = sunDepthTexture;
                        preRenderers.shader = sunDepthShader;
                        //RenderSingleCameraCompat(context, shadowCam);

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));

                        //v0.3
                        if (urpAsset != null)
                        {
                            GraphicsSettings.defaultRenderPipeline = urpAsset;
                        }
                        if (urpAsset2 != null)
                        {
                            QualitySettings.renderPipeline = urpAsset2;
                        }
                        if (urpAsset0 != null)
                        {
                            GraphicsSettings.defaultRenderPipeline = urpAsset0;
                        }
                    }
                    else
                    {
                        shadowCam.cullingMask = giCullingMask;

                        Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

                        shadowCamTransform.position = shadowCamPosition;
                        shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

                        shadowCam.renderingPath = RenderingPath.Forward;
                        shadowCam.depthTextureMode |= DepthTextureMode.None;

                        shadowCam.orthographicSize = shadowSpaceSize;
                        shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;


                        Graphics.SetRenderTarget(sunDepthTexture);
                        shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

                        //shadowCam.RenderWithShader(sunDepthShader, "");
                        shadowCam.targetTexture = sunDepthTexture;
                        preRenderers.shader = sunDepthShader;

                        if (UniversalRenderPipeline.asset.renderScale != 1)
                        {
                            float scaleSaved = UniversalRenderPipeline.asset.renderScale;
                            UniversalRenderPipeline.asset.renderScale = 1;
                            RenderSingleCameraCompat(context, shadowCam);
                            UniversalRenderPipeline.asset.renderScale = scaleSaved;
                        }
                        else
                        {
                            RenderSingleCameraCompat(context, shadowCam);
                        }

                        Shader.SetGlobalTexture(SegiSunDepthId, sunDepthTexture);
                        Shader.SetGlobalVector(SegiSunDepthTexelSizeId, new Vector4(1.0f / sunDepthTexture.width, 1.0f / sunDepthTexture.height, sunDepthTexture.width, sunDepthTexture.height));
                    }
                }









                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.SetInt("Res", (int)voxelResolution);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);






                //v0.2
                //RenderTexture gi1 = RenderTexture.GetTemporary(dummyVoxelTextureAAScaled.width, dummyVoxelTextureAAScaled.height, 0, RenderTextureFormat.ARGBHalf);
                //voxelCamera.targetTexture = gi1;
                //preRenderers.shader = null;
                //RenderSingleCameraCompat(context, voxelCamera);
                //preRenderers.SetTexture("_MainTex", gi1);
                //voxelCamera.targetTexture = dummyVoxelTextureAAScaled;




                //v0.2
                //if(gi1 == null)
                //{
                //    gi1 = new RenderTexture(dummyVoxelTextureAAScaled.width, dummyVoxelTextureAAScaled.height, 0, RenderTextureFormat.ARGBHalf);
                //}
                // RenderTexture gi1 = RenderTexture.GetTemporary(dummyVoxelTextureAAScaled.width, dummyVoxelTextureAAScaled.height, 0, RenderTextureFormat.ARGBHalf);
                //if (Camera.main != null && 1==1)
                //{
                //    //Camera.main.targetTexture = gi1;
                //    if (savedCam == null)
                //    {
                //        GameObject savedCamOBJ = new GameObject();
                //        savedCam = savedCamOBJ.AddComponent<Camera>();
                //        savedCam.enabled = false;
                //    }
                //    savedCam.CopyFrom(voxelCamera);
                //    savedCam.enabled = false;
                //    savedCam.hideFlags = HideFlags.None;
                //    if (savedCam.targetTexture == null)
                //    {
                //        savedCam.targetTexture = gi1;
                //    }
                //    UniversalRenderPipeline.RenderSingleCamera(context, savedCam);
                //    preRenderers.SetTexture("_MainTex", gi1);
                //    //Camera.main.targetTexture = null;
                //    //Camera.main.CopyFrom(savedCam);
                //}

                //v0.2
                //if (debugSceneColorMat != null)
                //{
                //    debugSceneColorMat.SetTexture("_BaseMap", gi1);
                //}
                // preRenderers.SetTexture("_MainTex", gi1);


                //if (savedCam != null)
                //{
                //    savedCam.targetTexture = gi1;
                //    UniversalRenderPipeline.RenderSingleCamera(context, savedCam);
                //    preRenderers.SetTexture("_MainTex", gi1);
                //}

                if (!colorizeVolume)
                {
                    //Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
                    Graphics.SetRandomWriteTarget(1, integerVolume);

                    voxelCamera.targetTexture = dummyVoxelTextureAAScaled;

                    //v0.1
                    //voxelCamera.RenderWithShader(voxelizationShader, "");

                    Shader.SetGlobalVector(CutoffGiId, cutoff); //v1.6a

                    if (proxyNoGeom) //v1.3
                    {
                        if (enableLocalLightGI)//v1.5
                        {
                            Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                            preRenderers.shader = voxelizationShaderVERTL;
                        }
                        else
                        {
                            preRenderers.shader = voxelizationShaderVERT;
                        }
                    }
                    else
                    {
                        if (enableLocalLightGI)//v1.5
                        {
                            Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                            preRenderers.shader = voxelizationShaderL;
                        }
                        else
                        {
                            preRenderers.shader = voxelizationShader;
                        }
                    }

                    //2.0
                    UnityEngine.Rendering.Universal.UniversalAdditionalCameraData additionalCameraData =
                        voxelCamera.transform.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    additionalCameraData.SetRenderer(voxelizerID);

                    //preRenderers.SetTexture("_MainTex", gi1);
                    if (UniversalRenderPipeline.asset.renderScale != 1)
                    {
                        float scaleSaved = UniversalRenderPipeline.asset.renderScale;
                        UniversalRenderPipeline.asset.renderScale = 1;
                        RenderSingleCameraCompat(context, voxelCamera);
                        UniversalRenderPipeline.asset.renderScale = scaleSaved;
                    }
                    else
                    {
                        RenderSingleCameraCompat(context, voxelCamera);
                    }

                    Graphics.ClearRandomWriteTargets();
                }
                else
                {
                    var urpAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                    var urpAsset0 = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                    GraphicsSettings.defaultRenderPipeline = null;
                    GraphicsSettings.defaultRenderPipeline = null;

                    //v0.3
                    var urpAsset2 = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
                    QualitySettings.renderPipeline = null;

                    //Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
                    Graphics.SetRandomWriteTarget(1, integerVolume);

                    voxelCamera.targetTexture = dummyVoxelTextureAAScaled;

                    Shader.SetGlobalVector(CutoffGiId, cutoff); //v1.6

                    if (proxyNoGeom) //v1.3
                    {
                        if (enableLocalLightGI)//v1.5
                        {                           
                            Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                            voxelCamera.RenderWithShader(voxelizationShaderVERTL, "");
                        }
                        else
                        {
                            voxelCamera.RenderWithShader(voxelizationShaderVERT, "");
                        }
                    }
                    else
                    {
                        if (enableLocalLightGI)//v1.5
                        {                           
                            Shader.SetGlobalFloat(ShadowedLocalPowerId, shadowedLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalPowerId, shadowlessLocalPower);
                            Shader.SetGlobalFloat(ShadowlessLocalOcclusionId, shadowlessLocalOcclusion);
                            voxelCamera.RenderWithShader(voxelizationShaderL, "");
                        }
                        else
                        {
                            //v0.1
                            voxelCamera.RenderWithShader(voxelizationShader, "");
                        }
                    }


                    //preRenderers.shader = voxelizationShader;
                    //preRenderers.SetTexture("_MainTex", gi1);
                    //RenderSingleCameraCompat(context, voxelCamera);

                    Graphics.ClearRandomWriteTargets();

                    //v0.3
                    if (urpAsset != null)
                    {
                        GraphicsSettings.defaultRenderPipeline = urpAsset;
                    }
                    if (urpAsset2 != null)
                    {
                        QualitySettings.renderPipeline = urpAsset2;
                    }
                    if (urpAsset0 != null)
                    {
                        GraphicsSettings.defaultRenderPipeline = urpAsset0;
                    }
                    //UniversalRenderPipeline.RenderSingleCamera(context, Camera.main);
                }

                //v0.2
                //gi1.Release();
                // RenderTexture.ReleaseTemporary(gi1);

                //Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
                transferIntsCompute.SetTexture(0, "Result", activeVolume);
                transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
                transferIntsCompute.SetTexture(0, "RG0", integerVolume);
                transferIntsCompute.SetInt("VoxelAA", voxelAA ? 1 : 0);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
                transferIntsCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeLevel0Id, activeVolume);

                //Manually filter/render mip maps
                for (int i = 0; i < numMipLevels - 1; i++)
                {
                    RenderTexture source = volumeTextures[i];

                    if (i == 0)
                    {
                        source = activeVolume;
                    }

                    int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
                    mipFilterCompute.SetInt("destinationRes", destinationRes);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Source", source);
                    mipFilterCompute.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);

                    //v0.6
                    int threadGroupSize = destinationRes / 8;
                    threadGroupSize = (threadGroupSize > 0) ? threadGroupSize : 1;
                    mipFilterCompute.Dispatch(mipFilterKernel, threadGroupSize, threadGroupSize, 1);

                    //mipFilterCompute.Dispatch(mipFilterKernel, destinationRes / 8, destinationRes / 8, 1);
                    Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
                }

                //Advance the voxel flip flop counter
                voxelFlipFlop += 1;
                voxelFlipFlop = voxelFlipFlop % 2;

                if (infiniteBounces)
                {
                    renderState = RenderState.Bounce;
                }

                //v0.2
                // RenderTexture.ReleaseTemporary(gi1);
            }
            else if (renderState == RenderState.Bounce)
            {

                //Clear the volume texture that is immediately written to in the voxelization scene shader
                clearCompute.SetTexture(0, "RG0", integerVolume);
                clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                //Set secondary tracing parameters
                Shader.SetGlobalInt(SegiSecondaryConesId, secondaryCones);
                Shader.SetGlobalFloat(SegiSecondaryOcclusionStrengthId, secondaryOcclusionStrength);






                ////v0.2
                //RenderTexture gi1 = RenderTexture.GetTemporary(dummyVoxelTextureAAScaled.width, dummyVoxelTextureAAScaled.height, 0, RenderTextureFormat.ARGBHalf);
                //if (Camera.main != null && 1 == 1)
                //{
                //    //Camera.main.targetTexture = gi1;
                //    if (savedCam == null)
                //    {
                //        GameObject savedCamOBJ = new GameObject();
                //        savedCam = savedCamOBJ.AddComponent<Camera>();
                //        savedCam.enabled = false;
                //    }
                //    savedCam.CopyFrom(voxelCamera);
                //    savedCam.enabled = false;
                //    savedCam.hideFlags = HideFlags.None;
                //    if (savedCam.targetTexture == null)
                //    {
                //        savedCam.targetTexture = gi1;
                //    }
                //    UniversalRenderPipeline.RenderSingleCamera(context, savedCam);
                //    preRenderers.SetTexture("_MainTex", gi1);
                //    //Camera.main.targetTexture = null;
                //    //Camera.main.CopyFrom(savedCam);
                //}




                if (!colorizeVolume)
                {
                    //Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
                    Graphics.SetRandomWriteTarget(1, integerVolume);
                    voxelCamera.targetTexture = dummyVoxelTextureFixed;

                    //v0.1
                    //voxelCamera.RenderWithShader(voxelTracingShader, "");
                    //v1.3
                    if (proxyNoGeom)
                    {
                        preRenderers.shader = voxelTracingShaderVERT;
                    }
                    else
                    {
                        preRenderers.shader = voxelTracingShader;
                    }

                    //2.0
                    UnityEngine.Rendering.Universal.UniversalAdditionalCameraData additionalCameraData =
                        voxelCamera.transform.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    additionalCameraData.SetRenderer(bouncerID);

                    if (UniversalRenderPipeline.asset.renderScale != 1)
                    {
                        float scaleSaved = UniversalRenderPipeline.asset.renderScale;
                        UniversalRenderPipeline.asset.renderScale = 1;
                        RenderSingleCameraCompat(context, voxelCamera);
                        UniversalRenderPipeline.asset.renderScale = scaleSaved;
                    }
                    else
                    {
                        RenderSingleCameraCompat(context, voxelCamera);
                    }


                    Graphics.ClearRandomWriteTargets();
                }
                else
                {
                    var urpAsset = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
                    GraphicsSettings.defaultRenderPipeline = null;

                    //v0.3
                    var urpAsset2 = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
                    QualitySettings.renderPipeline = null;


                    Graphics.SetRandomWriteTarget(1, integerVolume);
                    voxelCamera.targetTexture = dummyVoxelTextureFixed;

                    //v1.3
                    if (proxyNoGeom)
                    {
                        voxelCamera.RenderWithShader(voxelTracingShaderVERT, "");
                    }
                    else
                    {
                        //v0.1
                        voxelCamera.RenderWithShader(voxelTracingShader, "");
                    }
                    //preRenderers.shader = voxelTracingShader;
                    //RenderSingleCameraCompat(context, voxelCamera);

                    Graphics.ClearRandomWriteTargets();

                    //v0.3
                    if (urpAsset != null)
                    {
                        GraphicsSettings.defaultRenderPipeline = urpAsset;
                    }
                    if (urpAsset2 != null)
                    {
                        QualitySettings.renderPipeline = urpAsset2;
                    }
                }


                //v0.2
                if (clearBounceCameraTarget)
                {
                    voxelCamera.targetTexture = null;
                }



                //Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
                transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
                transferIntsCompute.SetTexture(1, "RG0", integerVolume);
                transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
                transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

                Shader.SetGlobalTexture(SegiVolumeTexture1Id, secondaryIrradianceVolume);

                renderState = RenderState.Voxelize;

                //v0.2
                //RenderTexture.ReleaseTemporary(gi1);
            }

            //v0.1 - added so depth texture returns to main camera one !!!!!!!!!!!!!!!!!
            //UniversalRenderPipeline.RenderSingleCamera(context, Camera.main);

            RenderTexture.active = previousActive;
        }

        //[ImageEffectOpaque]
        //void OnRenderImage(RenderTexture source, RenderTexture destination)
        //{
        //    if (notReadyToRender)
        //    {
        //        Graphics.Blit(source, destination);
        //        return;
        //    }

        //    //Set parameters
        //    Shader.SetGlobalFloat("SEGIVoxelScaleFactor", voxelScaleFactor);

        //    material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
        //    material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
        //    material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
        //    material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
        //    material.SetInt("FrameSwitch", frameCounter);
        //    Shader.SetGlobalInt("SEGIFrameSwitch", frameCounter);
        //    material.SetVector("CameraPosition", transform.position);
        //    material.SetFloat("DeltaTime", Time.deltaTime);

        //    material.SetInt("StochasticSampling", stochasticSampling ? 1 : 0);
        //    material.SetInt("TraceDirections", cones);
        //    material.SetInt("TraceSteps", coneTraceSteps);
        //    material.SetFloat("TraceLength", coneLength);
        //    material.SetFloat("ConeSize", coneWidth);
        //    material.SetFloat("OcclusionStrength", occlusionStrength);
        //    material.SetFloat("OcclusionPower", occlusionPower);
        //    material.SetFloat("ConeTraceBias", coneTraceBias);
        //    material.SetFloat("GIGain", giGain);
        //    material.SetFloat("NearLightGain", nearLightGain);
        //    material.SetFloat("NearOcclusionStrength", nearOcclusionStrength);
        //    material.SetInt("DoReflections", doReflections ? 1 : 0);
        //    material.SetInt("HalfResolution", halfResolution ? 1 : 0);
        //    material.SetInt("ReflectionSteps", reflectionSteps);
        //    material.SetFloat("ReflectionOcclusionPower", reflectionOcclusionPower);
        //    material.SetFloat("SkyReflectionIntensity", skyReflectionIntensity);
        //    material.SetFloat("FarOcclusionStrength", farOcclusionStrength);
        //    material.SetFloat("FarthestOcclusionStrength", farthestOcclusionStrength);
        //    material.SetTexture("NoiseTexture", blueNoise[frameCounter % 64]);
        //    material.SetFloat("BlendWeight", temporalBlendWeight);

        //    //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
        //    if (visualizeVoxels)
        //    {
        //        Graphics.Blit(source, destination, material, Pass.VisualizeVoxels);
        //        return;
        //    }

        //    //Setup temporary textures
        //    RenderTexture gi1 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
        //    RenderTexture gi2 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
        //    RenderTexture reflections = null;

        //    //If reflections are enabled, create a temporary render buffer to hold them
        //    if (doReflections)
        //    {
        //        reflections = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
        //    }

        //    //Setup textures to hold the current camera depth and normal
        //    RenderTexture currentDepth = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        //    currentDepth.filterMode = FilterMode.Point;

        //    RenderTexture currentNormal = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        //    currentNormal.filterMode = FilterMode.Point;

        //    //Get the camera depth and normals
        //    Graphics.Blit(source, currentDepth, material, Pass.GetCameraDepthTexture);
        //    material.SetTexture("CurrentDepth", currentDepth);
        //    Graphics.Blit(source, currentNormal, material, Pass.GetWorldNormals);
        //    material.SetTexture("CurrentNormal", currentNormal);

        //    //Set the previous GI result and camera depth textures to access them in the shader
        //    material.SetTexture("PreviousGITexture", previousGIResult);
        //    Shader.SetGlobalTexture("PreviousGITexture", previousGIResult);
        //    material.SetTexture("PreviousDepth", previousCameraDepth);

        //    //Render diffuse GI tracing result
        //    Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
        //    if (doReflections)
        //    {
        //        //Render GI reflections result
        //        Graphics.Blit(source, reflections, material, Pass.SpecularTrace);
        //        material.SetTexture("Reflections", reflections);
        //    }


        //    //Perform bilateral filtering
        //    if (useBilateralFiltering)
        //    {
        //        material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
        //        Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);

        //        material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
        //        Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);

        //        material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
        //        Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);

        //        material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
        //        Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);
        //    }

        //    //If Half Resolution tracing is enabled
        //    if (giRenderRes == 2)
        //    {
        //        RenderTexture.ReleaseTemporary(gi1);

        //        //Setup temporary textures
        //        RenderTexture gi3 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
        //        RenderTexture gi4 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);


        //        //Prepare the half-resolution diffuse GI result to be bilaterally upsampled
        //        gi2.filterMode = FilterMode.Point;
        //        Graphics.Blit(gi2, gi4);

        //        RenderTexture.ReleaseTemporary(gi2);

        //        gi4.filterMode = FilterMode.Point;
        //        gi3.filterMode = FilterMode.Point;


        //        //Perform bilateral upsampling on half-resolution diffuse GI result
        //        material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
        //        Graphics.Blit(gi4, gi3, material, Pass.BilateralUpsample);
        //        material.SetVector("Kernel", new Vector2(0.0f, 1.0f));

        //        //Perform temporal reprojection and blending
        //        if (temporalBlendWeight < 1.0f)
        //        {
        //            Graphics.Blit(gi3, gi4);
        //            Graphics.Blit(gi4, gi3, material, Pass.TemporalBlend);
        //            Graphics.Blit(gi3, previousGIResult);
        //            Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
        //        }

        //        //Set the result to be accessed in the shader
        //        material.SetTexture("GITexture", gi3);

        //        //Actually apply the GI to the scene using gbuffer data
        //        Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

        //        //Release temporary textures
        //        RenderTexture.ReleaseTemporary(gi3);
        //        RenderTexture.ReleaseTemporary(gi4);
        //    }
        //    else    //If Half Resolution tracing is disabled
        //    {
        //        //Perform temporal reprojection and blending
        //        if (temporalBlendWeight < 1.0f)
        //        {
        //            Graphics.Blit(gi2, gi1, material, Pass.TemporalBlend);
        //            Graphics.Blit(gi1, previousGIResult);
        //            Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
        //        }

        //        //Actually apply the GI to the scene using gbuffer data
        //        material.SetTexture("GITexture", temporalBlendWeight < 1.0f ? gi1 : gi2);
        //        Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

        //        //Release temporary textures
        //        RenderTexture.ReleaseTemporary(gi1);
        //        RenderTexture.ReleaseTemporary(gi2);
        //    }

        //    //Release temporary textures
        //    RenderTexture.ReleaseTemporary(currentDepth);
        //    RenderTexture.ReleaseTemporary(currentNormal);

        //    //Visualize the sun depth texture
        //    if (visualizeSunDepthTexture)
        //        Graphics.Blit(sunDepthTexture, destination);


        //    //Release the temporary reflections result texture
        //    if (doReflections)
        //    {
        //        RenderTexture.ReleaseTemporary(reflections);
        //    }

        //    //Set matrices/vectors for use during temporal reprojection
        //    material.SetMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
        //    material.SetMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
        //    material.SetMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
        //    material.SetMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
        //    material.SetVector("CameraPositionPrev", transform.position);

        //    //Advance the frame counter
        //    frameCounter = (frameCounter + 1) % (64);
        //}
    }

}


