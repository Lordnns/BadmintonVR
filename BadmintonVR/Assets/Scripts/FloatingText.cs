using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    [Header("Paramètres d'animation")]
    public float floatSpeed = 1.5f;
    public float destroyTime = 1.5f;

    private TextMeshProUGUI textMesh;
    private Color textColor;
    private float timer;

    void Awake() 
    {
        textMesh = GetComponentInChildren<TextMeshProUGUI>();
        if (textMesh != null)
        {
            textColor = textMesh.color;
        }
    }

    public void Setup(int scoreValue)
    {
        if (textMesh != null)
        {
            textMesh.text = "+" + scoreValue.ToString();
        }
        Destroy(gameObject, destroyTime);
    }

    void Update()
    {
        // Animation going up and fadingi gu
        transform.position += Vector3.up * (floatSpeed * Time.deltaTime);
        timer += Time.deltaTime;
        if (textMesh != null)
        {
            textColor.a = Mathf.Lerp(1f, 0f, timer / destroyTime);
            textMesh.color = textColor;
        }
    }
}