using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(Collider))]
public class TargetBlock : MonoBehaviour
{
    [Header("Kimlik")]
    public string id = "R"; // R,G,B,Y...

    [Header("Grid Meta (runtime)")]
    [HideInInspector] public int row;
    [HideInInspector] public int col;

    [HideInInspector] public int uid;
    private static int _uidCounter = 1;

    [Header("Görsel (opsiyonel)")]
    public Renderer[] renderers;
    [Header("Efekt")]
    public GameObject effect;

    void Awake()
    {
        if (uid == 0) uid = _uidCounter++;
    }

    public void SetColorMaterial(Material m)
    {
        if (m == null || renderers == null) return;
        foreach (var r in renderers)
            if (r != null) r.material = m;
    }

  
    public void SafePunchScale(float punch, float dur)
    {
        var t = transform;

        DOTween.Kill(this);

        t.localScale = Vector3.one;

        t.DOPunchScale(Vector3.one * punch, dur, 6, 0.8f)
            .SetId(this)                                  
            .OnComplete(() => t.localScale = Vector3.one) 
            .OnKill(()    => t.localScale = Vector3.one); 
    }

    public void JellyNudge(float punch = 0.08f, float dur = 0.18f)
    {
        SafePunchScale(punch, dur);
    }
}
