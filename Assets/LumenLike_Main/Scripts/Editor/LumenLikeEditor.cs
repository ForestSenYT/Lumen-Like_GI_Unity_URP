using UnityEditor;
using UnityEngine;

namespace LumenLike
{
    [CustomEditor(typeof(LumenLike))]
    public class LumenLikeEditor : Editor
    {
        SerializedObject serObj;

        SerializedProperty helperRendererID;
        SerializedProperty voxelizerID;
        SerializedProperty bouncerID;
        SerializedProperty colorizeVolume;

        SerializedProperty cutoff;

        SerializedProperty enableLocalLightGI;
        SerializedProperty shadowedLocalPower;
        SerializedProperty shadowlessLocalPower;
        SerializedProperty shadowlessLocalOcclusion;

        SerializedProperty proxyNoGeom;
        SerializedProperty smoothNormals;
        SerializedProperty DitherControl;
        SerializedProperty contrastA;
        SerializedProperty ReflectControl;
        SerializedProperty disableGI;
        SerializedProperty updatePerDistance;
        SerializedProperty updatePerTime;
        SerializedProperty updateTime;
        SerializedProperty updateDistance;

        SerializedProperty voxelResolution;
        SerializedProperty visualizeSunDepthTexture;
        SerializedProperty visualizeGI;
        SerializedProperty sun;
        SerializedProperty giCullingMask;
        SerializedProperty shadowSpaceSize;
        SerializedProperty temporalBlendWeight;
        SerializedProperty visualizeVoxels;
        SerializedProperty updateGI;
        SerializedProperty skyColor;
        SerializedProperty voxelSpaceSize;
        SerializedProperty useBilateralFiltering;
        SerializedProperty halfResolution;
        SerializedProperty stochasticSampling;
        SerializedProperty infiniteBounces;
        SerializedProperty followTransform;
        SerializedProperty cones;
        SerializedProperty coneTraceSteps;
        SerializedProperty coneLength;
        SerializedProperty coneWidth;
        SerializedProperty occlusionStrength;
        SerializedProperty nearOcclusionStrength;
        SerializedProperty occlusionPower;
        SerializedProperty coneTraceBias;
        SerializedProperty nearLightGain;
        SerializedProperty giGain;
        SerializedProperty secondaryBounceGain;
        SerializedProperty softSunlight;
        SerializedProperty doReflections;
        SerializedProperty voxelAA;
        SerializedProperty reflectionSteps;
        SerializedProperty skyReflectionIntensity;
        SerializedProperty gaussianMipFilter;
        SerializedProperty reflectionOcclusionPower;
        SerializedProperty farOcclusionStrength;
        SerializedProperty farthestOcclusionStrength;
        SerializedProperty secondaryCones;
        SerializedProperty secondaryOcclusionStrength;
        SerializedProperty skyIntensity;
        SerializedProperty sphericalSkylight;
        SerializedProperty innerOcclusionLayers;

        SerializedProperty renderSunWithSRP;
        SerializedProperty clearBounceCameraTarget;
        SerializedProperty preRenderers;

        SerializedProperty visualizeDepth;
        SerializedProperty visualizeNormals;
        SerializedProperty downscaleFactor;

        LumenLike instance;


        GUIStyle sectionTitleStyle;
        GUIStyle sectionBodyStyle;
        GUIStyle statValueStyle;
        bool showSceneCapture = true;
        bool showLighting = true;
        bool showGlobalIllumination = true;
        bool showReflections = true;
        bool showPerformance = true;
        bool showDebug = false;
        bool showRawDetails;


