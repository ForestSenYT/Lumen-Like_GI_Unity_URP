using UnityEditor;
using UnityEngine;

namespace LumenLike
{
    [CustomEditor(typeof(VolumetricLightedSEGI))]
    public class VolumetricLightedSEGIEditor : Editor
    {
        static readonly GUIContent[] DebugViewLabels =
        {
            new GUIContent("None"),
            new GUIContent("Radiance"),
            new GUIContent("Confidence"),
            new GUIContent("Radiance + Confidence")
        };

        static readonly int[] DebugViewValues = { 0, 1, 2, 3 };

        SerializedProperty settings;
        SerializedProperty resolutionScale;
        SerializedProperty intensity;
        SerializedProperty blurWidth;
        SerializedProperty fadeRange;
        SerializedProperty numSamples;
        SerializedProperty noiseSpeed;
        SerializedProperty noiseScale;
        SerializedProperty noiseStrength;
        SerializedProperty enableSurfaceCache;
        SerializedProperty surfaceCacheResolutionScale;
        SerializedProperty surfaceCacheTemporalBlend;
        SerializedProperty surfaceCacheNormalReject;
        SerializedProperty surfaceCacheDepthReject;
        SerializedProperty useSurfaceCacheFallback;
        SerializedProperty surfaceCacheGIBlend;
        SerializedProperty surfaceCacheReflectionBlend;
        SerializedProperty surfaceCacheMinConfidence;
        SerializedProperty surfaceCacheDebugView;
        SerializedProperty surfaceCacheDebugExposure;
        SerializedProperty eventA;

        GUIStyle sectionBodyStyle;
        GUIStyle sectionTitleStyle;

        bool showPass = true;
        bool showNoise = false;
        bool showSurfaceCache = true;
        bool showDebug = false;
        bool showDeveloper = false;

        void OnEnable()
        {
            settings = serializedObject.FindProperty("_settings");
            resolutionScale = FindSetting("resolutionScale");
            intensity = FindSetting("intensity");
            blurWidth = FindSetting("blurWidth");
            fadeRange = FindSetting("fadeRange");
            numSamples = FindSetting("numSamples");
            noiseSpeed = FindSetting("noiseSpeed");
            noiseScale = FindSetting("noiseScale");
            noiseStrength = FindSetting("noiseStrength");
            enableSurfaceCache = FindSetting("enableSurfaceCache");
            surfaceCacheResolutionScale = FindSetting("surfaceCacheResolutionScale");
            surfaceCacheTemporalBlend = FindSetting("surfaceCacheTemporalBlend");
            surfaceCacheNormalReject = FindSetting("surfaceCacheNormalReject");
            surfaceCacheDepthReject = FindSetting("surfaceCacheDepthReject");
            useSurfaceCacheFallback = FindSetting("useSurfaceCacheFallback");
            surfaceCacheGIBlend = FindSetting("surfaceCacheGIBlend");
            surfaceCacheReflectionBlend = FindSetting("surfaceCacheReflectionBlend");
            surfaceCacheMinConfidence = FindSetting("surfaceCacheMinConfidence");
            surfaceCacheDebugView = FindSetting("surfaceCacheDebugView");
            surfaceCacheDebugExposure = FindSetting("surfaceCacheDebugExposure");
            eventA = FindSetting("eventA");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            DrawOverview();
            EditorGUILayout.Space(6f);
            DrawSection(ref showPass, "Composite Pass", "Primary volumetric composite controls and the URP injection point.", DrawPassSection);
            DrawSection(ref showNoise, "Noise & Stability", "Animated noise shaping used to stabilize volumetric sampling.", DrawNoiseSection);
            DrawSection(ref showSurfaceCache, "Surface Cache", "Screen cache reuse for final gather and reflection stability.", DrawSurfaceCacheSection);
            DrawSection(ref showDebug, "Visualization", "Debug views and exposure controls for active cache validation.", DrawDebugSection);
            DrawSection(ref showDeveloper, "Developer Overrides", "Legacy surface-card code has been deleted. Only screen-cache-related developer settings remain.", DrawDeveloperSection);
            serializedObject.ApplyModifiedProperties();
        }

        SerializedProperty FindSetting(string name)
        {
            return settings != null ? settings.FindPropertyRelative(name) : null;
        }

