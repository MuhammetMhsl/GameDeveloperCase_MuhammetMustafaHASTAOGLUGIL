using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LevelProgressUI : MonoBehaviour
{
    [Header("Refs")]
    public Slider progress;
    public TMP_Text progressLabel;

    private int _total;
    private int _remaining;
    private bool _hooked;

    void OnEnable()
    {
        if (LevelManager.I != null)
        {
            LevelManager.I.OnLevelLoaded -= HandleLevelLoaded; 
            LevelManager.I.OnLevelLoaded += HandleLevelLoaded;
        }

        StartCoroutine(HookWhenReady());
    }

    void OnDisable()
    {
        if (LevelManager.I != null)
            LevelManager.I.OnLevelLoaded -= HandleLevelLoaded;

        if (_hooked && TargetGridManager.I != null)
            TargetGridManager.I.OnTargetsUpdated -= HandleTargetsUpdated;

        _hooked = false;
    }

    IEnumerator HookWhenReady()
    {
        float t = 0f;
        // TargetGridManager oluşana kadar (maks 2 sn) bekle
        while (TargetGridManager.I == null && t < 2f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (TargetGridManager.I == null)
        {
            Debug.LogWarning("[LevelProgressUI] TargetGridManager bulunamadı.");
            yield break;
        }

        if (!_hooked)
        {
            TargetGridManager.I.OnTargetsUpdated -= HandleTargetsUpdated; // güvenlik
            TargetGridManager.I.OnTargetsUpdated += HandleTargetsUpdated;
            _hooked = true;
        }

        // İlk değerleri çek ve UI’yi güncelle
        _total     = TargetGridManager.I.TotalTargets;
        _remaining = TargetGridManager.I.RemainingTargets;
        UpdateUI();
    }

    void HandleLevelLoaded(int _)
    {
        StartCoroutine(HookWhenReady());
    }

    void HandleTargetsUpdated(int remaining, int total)
    {
        _total = total;
        _remaining = remaining;

        UpdateUI();
    }

    void UpdateUI()
    {
        int destroyed = Mathf.Clamp(_total - _remaining, 0, _total);
        float ratio = (_total > 0) ? (destroyed / (float)_total) : 1f;

        if (progress != null) progress.value = ratio;
        if (progressLabel != null)
        {
            int percent = Mathf.RoundToInt(ratio * 100f);
            progressLabel.text = percent + "%";
        }
    }
}