        void OnEnable()
        {
            serObj = new SerializedObject(target);

            helperRendererID = serObj.FindProperty("helperRendererID");
            colorizeVolume = serObj.FindProperty("colorizeVolume");
            voxelizerID = serObj.FindProperty("voxelizerID");
            bouncerID = serObj.FindProperty("bouncerID");

            cutoff = serObj.FindProperty("cutoff");

            enableLocalLightGI = serObj.FindProperty("enableLocalLightGI");
            shadowedLocalPower = serObj.FindProperty("shadowedLocalPower");
            shadowlessLocalPower = serObj.FindProperty("shadowlessLocalPower");
            shadowlessLocalOcclusion = serObj.FindProperty("shadowlessLocalOcclusion");

            proxyNoGeom = serObj.FindProperty("proxyNoGeom");
            smoothNormals = serObj.FindProperty("smoothNormals");
            DitherControl = serObj.FindProperty("DitherControl");
            contrastA = serObj.FindProperty("contrastA");
            ReflectControl = serObj.FindProperty("ReflectControl");
            disableGI = serObj.FindProperty("disableGI");
            updatePerDistance = serObj.FindProperty("updatePerDistance");
            updatePerTime = serObj.FindProperty("updatePerTime");
            updateTime = serObj.FindProperty("updateTime");
            updateDistance = serObj.FindProperty("updateDistance");

            voxelResolution = serObj.FindProperty("voxelResolution");
            visualizeSunDepthTexture = serObj.FindProperty("visualizeSunDepthTexture");
            visualizeGI = serObj.FindProperty("visualizeGI");
            sun = serObj.FindProperty("sun");
            giCullingMask = serObj.FindProperty("giCullingMask");
            shadowSpaceSize = serObj.FindProperty("shadowSpaceSize");
            temporalBlendWeight = serObj.FindProperty("temporalBlendWeight");
            visualizeVoxels = serObj.FindProperty("visualizeVoxels");
            updateGI = serObj.FindProperty("updateGI");
            skyColor = serObj.FindProperty("skyColor");
            voxelSpaceSize = serObj.FindProperty("voxelSpaceSize");
            useBilateralFiltering = serObj.FindProperty("useBilateralFiltering");
            halfResolution = serObj.FindProperty("halfResolution");
            stochasticSampling = serObj.FindProperty("stochasticSampling");
            infiniteBounces = serObj.FindProperty("infiniteBounces");
            followTransform = serObj.FindProperty("followTransform");
            cones = serObj.FindProperty("cones");
            coneTraceSteps = serObj.FindProperty("coneTraceSteps");
            coneLength = serObj.FindProperty("coneLength");
            coneWidth = serObj.FindProperty("coneWidth");
            occlusionStrength = serObj.FindProperty("occlusionStrength");
            nearOcclusionStrength = serObj.FindProperty("nearOcclusionStrength");
            occlusionPower = serObj.FindProperty("occlusionPower");
            coneTraceBias = serObj.FindProperty("coneTraceBias");
            nearLightGain = serObj.FindProperty("nearLightGain");
            giGain = serObj.FindProperty("giGain");
            secondaryBounceGain = serObj.FindProperty("secondaryBounceGain");
            softSunlight = serObj.FindProperty("softSunlight");
            doReflections = serObj.FindProperty("doReflections");
            voxelAA = serObj.FindProperty("voxelAA");
            reflectionSteps = serObj.FindProperty("reflectionSteps");
            skyReflectionIntensity = serObj.FindProperty("skyReflectionIntensity");
            gaussianMipFilter = serObj.FindProperty("gaussianMipFilter");
            reflectionOcclusionPower = serObj.FindProperty("reflectionOcclusionPower");
            farOcclusionStrength = serObj.FindProperty("farOcclusionStrength");
            farthestOcclusionStrength = serObj.FindProperty("farthestOcclusionStrength");
            secondaryCones = serObj.FindProperty("secondaryCones");
            secondaryOcclusionStrength = serObj.FindProperty("secondaryOcclusionStrength");
            skyIntensity = serObj.FindProperty("skyIntensity");
            sphericalSkylight = serObj.FindProperty("sphericalSkylight");
            innerOcclusionLayers = serObj.FindProperty("innerOcclusionLayers");

            renderSunWithSRP = serObj.FindProperty("renderSunWithSRP");
            clearBounceCameraTarget = serObj.FindProperty("clearBounceCameraTarget");
            preRenderers = serObj.FindProperty("preRenderers");

            visualizeDepth = serObj.FindProperty("visualizeDEPTH");
            visualizeNormals = serObj.FindProperty("visualizeNORMALS");
            downscaleFactor = serObj.FindProperty("downscaleFactor");

            instance = target as LumenLike;
        }