        void EnsureStyles()
        {
            if (sectionBodyStyle == null)
            {
                sectionBodyStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10) };
            }
            if (sectionTitleStyle == null)
            {
                sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            }
        }

        void DrawOverview()
        {
            EditorGUILayout.BeginVertical(sectionBodyStyle);
            EditorGUILayout.LabelField("LumenLike Renderer Feature", sectionTitleStyle);
            EditorGUILayout.LabelField("UE-style layout for volumetric composite, screen cache reuse, and debug visualization.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Screen Cache", GUILayout.Width(95f));
            EditorGUILayout.LabelField(enableSurfaceCache.boolValue ? "Enabled" : "Disabled", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.PropertyField(eventA, GUIContent.none, GUILayout.Width(210f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        void DrawSection(ref bool expanded, string title, string description, System.Action drawContent)
        {
            expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, title);
            if (expanded)
            {
                EditorGUILayout.BeginVertical(sectionBodyStyle);
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);
                drawContent?.Invoke();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space(4f);
        }

        void DrawPassSection()
        {
            EditorGUILayout.PropertyField(eventA, new GUIContent("Injection Point", "URP render event used to inject the feature."));
            EditorGUILayout.PropertyField(resolutionScale, new GUIContent("Resolution Scale", "Internal volumetric buffer scale."));
            EditorGUILayout.PropertyField(intensity, new GUIContent("Intensity", "Overall volumetric contribution."));
            EditorGUILayout.PropertyField(blurWidth, new GUIContent("Blur Width", "Final blur width used on the volumetric result."));
            EditorGUILayout.PropertyField(fadeRange, new GUIContent("Fade Range", "Distance fade used before the final composite."));
            EditorGUILayout.PropertyField(numSamples, new GUIContent("Sample Count", "Raymarch sample count for the volumetric pass."));
        }

        void DrawNoiseSection()
        {
            EditorGUILayout.PropertyField(noiseSpeed, new GUIContent("Noise Pan Speed", "World-space drift of the animated sampling noise."));
            EditorGUILayout.PropertyField(noiseScale, new GUIContent("Noise Scale", "Frequency of the volumetric noise pattern."));
            EditorGUILayout.PropertyField(noiseStrength, new GUIContent("Noise Strength", "How strongly the noise distorts the volumetric integration."));
        }

        void DrawSurfaceCacheSection()
        {
            EditorGUILayout.PropertyField(enableSurfaceCache, new GUIContent("Enable Screen Cache", "Turn on the screen-driven cache reuse path."));
            if (!enableSurfaceCache.boolValue)
            {
                return;
            }
            EditorGUILayout.PropertyField(surfaceCacheResolutionScale, new GUIContent("Screen Cache Resolution", "Internal resolution used for the screen cache history buffers."));
            EditorGUILayout.PropertyField(surfaceCacheTemporalBlend, new GUIContent("Screen Cache History Weight", "History weight used by the screen cache."));
            EditorGUILayout.PropertyField(surfaceCacheNormalReject, new GUIContent("Normal Rejection", "Reject history when normal deviation is too large."));
            EditorGUILayout.PropertyField(surfaceCacheDepthReject, new GUIContent("Depth Rejection", "Reject history when depth mismatch is too large."));
            EditorGUILayout.PropertyField(useSurfaceCacheFallback, new GUIContent("Use Screen Cache Fallback", "Allow the screen cache to feed the final gather and reflection composite."));
            EditorGUILayout.PropertyField(surfaceCacheGIBlend, new GUIContent("Final Gather Blend", "Diffuse contribution injected by the screen cache."));
            EditorGUILayout.PropertyField(surfaceCacheReflectionBlend, new GUIContent("Reflection Blend", "Reflection contribution injected by the screen cache."));
            EditorGUILayout.PropertyField(surfaceCacheMinConfidence, new GUIContent("Minimum Confidence", "Confidence threshold before screen cache contribution becomes visible."));
        }

        void DrawDebugSection()
        {
            int debugView = Mathf.Clamp(surfaceCacheDebugView.enumValueIndex, 0, 3);
            debugView = EditorGUILayout.IntPopup(new GUIContent("Debug View", "Visualize active screen-cache states directly in the Game view."), debugView, DebugViewLabels, DebugViewValues);
            surfaceCacheDebugView.enumValueIndex = debugView;
            EditorGUILayout.PropertyField(surfaceCacheDebugExposure, new GUIContent("Debug Exposure", "Exposure multiplier for active cache debug visualizations."));
        }

        void DrawDeveloperSection()
        {
            EditorGUILayout.HelpBox("Surface-card generation and atlas tooling have been removed from this package.", MessageType.None);
        }
    }
}

