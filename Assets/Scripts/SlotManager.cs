using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class SlotManager : MonoBehaviour
{
    public static SlotManager I;
    public event Action OnFail;

    [Header("Slot Noktaları (soldan sağa)")]
    public List<Transform> slots = new List<Transform>();

    [Header("Yerleştirme Tween")]
    public float moveDuration = 0.4f;
    public Ease moveEase = Ease.InOutSine;

    [Header("Merge Tween (ESKİ: slot içi) / (YENİ: merkez animleri)")]
    [Tooltip("Merkezde birleştirmeye giderken yükselme süresi")]
    public float mergeRiseDuration = 0.25f;
    public Ease mergeRiseEase = Ease.OutSine;

    [Tooltip("Yükseldikten sonra merkeze hareket süresi")]
    public float mergeToCenterDuration = 0.35f;
    public Ease mergeToCenterEase = Ease.InOutSine;

    [Tooltip("Merkezde iç içe çekme (opsiyonel kısa kaydırma) süresi")]
    public float mergeCollapseDuration = 0.2f;
    public Ease mergeCollapseEase = Ease.InQuad;

    [Tooltip("Yükselme miktarı (Dünya uzayı Y ekseni)")]
    public float mergeRiseAmount = 1.5f;

    [Tooltip("Merkez noktasını temsil eden Transform. Boşsa slotların ortalaması alınır.")]
    public Transform mergeAnchor;

    // runtime
    public bool[] occupied;

    // Hangi blok hangi slotta?
    private Dictionary<Block3D, int> cubeToSlot = new Dictionary<Block3D, int>();
    public IReadOnlyDictionary<Block3D, int> CubeToSlot => cubeToSlot;

    private HashSet<Block3D> _seated = new HashSet<Block3D>();

    private Coroutine _autoFireCoroutine;
    private bool _isMerging;

    // ---- FAIL "hedef yok" durumunu sabitlemek için küçük debounce ----
    private float _noTargetSince = -1f;
    [SerializeField] private float _failDebounceSeconds = 0.3f;
    private HashSet<Block3D> _mergeLock = new HashSet<Block3D>(); 
    private HashSet<int> _mergeReservedSlots = new HashSet<int>();

    private float _suppressFailUntil = -1f;


    private readonly List<KeyValuePair<Block3D, int>> _pairsBuf = new List<KeyValuePair<Block3D, int>>(16);
    private readonly HashSet<string> _colorSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Block3D>> _groupsByColor =
        new Dictionary<string, List<Block3D>>(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Block3D cube, int slotIdx)> _capturedBuf = new List<(Block3D, int)>(4);
    private readonly List<int> _sortedIdxBuf = new List<int>(4);

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (slots == null) slots = new List<Transform>();
        occupied = new bool[slots.Count];
        cubeToSlot.Clear();
        _seated.Clear();
        _noTargetSince = -1f;
        _suppressFailUntil = -1f;
    }

    /// <summary> Dışarıdan "bu küp slota oturdu mu?" </summary>
    public bool IsSeated(Block3D cube) => cube != null && _seated.Contains(cube);

    /// <summary> Kullanıcı tıklayınca: soldaki ilk boş slota yerleştir. </summary>
    public bool TryPlaceToLeftmostEmpty(Block3D cube)
    {
        if (cube == null || slots == null || slots.Count == 0) return false;

        for (int i = 0; i < slots.Count; i++)
        {
            if (!occupied[i])
            {
                PlaceToSlot(cube, i);
                return true;
            }
        }
        return false;
    }

    /// <summary> Verilen slot indexine yerleştir. Ateş, tween bitince başlar. </summary>
    public void PlaceToSlot(Block3D cube, int slotIndex)
    {
        if (cube == null || slotIndex < 0 || slotIndex >= slots.Count) return;

        // Ön sıradan bir blok kopuyorsa Grid'e haber ver → kolonu öne kaydır
        if (cube.grid != null && cube.row == 0)
            cube.grid.DetachFrontAndShift(cube);

        occupied[slotIndex] = true;
        cubeToSlot[cube] = slotIndex;

        _seated.Remove(cube);

        Vector3 targetPos = slots[slotIndex].position;

        // Gidiş yönüne doğru doğal dönüş (slot rotasyonuna sabitlemiyoruz)
        Vector3 dir = (targetPos - cube.transform.position).normalized;
        if (dir != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir, Vector3.up);
            cube.transform
                .DORotateQuaternion(lookRot, moveDuration * 0.7f)
                .SetEase(moveEase);
        }

        Sequence seq = DOTween.Sequence();
        seq.Append(
            cube.transform.DOMove(targetPos, moveDuration)
                .SetEase(moveEase)
        );

        float hopPower = 0.8f;
        float hopDur = 0.18f;      
        seq.AppendCallback(() =>
        {
            cube.transform.DORotate(Vector3.zero, 0.12f).SetEase(Ease.OutQuad);
            cube.transform.DOJump(targetPos, hopPower, 1, hopDur).SetEase(Ease.OutQuad);
        });

        seq.AppendInterval(hopDur);
        seq.OnComplete(() =>
        {
            _seated.Add(cube);
            _suppressFailUntil = Mathf.Max(_suppressFailUntil, Time.time + 0.2f);
            BeginAutoWave();
        });

        seq.Play();
    }





    public bool TryPlaceByWorld(Block3D cube, Vector3 worldPos)
    {
        int best = -1; float bestDist = float.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            if (occupied[i]) continue;
            float d = Vector3.SqrMagnitude(slots[i].position - worldPos);
            if (d < bestDist) { best = i; bestDist = d; }
        }
        if (best >= 0) { PlaceToSlot(cube, best); return true; }
        return false;
    }

    public void BeginAutoWave()
    {
        if (TryStartMerge()) return;

        if (TargetGridManager.I != null)
            TargetGridManager.I.BeginWave(cubeToSlot);

        _suppressFailUntil = Mathf.Max(_suppressFailUntil, Time.time + 0.15f);

        _noTargetSince = -1f;

        if (_autoFireCoroutine != null) StopCoroutine(_autoFireCoroutine);
        _autoFireCoroutine = StartCoroutine(AutoFireRoutine());
    }

    IEnumerator AutoFireRoutine()
    {
        while (true)
        {
            bool anyFired = false;

            _pairsBuf.Clear();
            foreach (var kv in cubeToSlot) _pairsBuf.Add(kv);

            for (int i = 0; i < _pairsBuf.Count; i++)
            {
                var kv = _pairsBuf[i];
                var cube = kv.Key;
                var slotIndex = kv.Value;
                if (cube == null) continue;
                if (!_seated.Contains(cube)) continue;
                if (_mergeLock.Contains(cube)) continue; 

                if (cube.FireOneFromSlot(slotIndex))
                {
                    anyFired = true;
                    yield return new WaitForSeconds(cube.fireCooldown);
                }
            }

            if (!anyFired)
            {
                
                if (_mergeLock != null && _mergeLock.Count > 0)
                {
                    _noTargetSince = -1f;
                    yield return null;
                    continue;
                }

                if (TargetGridManager.I != null)
                {
                    
                    _colorSet.Clear();
                    for (int i = 0; i < _pairsBuf.Count; i++)
                    {
                        var cube = _pairsBuf[i].Key;
                        if (cube != null && _seated.Contains(cube) && cube.ammo > 0)
                        {
                            var id = TargetGridManager.NormalizeId(cube.blockId);
                            if (!string.IsNullOrEmpty(id)) _colorSet.Add(id);
                        }
                    }

                    bool hasFrontTargets =
                        _colorSet.Count > 0 &&
                        TargetGridManager.I.HasAnyFrontForColors(_colorSet);

                    bool allSlotsFull = false;
                    if (occupied != null && occupied.Length > 0)
                    {
                        allSlotsFull = true;
                        for (int i = 0; i < occupied.Length; i++)
                        {
                            if (_mergeReservedSlots.Contains(i)) continue;
                            if (!occupied[i]) { allSlotsFull = false; break; }
                        }
                    }

                    
                    if (Time.time < _suppressFailUntil)
                    {
                        _noTargetSince = -1f;
                        yield return null;
                        continue;
                    }

                    if (hasFrontTargets)
                    {
                        _noTargetSince = -1f;
                        yield return null;
                        continue;
                    }
                    else
                    {
                        if (allSlotsFull)
                        {
                            if (_noTargetSince < 0f) _noTargetSince = Time.time;
                            if (Time.time - _noTargetSince >= _failDebounceSeconds)
                            {
                                Debug.Log("FAIL: Tüm slotlar dolu ve önde vurulacak hedef yok ❌");
                                OnFail?.Invoke();
                                _noTargetSince = -1f;
                                break;
                            }
                            else
                            {
                                yield return null;
                                continue;
                            }
                        }
                        else
                        {
                            _noTargetSince = -1f;
                            break;
                        }
                    }
                }
                break;
            }

            yield return null;
        }
        _autoFireCoroutine = null;
    }

    bool TryStartMerge()
    {
        if (cubeToSlot == null || cubeToSlot.Count == 0) return false;

        _groupsByColor.Clear();

        foreach (var kv in cubeToSlot)
        {
            var cube = kv.Key;
            if (cube == null) continue;
            if (cube.ammo <= 0) continue;
            if (!_seated.Contains(cube)) continue;

            var color = TargetGridManager.NormalizeId(cube.blockId);
            if (string.IsNullOrEmpty(color)) continue;

            if (!_groupsByColor.TryGetValue(color, out var list))
            {
                list = new List<Block3D>(3);
                _groupsByColor[color] = list;
            }
            list.Add(cube);

            if (list.Count == 3)
            {
                var trio = new List<Block3D>(3) { list[0], list[1], list[2] };
                StartCoroutine(MergeRoutineCenter(color, trio));
                return true;
            }
        }

        return false;
    }

    Vector3 GetMergeCenter()
    {
        if (mergeAnchor != null) return mergeAnchor.position;

        if (slots != null && slots.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var t = slots[i];
                if (t == null) continue;
                sum += t.position;
                count++;
            }
            if (count > 0) return sum / Mathf.Max(1, count);
        }
        return Vector3.zero;
    }

    int FindLeftmostEmptyIndex()
    {
        if (occupied == null || occupied.Length == 0) return -1;
        for (int i = 0; i < occupied.Length; i++)
            if (!occupied[i]) return i;
        return -1;
    }

    IEnumerator MergeRoutineCenter(string color, List<Block3D> cubes)
    {
        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i];
            if (c != null) _mergeLock.Add(c);
        }

        float mergeTotal = mergeRiseDuration + mergeToCenterDuration + mergeCollapseDuration + 0.1f;
        _suppressFailUntil = Mathf.Max(_suppressFailUntil, Time.time + mergeTotal);

        _capturedBuf.Clear();
        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i];
            if (c != null && cubeToSlot.TryGetValue(c, out var s))
                _capturedBuf.Add((c, s));
        }

        _sortedIdxBuf.Clear();
        for (int i = 0; i < _capturedBuf.Count; i++) _sortedIdxBuf.Add(_capturedBuf[i].slotIdx);
        _sortedIdxBuf.Sort();
        int midSlot = _sortedIdxBuf.Count == 3 ? _sortedIdxBuf[1] : (_sortedIdxBuf.Count > 0 ? _sortedIdxBuf[0] : -1);

        for (int i = 0; i < _capturedBuf.Count; i++)
        {
            _seated.Remove(_capturedBuf[i].cube);
            cubeToSlot.Remove(_capturedBuf[i].cube);
        }

        for (int i = 0; i < _capturedBuf.Count; i++)
        {
            int idx = _capturedBuf[i].slotIdx;
            if (idx >= 0 && idx < occupied.Length)
                occupied[idx] = (idx == midSlot); // sadece orta true
        }
        if (midSlot >= 0) _mergeReservedSlots.Add(midSlot);

        // Tweenler
        Vector3 center = GetMergeCenter();
        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i];
            if (c == null) continue;

            var upPos = c.transform.position + Vector3.up * mergeRiseAmount;
            Sequence seq = DOTween.Sequence();
            seq.Append(c.transform.DOMoveY(upPos.y, mergeRiseDuration).SetEase(mergeRiseEase));
            seq.AppendCallback(() =>
            {
                if (c != null) c.SetAmmoUIVisible(false);
            });
            seq.Append(c.transform.DOMove(center, mergeToCenterDuration).SetEase(mergeToCenterEase));
            seq.Append(c.transform.DOMove(center, mergeCollapseDuration).SetEase(mergeCollapseEase));
        }

        float totalWait = mergeRiseDuration + mergeToCenterDuration + mergeCollapseDuration;
        yield return new WaitForSeconds(totalWait);

        int totalAmmo = 0;
        for (int i = 0; i < cubes.Count; i++)
            if (cubes[i] != null) totalAmmo += Mathf.Max(0, cubes[i].ammo);

        Block3D keeper = null;
        int minSlot = int.MaxValue;
        for (int i = 0; i < _capturedBuf.Count; i++)
        {
            var pair = _capturedBuf[i];
            if (pair.slotIdx < minSlot) { minSlot = pair.slotIdx; keeper = pair.cube; }
        }

        if (keeper != null) keeper.SetAmmoUIVisible(true);

        if (keeper == null)
        {
            // Temizlik: rezervi kaldır, orta slotu da boşalt
            if (midSlot >= 0 && midSlot < occupied.Length) occupied[midSlot] = false;
            _mergeReservedSlots.Remove(midSlot);
            for (int i = 0; i < cubes.Count; i++) _mergeLock.Remove(cubes[i]);
            BeginAutoWave();
            yield break;
        }

        keeper.ammo = totalAmmo;
        keeper.RefreshAmmoUI();

        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i];
            if (c == null || c == keeper) continue;
            _seated.Remove(c);
            _mergeLock.Remove(c);
            Destroy(c.gameObject);
        }

        int targetSlot = (midSlot >= 0) ? midSlot : Mathf.Clamp(minSlot, 0, slots.Count - 1);
        Vector3 targetPos = slots[targetSlot].position;

        keeper.transform.DOJump(targetPos, 1.2f, 1, 0.5f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                PlaceToSlot(keeper, targetSlot);

                _mergeReservedSlots.Remove(targetSlot);

                _mergeLock.Remove(keeper);

                _suppressFailUntil = Mathf.Max(_suppressFailUntil, Time.time + 0.15f);
            });
    }

    // ===================================================================

    [ContextMenu("Reset Slots")]
    public void ResetSlots()
    {
        occupied = new bool[slots.Count];
        cubeToSlot.Clear();
        _seated.Clear();
        if (_autoFireCoroutine != null) { StopCoroutine(_autoFireCoroutine); _autoFireCoroutine = null; }
        _noTargetSince = -1f;
        _mergeLock.Clear();
        _mergeReservedSlots.Clear(); 
        _suppressFailUntil = -1f;
    }

    public void FreeSlotFor(Block3D cube)
    {
        if (cube == null) return;
        if (cubeToSlot != null && cubeToSlot.TryGetValue(cube, out var idx))
        {
            if (occupied != null && idx >= 0 && idx < occupied.Length)
                occupied[idx] = false;
            cubeToSlot.Remove(cube);
        }
        _seated.Remove(cube);
        _mergeLock.Remove(cube);
    }
}
