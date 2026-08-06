using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class InterrogationLightFlicker : MonoBehaviour
{
    [Header("Volume")]
    [SerializeField] private Volume volume;

    [Header("기본 화면")]
    [SerializeField] private float normalExposure = -0.25f;
    [SerializeField] private float normalBloom = 0.2f;

    [Header("평상시 깜빡임 설정")]
    [SerializeField] private Vector2 flickerInterval = new Vector2(4f, 9f);
    [SerializeField] private Vector2 darkExposureRange = new Vector2(-1.4f, -0.7f);
    [SerializeField] private Vector2 flickerDurationRange = new Vector2(0.03f, 0.12f);
    [SerializeField] private Vector2 flickerCountRange = new Vector2(2f, 5f);

    [Header("인물 호출 연출")]
    [SerializeField] private Image characterImage;
    [SerializeField] private int callFlickerCount = 3;
    [SerializeField] private float blackoutExposure = -5f;
    [SerializeField] private float blackoutDuration = 0.15f;
    [SerializeField] private float lightRestoreDuration = 0.2f;
    [Tooltip("화면 전체를 덮는 검은 UI Image. Canvas 최상단(가장 마지막 자식 or 가장 높은 Sorting Order)에 배치.\n" +
             "postExposure만으로는 완전한 검정이 되지 않을 때(특히 배경/인물이 Overlay 캔버스 UI라 카메라 후처리를 안 받을 때) 이걸로 확실히 암전시킨다.\n" +
             "비워두면 기존처럼 노출값 조정만 사용한다.")]
    [SerializeField] private Image blackoutOverlay;

    [Header("사운드")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip electricFlickerSound;
    [SerializeField] private AudioClip characterAppearSound;

    private ColorAdjustments colorAdjustments;
    private Bloom bloom;

    private Coroutine flickerCoroutine;
    private bool isCallingCharacter;

    private void Awake()
    {
        if (volume == null)
        {
            Debug.LogError("Global Volume이 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        if (!volume.profile.TryGet(out colorAdjustments))
        {
            Debug.LogError(
                "Volume Profile에 Color Adjustments가 없습니다.",
                this
            );

            enabled = false;
            return;
        }

        // Bloom은 선택 사항
        volume.profile.TryGet(out bloom);

        colorAdjustments.postExposure.overrideState = true;

        // 첫 호출 전까지는 초상화를 꺼둔다(빈 스프라이트의 흰 박스가 미리 보이지 않도록).
        if (characterImage != null)
        {
            characterImage.gameObject.SetActive(false);
        }

        SetOverlayAlpha(0f);
    }

    private void OnEnable()
    {
        SetNormalState();
        StartFlickerLoop();
    }

    private void OnDisable()
    {
        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }

        SetNormalState();
    }

    private void StartFlickerLoop()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
        }

        flickerCoroutine = StartCoroutine(FlickerLoop());
    }

    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            float waitTime = Random.Range(
                flickerInterval.x,
                flickerInterval.y
            );

            yield return new WaitForSeconds(waitTime);

            if (!isCallingCharacter)
            {
                yield return FlickerSequence();
            }
        }
    }

    private IEnumerator FlickerSequence()
    {
        PlayElectricSound();

        int flickerCount = Random.Range(
            Mathf.RoundToInt(flickerCountRange.x),
            Mathf.RoundToInt(flickerCountRange.y) + 1
        );

        for (int i = 0; i < flickerCount; i++)
        {
            SetDarkState(
                Random.Range(
                    darkExposureRange.x,
                    darkExposureRange.y
                )
            );

            yield return new WaitForSeconds(
                Random.Range(
                    flickerDurationRange.x,
                    flickerDurationRange.y
                )
            );

            SetBrightState();

            yield return new WaitForSeconds(
                Random.Range(0.03f, 0.09f)
            );
        }

        colorAdjustments.postExposure.value =
            normalExposure + 0.15f;

        if (bloom != null)
        {
            bloom.intensity.value = normalBloom + 0.2f;
        }

        yield return new WaitForSeconds(0.05f);

        SetNormalState();
    }

    /// <summary>
    /// 외부에서 일반 깜빡임을 강제로 재생한다.
    /// </summary>
    public void PlayFlicker()
    {
        if (!isActiveAndEnabled || isCallingCharacter)
        {
            return;
        }

        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
        }

        flickerCoroutine = StartCoroutine(
            PlayFlickerAndResumeLoop()
        );
    }

    private IEnumerator PlayFlickerAndResumeLoop()
    {
        yield return FlickerSequence();

        flickerCoroutine = StartCoroutine(FlickerLoop());
    }

    // =========================================================
    // 인물 호출 연출
    // =========================================================

    /// <summary>
    /// 전화기로 다른 인물을 호출할 때 사용한다.
    /// 암전 중 characterImage의 Sprite를 교체하고, 화면이 완전히 다시 밝아진 뒤 onComplete를 호출한다
    /// (대사창을 연출이 끝난 다음에 띄우고 싶을 때 사용).
    /// </summary>
    public void PlayCharacterCall(Sprite newCharacterSprite, System.Action onComplete = null, Vector2? customSize = null)
    {
        if (!isActiveAndEnabled)
        {
            onComplete?.Invoke();
            return;
        }

        if (isCallingCharacter)
        {
            onComplete?.Invoke();
            return;
        }

        if (newCharacterSprite == null)
        {
            Debug.LogWarning(
                "호출할 인물 Sprite가 없습니다.",
                this
            );

            onComplete?.Invoke();
            return;
        }

        if (characterImage == null)
        {
            Debug.LogError(
                "Character Image가 연결되지 않았습니다.",
                this
            );

            onComplete?.Invoke();
            return;
        }

        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }

        flickerCoroutine = StartCoroutine(
            BlackoutTransition(
                () =>
                {
                    characterImage.sprite = newCharacterSprite;
                    characterImage.gameObject.SetActive(true);
                    characterImage.enabled = true;

                    if (customSize.HasValue)
                    {
                        characterImage.rectTransform.sizeDelta = customSize.Value;
                    }
                },
                characterAppearSound,
                onComplete
            )
        );
    }

    /// <summary>
    /// 취조 종료 시 사용한다. 암전 중 characterImage를 꺼서 인물을 퇴장시킨다.
    /// </summary>
    public void PlayCharacterDismiss()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (isCallingCharacter)
        {
            return;
        }

        if (characterImage == null)
        {
            Debug.LogError(
                "Character Image가 연결되지 않았습니다.",
                this
            );

            return;
        }

        if (!characterImage.gameObject.activeSelf)
        {
            // 화면에 인물이 없으면 퇴장 연출을 할 필요가 없다.
            return;
        }

        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }

        flickerCoroutine = StartCoroutine(
            BlackoutTransition(
                () =>
                {
                    characterImage.sprite = null;
                    characterImage.gameObject.SetActive(false);
                },
                null
            )
        );
    }

    /// <summary>
    /// 불안정하게 깜빡이다 완전 암전된 뒤 duringBlackout을 실행하고(인물 교체/퇴장 등)
    /// 다시 서서히 밝아지는 공용 시퀀스. 호출/종료 연출이 이 시퀀스를 공유한다.
    /// </summary>
    private IEnumerator BlackoutTransition(
        System.Action duringBlackout,
        AudioClip revealSound,
        System.Action onComplete = null
    )
    {
        isCallingCharacter = true;

        // 전환 직전 불안정하게 여러 번 깜빡인다.
        for (int i = 0; i < callFlickerCount; i++)
        {
            PlayElectricSound();

            SetDarkState(
                Random.Range(
                    darkExposureRange.x - 0.5f,
                    darkExposureRange.y
                )
            );

            yield return new WaitForSeconds(
                Random.Range(
                    flickerDurationRange.x,
                    flickerDurationRange.y
                )
            );

            SetBrightState();

            yield return new WaitForSeconds(
                Random.Range(0.04f, 0.1f)
            );
        }

        // 완전 암전 (노출값 + 확실한 검정 오버레이)
        SetDarkState(blackoutExposure);
        SetOverlayAlpha(1f);
        if (blackoutOverlay != null) blackoutOverlay.raycastTarget = true;

        yield return new WaitForSeconds(blackoutDuration);

        // 화면이 보이지 않을 때 인물을 교체하거나 치운다.
        duringBlackout?.Invoke();

        if (audioSource != null && revealSound != null)
        {
            audioSource.PlayOneShot(revealSound);
        }

        // Sprite 변경이 렌더링될 한 프레임 확보
        yield return null;

        // 천천히 다시 밝게
        yield return RestoreLight();

        isCallingCharacter = false;

        // 평상시 랜덤 깜빡임 재개
        flickerCoroutine = StartCoroutine(FlickerLoop());

        // 화면이 완전히 밝아진 뒤에 호출측 후속 처리(대사창 띄우기 등)를 실행한다.
        onComplete?.Invoke();
    }

    private IEnumerator RestoreLight()
    {
        float elapsedTime = 0f;
        float startExposure = blackoutExposure;

        while (elapsedTime < lightRestoreDuration)
        {
            elapsedTime += Time.deltaTime;

            float ratio = Mathf.Clamp01(
                elapsedTime / lightRestoreDuration
            );

            colorAdjustments.postExposure.value =
                Mathf.Lerp(
                    startExposure,
                    normalExposure,
                    ratio
                );

            if (bloom != null)
            {
                bloom.intensity.value = Mathf.Lerp(
                    0f,
                    normalBloom,
                    ratio
                );
            }

            SetOverlayAlpha(Mathf.Lerp(1f, 0f, ratio));

            yield return null;
        }

        SetOverlayAlpha(0f);
        if (blackoutOverlay != null) blackoutOverlay.raycastTarget = false;

        SetNormalState();
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (blackoutOverlay == null) return;

        var c = blackoutOverlay.color;
        c.a = alpha;
        blackoutOverlay.color = c;
    }

    private void SetDarkState(float exposure)
    {
        colorAdjustments.postExposure.value = exposure;

        if (bloom != null)
        {
            bloom.intensity.value = 0f;
        }
    }

    private void SetBrightState()
    {
        colorAdjustments.postExposure.value =
            Random.Range(
                normalExposure,
                normalExposure + 0.2f
            );

        if (bloom != null)
        {
            bloom.intensity.value =
                Random.Range(
                    normalBloom,
                    normalBloom + 0.15f
                );
        }
    }

    private void SetNormalState()
    {
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value =
                normalExposure;
        }

        if (bloom != null)
        {
            bloom.intensity.value = normalBloom;
        }
    }

    private void PlayElectricSound()
    {
        if (audioSource != null &&
            electricFlickerSound != null)
        {
            audioSource.PlayOneShot(
                electricFlickerSound
            );
        }
    }
}