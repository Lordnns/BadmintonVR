using UnityEngine;

public class RailsMovement : MonoBehaviour
{
    [Header("Objects")]
    public GameObject launcher;
    public Transform startPoint;
    public Transform endPoint;

    [Header("Parameters")]
    [Range(-1f, 1f)]
    public float alpha = 0f;
    public bool oscillate = false;
    public float oscillationFrequency = 0.1f;
    
    float currentT = 0.5f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        float targetT;

        if (oscillate)
            targetT = (Mathf.Sin(Time.time * oscillationFrequency * 2f * Mathf.PI) + 1f) / 2f;
        else
            targetT = (alpha + 1f) / 2f;

        currentT = Mathf.Lerp(currentT, targetT, 0.1f);
        launcher.transform.position = Vector3.Lerp(startPoint.position, endPoint.position, currentT);
    }
}
