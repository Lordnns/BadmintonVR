using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class RacketScript : MonoBehaviour
{
    [Header("Audio")]
    public AudioClip WhooshClip;
    public AudioClip HitClip;

    [Header("Speed parameters")]
    public float speedThreshold = 8f;
    public float maxSpeed = 16f;

    [Header("Volume")]
    public float minVolume = 0.2f;
    public float maxVolume = 1.0f;

    [Header("Pitch")]
    public float minPitch = 0.5f;
    public float maxPitch = 1f;

    [Header("Cooldowns")]
    public float swooshCooldown = 0.3f;
    public float hitCooldown = 0.3f;

    [Header("Tags")]
    public string shuttleCockTag = "Volant";
    public string racketHeadTag = "RacketHead";

    private AudioSource _audioSource;
    private Vector3 _previousPosition;
    private float _lastSwooshTime;
    private float _lastHitTime;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1f;
        _previousPosition = transform.position;
    }

    // FixedUpdate is called once per fixed frame
    void FixedUpdate()
    {
        // Swoosh
        float speed = (transform.position - _previousPosition).magnitude / Time.fixedDeltaTime;
        _previousPosition = transform.position;

        if (speed >= speedThreshold && Time.time - _lastSwooshTime >= swooshCooldown)
        {
            float t = Mathf.Lerp(minVolume, maxVolume, speed);

            _audioSource.volume = Mathf.Lerp(minVolume, maxVolume, t);
            _audioSource.pitch = Mathf.Lerp(minPitch, maxPitch, t);

            _audioSource.PlayOneShot(WhooshClip);
            _lastSwooshTime = Time.time;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.thisCollider.gameObject.CompareTag(racketHeadTag))
            {
                if (!collision.gameObject.CompareTag(shuttleCockTag)) return;
                if (Time.time - _lastHitTime < hitCooldown) return;
                if (collision.rigidbody.linearVelocity.magnitude < 5f) return;

                _audioSource.PlayOneShot(HitClip);
                _lastHitTime = Time.time;
            }
        }
    }
}
