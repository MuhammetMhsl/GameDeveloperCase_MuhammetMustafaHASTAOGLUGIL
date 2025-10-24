using System.Collections.Generic;
using UnityEngine;

public class BulletPool : MonoBehaviour
{
    public static BulletPool I;

    [Header("Havuz Ayarları")]
    public GameObject bulletPrefab;
    public int poolSize = 50;

    private Queue<GameObject> pool = new Queue<GameObject>();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject b = Instantiate(bulletPrefab);
            b.SetActive(false);
            pool.Enqueue(b);
        }
    }

    public GameObject GetBullet(Vector3 pos, Quaternion rot)
    {
        if (pool.Count == 0)
        {
            GameObject extra = Instantiate(bulletPrefab);
            extra.SetActive(false);
            pool.Enqueue(extra);
        }

        GameObject go = pool.Dequeue();
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.SetActive(true);
        return go;
    }

    public void ReturnBullet(GameObject bullet)
    {
        bullet.SetActive(false);
        pool.Enqueue(bullet);
    }
}