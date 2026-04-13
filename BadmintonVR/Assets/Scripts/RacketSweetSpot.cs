using UnityEngine;

public class RacketSweetSpotOval : MonoBehaviour
{
    
    public enum StringAlignment { XY_Plane, XZ_Plane, YZ_Plane }
    
    [Header("Orientation")]
    [Tooltip("Which local axes represent the flat surface of your strings? Look at the transform arrows on your racket.")]
    public StringAlignment stringPlane = StringAlignment.XY_Plane;

    [Header("Racket Setup")] [Tooltip("Drag the child GameObject that has your Strings Collider here.")]
    public Collider stringsCollider;
    
    [Header("Sweet Spot Settings")]
    public float maxBounceForce = 20f;
    public float minBounceForce = 5f;
    
    [Header("Racket Dimensions (Local Space)")]
    [Tooltip("The horizontal radius (width / 2) of the strings.")]
    public float radiusWidth = 0.11f; 
    
    [Tooltip("The vertical radius (height / 2) of the strings.")]
    public float radiusHeight = 0.15f; 
    
    private float lastHitTime = 0f;
    private float hitCooldown = 0.05f;

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"<b><color=cyan>[Racket Hit]</color></b> " + "Collision entered");
        if (collision.gameObject.CompareTag("Volant"))
        {
            if (Time.time - lastHitTime < hitCooldown) return;

            if (collision.GetContact(0).thisCollider != stringsCollider)
            {
                Debug.Log("Hit the frame/handle! Skipping sweet spot logic.");
                return; 
            }

            Transform sTransform = stringsCollider.transform;
            Vector3 impactPoint = collision.GetContact(0).point;
            Vector3 localImpact = sTransform.InverseTransformPoint(impactPoint);

            float localW = 0f;
            float localH = 0f;
            Vector3 rawFaceNormal = Vector3.zero;

            switch (stringPlane)
            {
                case StringAlignment.XY_Plane:
                    localW = localImpact.x; localH = localImpact.y; rawFaceNormal = sTransform.forward; break;
                case StringAlignment.XZ_Plane:
                    localW = localImpact.x; localH = localImpact.z; rawFaceNormal = sTransform.up; break;
                case StringAlignment.YZ_Plane:
                    localW = localImpact.y; localH = localImpact.z; rawFaceNormal = sTransform.right; break;
            }
            // 2. Forehand vs Backhand check
            Vector3 directionToShuttle = collision.transform.position - sTransform.position;
            Vector3 finalHitDirection = rawFaceNormal;
            string strokeType = "Forehand";
            
            if (Vector3.Dot(rawFaceNormal, directionToShuttle) < 0)
            {
                finalHitDirection = -rawFaceNormal; // Flip it for backhands!
                strokeType = "Backhand";
            }

            // 3. Math Multipliers
            float wRatio = localW / radiusWidth;
            float hRatio = localH / radiusHeight;
            float distanceRatio = Mathf.Clamp01(Mathf.Sqrt((wRatio * wRatio) + (hRatio * hRatio)));
            float powerMultiplier = 1f - distanceRatio;
            
            float currentMultiplier = Mathf.Lerp(minBounceForce, maxBounceForce, powerMultiplier);
            float incomingSpeed = collision.relativeVelocity.magnitude;
            float finalSpeed = incomingSpeed * currentMultiplier;

            Rigidbody shuttleRb = collision.gameObject.GetComponent<Rigidbody>();
            if (shuttleRb != null)
            {
                // --- NEW DEBUG VECTOR SECTION ---

                // Capture global velocities BEFORE we zero them out
                Vector3 globalVelocityIn = shuttleRb.linearVelocity;
                Vector3 globalVelocityOut = finalHitDirection * finalSpeed;

                // Convert global velocities into local racket space
                Vector3 localVelocityIn = sTransform.InverseTransformDirection(globalVelocityIn);
                Vector3 localVelocityOut = sTransform.InverseTransformDirection(globalVelocityOut);

                // Log the exact vectors
                Debug.Log($"<b><color=orange>[{strokeType} Hit]</color></b> Multiplier: {currentMultiplier:F2}\n" +
                          $"<b>Global In:</b> {globalVelocityIn} | <b>Global Out:</b> {globalVelocityOut}\n" +
                          $"<b>Local In:</b> {localVelocityIn} | <b>Local Out:</b> {localVelocityOut}");

                // Draw visual lines in the Scene View (Visible for 2 seconds)
                // RED = Shuttlecock incoming direction
                Debug.DrawRay(impactPoint, globalVelocityIn.normalized * 0.3f, Color.red, 2f);
                
                // BLUE = The Raw direction the racket is facing
                Debug.DrawRay(sTransform.position, rawFaceNormal * 0.4f, Color.blue, 2f);
                
                // GREEN = Shuttlecock outgoing hit direction
                Debug.DrawRay(impactPoint, finalHitDirection * 0.5f, Color.green, 2f);

                // --- END DEBUG SECTION ---

                shuttleRb.linearVelocity = Vector3.zero;
                shuttleRb.AddForce(finalHitDirection * finalSpeed, ForceMode.VelocityChange);
                lastHitTime = Time.time;
            }
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        // We only draw if you've assigned the strings!
        if (stringsCollider == null) return;

        Transform sTransform = stringsCollider.transform;
        
        // 1. Draw the dead center (Sweetest Spot) in Red
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(sTransform.position, 0.01f);

        // 2. Draw the outer edge of the sweet spot in Green
        Gizmos.color = Color.green;
        
        int segments = 36; // How smooth the drawn oval will be
        float angle = 0f;
        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            // Calculate the local X and Y for this segment of the ellipse
            float w = Mathf.Sin(angle) * radiusWidth;
            float h = Mathf.Cos(angle) * radiusHeight;
            
            Vector3 localPoint = Vector3.zero;

            // Map the drawn points to the correct axes
            switch (stringPlane)
            {
                case StringAlignment.XY_Plane: localPoint = new Vector3(w, h, 0f); break;
                case StringAlignment.XZ_Plane: localPoint = new Vector3(w, 0f, h); break;
                case StringAlignment.YZ_Plane: localPoint = new Vector3(0f, w, h); break;
            }
            
            // Convert that local point into world space so it rotates with the racket
            // Assuming the strings face the Z-axis, we draw flat on X and Y
            Vector3 currentPoint = sTransform.TransformPoint(localPoint);

            // Draw a line connecting the previous point to the current point
            if (i > 0)
            {
                Gizmos.DrawLine(previousPoint, currentPoint);
            }

            previousPoint = currentPoint;
            angle += (Mathf.PI * 2f) / segments; // Move to the next slice of the pie
        }
    }
}