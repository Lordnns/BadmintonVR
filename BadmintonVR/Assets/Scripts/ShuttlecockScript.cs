using UnityEngine;

[RequireComponent (typeof(Rigidbody))]
public class ShuttlecockScript : MonoBehaviour
{
    [SerializeField] ShuttlecockData data;

    [Header("Aerodynamism")]
    public float liftCoefficient = 0.1f;
    public float autoRotateStrength = 8f;
    public float mass = 1f;

    [Header("Stabilisation")]
    public float angularDamping = 3f;

    private Rigidbody rb;
    
    [Tooltip("A touché le sol")]
    public bool hasTouchedGround = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = data.shuttlecockMass;
        rb.useGravity = true;
        rb.angularDamping = angularDamping;
    }

    // Update is called once per fixed frame
    void FixedUpdate()
    {
        ApplyAerodynamicDrag();
        ApplyAutoOrientation();
    }

    void ApplyAerodynamicDrag()
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < 1f) return;

        // Drag proportional to speed
        Vector3 drag = data.dragCoefficient * speed * mass * -velocity.normalized;
        rb.AddForce(drag, ForceMode.Force);
    }

    void ApplyAutoOrientation()
    {
        if (rb.linearVelocity.magnitude < 0.5f) return;

        rb.angularVelocity = Vector3.zero;
        Quaternion targetRotation = Quaternion.LookRotation(rb.linearVelocity.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * 8f);
    }
    
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ground"))
        {
            Debug.Log("Touched Ground");
            hasTouchedGround = true;
        }
    }
}
