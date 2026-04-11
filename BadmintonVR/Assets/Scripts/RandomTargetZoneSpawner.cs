using UnityEngine;

public class RandomTargetZoneSpawner : MonoBehaviour
{
    [Header("Détails de la zone de spawns possibles")]
    private Vector3 TopLeftPoint;

    private Vector3 BottomRightPoint;

    private float width;
    private float height;
    
    [Header("Prefab de la target zone")]
    public GameObject TargetZonePrefab;
    
    public float cylinderRadius = 0.5f;

    private GameObject currentTargetZone;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TopLeftPoint = transform.GetChild(0).position;
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
        
        currentTargetZone = Instantiate(TargetZonePrefab, targetZonePos, Quaternion.identity);
        currentTargetZone.GetComponent<TargetZone>().spawner = this.gameObject;
    }
}
