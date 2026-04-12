using UnityEngine;

[CreateAssetMenu(fileName = "ShuttlecockData", menuName = "Scriptable Objects/ShuttlecockData")]
public class ShuttlecockData : ScriptableObject
{
    public float dragCoefficient = 0.1f;
    public float initialSpeed = 15f;
    public float shuttlecockMass = 0.005f;
}
