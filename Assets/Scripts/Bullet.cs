using UnityEngine;
using System;

public class Bullet : MonoBehaviour
{
    public float lifeTime = 8f;
    private float _spawnTime;

    // runtime atanır
    public string colorId;
    public int targetRow;
    public int targetCol;
    public Vector3 targetPos;
    public float speed = 12f;

    public int targetUid;

    private Rigidbody _rb;
    private Collider _col;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate; 
        }

        _col = GetComponent<Collider>();
        if (_col != null)
            _col.isTrigger = true;
    }

    void OnEnable()
    {
        _spawnTime = Time.time;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    void FixedUpdate()
    {
        if (Time.time - _spawnTime > lifeTime)
        {
            SafeReleaseAndDespawn();
            return;
        }

        var t = transform;
        Vector3 toTarget = (targetPos - t.position);
        float dist = toTarget.magnitude;

        if (dist < 0.05f)
        {
            if (TargetGridManager.I != null
                && TargetGridManager.I.IsFrontUid(targetCol, targetUid, colorId))
            {
                TargetGridManager.I.ResolveHit(0, targetCol, colorId); // ReleaseUid içeride
            }
            else
            {
                if (TargetGridManager.I != null) TargetGridManager.I.ReleaseUid(targetUid);
            }
            DespawnOnly();
            return;
        }

        float stepLen = speed * Time.fixedDeltaTime;
        if (stepLen > dist) stepLen = dist - 0.0005f; // hafif tampon
        Vector3 step = toTarget.normalized * stepLen;

        if (_rb != null)
        {
            _rb.MovePosition(t.position + step);
        }
        else
        {
            t.position += step;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<TargetBlock>(out var tb)) return;

        bool isSameColor = string.Equals(tb.id, colorId, StringComparison.OrdinalIgnoreCase);

        // --- YANLIŞ RENKTE DENT TETİKLE ---
        if (!isSameColor)
        {
            var dent = other.GetComponent<LocalDentOnImpact>();
            if (dent != null)
            {
                Vector3 cp = other.ClosestPoint(transform.position);
                Vector3 approxNormal = (cp - other.bounds.center).normalized;
                float depth = dent.maxDepth * 0.8f;
                dent.DentAtWorld(cp, approxNormal, depth, dent.radiusWorld);
            }

            TargetGridManager.I?.TouchDifferentColor(tb);
            return;
        }

        // --- AYNı RENKTEKİ HEDEF (normal vurma akışı) ---
        if (tb.row != 0 || tb.col != targetCol || tb.uid != targetUid)
        {
            TargetGridManager.I?.TouchDifferentColor(tb);
            return;
        }

        if (TargetGridManager.I != null)
        {
            TargetGridManager.I.ResolveHit(targetRow, targetCol, colorId);
            if (tb.effect != null) tb.effect.SetActive(true);
        }

        DespawnOnly();
    }

    void SafeReleaseAndDespawn()
    {
        if (TargetGridManager.I != null) TargetGridManager.I.ReleaseUid(targetUid);
        DespawnOnly();
    }

    void DespawnOnly()
    {
        if (BulletPool.I != null) BulletPool.I.ReturnBullet(gameObject);
        else Destroy(gameObject);
    }
}
