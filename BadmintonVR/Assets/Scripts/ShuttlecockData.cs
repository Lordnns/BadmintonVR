using UnityEngine;

[CreateAssetMenu(fileName = "ShuttlecockData", menuName = "Scriptable Objects/ShuttlecockData")]
public class ShuttlecockData : ScriptableObject
{
    [Header("Global speed")]
    [Range(5f, 15f)]
    public float initialSpeed = 10f;

    [Header("Unity Physics")]
    public float shuttlecockMass = 0.005f;
    public float dragCoefficient = 0.1f;

    [Header("Sine Physics")]
    [Range(0f, 10f)]
    public float height = 2.0f;

    [Header("Aerodynamism")]
    public float liftCoefficient = 0.1f;
    public float autoRotateStrength = 8f;

    [Header("Stabilisation")]
    public float angularDamping = 3f;

    [Header("Others")]
    public float despawnTime = 10f;
}
