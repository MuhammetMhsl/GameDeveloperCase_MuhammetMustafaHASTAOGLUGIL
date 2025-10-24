using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    [Header("Layout")]
    public LevelLayout layout;

    [Header("Konumlandırma")]
    public Transform origin;                  // (0,0) hücresinin dünyadaki başlangıç noktası
    public Vector3 right = Vector3.right;     // sütun yönü
    public Vector3 forward = Vector3.back;    // satır yönü (row=0 ön)
    public float cellSizeX = 1.5f;
    public float cellSizeZ = 1.5f;

    [Header("Tween")]
    public float moveDuration = 0.35f;
    public Ease moveEase = Ease.InOutSine;
    public bool tweenIgnoreTimeScale = false;

    // runtime durum
    private Block3D[,] grid;
    private Dictionary<string, BlockDef> _defMap; // ID → BlockDef
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public Transform exitTransform;
    public Transform exitTransformFirst;

    // --- EKLENDİ: MPB için renk alan ID'leri (Opaque/URP uyumu) ---
    private static readonly int _ColorID     = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    void Start()
    {
        if (layout != null)
            BuildFromLayout();
    }

    #region Build

    [ContextMenu("Build From Layout")]
    public void BuildFromLayout()
    {
        if (!ValidateLayout()) return;

#if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
#else
        foreach (Transform t in transform) Destroy(t.gameObject);
#endif

        Columns = Mathf.Max(1, layout.columns);
        Rows    = Mathf.Max(1, layout.rows);
        grid    = new Block3D[Rows, Columns];

        
        _defMap = new Dictionary<string, BlockDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in layout.blockDefs)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
            var key = def.id.Trim();
            if (!_defMap.ContainsKey(key))
                _defMap.Add(key, def);
        }

        for (int r = 0; r < Rows; r++)
        {
            var ids   = ParseIds(layout.idRows[r]);
            var ammos = ParseAmmos(layout.ammoRows[r]);

            if (ids.Length != Columns || ammos.Length != Columns)
            {
                Debug.LogError($"GridManager: Satır {r} token sayıları Columns ile eşleşmiyor. " +
                               $"IDs={ids.Length}, Ammos={ammos.Length}, Columns={Columns}");
                return;
            }

            for (int c = 0; c < Columns; c++)
            {
                string idToken = ids[c];
                if (string.IsNullOrWhiteSpace(idToken) || idToken == ".")
                    continue; // boş hücre

                if (!_defMap.TryGetValue(idToken, out var def) || def == null || def.prefab == null)
                {
                    Debug.LogError($"GridManager: ID '{idToken}' için BlockDef/prefab bulunamadı (satır {r}, sütun {c}).");
                    continue;
                }

                int ammo = ammos[c];

                var pos   = CellToWorld(r, c);
                var block = Instantiate(def.prefab, pos, Quaternion.identity, transform);

                // grid meta
                block.grid = this;
                block.row  = r;
                block.col  = c;
                block.SetId(def.id);

                // ammo & UI
                block.ammo = Mathf.Max(0, ammo);
                block.RefreshAmmoUI();

                // Materyal atama
                if (def.material != null)
                {
                    var renderers = block.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (renderers[i] != null)
                            renderers[i].material = def.material;
                    }
                }

                grid[r, c] = block;
            }
        }

       
        UpdateFades();
    }

    bool ValidateLayout()
    {
        if (layout == null)
        {
            Debug.LogError("GridManager: Layout atanmadı.");
            return false;
        }

        if (layout.idRows == null || layout.ammoRows == null)
        {
            Debug.LogError("GridManager: idRows veya ammoRows null.");
            return false;
        }

        if (layout.idRows.Count != layout.rows || layout.ammoRows.Count != layout.rows)
        {
            Debug.LogError($"GridManager: idRows({layout.idRows.Count}) / ammoRows({layout.ammoRows.Count}) " +
                           $"rows({layout.rows}) ile uyuşmuyor.");
            return false;
        }

        if (layout.blockDefs == null || layout.blockDefs.Count == 0)
        {
            Debug.LogError("GridManager: blockDefs listesi boş. En az bir BlockDef ekle.");
            return false;
        }

        return true;
    }

    string[] ParseIds(string row)
    {
        if (row == null) return new string[0];

        
        var tokens = row.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); 
        if (tokens.Length == 0 && layout.idSeparator != '\0')
            tokens = row.Split(layout.idSeparator).Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();

       
        for (int i = 0; i < tokens.Length; i++)
            tokens[i] = tokens[i].Trim().ToUpperInvariant();

        return tokens;
    }

    int[] ParseAmmos(string row)
    {
        if (row == null) return new int[0];

        var raw = row.Split(layout.ammoSeparator);
        var list = new List<int>(raw.Length);
        foreach (var t in raw)
        {
            var s = t.Trim();
            if (s.Length == 0) continue;
            if (int.TryParse(s, out var v)) list.Add(v);
            else
            {
                Debug.LogWarning($"GridManager: Ammo parse edilemedi '{s}', 0 kabul edildi.");
                list.Add(0);
            }
        }
        return list.ToArray();
    }

    Vector3 CellToWorld(int row, int col)
    {
        var o = origin ? origin.position : Vector3.zero;
        return o + right.normalized * (col * cellSizeX)
                 + forward.normalized * (row * cellSizeZ);
    }

    #endregion

    #region Runtime Interaction

    /// <summary>
    /// Slot
    /// </summary>
    public void DetachFrontAndShift(Block3D b)
    {
        if (b == null) return;
        if (b.row != 0) return; // sadece ön sıra

        if (grid != null &&
            b.col >= 0 && b.col < Columns &&
            grid[b.row, b.col] == b)
        {
            grid[b.row, b.col] = null;
            ShiftColumnForward(b.col);
        }

    
        b.row = -1;
        b.col = -1;
    }

    
    public void OnBlockClicked(Block3D b)
    {
        if (b == null) return;
        if (b.row != 0) return; // sadece ön sıra

        if (SlotManager.I == null) return;
        bool ok = SlotManager.I.TryPlaceToLeftmostEmpty(b);
        if (!ok) return;

       
        DetachFrontAndShift(b);
    }

    void ShiftColumnForward(int col)
    {
        for (int r = 1; r < Rows; r++)
        {
            var blk = grid[r, col];
            if (blk == null) continue;

            int newRow = r - 1;
            grid[newRow, col] = blk;
            grid[r, col]      = null;

            blk.row = newRow;

            var target = CellToWorld(newRow, col);
            TweenMove(blk, target);
        }

        
        UpdateFades();
    }

    /// <summary>
    /// Rigidbody 
    /// </summary>
    void TweenMove(Block3D blk, Vector3 target)
    {
        var rb = blk.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            DOTween.Kill(rb, complete: false);
            var t = rb.DOMove(target, moveDuration).SetEase(moveEase);
            if (tweenIgnoreTimeScale) t.SetUpdate(true);
        }
        else
        {
            DOTween.Kill(blk.transform, complete: false);
            var t = blk.transform.DOMove(target, moveDuration).SetEase(moveEase);
            if (tweenIgnoreTimeScale) t.SetUpdate(true);
        }
    }

    public bool IsOccupied(int row, int col)
    {
        if (row < 0 || row >= Rows || col < 0 || col >= Columns) return false;
        return grid[row, col] != null;
    }

    public Vector3 GetWorldPos(int row, int col) => CellToWorld(row, col);

    #endregion

    // Ön sıra (row == 0) orijinal; arkadakiler soluk
    // -------------------------------
    void ApplyFade(Block3D blk, bool isFront)
    {
        var renderers = blk.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var r in renderers)
        {
            if (!r) continue;

            var baseCol = r.sharedMaterial ? r.sharedMaterial.color : r.material.color;

            float fade = isFront ? 1f : 0.45f;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            mpb.SetColor(_ColorID,     new Color(baseCol.r * fade, baseCol.g * fade, baseCol.b * fade, baseCol.a));
            mpb.SetColor(_BaseColorID, new Color(baseCol.r * fade, baseCol.g * fade, baseCol.b * fade, baseCol.a));

            r.SetPropertyBlock(mpb);
        }
    }

    void UpdateFades()
    {
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                var blk = grid[r, c];
                if (blk == null) continue;
                ApplyFade(blk, isFront: r == 0);
            }
        }
    }
}
