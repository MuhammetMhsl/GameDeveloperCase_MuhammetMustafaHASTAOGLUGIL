using UnityEngine;
using DG.Tweening;
using TMPro;

[RequireComponent(typeof(Collider))]
public class Block3D : MonoBehaviour
{
    private bool _depletedHandled = false;

    [Header("Kimlik")]
    public string blockId = "R"; 

    [Header("Grid Meta (opsiyonel)")]
    public GridManager grid;   
    public int row = -1;
    public int col = -1;

    [Header("Ateş Ayarları")]
    public GameObject bulletPrefab;
    public Transform muzzle;        
    public int ammo = 10;
    public float fireCooldown = 0.15f;
    public float bulletSpeed = 12f;

    [Header("UI")]
    public TMP_Text ammoText;
    public Transform runtimeExitTransformFirst;
    public Transform runtimeExitTransform;

    // --- dahili ---
    private float _lastFireTime = -999f;
    private Collider _col;
    private float _clickCheckTimer = 0f;   

    private Quaternion _baseRotation;

    [Header("Ses")]
    public AudioSource audioSource;
    public AudioClip fireClip;
    public AudioClip onMouseDownClip;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (ammoText == null)
            ammoText = GetComponentInChildren<TMP_Text>(true);
        SetId(blockId);
    }

    public void SetId(string id)
    {
        blockId = TargetGridManager.NormalizeId(string.IsNullOrWhiteSpace(id) ? blockId : id);
    }

    void Start()
    {
        UpdateAmmoUI();
        RefreshClickability();
        _baseRotation = transform.rotation;
    }

    void Update()
    {
        
        _clickCheckTimer += Time.deltaTime;
        if (_clickCheckTimer >= 0.25f)
        {
            _clickCheckTimer = 0f;
            RefreshClickability();
        }
    }

    // ==== TIKLANABİLİRLİK ====
    private bool IsInSlot()
    {
        // SlotManager var ve bu küp şu an slot map'inde ise "slotta" sayılır
        return (SlotManager.I != null && SlotManager.I.CubeToSlot != null
                && SlotManager.I.CubeToSlot.ContainsKey(this));
    }

    public void SetAmmoUIVisible(bool v)
    {
        if (ammoText != null) ammoText.gameObject.SetActive(v);
    }

    private bool IsClickable()
    {
        // Yalnızca gridde ÖN SIRADAKİ (row == 0) küpler ve slota gitmemiş olanlar tıklanabilir
        return (grid != null && row == 0 && !IsInSlot());
    }

    private void RefreshClickability()
    {
        bool desired = IsClickable();
        if (_col != null && _col.enabled != desired)
            _col.enabled = desired;
    }
    // ==================================

    void OnMouseDown()
    {
        // Sadece ön sıradaysa ve slota yerleşmemişse tıklamayı kabul et
        if (!IsClickable()) return;

        
        if (SlotManager.I != null)
        {
            bool placed = SlotManager.I.TryPlaceToLeftmostEmpty(this);
            if (placed)
            {
                // Slot sürecine girdiği için artık tıklanamaz olmalı
                if (audioSource != null && onMouseDownClip != null)
                    audioSource.PlayOneShot(onMouseDownClip);
                RefreshClickability();
            }
        }
        else if (grid != null)
        {
            
            grid.OnBlockClicked(this);
        }
    }

    
    public bool FireOneFromSlot(int slotIndex)
    {
        if (!CanFire()) return false;
        if (TargetGridManager.I == null) return false;

        
        int tRow, tCol, tUid; Vector3 wpos;
        if (!TargetGridManager.I.TryGetNextAssignedTarget(slotIndex, blockId, out tRow, out tCol, out wpos, out tUid))
            return false;

        transform.DOPunchScale(Vector3.one * 0.2f, 0.15f, 5, 0.8f);//Atei ettiğinde efekt
        PlayFireRecoil();//Silah efekti
        
        ammo--;
        _lastFireTime = Time.time;
        UpdateAmmoUI();

        // spawn noktası
        Vector3 spawnPos = (muzzle != null ? muzzle.position : transform.position);
        spawnPos += (muzzle != null ? muzzle.forward : transform.forward) * 0.1f;
        Quaternion spawnRot = (muzzle != null ? muzzle.rotation : transform.rotation);

        GameObject go = (BulletPool.I != null)
            ? BulletPool.I.GetBullet(spawnPos, spawnRot)
            : Instantiate(bulletPrefab, spawnPos, spawnRot);

        
        Vector3 dir = (wpos - transform.position).normalized;
        Quaternion lookRot = Quaternion.LookRotation(dir, Vector3.up);

        
        transform.DORotateQuaternion(lookRot, 0.1f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                
                transform.DORotateQuaternion(_baseRotation, 0.2f).SetEase(Ease.OutBack);
            });

        var b = go.GetComponent<Bullet>();
        if (b == null) b = go.AddComponent<Bullet>();
        b.colorId = blockId;
        b.targetRow = tRow;
        b.targetCol = tCol;
        b.targetPos = wpos;
        b.speed = bulletSpeed;
        b.targetUid = tUid; 

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        return true;
    }

    public void PlayFireRecoil()
    {
        if (muzzle == null) return;

        
        Vector3 basePos = muzzle.localPosition;
        Vector3 recoilOffset = -muzzle.forward * 0.1f;
        muzzle.DOLocalMove(basePos + recoilOffset, 0.05f)
              .SetEase(Ease.OutQuad)
              .OnComplete(() => muzzle.DOLocalMove(basePos, 0.1f).SetEase(Ease.OutBack));

        if (audioSource != null && fireClip != null)
            audioSource.PlayOneShot(fireClip);
    }

    public bool CanFire()
    {
        if (SlotManager.I != null && !SlotManager.I.IsSeated(this)) return false;

        if (ammo <= 0) return false;
        return (Time.time - _lastFireTime) >= fireCooldown;
    }

    private void UpdateAmmoUI()
    {
        if (ammoText != null) ammoText.text = ammo.ToString();
        if (ammo <= 0) HandleAmmoDepletedOnce();
    }

    private void HandleAmmoDepletedOnce()
    {
        if (_depletedHandled) return;
        _depletedHandled = true;

        Transform first = runtimeExitTransformFirst != null ? runtimeExitTransformFirst
                        : (grid != null ? grid.exitTransformFirst : null);

        Transform final = runtimeExitTransform != null ? runtimeExitTransform
                        : (grid != null ? grid.exitTransform : null);

        if (final == null && first == null)
        {
            if (SlotManager.I != null) SlotManager.I.FreeSlotFor(this);
            return;
        }

        DOTween.Kill(transform, complete: false);

        Sequence seq = DOTween.Sequence();

        seq.Append(transform.DOJump(transform.position, 0.25f, 1, 0.18f).SetEase(Ease.OutQuad));

        seq.AppendCallback(() =>
        {
            if (SlotManager.I != null) SlotManager.I.FreeSlotFor(this);
        });

        if (first != null)
        {
            seq.Append(transform.DOMove(first.position, 0.40f).SetEase(Ease.InOutSine));
            seq.Join(transform.DOLookAt(first.position, 0.35f, AxisConstraint.None, Vector3.up)
                               .SetEase(Ease.InOutSine));
        }

        if (final != null)
        {
            seq.Append(transform.DOMove(final.position, 0.45f).SetEase(Ease.InSine));
            seq.Join(transform.DOLookAt(final.position, 0.35f, AxisConstraint.None, Vector3.up)
                               .SetEase(Ease.InOutSine));
        }

        seq.OnComplete(() =>
        {
            gameObject.SetActive(false);
        });
    }



    public void RefreshAmmoUI() => UpdateAmmoUI();
}
