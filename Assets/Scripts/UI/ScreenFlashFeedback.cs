using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ScreenFlashFeedback : MonoBehaviour
{
    private const string FlashShaderName = "UI/Health Flash";

    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

    [Header("Shader")]
    [SerializeField] private Shader flashShader;

    [Header("Damage Flash")]
    [SerializeField] private Color damageColor = new Color(1f, 0.08f, 0.04f, 0.42f);
    [SerializeField] private float damageDuration = 0.42f;
    [SerializeField] private float damageIntensity = 0.82f;
    [SerializeField] private string damageSfxPath;

    [Header("Heal Flash")]
    [SerializeField] private Color healColor = new Color(0.14f, 1f, 0.36f, 0.36f);
    [SerializeField] private float healDuration = 0.58f;
    [SerializeField] private float healIntensity = 0.72f;
    [SerializeField] private string healSfxPath;

    [Header("Look")]
    [SerializeField] private float softness = 1.7f;
    [SerializeField] private float noiseStrength = 0.045f;
    [SerializeField] private float pulse = 0.18f;

    private Image overlayImage;
    private Material flashMaterial;
    private Coroutine flashCoroutine;

    public void PlayDamageFlash()
    {
        PlayFlash(0f, damageColor, damageDuration, damageIntensity, damageSfxPath);
    }

    public void PlayHealFlash()
    {
        PlayFlash(1f, healColor, healDuration, healIntensity, healSfxPath);
    }

    public void SetSoundPaths(string damagePath, string healPath)
    {
        damageSfxPath = damagePath;
        healSfxPath = healPath;
    }

    public void StopFlash()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        SetIntensity(0f);
        if (overlayImage != null)
            overlayImage.enabled = false;
    }

    private void PlayFlash(float mode, Color color, float duration, float intensity, string sfxPath)
    {
        EnsureOverlay();
        PlayFeedbackSound(sfxPath);

        if (overlayImage == null || flashMaterial == null)
            return;

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);

        overlayImage.enabled = true;
        overlayImage.transform.SetAsLastSibling();
        flashCoroutine = StartCoroutine(AnimateFlash(mode, color, Mathf.Max(0.05f, duration), Mathf.Clamp01(intensity)));
    }

    private IEnumerator AnimateFlash(float mode, Color color, float duration, float intensity)
    {
        flashMaterial.SetFloat(ModeId, mode);
        flashMaterial.SetColor(TintColorId, color);
        flashMaterial.SetFloat(SoftnessId, Mathf.Max(0.15f, softness));
        flashMaterial.SetFloat(NoiseStrengthId, Mathf.Max(0f, noiseStrength));
        flashMaterial.SetFloat(PulseId, Mathf.Max(0f, pulse));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float snapIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.16f));
            float fadeOut = Mathf.Pow(1f - t, 1.35f);
            float shimmer = 0.92f + 0.08f * Mathf.Sin(t * Mathf.PI * 5f);

            flashMaterial.SetFloat(ProgressId, t);
            SetIntensity(intensity * snapIn * fadeOut * shimmer);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        flashMaterial.SetFloat(ProgressId, 1f);
        SetIntensity(0f);
        if (overlayImage != null)
            overlayImage.enabled = false;

        flashCoroutine = null;
    }

    private void EnsureOverlay()
    {
        if (overlayImage != null)
            return;

        GameObject overlay = new GameObject("ScreenFlashOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(transform, false);
        overlay.transform.SetAsLastSibling();

        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayImage = overlay.GetComponent<Image>();
        overlayImage.raycastTarget = false;
        overlayImage.color = Color.white;
        overlayImage.enabled = false;

        Shader shader = flashShader != null ? flashShader : Shader.Find(FlashShaderName);
        if (shader == null)
        {
            Debug.LogWarning("Screen flash shader not found: " + FlashShaderName);
            return;
        }

        flashMaterial = new Material(shader);
        flashMaterial.name = "Screen Flash Feedback (Runtime)";
        overlayImage.material = flashMaterial;
        SetIntensity(0f);
    }

    private void PlayFeedbackSound(string sfxPath)
    {
        if (!string.IsNullOrWhiteSpace(sfxPath))
            AudioMgr.Instance?.PlaySound(sfxPath);
    }

    private void SetIntensity(float intensity)
    {
        if (flashMaterial != null)
            flashMaterial.SetFloat(IntensityId, Mathf.Clamp01(intensity));
    }

    private void OnDestroy()
    {
        if (flashMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(flashMaterial);
        else
            DestroyImmediate(flashMaterial);
    }
}
