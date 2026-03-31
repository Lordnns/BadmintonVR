using UnityEngine;

public class ShuttlecockLauncher : MonoBehaviour
{
    public GameObject shuttlecockPrefab;
    public Transform spawnPoint;

    public float launchSpeed = 15f;
    public float launchInterval = 2f;
    public float despawnTime = 10f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InvokeRepeating("Launch", 1f, launchInterval);
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
