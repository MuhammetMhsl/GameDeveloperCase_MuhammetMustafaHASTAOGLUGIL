using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelLayout", menuName = "Levels/Level Layout (String-Based)")]
public class LevelLayout : ScriptableObject
{
    // ================= PLAYER (GRID) =================
    [Header("Grid Boyutu (PLAYER)")]
    [Min(1)] public int columns = 4;
    [Min(1)] public int rows = 1;

    [Header("ID Satırları (1. satır = ön sıra, PLAYER)")]
    [Tooltip("Ör: \"Y G R B\"  | Auto kapalıyken boş hücre için '.' kullanabilirsin")]
    public List<string> idRows = new List<string>();

    [Header("Ammo Satırları (ID sayısıyla birebir, PLAYER)")]
    [Tooltip("Ör: \"10-20-30-40\"  | ID sayısıyla aynı adette olmalı")]
    public List<string> ammoRows = new List<string>();

    [Header("ID → Prefab Eşleşmesi")]
    [Tooltip("Kullanılacak tüm BlockDef'leri buraya ekle (id: G,Y,R,B...)")]
    public List<BlockDef> blockDefs = new List<BlockDef>();

    [Header("Ayırıştırma Ayarları (PLAYER)")]
    public char idSeparator = ' ';
    public char ammoSeparator = '-';

    // ================= TARGET =================
    [Header("TARGET Grid Boyutu")]
    [Min(1)] public int targetColumns = 10;  // SABİT 10
    [Min(1)] public int targetRows = 12;

    [Header("TARGET ID Satırları (1. satır = ön sıra)")]
    [Tooltip("Ör: \"R R R R ...\"  | Auto açıkken otomatik üretilir")]
    public List<string> targetIdRows = new List<string>();

    // ================= AUTO GENERATE =================
    public enum Difficulty { Easy, Medium, Hard, Extreme }

    [Header("AUTO GENERATE")]
    [Tooltip("Açıksa, manuel girdileri yok sayar ve level otomatik üretilir.Bir kez bas üret geri kapat.")]
    public bool autoGenerate = false;

    [Tooltip("Zorluk seçimi – Target satır sayısı, Player sütun/ammo limitleri etkiler")]
    public Difficulty difficulty = Difficulty.Easy;

    [Tooltip("Aynı seed ile aynı level. Kapalıysa her seferinde farklı üretir.")]
    public bool useSeed = true;

    [Tooltip("Rastgele üretim için seed")]
    public int seed = 12345;

    [Header("Yeni Kurallar (10'un Katları)")]
    [Tooltip("Player grid'de her ammo 10'un katı olmalı (10,20,30...)")]
    public bool useMultiplesOfTen = true;

    [Tooltip("Target grid genişliği her zaman 10 (sabit)")]
    public bool fixedTargetWidth = true;

    // --------- RUNTIME / EDITOR ---------
    private System.Random _rng;

    void OnValidate()
    {
        if (autoGenerate)
        {
            GenerateNow();
        }
    }