        public override void OnInspectorGUI()
        {
            serObj.Update();
            EnsureStyles();

            DrawOverview();
            EditorGUILayout.Space(6f);
            DrawSection(ref showSceneCapture, "Scene Capture", "Configure the voxel volume, scene coverage, and core update path.", DrawSceneCaptureSection);
            DrawSection(ref showLighting, "Lighting", "Drive sky, sun, and local light injection into the GI volume.", DrawLightingSection);
            DrawSection(ref showGlobalIllumination, "Global Illumination", "Adjust trace quality, occlusion response, and bounce energy.", DrawGlobalIlluminationSection);
            DrawSection(ref showReflections, "Reflections", "Tune the cone-traced reflection pass separately from diffuse GI.", DrawReflectionSection);
            DrawSection(ref showPerformance, "Performance", "Control update cadence and the main performance-saving switches.", DrawPerformanceSection);
            DrawSection(ref showDebug, "Debug", "Visualization and validation tools for tracing, depth, and voxel data.", DrawDebugSection);
            DrawSection(ref showRawDetails, "Developer Overrides", "Low-level legacy and renderer wiring controls. These are kept out of the main workflow on purpose.", DrawRawSection);

            serObj.ApplyModifiedProperties();
        }

        void EnsureStyles()
        {
            if (sectionTitleStyle == null)
            {
                sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    richText = true
                };
            }

