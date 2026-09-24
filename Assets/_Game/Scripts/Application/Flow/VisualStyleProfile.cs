#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Application.Flow
{
    /// <summary>Disposable look-dev data. No shipped asset or gameplay state is written.</summary>
    public sealed class VisualStyleProfile : ScriptableObject
    {
        public string id;
        public TonemappingMode tonemapping;
        public float exposure, contrast, saturation, vignette, bloomThreshold, bloomIntensity, bloomScatter;
        public Vector4 shadows, highlights;
        public AmbientMode ambientMode;
        public Color sky, equator, ground, fogColor, background, lightColor;
        public float ambientIntensity, fogStart, fogEnd, lightIntensity;
        public bool fog;
        public CameraClearFlags clearFlags;

        public static VisualStyleProfile Snapshot(Camera camera, VolumeProfile profile, Light sun)
        {
            var result = CreateInstance<VisualStyleProfile>();
            result.id = "current";
            profile.TryGet(out Tonemapping tone);
            profile.TryGet(out ColorAdjustments color);
            profile.TryGet(out Vignette vignette);
            profile.TryGet(out Bloom bloom);
            profile.TryGet(out ShadowsMidtonesHighlights smh);
            result.tonemapping = tone.mode.value;
            result.exposure = color.postExposure.value;
            result.contrast = color.contrast.value;
            result.saturation = color.saturation.value;
            result.vignette = vignette.intensity.value;
            result.bloomThreshold = bloom.threshold.value;
            result.bloomIntensity = bloom.intensity.value;
            result.bloomScatter = bloom.scatter.value;
            result.shadows = smh.shadows.value;
            result.highlights = smh.highlights.value;
            result.ambientMode = RenderSettings.ambientMode;
            result.sky = RenderSettings.ambientSkyColor;
            result.equator = RenderSettings.ambientEquatorColor;
            result.ground = RenderSettings.ambientGroundColor;
            result.ambientIntensity = RenderSettings.ambientIntensity;
            result.fog = RenderSettings.fog;
            result.fogColor = RenderSettings.fogColor;
            result.fogStart = RenderSettings.fogStartDistance;
            result.fogEnd = RenderSettings.fogEndDistance;
            result.clearFlags = camera.clearFlags;
            result.background = camera.backgroundColor;
            result.lightColor = sun.color;
            result.lightIntensity = sun.intensity;
            return result;
        }

        public VisualStyleProfile Copy(string label)
        {
            var copy = Instantiate(this);
            copy.id = label;
            return copy;
        }

        public static VisualStyleProfile StepOne(VisualStyleProfile current)
        {
            var p = current.Copy("step1");
            p.contrast = 4f;
            p.vignette = 0.12f;
            p.ambientMode = AmbientMode.Trilight;
            p.sky = new Color(0.32f, 0.38f, 0.44f);
            p.equator = new Color(0.17f, 0.18f, 0.19f);
            p.ground = new Color(0.10f, 0.10f, 0.09f);
            p.ambientIntensity = 1.4f;
            return p;
        }

        public static VisualStyleProfile StepTwo(VisualStyleProfile stepOne, TonemappingMode tone)
        {
            var p = stepOne.Copy(tone == TonemappingMode.ACES ? "step2-aces" : "step2-neutral");
            p.exposure = 0.5f;
            p.fogColor = new Color(0.17f, 0.21f, 0.23f);
            p.tonemapping = tone;
            return p;
        }

        public static VisualStyleProfile Cartoon(VisualStyleProfile current)
        {
            var p = current.Copy("cartoon-basic");
            p.tonemapping = TonemappingMode.Neutral;
            p.exposure = 0.7f;
            p.contrast = 2f;
            p.saturation = 25f;
            p.vignette = 0f;
            p.bloomThreshold = 0.95f;
            p.bloomIntensity = 0.35f;
            p.ambientMode = AmbientMode.Trilight;
            p.sky = new Color(0.55f, 0.62f, 0.72f);
            p.equator = new Color(0.40f, 0.42f, 0.42f);
            p.ground = new Color(0.24f, 0.22f, 0.20f);
            p.ambientIntensity = 1.5f;
            p.fog = false;
            p.clearFlags = CameraClearFlags.Skybox;
            p.background = new Color(0.45f, 0.68f, 0.85f);
            p.lightColor = new Color(1f, 0.96f, 0.88f);
            p.lightIntensity = current.lightIntensity * 1.2f;
            return p;
        }

        public void Apply(Camera camera, VolumeProfile runtimeProfile, Light sun)
        {
            // Caller supplies Volume.profile (deep runtime clone), NEVER sharedProfile.
            runtimeProfile.TryGet(out Tonemapping tone);
            runtimeProfile.TryGet(out ColorAdjustments color);
            runtimeProfile.TryGet(out Vignette edge);
            runtimeProfile.TryGet(out Bloom bloom);
            tone.mode.Override(tonemapping);
            color.postExposure.Override(exposure);
            color.contrast.Override(contrast);
            color.saturation.Override(saturation);
            edge.intensity.Override(vignette);
            bloom.threshold.Override(bloomThreshold);
            bloom.intensity.Override(bloomIntensity);
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            camera.clearFlags = clearFlags;
            camera.backgroundColor = background;
            sun.color = lightColor;
            sun.intensity = lightIntensity;
        }
    }
}
#endif