    [ContextMenu("Regenerate Now")]
    public void GenerateNow()
    {
        _rng = useSeed ? new System.Random(seed) : new System.Random(Environment.TickCount);

        // TARGET: Sabit genişlik 10
        if (fixedTargetWidth) targetColumns = 10;

        // TARGET: Zorluk bazlı satır sayısı (alt sınır)
        switch (difficulty)
        {
            case Difficulty.Easy:
                targetRows = Mathf.Max(12, targetRows);
                break;
            case Difficulty.Medium:
                targetRows = Mathf.Max(16, targetRows);
                break;
            case Difficulty.Hard:
                targetRows = Mathf.Max(18, targetRows);
                break;
            case Difficulty.Extreme:
                targetRows = Mathf.Max(20, targetRows);
                break;
        }

        EnsureListSize(targetIdRows, targetRows);

        // Palet (BlockDef'lerden) – boşsa RGBY
        var palette = GetPalette();
        if (palette.Count == 0)
            palette = new List<string> { "R", "G", "B", "Y" };

        var targetGrid = GenerateBalancedTargetGrid(palette);

        var colorCounts = CountColors(targetGrid);

        if (useMultiplesOfTen)
        {
            // 10'un katı hedeflerine ayarla (çok az hücre rengi değişebilir)
            AdjustTargetCountsToMultiplesOfTen(targetGrid, palette);
            colorCounts = CountColors(targetGrid); // güncelle
        }

        for (int r = 0; r < targetRows; r++)
            targetIdRows[r] = string.Join(" ", targetGrid[r]);

        int totalTargets = colorCounts.Values.Sum(); 
        GeneratePlayerGridExactMultiples(colorCounts, totalTargets);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ================= TARGET GRID ÜRETİMİ =================
    List<string[]> GenerateBalancedTargetGrid(List<string> palette)
    {
        var grid = new List<string[]>();

        // Renk sayısı (Easy: 2-3, Medium: 3-4, Hard/Extreme: 3-5)
        int colorCount;
        switch (difficulty)
        {
            case Difficulty.Easy:
                colorCount = _rng.Next(2, Mathf.Min(4, palette.Count + 1));
                break;
            case Difficulty.Medium:
                colorCount = _rng.Next(3, Mathf.Min(5, palette.Count + 1));
                break;
            default:
                colorCount = _rng.Next(3, Mathf.Min(6, palette.Count + 1));
                break;
        }

        var chosen = PickColors(palette, colorCount);

        int minStreak = (difficulty == Difficulty.Easy) ? 3 : 2;
        int maxStreak = (difficulty == Difficulty.Extreme) ? 4 : 5;

        for (int r = 0; r < targetRows; r++)
        {
            var row = MakeStreakRow(targetColumns, chosen, minStreak, maxStreak);
            grid.Add(row);
        }

        return grid;
    }

    string[] MakeStreakRow(int cols, List<string> colors, int minStreak, int maxStreak)
    {
        var row = new List<string>(cols);
        string last = null;

        while (row.Count < cols)
        {
            var pool = (last == null) ? colors : colors.Where(c => c != last).ToList();
            if (pool.Count == 0) pool = colors;

            string pick = pool[_rng.Next(pool.Count)];
            int len = _rng.Next(minStreak, maxStreak + 1);

            for (int i = 0; i < len && row.Count < cols; i++)
                row.Add(pick);

            last = pick;
        }

        return row.ToArray();
    }

    void AdjustTargetCountsToMultiplesOfTen(List<string[]> grid, List<string> palette)
    {
        int totalCells = targetColumns * targetRows; // 10 * rows → zaten 10'un katı

        var counts = CountColors(grid);

        var desired = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int sumFloors = 0;

        var allColors = new HashSet<string>(counts.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var p in palette) allColors.Add(p);

        foreach (var col in allColors)
        {
            int cur = counts.ContainsKey(col) ? counts[col] : 0;
            int floor10 = (cur / 10) * 10;
            desired[col] = floor10;
            sumFloors += floor10;
        }

        int remaining = totalCells - sumFloors; // her zaman 10'un katı
        if (remaining < 0) remaining = 0;

        if (remaining > 0)
        {
            var byRema = allColors
                .Select(c => new { color = c, rem = counts.ContainsKey(c) ? (counts[c] % 10) : 0 })
                .OrderByDescending(x => x.rem)
                .ToList();

            int toGivePacks = remaining / 10;
            int idx = 0;
            while (toGivePacks > 0 && byRema.Count > 0)
            {
                var pick = byRema[idx % byRema.Count];
                desired[pick.color] += 10;
                idx++;
                toGivePacks--;
            }
        }

        var surplus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var deficit = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in desired.Keys)
        {
            int cur = counts.ContainsKey(c) ? counts[c] : 0;
            int tgt = desired[c];

            if (cur > tgt) surplus[c] = cur - tgt;
            else if (cur < tgt) deficit[c] = tgt - cur;
        }

        if (surplus.Count == 0 && deficit.Count == 0)
            return; 

       
        var deficitList = deficit.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        Func<string> nextDeficitColor = () =>
        {
            var kv = deficitList.FirstOrDefault(x => x.Value > 0);
            return kv.Key; 
        };

        for (int r = 0; r < grid.Count; r++)
        {
            var row = grid[r];
            for (int c = 0; c < row.Length; c++)
            {
                string id = NormalizeId(row[c]);
                if (string.IsNullOrWhiteSpace(id) || id == ".") continue;

                if (surplus.ContainsKey(id) && surplus[id] > 0)
                {
                    string needColor = nextDeficitColor();
                    if (!string.IsNullOrEmpty(needColor))
                    {
                        row[c] = needColor;
                        surplus[id]--;
                        deficitList[needColor]--;
                        if (deficitList[needColor] <= 0) deficitList.Remove(needColor);
                    }
                }
            }
        }
    }

