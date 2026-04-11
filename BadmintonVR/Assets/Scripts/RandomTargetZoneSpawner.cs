using UnityEngine;

public class RandomTargetZoneSpawner : MonoBehaviour
{
    [Header("Détails de la zone de spawns possibles")]
    private Vector3 TopLeftPoint;

    public Vector3 BottomRightPoint;

    public float width;
    public float height;
    
    [Header("Prefab de la target zone")]
    public GameObject TargetZonePrefab;
    
    public float cylinderRadius = 0.5f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log("Top left : " + TopLeftPoint);
        BottomRightPoint = transform.GetChild(1).position;
        
        width = BottomRightPoint.x - TopLeftPoint.x;
        height = BottomRightPoint.z - TopLeftPoint.z;

        spawnNewZone();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void spawnNewZone()
    {
        float x = Random.Range(cylinderRadius,width);
        float y = Random.Range(cylinderRadius, height);
        Vector3 targetZonePos = TopLeftPoint + new Vector3(x,0,y);
        Instantiate(TargetZonePrefab, targetZonePos, Quaternion.identity);
    }
}
