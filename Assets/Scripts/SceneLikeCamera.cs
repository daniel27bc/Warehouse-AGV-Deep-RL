using UnityEngine;

public class SceneLikeCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float fastMultiplier = 3f;
    public float scrollSpeed = 5f;

    [Header("Rotation")]
    public float lookSensitivity = 2f;

    private float yaw;
    private float pitch;

    void Start()
    {
        yaw   = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
    }

    void Update()
    {
        // Scroll to zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        // Multiplicamos por un factor alto porque ScrollWheel da valores muy pequeños
        transform.Translate(Vector3.forward * scroll * scrollSpeed * 10f * Time.deltaTime, Space.Self);

        // Right-click drag to rotate
        if (Input.GetMouseButton(1))
        {
            yaw   += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
            pitch  = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // WASD + QE movement relative to camera orientation
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
        Vector3 dir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) dir -= Vector3.forward;
        if (Input.GetKey(KeyCode.A)) dir -= Vector3.right;
        if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
        if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) dir -= Vector3.up;
        
        // Normalizamos para no ir más rápido en diagonal
        transform.Translate(dir.normalized * speed * Time.deltaTime, Space.Self);
    }
}