    // ================= PLAYER GRID ÜRETİMİ (RENK BAZINDA TAM EŞLEŞME) =================
    void GeneratePlayerGridExactMultiples(Dictionary<string, int> colorCounts, int totalTargets)
    {
        // Sütun aralığı (zorluğa göre)
        int minCols, maxCols;
        switch (difficulty)
        {
            case Difficulty.Easy:     minCols = 3; maxCols = 5; break;
            case Difficulty.Medium:   minCols = 3; maxCols = 4; break;
            case Difficulty.Hard:     minCols = 3; maxCols = 4; break;
            case Difficulty.Extreme:  minCols = 2; maxCols = 4; break;
            default:                  minCols = 3; maxCols = 4; break;
        }

        int minAmmo = 10;
        int maxAmmo;
        switch (difficulty)
        {
            case Difficulty.Easy:     maxAmmo = 40; break;
            case Difficulty.Medium:   maxAmmo = 30; break;
            case Difficulty.Hard:     maxAmmo = 30; break;
            case Difficulty.Extreme:  maxAmmo = 20; break;
            default:                  maxAmmo = 30; break;
        }

        if (useMultiplesOfTen)
        {
            foreach (var kv in colorCounts)
            {
                if (kv.Value % 10 != 0)
                {
                    Debug.LogError($"TARGET {kv.Key} = {kv.Value}. 10'un katı değil; player eşitleme iptal.");
                    return;
                }
            }
        }

        var finalChunks = new List<(string color, int ammo)>();
        foreach (var kv in colorCounts)
        {
            string color = kv.Key;
            int remaining = kv.Value;

            while (remaining > 0)
            {
                int take = Mathf.Min(maxAmmo, remaining);
                take = Mathf.Clamp((take / 10) * 10, minAmmo, maxAmmo);

                int after = remaining - take;
                if (after > 0 && after < 10)
                {
                    int tryTake = Mathf.Clamp(take - 10, minAmmo, maxAmmo);
                    if (tryTake >= minAmmo && (remaining - tryTake) >= 10)
                        take = tryTake;
                }

                if (take > remaining) take = remaining;
                take = Mathf.Max(minAmmo, (take / 10) * 10);

                finalChunks.Add((color, take));
                remaining -= take;
            }
        }

        int chunkCount = finalChunks.Count;
        if (columns < minCols || columns > maxCols)
            columns = _rng.Next(minCols, maxCols + 1);

        rows = Mathf.CeilToInt(chunkCount / (float)columns);

        Shuffle(finalChunks);

        EnsureListSize(idRows, rows);
        EnsureListSize(ammoRows, rows);

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            var idsRow = new List<string>();
            var ammRow = new List<string>();

            for (int c = 0; c < columns; c++)
            {
                if (idx < finalChunks.Count)
                {
                    idsRow.Add(finalChunks[idx].color);
                    ammRow.Add(finalChunks[idx].ammo.ToString());
                    idx++;
                }
                else
                {
                    idsRow.Add(".");
                    ammRow.Add("0");
                }
            }

            idRows[r] = string.Join(" ", idsRow);
            ammoRows[r] = string.Join(ammoSeparator.ToString(), ammRow);
        }

