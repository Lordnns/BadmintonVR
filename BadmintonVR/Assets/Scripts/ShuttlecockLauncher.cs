using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class ShuttlecockLauncher : MonoBehaviour
{
    [SerializeField] ShuttlecockData data;

    [Header("Objects")]
    public GameObject shuttlecockPrefab;
    public Transform rotatingPart;
    public Transform spawnPoint;
    public Transform target;

    [Header("Actions")]
    public float launch;

    [Header("Parameters")]
    public float despawnTime = 10f;

    void Start()
    {
        // Time-based auto-launch removed.
        // Call LaunchShuttlecock() from your game-mode script instead.
    }

    void Update()
    {
        // Aim for target
        Vector3 cannonPos = spawnPoint.position;
        Vector3 delta = target.position - cannonPos;

        float X = new Vector2(delta.x, delta.z).magnitude;
        float Y = delta.y;

        float pitch = SolvePitch(data.initialSpeed, X, Y, data.dragCoefficient, -Physics.gravity.y);
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;

        rotatingPart.localRotation = Quaternion.Euler(0f, yaw - 90, -pitch);
    }

    /// <summary>
    /// Fire one shuttlecock toward the current target position.
    /// Call this from your game-mode script after positioning target.
    /// </summary>
    public void LaunchShuttlecock()
    {
        GameObject sc = Instantiate(
            shuttlecockPrefab,
            spawnPoint.position,
            spawnPoint.rotation
            );

        sc.transform.SetParent(null);

        Rigidbody rb = sc.GetComponent<Rigidbody>();
        rb.linearVelocity = spawnPoint.forward * data.initialSpeed;

        Destroy(sc, despawnTime);
    }

    // Calculation methods
    float SolvePitch(float speed, float X, float Y, float K, float g)
    {
        float lo = 0f, hi = FindMaxRangeAngle(speed, X, K, g);
        for (int i = 0; i < 30; i++)
        {
            float mid = (lo + hi) / 2f;
            float y = EvalY(speed, mid, X, Y, K, g);
            if (y < 0) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2f;
    }

    float FindMaxRangeAngle(float speed, float X, float K, float g)
    {
        float lo = 0f, hi = 180f;
        for (int i = 0; i < 50; i++)
        {
            float m1 = lo + (hi - lo) / 3f;
            float m2 = hi - (hi - lo) / 3f;
            if (EvalY(speed, m1, X, 0f, K, g) < EvalY(speed, m2, X, 0f, K, g))
                lo = m1;
            else
                hi = m2;
        }
        return (lo + hi) / 2f;
    }

    float EvalY(float speed, float pitchDeg, float X, float Y, float K, float g)
    {
        float rad = pitchDeg * Mathf.Deg2Rad;
        float vx0 = speed * Mathf.Sin(rad);
        float vy0 = speed * Mathf.Cos(rad);
        if (vx0 < 0.0001f) return float.NegativeInfinity;
        float u = K * X / vx0;
        if (u >= 1f) return float.NegativeInfinity;
        return (g / (K * K)) * Mathf.Log(1f - u)
             + (vy0 / K + g / (K * K)) * u
             - Y;
    }
}