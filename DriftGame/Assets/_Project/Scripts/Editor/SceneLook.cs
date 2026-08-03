using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// M0 畫面升級：後處理、天空、光照、相機抗鋸齒、URP 管線設定。
/// 不動任何模型，這是投報率最高的一步。
///
/// 注意（原本的 bug）：VolumeProfile.Add&lt;T&gt;() 只在記憶體建立元件，
/// 必須再 AssetDatabase.AddObjectToAsset 才會寫進 .asset 檔。
/// 舊版 RallySceneBuilder 漏了這步，導致存檔後 components 是空的 → 後處理完全沒作用。
public static class SceneLook
{
    const string SettingsDir = "Assets/Settings";
    const string MaterialsDir = "Assets/_Project/Materials";
    const string ProfilePath = SettingsDir + "/RallyPostFX.asset";
    const string SkyboxPath = MaterialsDir + "/Skybox_GoldenHour.mat";

    [MenuItem("Tools/Drift Game/Apply Scene Look (M0)")]
    public static void ApplyAll()
    {
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(MaterialsDir);

        var profile = BuildProfile();
        AttachGlobalVolume(profile);
        ApplySkyAndLighting();
        ApplyCameraSettings();
        ApplyPipelineSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneMarkDirty();
        Debug.Log("[SceneLook] M0 畫面設定完成（後處理 / 天空 / 光照 / 抗鋸齒）");
    }

    static void EditorSceneMarkDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    // ---------------- 後處理 ----------------

    /// 建立（或重建）Volume Profile，元件一律以子資產寫入檔案。
    public static VolumeProfile BuildProfile()
    {
        // 既有的先清掉，避免重複堆疊元件
        var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (old != null) AssetDatabase.DeleteAsset(ProfilePath);

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "RallyPostFX";
        AssetDatabase.CreateAsset(profile, ProfilePath);

        // 色調映射：ACES —— 單項效果最大，讓高光不會死白、暗部有層次
        var tone = Add<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);

        // 顏色調整：微提曝光與飽和，拉開對比
        var color = Add<ColorAdjustments>(profile);
        color.postExposure.Override(0.25f);
        color.contrast.Override(14f);
        color.saturation.Override(16f);
        color.colorFilter.Override(new Color(1f, 0.98f, 0.94f)); // 極淡暖色

        // 分離色調：暗部偏藍、亮部偏橘 —— 黃昏感的關鍵
        var split = Add<SplitToning>(profile);
        split.shadows.Override(new Color(0.30f, 0.42f, 0.72f));
        split.highlights.Override(new Color(1f, 0.72f, 0.38f));
        split.balance.Override(-10f);

        // 泛光
        var bloom = Add<Bloom>(profile);
        bloom.intensity.Override(0.85f);
        bloom.threshold.Override(0.95f);
        bloom.scatter.Override(0.68f);
        bloom.tint.Override(new Color(1f, 0.92f, 0.82f));

        // 暗角：把視線收進畫面中央
        var vignette = Add<Vignette>(profile);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.45f);

        // 色差與顆粒：少量即可，多了會髒
        var ca = Add<ChromaticAberration>(profile);
        ca.intensity.Override(0.12f);

        var grain = Add<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.18f);

        // 動態模糊：速度感，強度壓低避免暈車
        var mb = Add<MotionBlur>(profile);
        mb.mode.Override(MotionBlurMode.CameraOnly);
        mb.intensity.Override(0.18f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    /// 建立元件並寫入 profile 資產（關鍵：AddObjectToAsset）。
    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var comp = profile.Add<T>(true);
        comp.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(comp, profile);
        return comp;
    }

    static void AttachGlobalVolume(VolumeProfile profile)
    {
        var existing = Object.FindObjectOfType<Volume>();
        var go = existing != null ? existing.gameObject : new GameObject("Global Volume");
        var volume = existing != null ? existing : go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.profile = profile;
    }

    // ---------------- 天空與光照 ----------------

    static void ApplySkyAndLighting()
    {
        // 優先用 Codex 生成的黃昏全景照片當天空盒（2:1 equirectangular），
        // 沒有才退回程序化天空。全景照的雲層與色彩層次是程序化天空做不出來的。
        var pano = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/SKY_Dusk.png");
        Material sky;

        if (pano != null)
        {
            sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            var panoShader = Shader.Find("Skybox/Panoramic");
            if (sky == null || sky.shader != panoShader)
            {
                if (sky != null) AssetDatabase.DeleteAsset(SkyboxPath);
                sky = new Material(panoShader);
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }
            sky.SetTexture("_MainTex", pano);
            sky.SetFloat("_Exposure", 1.15f);
            sky.SetFloat("_Rotation", 205f);   // 把最亮的夕陽轉到太陽方向
            sky.SetFloat("_Mapping", 1f);      // Latitude-Longitude Layout
            sky.SetFloat("_ImageType", 0f);    // 360 度
        }
        else
        {
            sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.60f, 0.82f));
            sky.SetColor("_GroundColor", new Color(0.32f, 0.29f, 0.26f));
            sky.SetFloat("_Exposure", 1.25f);
        }

        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        // 太陽：低角度長影子是「黃昏感」的來源
        var sun = FindSun();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.color = new Color(1f, 0.89f, 0.72f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.82f;
        sun.transform.rotation = Quaternion.Euler(22f, -38f, 0f);  // 低仰角
        RenderSettings.sun = sun;

        // 環境光用三段漸層：天空偏藍、地面反彈偏暖，物件立刻有空間感
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.50f, 0.68f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.42f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.25f, 0.20f);
        RenderSettings.ambientIntensity = 1f;

        // 霧：壓遠景、藏視距邊界，顏色要貼近天空否則會出現一條灰帶
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 180f;
        RenderSettings.fogEndDistance = 780f;
        RenderSettings.fogColor = new Color(0.68f, 0.70f, 0.76f);

        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
    }

    static Light FindSun()
    {
        foreach (var l in Object.FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) return l;
        var go = new GameObject("Sun");
        return go.AddComponent<Light>();
    }

    // ---------------- 相機 ----------------

    static void ApplyCameraSettings()
    {
        var cam = Camera.main;
        if (cam == null) cam = Object.FindObjectOfType<Camera>();
        if (cam == null) return;

        cam.allowHDR = true;
        cam.allowMSAA = true;
        cam.farClipPlane = 900f;

        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            // 這一行沒開，上面所有後處理都不會出現在畫面上
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
        }
    }

    // ---------------- URP 管線 ----------------

    static void ApplyPipelineSettings()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsDir + "/URP_Pipeline.asset");
        if (urp == null) return;

        urp.supportsHDR = true;              // 沒有 HDR，Bloom 與 ACES 都會失真
        urp.msaaSampleCount = 4;
        urp.shadowDistance = 190f;
        urp.shadowCascadeCount = 4;

        // supportsSoftShadows 在 Unity 6 是唯讀屬性，只能改序列化欄位
        var so = new SerializedObject(urp);
        var softShadows = so.FindProperty("m_SoftShadowsSupported");
        if (softShadows != null) softShadows.boolValue = true;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(urp);
    }
}