        var playerColorTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in finalChunks)
        {
            if (!playerColorTotals.ContainsKey(ch.color)) playerColorTotals[ch.color] = 0;
            playerColorTotals[ch.color] += ch.ammo;
        }
        foreach (var kv in colorCounts)
        {
            int playerSum = playerColorTotals.TryGetValue(kv.Key, out var v) ? v : 0;
            if (playerSum != kv.Value)
            {
                Debug.LogError($"EŞLEŞME HATASI: {kv.Key} Target={kv.Value}, Player={playerSum}");
            }
        }
    }

    // ================= HELPER'LAR =================
    void EnsureListSize(List<string> list, int count)
    {
        if (list == null) return;
        while (list.Count < count) list.Add("");
        while (list.Count > count) list.RemoveAt(list.Count - 1);
    }

    List<string> GetPalette()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in blockDefs)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.id)) continue;
            string id = NormalizeId(def.id);
            if (!string.IsNullOrWhiteSpace(id) && id.Length == 1 && "RGBYOPC".IndexOf(id[0]) >= 0)
                set.Add(id);
        }
        return set.ToList();
    }

    static string NormalizeTurkish(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var map = new Dictionary<char, char>
        {
            ['\u0130']='I', ['\u0131']='I', ['\u015E']='S', ['\u015F']='S',
            ['\u011E']='G', ['\u011F']='G', ['\u00DC']='U', ['\u00FC']='U',
            ['\u00D6']='O', ['\u00F6']='O', ['\u00C7']='C', ['\u00E7']='C'
        };
        var arr = s.Trim().ToUpperInvariant().ToCharArray();
        for (int i = 0; i < arr.Length; i++)
            if (map.TryGetValue(arr[i], out var repl)) arr[i] = repl;
        return new string(arr);
    }

    public static string NormalizeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        string t = NormalizeTurkish(raw.Trim());
        if (t.Length == 1 && "RGBYOPC".IndexOf(t[0]) >= 0) return t;

        if (t.Contains("KIRMIZI") || t.Contains("RED"))    return "R";
        if (t.Contains("SARI")    || t.Contains("YELLOW")) return "Y";
        if (t.Contains("YESIL")   || t.Contains("GREEN"))  return "G";
        if (t.Contains("MAVI")    || t.Contains("BLUE"))   return "B";
        if (t.Contains("TURUNCU") || t.Contains("ORANGE")) return "O";
        if (t.Contains("MOR")     || t.Contains("PURPLE") || t.Contains("MAGENTA")) return "P";
        if (t.Contains("CYAN")    || t.Contains("CAMGOBEGI") || t.Contains("CAMGOBEĞİ")) return "C";

        char c0 = t[0];
        if ("RGBYOPC".IndexOf(c0) >= 0) return c0.ToString();
        return t;
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Dictionary<string, int> CountColors(List<string[]> grid)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < grid.Count; r++)
        {
            var row = grid[r];
            for (int c = 0; c < row.Length; c++)
            {
                var id = NormalizeId(row[c]);
                if (string.IsNullOrWhiteSpace(id) || id == ".") continue;
                if (!map.ContainsKey(id)) map[id] = 0;
                map[id]++;
            }
        }
        return map;
    }

    List<string> PickColors(List<string> palette, int count)
    {
        var p = new List<string>(palette);
        Shuffle(p);
        return p.Take(Mathf.Clamp(count, 1, p.Count)).ToList();
    }
}
