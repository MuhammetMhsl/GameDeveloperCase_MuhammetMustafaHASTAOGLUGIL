using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class TargetGridManager : MonoBehaviour
{
    public static TargetGridManager I;
    public event Action OnWin; // Win kontrolü
    public event Action<int, int> OnTargetsUpdated; // (remaining, total)

    [Header("Layout (LevelLayouttan)")]
    public LevelLayout layout;

    [Header("Konumlandırma")]
    public Transform origin;
    public Vector3 right = Vector3.right;
    public Vector3 forward = Vector3.forward;   // sahnede ileri yön
    public float cellSizeX = 1.5f;
    public float cellSizeZ = 1.5f;
    public bool row0IsFront = true;             // row=0 en ön

    [Header("Prefab / Def (opsiyonel)")]
    public List<BlockDef> blockDefs = new List<BlockDef>();
    public TargetBlock targetPrefab;

    [Header("Tween")]
    public float spawnMoveDuration = 0.25f;
    public Ease spawnEase = Ease.OutBack;
    public float fallMoveDuration = 0.35f;
    public Ease fallEase = Ease.InOutSine;
    public float fallPunchScale = 0.08f;
    public float destroyTime = 0.08f;
    public Ease destroyEase = Ease.InOutBack;

    [Header("Domino (Stagger)")]
    public bool dominoStagger = true;
    [Range(0f, 0.2f)] public float dominoStartDelayPerCell = 0.03f;
    [Range(0f, 0.2f)] public float dominoPunchExtraLag = 0.015f;

    public bool fallDurationScalesWithCells = false;
    [Range(0.02f, 0.25f)] public float fallDurationPerCell = 0.08f;

    [Serializable] public struct IdMaterial { public string id; public Material material; }

    [Header("Materyal Sürükle-Bırak")]
    public List<Material> draggedMaterials = new List<Material>();
    public List<IdMaterial> idMaterials = new List<IdMaterial>();
    public bool inferIdsFromMaterialName = true;

    [Header("Auto Color Görünüm")]
    public bool autoColorUseUnlit = true;
    [Range(0.5f, 2.5f)] public float autoColorIntensity = 1.3f;
    [Range(0f, 2f)] public float autoEmission = 0.25f;

    // ---------------- RUNTIME ----------------
    public int TotalTargets { get; private set; }
    public int RemainingTargets { get; private set; }

    private TargetBlock[,] grid;
    private Dictionary<string, BlockDef> _defMap;       // ID -> BlockDef
    private Dictionary<string, Material> _matMap;       // ID -> Dragged/Def materyali
    private Dictionary<string, Material> _matCache =    // ID -> AUTO üretildi ise paylaşılan materyal
        new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    // SlotIndex → hedef kuyruğu (sadece row=0) (uid, col)
    private readonly Dictionary<int, Queue<(int uid, int col)>> _assigned =
        new Dictionary<int, Queue<(int uid, int col)>>();

    // Bir hedefe mermi çıktıysa rezerve (yeniden paylaştırma yok) Dİkkat edEilim
    private readonly HashSet<int> _reservedUids = new HashSet<int>();

    // --- GC azaltmak için geçici tamponlar ---(Performansı )
    private readonly List<(Block3D cube, int slot, string color, int ammo)> _tmpShooters =
        new List<(Block3D, int, string, int)>(32);
    private readonly Dictionary<string, List<(int uid, int col)>> _tmpFrontByColor =
        new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> _tmpShootersIndexByColor =
        new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

    static readonly char[] ExtraSpaces = new char[] {
        '\u00A0','\u1680','\u2000','\u2001','\u2002','\u2003','\u2004','\u2005','\u2006',
        '\u2007','\u2008','\u2009','\u200A','\u202F','\u205F','\u3000','\u200B'
    };

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void Start()
    {
        if (layout != null) BuildFromLayout();
    }

    // ---------------- UTIL ----------------
    void NotifyTargets() => OnTargetsUpdated?.Invoke(RemainingTargets, TotalTargets);

    [ContextMenu("Flip Front/Back Row Mapping")]
    void FlipFrontBack()
    {
        row0IsFront = !row0IsFront;
#if UNITY_EDITOR
        if (Application.isPlaying) BuildFromLayout();
        else UnityEditor.EditorApplication.delayCall += () => { if (this) BuildFromLayout(); };
        UnityEditor.EditorUtility.SetDirty(this);
#else
        BuildFromLayout();
#endif
    }

    [ContextMenu("Auto-Align Front Towards Slots")]
    void AutoAlignFrontTowardsSlots()
    {
        var sm = SlotManager.I;
        if (sm == null || sm.slots == null || sm.slots.Count == 0) return;

        Vector3 avg = Vector3.zero; int cnt = 0;
        for (int i = 0; i < sm.slots.Count; i++)
        {
            var s = sm.slots[i];
            if (s == null) continue;
            avg += s.position; cnt++;
        }
        if (cnt == 0) return; avg /= cnt;

        var o = origin ? origin.position : transform.position;
        var dir = (avg - o).normalized;
        if (dir.sqrMagnitude < 0.0001f) return;
        forward = dir;
#if UNITY_EDITOR
        if (Application.isPlaying) BuildFromLayout();
        else UnityEditor.EditorApplication.delayCall += () => { if (this) BuildFromLayout(); };
        UnityEditor.EditorUtility.SetDirty(this);
#else
        BuildFromLayout();
#endif
    }

    // ---------- ID NORMALİZASYONU ----------
    static string NormalizeTurkish(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            switch (ch)
            {
                case '\u0130': case '\u0131': sb.Append('I'); break;
                case '\u015E': case '\u015F': sb.Append('S'); break;
                case '\u011E': case '\u011F': sb.Append('G'); break;
                case '\u00DC': case '\u00FC': sb.Append('U'); break;
                case '\u00D6': case '\u00F6': sb.Append('O'); break;
                case '\u00C7': case '\u00E7': sb.Append('C'); break;
                default: sb.Append(char.ToUpperInvariant(ch)); break;
            }
        }
        return sb.ToString();
    }
    
    //Ekstra rnek  belki gelebilir
    public static string NormalizeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        string t = NormalizeTurkish(raw.Trim());

        if (t.Length == 1 && "RGBYOPC".IndexOf(t[0]) >= 0) return t;

        if (t.Contains("KIRMIZI") || t.Contains("RED")) return "R";
        if (t.Contains("SARI") || t.Contains("YELLOW")) return "Y";
        if (t.Contains("YESIL") || t.Contains("GREEN")) return "G";
        if (t.Contains("MAVI") || t.Contains("BLUE")) return "B";
        if (t.Contains("TURUNCU") || t.Contains("ORANGE")) return "O";
        if (t.Contains("MOR") || t.Contains("PURPLE") || t.Contains("MAGENTA")) return "P";
        if (t.Contains("CYAN") || t.Contains("CAMGOBEGI") || t.Contains("CAMGOBEĞI")) return "C";

        char c0 = t[0];
        if ("RGBYOPC".IndexOf(c0) >= 0) return c0.ToString();
        return t;
    }
    // --------------------------------------

    [ContextMenu("Infer IDs From Dragged Materials")]
    public void InferIdsFromDraggedMaterials()
    {
        if (!inferIdsFromMaterialName) return;
        var map = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < draggedMaterials.Count; i++)
        {
            var m = draggedMaterials[i];
            if (m == null) continue;
            var id = NormalizeId(m.name);
            if (string.IsNullOrEmpty(id)) continue;
            if (!map.ContainsKey(id)) map.Add(id, m);
        }
        idMaterials = new List<IdMaterial>();
        foreach (var kv in map) idMaterials.Add(new IdMaterial { id = kv.Key, material = kv.Value });
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    void BuildMaterialMaps()
    {
        _defMap = new Dictionary<string, BlockDef>(StringComparer.OrdinalIgnoreCase);

        if (layout != null && layout.blockDefs != null)
        {
            for (int i = 0; i < layout.blockDefs.Count; i++)
            {
                var def = layout.blockDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
                var key = NormalizeId(def.id);
                _defMap[key] = def;
            }
        }

        if (blockDefs != null)
        {
            for (int i = 0; i < blockDefs.Count; i++)
            {
                var def = blockDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
                var key = NormalizeId(def.id);
                if (_defMap.TryGetValue(key, out var baseDef))
                {
                    if ((baseDef.material == null && def.material != null) || def.material != null)
                        _defMap[key] = def;
                }
                else _defMap[key] = def;
            }
        }

        _matMap = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        if (inferIdsFromMaterialName && draggedMaterials.Count > 0 && (idMaterials == null || idMaterials.Count == 0))
            InferIdsFromDraggedMaterials();

        if (idMaterials != null)
        {
            for (int i = 0; i < idMaterials.Count; i++)
            {
                var im = idMaterials[i];
                if (im.material == null || string.IsNullOrWhiteSpace(im.id)) continue;
                var key = NormalizeId(im.id);
                _matMap[key] = im.material;
            }
        }

        _matCache.Clear();
    }

    Material ResolveMaterialForId(string token)
    {
        var key = NormalizeId(token);
        if (_matMap != null && _matMap.TryGetValue(key, out var m1) && m1 != null) return m1;
        if (_defMap != null && _defMap.TryGetValue(key, out var def) && def != null && def.material != null) return def.material;

        if (_matCache.TryGetValue(key, out var cached) && cached != null) return cached;

        var m = MakeAutoColorMaterial(key);
        _matCache[key] = m;
        return m;
    }

    Material MakeAutoColorMaterial(string token)
    {
        Color col = Color.gray;
        switch (token)
        {
            case "R": col = new Color(1f, 0.1f, 0.1f); break;
            case "G": col = new Color(0.1f, 0.85f, 0.15f); break;
            case "B": col = new Color(0.1f, 0.35f, 1f); break;
            case "Y": col = new Color(1f, 0.92f, 0.016f); break;
            case "C": col = new Color(0f, 1f, 1f); break;
            case "P": col = new Color(1f, 0f, 1f); break;
            case "O": col = new Color(1f, 0.5f, 0f); break;
            default: col = Color.gray; break;
        }

        Shader sh = Shader.Find("Unlit/Color")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard")
                  ?? Shader.Find("HDRP/Lit")
                  ?? Shader.Find("Unlit/Color");

        var m = new Material(sh);
        if (sh != null && sh.name.Contains("Unlit")) { m.color = col; return m; }
        if (m.HasProperty("_Color")) m.SetColor("_Color", col);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    [ContextMenu("Build From Layout")]
    public void BuildFromLayout()
    {
        if (!ValidateTargetLayout()) return;

#if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
#else
        var toDestroy = new List<GameObject>();
        foreach (Transform t in transform) toDestroy.Add(t.gameObject);
        for (int i = 0; i < toDestroy.Count; i++) Destroy(toDestroy[i]);
#endif

        Columns = Mathf.Max(1, layout.targetColumns);
        Rows    = Mathf.Max(1, layout.targetRows);
        grid    = new TargetBlock[Rows, Columns];

        _reservedUids.Clear();
        BuildMaterialMaps();

        for (int r = 0; r < Rows; r++)
        {
            var tokens = GetRowTokens(layout.targetIdRows[r], Columns);
            for (int c = 0; c < Columns; c++)
            {
                var token = NormalizeId(tokens[c]);
                if (string.IsNullOrWhiteSpace(token) || token == ".") continue;

                var pos = CellToWorld(r, c);
                var b   = Instantiate(targetPrefab, pos, Quaternion.identity, transform);
                b.row = r; b.col = c; b.id = token;

                var mat = ResolveMaterialForId(token);
                b.SetColorMaterial(mat);

                var testR = b.GetComponentInChildren<Renderer>(true);
                if (testR != null)
                {
                    var sms = testR.sharedMaterials;
                    if (sms != null && sms.Length > 0)
                    {
                        for (int i = 0; i < sms.Length; i++) sms[i] = mat;
                        testR.sharedMaterials = sms;
                    }
                }

                var t = b.transform;
                t.localScale = Vector3.one * 0.001f;
                t.DOScale(1f, spawnMoveDuration).SetEase(spawnEase);

                grid[r, c] = b;
            }
        }

        int total = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                if (grid[r, c] != null) total++;

        TotalTargets = total;
        RemainingTargets = total;
        NotifyTargets();

        if (SlotManager.I != null && SlotManager.I.CubeToSlot.Count > 0)
            SlotManager.I.BeginAutoWave();
    }

    bool ValidateTargetLayout()
    {
        if (layout == null) { Debug.LogError("TargetGrid: Layout yok."); return false; }
        if (layout.targetIdRows == null) { Debug.LogError("TargetGrid: targetIdRows null."); return false; }
        if (layout.targetIdRows.Count != layout.targetRows)
        {
            Debug.LogError($"TargetGrid: targetIdRows({layout.targetIdRows.Count}) rows({layout.targetRows}) ile uyuşmuyor.");
            return false;
        }
        if (targetPrefab == null)
        {
            Debug.LogError("TargetGrid: targetPrefab atanmalı.");
            return false;
        }
        return true;
    }

    string NormalizeSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        for (int i = 0; i < ExtraSpaces.Length; i++) s = s.Replace(ExtraSpaces[i], ' ');
        s = s.Replace(',', ' ').Replace('|', ' ').Replace(';', ' ');
        return s;
    }

    List<string> GetRowTokens(string raw, int expectedCols)
    {
        var list = new List<string>(expectedCols);
        if (string.IsNullOrWhiteSpace(raw))
        {
            for (int i = 0; i < expectedCols; i++) list.Add(".");
            return list;
        }

        raw = NormalizeSpaces(raw).Trim();

        var rough = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < rough.Length; i++)
        {
            var t = rough[i].Trim().ToUpperInvariant();
            if (t.Length > 0) list.Add(t);
        }

        if (list.Count > expectedCols) list = list.GetRange(0, expectedCols);
        else if (list.Count < expectedCols)
        {
            int need = expectedCols - list.Count;
            for (int i = 0; i < need; i++) list.Add(".");
        }

        return list;
    }

    int RowDepthIndex(int row) => row0IsFront ? row : (Rows - 1 - row);

    Vector3 CellToWorld(int row, int col)
    {
        var o = origin ? origin.position : Vector3.zero;
        int depth = RowDepthIndex(row);
        return o + right.normalized * (col * cellSizeX)
                 + forward.normalized * (depth * cellSizeZ);
    }

    public void BeginWave(Dictionary<Block3D, int> cubeToSlot)
    {
        _assigned.Clear();
        _tmpShooters.Clear();
        _tmpFrontByColor.Clear();
        _tmpShootersIndexByColor.Clear();

        if (cubeToSlot == null || cubeToSlot.Count == 0) return;

        for (int c = 0; c < Columns; c++)
        {
            var b = grid[0, c];
            if (b == null) continue;
            if (_reservedUids.Contains(b.uid)) continue; 

            var list = GetFrontListForColor(b.id);
            list.Add((b.uid, c));
        }

        foreach (var kv in cubeToSlot)
        {
            var cube = kv.Key;
            if (cube == null) continue;

            int ammo = (cube.ammo > 0) ? cube.ammo : 0;
            if (ammo <= 0) continue;

            string color = NormalizeId(cube.blockId);
            if (string.IsNullOrWhiteSpace(color)) continue;

            _tmpShooters.Add((cube, kv.Value, color, ammo));
        }

        _tmpShooters.Sort((a, b) => a.slot.CompareTo(b.slot));

        for (int i = 0; i < _tmpShooters.Count; i++)
        {
            var s = _tmpShooters[i];
            if (!_tmpShootersIndexByColor.TryGetValue(s.color, out var list))
            {
                list = new List<int>(8);
                _tmpShootersIndexByColor[s.color] = list;
            }
            list.Add(i);
        }

        foreach (var kv in _tmpShootersIndexByColor)
        {
            string color = kv.Key;
            var targets = GetFrontListForColor(color);
            if (targets == null || targets.Count == 0) continue;

            int ptr = 0;
            var shooterIdxs = kv.Value;

            for (int si = 0; si < shooterIdxs.Count && ptr < targets.Count; si++)
            {
                var s = _tmpShooters[shooterIdxs[si]];
                int take = Mathf.Min(s.ammo, targets.Count - ptr);
                if (take <= 0) continue;

                if (!_assigned.TryGetValue(s.slot, out var q))
                {
                    q = new Queue<(int uid, int col)>();
                    _assigned[s.slot] = q;
                }

                for (int t = 0; t < take; t++) q.Enqueue(targets[ptr + t]);
                ptr += take;
            }
        }
    }

    List<(int uid, int col)> GetFrontListForColor(string colorId)
    {
        var key = NormalizeId(colorId);
        if (!_tmpFrontByColor.TryGetValue(key, out var list))
        {
            list = new List<(int, int)>(16);
            _tmpFrontByColor[key] = list;
        }
        return list;
    }

    void ReserveUid(int uid) { if (uid != 0) _reservedUids.Add(uid); }

    public void ReleaseUid(int uid) { if (uid != 0) _reservedUids.Remove(uid); }

    public bool TryGetNextAssignedTarget(int slotIndex, string colorId,
                                         out int row, out int col,
                                         out Vector3 worldPos, out int targetUid)
    {
        row = col = -1; worldPos = default; targetUid = 0;
        if (!_assigned.TryGetValue(slotIndex, out var q) || q.Count == 0) return false;

        string key = NormalizeId(colorId);
        while (q.Count > 0)
        {
            var nxt = q.Peek();             
            var b   = grid[0, nxt.col];    
            if (b != null && b.uid == nxt.uid &&
                string.Equals(b.id, key, StringComparison.OrdinalIgnoreCase) &&
                !_reservedUids.Contains(b.uid))
            {
                row = 0; col = nxt.col; worldPos = CellToWorld(0, col); targetUid = nxt.uid;
                q.Dequeue();
                ReserveUid(targetUid); // artık paylaştırma dışı
                return true;
            }
            q.Dequeue();
        }
        return false;
    }

    public bool HasAnyFrontForColors(IEnumerable<string> colors)
    {
        if (colors == null) return false;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in colors) { var k = NormalizeId(c); if (!string.IsNullOrEmpty(k)) set.Add(k); }

        for (int c = 0; c < Columns; c++)
        {
            var b = grid[0, c];
            if (b != null && set.Contains(b.id)) return true;
        }
        return false;
    }

    public bool HasAnyReservedFrontForColors(IEnumerable<string> colors)
    {
        if (colors == null) return false;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in colors) { var k = NormalizeId(c); if (!string.IsNullOrEmpty(k)) set.Add(k); }

        for (int c = 0; c < Columns; c++)
        {
            var b = grid[0, c];
            if (b != null && set.Contains(b.id) && _reservedUids.Contains(b.uid))
                return true;
        }
        return false;
    }

    public bool IsFrontUid(int col, int uid, string colorId)
    {
        if (col < 0 || col >= Columns) return false;
        var b = grid[0, col];
        if (b == null) return false;
        if (b.uid != uid) return false;
        return string.Equals(b.id, NormalizeId(colorId), StringComparison.OrdinalIgnoreCase);
    }

    public bool ResolveHit(int row, int col, string colorId)
    {
        if (row != 0) return false;
        var b = grid[0, col];
        if (b == null) return false;
        if (!string.Equals(b.id, NormalizeId(colorId), StringComparison.OrdinalIgnoreCase)) return false;

        ReleaseUid(b.uid);

        grid[0, col] = null;
        KillWithJelly(b);
        CollapseColumn(col);

        RemainingTargets = Mathf.Max(0, RemainingTargets - 1);
        NotifyTargets();

        if (SlotManager.I != null)
        {
            var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in SlotManager.I.CubeToSlot)
            {
                var k = kv.Key;
                if (k != null && k.ammo > 0)
                {
                    var id = NormalizeId(k.blockId);
                    if (!string.IsNullOrEmpty(id)) colors.Add(id);
                }
            }
            if (HasAnyFrontForColors(colors))
                SlotManager.I.BeginAutoWave();
        }

        if (IsAllCleared())
        {
            Debug.Log("WIN: tüm hedefler vuruldu ✅");
            OnWin?.Invoke();
        }

        return true;
    }

    public bool IsAllCleared()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                if (grid[r, c] != null) return false;
        return true;
    }

    public void TouchDifferentColor(TargetBlock other)
    {
        if (other == null) return;
    }

    void KillWithJelly(TargetBlock b)
    {
        var t = b.transform;
        t.DOScale(0.01f, destroyTime)
         .SetEase(Ease.InBack, 0.3f)
         .OnComplete(() => Destroy(b.gameObject));
    }

    void CollapseColumn(int col)
    {
        for (int r = 0; r < Rows; r++)
        {
            if (grid[r, col] != null) continue;

            int src = -1;
            for (int rr = r + 1; rr < Rows; rr++)
            {
                if (grid[rr, col] != null) { src = rr; break; }
            }
            if (src == -1) break;

            var b = grid[src, col];
            grid[src, col] = null;
            grid[r, col] = b;

            b.row = r;
            var t = b.transform;
            var targetPos = CellToWorld(r, col);

            int cells = src - r; 
            float moveDur = fallMoveDuration;
            if (fallDurationScalesWithCells)
                moveDur = Mathf.Max(0.02f, fallDurationPerCell * Mathf.Max(1, cells));

            float startDelay = 0f;
            if (dominoStagger)
                startDelay = dominoStartDelayPerCell * r;

            float punchLag = dominoPunchExtraLag;

            t.DOMove(targetPos, moveDur)
             .SetEase(fallEase)
             .SetDelay(startDelay)
             .OnComplete(() =>
             {
                 if (punchLag > 0f)
                     DOVirtual.DelayedCall(punchLag, () => b.SafePunchScale(fallPunchScale, 0.18f));
                 else
                     b.SafePunchScale(fallPunchScale, 0.18f);
             });
        }
    }
}
