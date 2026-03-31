using UnityEngine;

public class ShuttlecockLauncher : MonoBehaviour
{
    [Header("Objects")]
    public GameObject shuttlecockPrefab;
    public Transform rotatingPart;
    public Transform spawnPoint;

    [Header("Actions")]
    public float launch;

    [Header("Parameters")]
    public float launchSpeed = 15f;
    public float launchInterval = 2f;
    public float despawnTime = 10f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InvokeRepeating("Launch", 1f, launchInterval);
    }

    void Update()
    {
        // rotatingPart.Rotate(0, 20 * Time.deltaTime, 0);
    }

    void Launch()
    {
        GameObject sc = Instantiate(
            shuttlecockPrefab,
            spawnPoint.position,
            spawnPoint.rotation
            );

        sc.transform.SetParent(null);

        Rigidbody rb = sc.GetComponent<Rigidbody>();
        rb.linearVelocity = spawnPoint.forward * launchSpeed;

        Destroy(sc, despawnTime);
    }
}
