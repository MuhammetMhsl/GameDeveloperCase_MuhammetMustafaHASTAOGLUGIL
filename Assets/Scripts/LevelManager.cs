using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager I;

    [Header("Level Kaynağı")]
    [Tooltip("Sıralı olarak oynatılacak LevelLayout listesi (drag & drop).")]
    public List<LevelLayout> levels = new List<LevelLayout>();

    [Header("UI (opsiyonel, dışarıdan kontrol edilecek)")]
    public GameObject nextLevelCanvas;
    public GameObject restartLevelCanvas;

    [Header("Build Targetleri")]
    public GridManager playerGrid;       // Player küpleri
    public TargetGridManager targetGrid; // Hedef/duvar

    [Header("Ayarlar")]
    public bool regenerateAutoLevelsOnLoad = false; // autoGenerate ise her yüklemede yeniden üret
    public bool loopWhenFinished = false;           // Son level sonrası başa sar
    public bool autoStartOnPlay = true;             // Play'de otomatik başlat
    public string playerPrefsKey = "LM_CurrentLevel";

    public event Action<int> OnLevelLoaded;
    public event Action<int> OnLevelCompleted;
    public event Action OnAllLevelsCompleted;

    [Header("Next davranışı")]
    public bool randomOnEnd = true;

    public int CurrentIndex { get; private set; } = 0;

    int GetRandomIndexExcept(int except)
    {
        if (levels == null || levels.Count == 0) return 0;
        if (levels.Count == 1) return 0; 

        int idx = UnityEngine.Random.Range(0, levels.Count);
        if (idx == except)
            idx = (idx + 1) % levels.Count; 
        return idx;
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        CurrentIndex = Mathf.Clamp(
            PlayerPrefs.GetInt(playerPrefsKey, 0),
            0,
            Math.Max(0, levels.Count - 1)
        );
    }

    void Start()
    {
        if (autoStartOnPlay)
            LoadLevel(CurrentIndex);
    }


    public void Replay() => LoadLevel(CurrentIndex);

    public void NextLevel()
    {
        if (levels == null || levels.Count == 0) return;

        int next = CurrentIndex + 1;
        if (next >= levels.Count)
        {
            if (randomOnEnd)
                next = GetRandomIndexExcept(CurrentIndex);
            else
                next = loopWhenFinished ? 0 : (levels.Count - 1);
        }

        LoadLevel(next);
    }

    public void PreviousLevel() => Advance(-1);

    public void LoadLevel(int index)
    {
        if (nextLevelCanvas) nextLevelCanvas.SetActive(false);
        if (restartLevelCanvas) restartLevelCanvas.SetActive(false);

        if (SlotManager.I != null)
            SlotManager.I.ResetSlots();

        var bullets = FindObjectsOfType<Bullet>();
        foreach (var blt in bullets)
        {
            if (TargetGridManager.I != null)
                TargetGridManager.I.ReleaseUid(blt.targetUid);
            Destroy(blt.gameObject);
        }

        var leftovers = FindObjectsOfType<Block3D>();
        foreach (var b in leftovers)
        {
            if (b == null) continue;
            bool notInGrid = (b.grid == null) || (b.row < 0) || (b.col < 0);
            if (notInGrid)
                Destroy(b.gameObject);
        }

        if (levels == null || levels.Count == 0)
        {
            Debug.LogError("LevelManager: levels boş.");
            return;
        }

        index = Mathf.Clamp(index, 0, levels.Count - 1);
        var layout = levels[index];
        if (layout == null)
        {
            Debug.LogError($"LevelManager: Level {index} null.");
            return;
        }

        if (regenerateAutoLevelsOnLoad && layout.autoGenerate)
        {
            layout.GenerateNow();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(layout);
#endif
        }

        if (playerGrid != null)
        {
            playerGrid.layout = layout;
            playerGrid.BuildFromLayout();
        }

        if (targetGrid != null)
        {
            targetGrid.layout = layout;
            targetGrid.BuildFromLayout();
        }

        CurrentIndex = index;
        PlayerPrefs.SetInt(playerPrefsKey, CurrentIndex);
        PlayerPrefs.Save();

        if (nextLevelCanvas) nextLevelCanvas.SetActive(false);
        if (restartLevelCanvas) restartLevelCanvas.SetActive(false);

        OnLevelLoaded?.Invoke(CurrentIndex);
        Debug.Log($"[LevelManager] Level yüklendi: {CurrentIndex} ({layout.name})");
    }


    public void Advance(int delta)
    {
        if (levels == null || levels.Count == 0) return;

        int newIndex = CurrentIndex + delta;

        if (newIndex >= levels.Count)
        {
            OnAllLevelsCompleted?.Invoke();
            newIndex = loopWhenFinished ? 0 : (levels.Count - 1);
        }
        else if (newIndex < 0)
        {
            newIndex = 0;
        }

        LoadLevel(newIndex);
    }
}
