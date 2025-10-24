using UnityEngine;

[CreateAssetMenu(fileName = "BlockDef", menuName = "Blocks/Block Definition")]
public class BlockDef : ScriptableObject
{
    [Header("Kimlik")]
    public string id = "G";          // G,Y,R,B gibi

    [Header("Prefab")]
    public Block3D prefab;           // Bu ID'nin Block3D prefab'ı

    [Header("Opsiyonel Varsayılanlar")]
    public int defaultAmmo = 10;   
    [Header("Materyal ataması")]
    public Material material;
}