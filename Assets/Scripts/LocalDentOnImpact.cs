using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(Collider))]
public class LocalDentOnImpact : MonoBehaviour
{
    [Header("Dent (lokal ezilme)")]
    public float radiusWorld = 0.35f;
    public float maxDepth = 0.12f;
    [Range(0.1f, 4f)] public float hardness = 1.2f;
    [Tooltip("Vertex atlama: 1=hepsi, 2=yarısı, 3=üçte biri...")]
    public int vertexStep = 1;

    [Header("Toparlanma")]
    public float recoverDelay = 0.05f;
    public float recoverDuration = 0.35f;
    public AnimationCurve recoverCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Şiddet eşiği")]
    public float minImpulse = 0.3f;
    public float maxImpulse = 8.0f;
    public float depthByImpulse = 1.0f;

    [Header("Yön")]
    public bool invertDentDirection = false;

    MeshFilter mf;
    Mesh meshInstance;
    Vector3[] baseVerts;
    Vector3[] workVerts;
    bool recovering;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        meshInstance = Instantiate(mf.sharedMesh);
        meshInstance.name = mf.sharedMesh.name + " (Instance)";
        meshInstance.MarkDynamic();
        mf.mesh = meshInstance;

        baseVerts = (Vector3[])meshInstance.vertices.Clone();
        workVerts = (Vector3[])meshInstance.vertices.Clone();
        if (vertexStep < 1) vertexStep = 1;
    }

    void OnCollisionEnter(Collision col)
    {
        float impulseMag = col.impulse.magnitude;
        float kImp = Mathf.InverseLerp(minImpulse, maxImpulse, impulseMag);
        if (kImp <= 0f) return;

        Vector3 p = Vector3.zero, n = Vector3.zero;
        int c = Mathf.Max(1, col.contactCount);
        for (int i = 0; i < c; i++)
        {
            var cp = col.GetContact(i);
            p += cp.point; n += cp.normal;
        }
        p /= c; n = (n / c).normalized;

        float depth = maxDepth * Mathf.Clamp01(kImp * depthByImpulse);
        DentAtWorld(p, n, depth, radiusWorld);
    }

    public void DentAtWorld(Vector3 worldPoint, Vector3 worldNormal, float depth, float worldRadius)
    {
        Vector3 lp = transform.InverseTransformPoint(worldPoint);
        Vector3 ln = transform.InverseTransformDirection(worldNormal).normalized;

        // 🔹 otomatik yön düzeltme
        Vector3 toCenter = (transform.position - worldPoint).normalized;
        if (Vector3.Dot(worldNormal, toCenter) < 0f)
            ln = -ln;

        ApplyDent(lp, ln, depth, worldRadius);

        meshInstance.vertices = workVerts;
        meshInstance.RecalculateNormals();
        meshInstance.RecalculateBounds();

        if (gameObject.activeInHierarchy)
        {
            if (recovering) StopAllCoroutines();
            StartCoroutine(RecoverRoutine());
        }
    }

    void ApplyDent(Vector3 localPoint, Vector3 localNormal, float depth, float worldRadius)
    {
        float scaleApprox = (transform.lossyScale.x + transform.lossyScale.y + transform.lossyScale.z) / 3f;
        float r = worldRadius / Mathf.Max(0.0001f, scaleApprox);
        float r2 = r * r;
        Vector3 dir = invertDentDirection ? localNormal : -localNormal;

        for (int i = 0; i < workVerts.Length; i += vertexStep)
        {
            Vector3 v = workVerts[i];
            float dist2 = (v - localPoint).sqrMagnitude;
            if (dist2 > r2) continue;

            float dist = Mathf.Sqrt(dist2);
            float t = 1f - Mathf.Pow(dist / r, hardness);
            float push = depth * t;
            v += dir * push;
            workVerts[i] = v;
        }
    }

    IEnumerator RecoverRoutine()
    {
        recovering = true;
        if (recoverDelay > 0f) yield return new WaitForSeconds(recoverDelay);
        Vector3[] startVerts = (Vector3[])workVerts.Clone();
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, recoverDuration);
            float k = recoverCurve.Evaluate(Mathf.Clamp01(t));
            for (int i = 0; i < workVerts.Length; i += vertexStep)
                workVerts[i] = Vector3.LerpUnclamped(startVerts[i], baseVerts[i], k);
            meshInstance.vertices = workVerts;
            meshInstance.RecalculateNormals();
            meshInstance.RecalculateBounds();
            yield return null;
        }
        System.Array.Copy(baseVerts, workVerts, baseVerts.Length);
        meshInstance.vertices = workVerts;
        meshInstance.RecalculateNormals();
        meshInstance.RecalculateBounds();
        recovering = false;
    }
}
