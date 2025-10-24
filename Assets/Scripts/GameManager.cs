using UnityEngine;

public class GameManager : MonoBehaviour
{
    void OnEnable()
    {
        Hook();
    }

    void Start()
    {
        Hook(); 
    }

    void OnDisable()
    {
        Unhook();
    }

    void Hook()
    {
        if (TargetGridManager.I != null)
        {
            TargetGridManager.I.OnWin -= HandleWin; // çift aboneliğe karşı
            TargetGridManager.I.OnWin += HandleWin;
        }

        if (SlotManager.I != null)
        {
            SlotManager.I.OnFail -= HandleFail;
            SlotManager.I.OnFail += HandleFail;
        }
    }

    void Unhook()
    {
        if (TargetGridManager.I != null)
            TargetGridManager.I.OnWin -= HandleWin;

        if (SlotManager.I != null)
            SlotManager.I.OnFail -= HandleFail;
    }

    void HandleWin()
    {
        Debug.Log("Win olmuş");
        LevelManager.I?.nextLevelCanvas?.SetActive(true);
        LevelManager.I?.restartLevelCanvas?.SetActive(false);
    }

    void HandleFail()
    {
        Debug.Log("Fail olmuş");
        LevelManager.I?.restartLevelCanvas?.SetActive(true);
        LevelManager.I?.nextLevelCanvas?.SetActive(false);
    }
}
