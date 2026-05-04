using UnityEngine;

/// <summary>
/// Script di chuyển FPS đơn giản cho Unity
/// Yêu cầu: CharacterController gắn trên cùng GameObject
/// Camera con phải là child object của Player
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FPSMovement : MonoBehaviour
{
    [Header("Di Chuyển")]
    public float walkSpeed = 5f;
    public float runSpeed = 10f;
    public float jumpHeight = 1.5f;
    public float gravity = -9.81f;

    [Header("Camera")]
    public Transform cameraTransform;
    public float mouseSensitivity = 100f;
    public float maxLookAngle = 85f;

    [Header("Mặt Đất")]
    public float groundDistance = 0.3f;

    // --- Private ---
    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private float xRotation = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        // Khóa và ẩn con trỏ chuột
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleGravityAndJump();
    }

    /// <summary>
    /// Xử lý nhìn bằng chuột (nhìn ngang xoay Player, nhìn dọc xoay Camera)
    /// </summary>
    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Nhìn dọc (giới hạn góc)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Nhìn ngang (xoay toàn bộ nhân vật)
        transform.Rotate(Vector3.up * mouseX);
    }

    /// <summary>
    /// Xử lý di chuyển WASD + Shift để chạy
    /// </summary>
    void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal"); // A / D
        float z = Input.GetAxis("Vertical");   // W / S

        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float speed = isRunning ? runSpeed : walkSpeed;

        Vector3 move = transform.right * x + transform.forward * z;

        // Chuẩn hoá để tránh di chuyển nhanh hơn khi đi chéo
        if (move.magnitude > 1f)
            move.Normalize();

        controller.Move(move * speed * Time.deltaTime);
    }

    /// <summary>
    /// Xử lý trọng lực và nhảy
    /// Dùng Raycast từ chân nhân vật xuống, không cần Layer
    /// </summary>
    void HandleGravityAndJump()
    {
        // Kiểm tra mặt đất bằng Raycast — bắn thẳng xuống từ chân nhân vật
        Vector3 feetPosition = transform.position + Vector3.down * (controller.height / 2f);
        isGrounded = Physics.Raycast(feetPosition, Vector3.down, groundDistance);

        if (isGrounded && velocity.y < 0)
            velocity.y = -2f; // Giữ nhân vật bám đất

        // Nhảy
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Áp dụng trọng lực
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    // Hiển thị raycast groundCheck trong Scene view để dễ debug
    void OnDrawGizmosSelected()
    {
        if (controller == null) return;
        Vector3 feetPosition = transform.position + Vector3.down * (controller.height / 2f);
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawLine(feetPosition, feetPosition + Vector3.down * groundDistance);
    }
}
