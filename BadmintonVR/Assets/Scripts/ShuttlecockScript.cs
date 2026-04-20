using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent (typeof(Rigidbody))]
public class ShuttlecockScript : MonoBehaviour
{
    [SerializeField] ShuttlecockData data;

    [Header("Sol")]
    [Tooltip("A touché le sol")]
    public bool hasTouchedGround = false;

    public UnityEvent OnShuttlecockLanded;
    public UnityEvent OnRacketHit;

    private Rigidbody rb;

    // Physics mode: true for sine-based, false for drag-based
    bool useSinePhysics = true;
    Vector3 posA;
    Vector3 posB;
    float duration;
    float height;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = data.shuttlecockMass;
        rb.angularDamping = data.angularDamping;
    }

    // On instantiation, initialize with either sine physics or real physics
    public void Initialize(bool useSine, Transform a, Transform b, float dur, float h)
    {
        useSinePhysics = useSine;
        posA = a.position;
        posB = b.position;
        duration = dur;
        height = h;

        if (useSinePhysics)
            StartSinePhysics();
        else
            StartRealPhysics();
    }

    // Update is called once per fixed frame
    void FixedUpdate()
    {
        if (!useSinePhysics)
        {
            ApplyAerodynamicDrag();
            ApplyAutoOrientation();
        }
    }

    // Sine physics
    void StartSinePhysics()
    {
        rb.isKinematic = true;
        rb.useGravity = false;
        StartCoroutine(MoveInSineCurve());
    }

    IEnumerator MoveInSineCurve()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Position
            float t = elapsed / duration;
            Vector3 currentPos = Vector3.Lerp(posA, posB, t);
            currentPos.y += Mathf.Sin(Mathf.PI * t) * height;

            // Orientation
            Vector3 nextPos = Vector3.Lerp(posA, posB, t + 0.01f);
            nextPos.y += Mathf.Sin(Mathf.PI * (t + 0.01f)) * height;
            Vector3 direction = (nextPos - currentPos).normalized;
            if (direction != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(direction);

            transform.position = currentPos;
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = posB;
        SwitchToRealPhysics();
    }

    void StartRealPhysics()
    {
        rb.isKinematic = false;
        rb.useGravity = true;
    }

    public void SwitchToRealPhysics()
    {
        if (!useSinePhysics) return;
        useSinePhysics = false;
        StopAllCoroutines();
        // Donne une vitesse initiale basée sur la direction vers B
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = (posB - transform.position).normalized * data.initialSpeed;
    }

    void ApplyAerodynamicDrag()
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < 1f) return;

        // Drag proportional to speed
        Vector3 drag = data.dragCoefficient * speed * speed * rb.mass * -velocity.normalized;
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
        if (other.CompareTag("Ground") || other.GetComponent<TargetZone>())
        {
            hasTouchedGround = true;
            SwitchToRealPhysics();
            if (Gamemode.Instance != null)
            {
                Gamemode.Instance.OnShuttlecockLanded(); 
            }
        }
    }

    private void OnCollisionEnter(Collision other)
    {
        Debug.Log($"[Shuttlecock] hit object named: {other.gameObject.name} with Tag: {other.gameObject.tag}");
        if (other.gameObject.CompareTag("RacketHead"))
        {
            SwitchToRealPhysics();
            Gamemode.Instance.OnRacketHit(); 
        }
    }
}
