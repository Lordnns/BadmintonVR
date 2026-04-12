using UnityEngine;

public class RailsMovement : MonoBehaviour
{
    [Header("Objects")]
    public GameObject launcher;
    public GameObject rails;

    [Header("Parameters")]
    public float oscillationFrequency = 0.1f;
    public float amplitude = 3f;

    Vector3 startPos;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startPos = launcher.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        // Oscillate position
        float oscillation = Mathf.Sin(Time.time * oscillationFrequency * 2f * Mathf.PI) * amplitude;
        launcher.transform.position = new Vector3(startPos.x, startPos.y, startPos.z + oscillation);
    }
}