            if (sectionBodyStyle == null)
            {
                sectionBodyStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 8, 10)
                };
            }

            if (statValueStyle == null)
            {
                statValueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }
        }

        void DrawOverview()
        {
            EditorGUILayout.BeginVertical(sectionBodyStyle);
            EditorGUILayout.LabelField("LumenLike Global Illumination", sectionTitleStyle);
            EditorGUILayout.LabelField("A UE-style control surface for scene capture, indirect lighting, reflections, and renderer integration.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(disableGI, new GUIContent("Disable System", "Disable the full LumenLike GI stack."));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("VRAM", GUILayout.Width(40f));
            EditorGUILayout.LabelField(instance != null ? instance.vramUsage.ToString("F2") + " MB" : "n/a", statValueStyle, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();

            if (instance != null && instance.sun == null)
            {
                EditorGUILayout.HelpBox("Assign a main directional light in Lighting so sunlight injection and stable shadowing work as expected.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawSection(ref bool expanded, string title, string description, System.Action drawContent)
        {
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, title);
            if (expanded)
            {
                EditorGUILayout.BeginVertical(sectionBodyStyle);
                if (!string.IsNullOrEmpty(description))
                {
                    EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4f);
                }

                drawContent?.Invoke();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space(4f);
        }
        void DrawSceneCaptureSection()
        {
            EditorGUILayout.PropertyField(voxelResolution, new GUIContent("Voxel Resolution", "Resolution of the main voxel volume used for GI."));
            EditorGUILayout.PropertyField(voxelSpaceSize, new GUIContent("Voxel Scene Extent", "World-space size covered by the GI voxel volume."));
            EditorGUILayout.PropertyField(shadowSpaceSize, new GUIContent("Shadow Scene Extent", "World-space coverage of the injected sunlight shadow map."));
            EditorGUILayout.PropertyField(giCullingMask, new GUIContent("GI Culling Mask", "Layers that get voxelized and contribute to indirect lighting."));
            EditorGUILayout.PropertyField(followTransform, new GUIContent("Volume Anchor", "Optional transform that the voxel volume follows instead of the current camera."));
            EditorGUILayout.PropertyField(updateGI, new GUIContent("Update GI", "Allow voxelization and bounce rendering to keep refreshing."));
            EditorGUILayout.PropertyField(voxelAA, new GUIContent("Voxel AA", "Supersample voxelization for cleaner occupancy."));
            EditorGUILayout.PropertyField(innerOcclusionLayers, new GUIContent("Inner Occlusion Layers", "Additional back-face occlusion layers that help contain light leaks."));
            EditorGUILayout.PropertyField(gaussianMipFilter, new GUIContent("Gaussian Mip Filter", "Smooth voxel mip generation for more stable large-scale lighting."));
            EditorGUILayout.PropertyField(smoothNormals, new GUIContent("Surface Normal Smoothing", "Blend normals before tracing to trade precision for stability or a softer look."));
        }

        void DrawLightingSection()
        {
            EditorGUILayout.PropertyField(sun, new GUIContent("Directional Light", "Primary directional light used for sunlight injection."));
            EditorGUILayout.PropertyField(softSunlight, new GUIContent("Soft Sunlight", "Extra diffuse sky-scattered sunlight contribution."));
            EditorGUILayout.PropertyField(skyColor, new GUIContent("Sky Color", "Tint of skylight that enters the scene."));
            EditorGUILayout.PropertyField(skyIntensity, new GUIContent("Sky Intensity", "Energy of the skylight contribution."));
            EditorGUILayout.PropertyField(sphericalSkylight, new GUIContent("Full Sphere Skylight", "Allow skylight from every direction instead of only the upper hemisphere."));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Local Light Injection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableLocalLightGI, new GUIContent("Enable Local Light GI", "Inject point and spot lights into the GI solution."));
            if (enableLocalLightGI.boolValue)
            {
                EditorGUILayout.PropertyField(shadowedLocalPower, new GUIContent("Shadowed Local Light Power", "Contribution from local lights with shadowing enabled."));
                EditorGUILayout.PropertyField(shadowlessLocalPower, new GUIContent("Shadowless Local Light Power", "Contribution from local lights without shadowing."));
                EditorGUILayout.PropertyField(shadowlessLocalOcclusion, new GUIContent("Shadowless Local Light Occlusion", "Occlusion factor applied to shadowless local light GI."));
            }
        }

        void DrawGlobalIlluminationSection()
        {
            EditorGUILayout.PropertyField(temporalBlendWeight, new GUIContent("Temporal Blend", "Lower values accumulate more history for smoother but slower-updating GI."));
            EditorGUILayout.PropertyField(useBilateralFiltering, new GUIContent("Bilateral Filter", "Filter the traced GI result while respecting depth discontinuities."));
            EditorGUILayout.PropertyField(stochasticSampling, new GUIContent("Stochastic Sampling", "Use randomized tracing to reduce banding and structured artifacts."));
            EditorGUILayout.PropertyField(infiniteBounces, new GUIContent("Infinite Bounces", "Enable iterative in-volume bounce accumulation."));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Diffuse Trace", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cones, new GUIContent("Cones", "Number of diffuse trace cones."));
            EditorGUILayout.PropertyField(coneTraceSteps, new GUIContent("Trace Steps", "Maximum steps taken per cone."));
            EditorGUILayout.PropertyField(coneLength, new GUIContent("Trace Length", "Maximum normalized distance traced by each cone."));
            EditorGUILayout.PropertyField(coneWidth, new GUIContent("Cone Width", "Angular spread of each cone."));
            EditorGUILayout.PropertyField(coneTraceBias, new GUIContent("Trace Bias", "Offset from the surface to reduce self-shadowing."));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Occlusion & Energy", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(occlusionStrength, new GUIContent("Occlusion Strength", "Primary indirect shadow strength."));
            EditorGUILayout.PropertyField(nearOcclusionStrength, new GUIContent("Near Occlusion", "Extra occlusion from nearby blockers."));
            EditorGUILayout.PropertyField(farOcclusionStrength, new GUIContent("Far Occlusion", "Additional occlusion from distant blockers."));
            EditorGUILayout.PropertyField(farthestOcclusionStrength, new GUIContent("Farthest Occlusion", "Extra distant blocker weight for very broad cones."));
            EditorGUILayout.PropertyField(occlusionPower, new GUIContent("Occlusion Power", "Curves the overall occlusion response."));
            EditorGUILayout.PropertyField(nearLightGain, new GUIContent("Near Light Gain", "Boost or clamp close-range indirect energy."));
            EditorGUILayout.PropertyField(giGain, new GUIContent("GI Gain", "Overall diffuse indirect light multiplier."));
            EditorGUILayout.PropertyField(secondaryBounceGain, new GUIContent("Secondary Bounce Gain", "Energy multiplier for higher-order bounces."));
            EditorGUILayout.PropertyField(secondaryCones, new GUIContent("Secondary Cones", "Trace count used for secondary bounce evaluation."));
            EditorGUILayout.PropertyField(secondaryOcclusionStrength, new GUIContent("Secondary Occlusion", "Occlusion strength used during secondary bounce tracing."));
        }

        void DrawReflectionSection()
        {
            EditorGUILayout.PropertyField(doReflections, new GUIContent("Enable Reflections", "Turn on cone-traced reflections."));
            if (doReflections.boolValue)
            {
                EditorGUILayout.PropertyField(reflectionSteps, new GUIContent("Trace Steps", "Number of steps used for the reflection trace."));
                EditorGUILayout.PropertyField(reflectionOcclusionPower, new GUIContent("Occlusion Power", "Shadowing strength applied inside the reflection trace."));
                EditorGUILayout.PropertyField(skyReflectionIntensity, new GUIContent("Sky Reflection Intensity", "Sky contribution seen through reflections."));
            }
        }

        void DrawPerformanceSection()
        {
            EditorGUILayout.PropertyField(halfResolution, new GUIContent("Half Resolution Tracing", "Trace GI at half resolution, then reconstruct to full resolution."));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Update Budget", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(updatePerDistance, new GUIContent("Update Per Distance", "Only refresh GI when the camera or anchor moves far enough."));
            if (updatePerDistance.boolValue)
            {
                EditorGUILayout.PropertyField(updateDistance, new GUIContent("Distance Threshold", "Distance required before the scene is re-voxelized."));
            }

            EditorGUILayout.PropertyField(updatePerTime, new GUIContent("Update Per Time", "Refresh GI on a time interval instead of every frame."));
            if (updatePerTime.boolValue)
            {
                EditorGUILayout.PropertyField(updateTime, new GUIContent("Time Threshold", "Time between GI refreshes."));
            }
        }
        void DrawDebugSection()
        {
            EditorGUILayout.PropertyField(visualizeSunDepthTexture, new GUIContent("Visualize Sun Depth", "Preview the injected sun shadow texture."));
            EditorGUILayout.PropertyField(visualizeGI, new GUIContent("Visualize GI", "Preview only the GI contribution without the scene color blend."));
            EditorGUILayout.PropertyField(visualizeVoxels, new GUIContent("Visualize Voxels", "Render voxel occupancy directly."));
        }

        void DrawRawSection()
        {
            EditorGUILayout.LabelField("Developer Overrides", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("These controls still affect the runtime path, but they are legacy or renderer-internal settings rather than normal product-facing tuning.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.PropertyField(renderSunWithSRP, new GUIContent("Render Sun With SRP", "Use the SRP path for sun depth rendering instead of the fallback path."));
            EditorGUILayout.PropertyField(downscaleFactor, new GUIContent("Half Resolution Divisor", "Internal divisor used when half-resolution tracing is enabled."));
            EditorGUILayout.PropertyField(clearBounceCameraTarget, new GUIContent("Clear Bounce Camera Target", "Force the bounce camera target to clear before reuse."));
            EditorGUILayout.PropertyField(preRenderers, new GUIContent("Helper Override Material", "Material assigned to the helper Render Objects pass."));
            EditorGUILayout.PropertyField(colorizeVolume, new GUIContent("Use Native URP Path", "Use the current URP-native helper path instead of the legacy fallback."));
            EditorGUILayout.PropertyField(helperRendererID, new GUIContent("Helper Renderer Slot", "Renderer index used by the helper pre-pass camera."));
            EditorGUILayout.PropertyField(voxelizerID, new GUIContent("Voxelizer Renderer Slot", "Renderer index used by the voxelization camera."));
            EditorGUILayout.PropertyField(bouncerID, new GUIContent("Bounce Renderer Slot", "Renderer index used by the bounce accumulation camera."));
            EditorGUILayout.PropertyField(proxyNoGeom, new GUIContent("Approximation Mode", "Use the reduced geometry path for compatibility or experimentation."));
            EditorGUILayout.PropertyField(cutoff, new GUIContent("Depth Occlusion Cutoff", "Vector parameters that shape the depth-based occlusion fallback."));
            EditorGUILayout.PropertyField(contrastA, new GUIContent("Albedo Contrast Bias", "Legacy albedo shaping control used in the SEGI blend stage."));
            EditorGUILayout.PropertyField(ReflectControl, new GUIContent("Reflection Control Vector", "Low-level reflection/specular shaping values."));
            EditorGUILayout.PropertyField(DitherControl, new GUIContent("Dither Control Vector", "Low-level jitter and trace divider controls."));

            EditorGUILayout.PropertyField(visualizeDepth, new GUIContent("Visualize Depth", "Debug preview of the depth path used by the GI pass."));
            EditorGUILayout.PropertyField(visualizeNormals, new GUIContent("Visualize Normals", "Debug preview of the world normal path used by the GI pass."));

        }
    }
}




