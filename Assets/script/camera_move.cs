using UnityEngine;

public class CameraFlyController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 10.0f;
    public float shiftMultiplier = 2.5f; // 按住Shift加速
    public float rotateSpeed = 2.0f;
    public float zoomSpeed = 5.0f;

    private float _yaw = 0.0f;
    private float _pitch = 0.0f;

    void Start()
    {
        // 初始化旋转角度为当前物体的欧拉角
        Vector3 rotation = transform.eulerAngles;
        _yaw = rotation.y;
        _pitch = rotation.x;
    }

    void Update()
    {
        HandleRotation();
        HandleMovement();
        HandleZoom();
    }

    // --- 1. 旋转逻辑：按住右键变换旋转 ---
    void HandleRotation()
    {
        if (Input.GetMouseButton(1))
        {
            Debug.Log("called mouse right clicked");
            // 锁定并隐藏鼠标（可选，提升体验）
            Cursor.lockState = CursorLockMode.Locked;

            _yaw += Input.GetAxis("Mouse X") * rotateSpeed;
            _pitch -= Input.GetAxis("Mouse Y") * rotateSpeed;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f); // 限制仰角防止翻转

            transform.eulerAngles = new Vector3(_pitch, _yaw, 0.0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }

    // --- 2. 移动逻辑：WASD 且 W 总是朝向前方 ---
    void HandleMovement()
    {
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= shiftMultiplier;

        float h = Input.GetAxis("Horizontal"); // A (-1) D (1)
        float v = Input.GetAxis("Vertical");   // S (-1) W (1)

        // 逻辑推导：
        // transform.forward 是相机当前视角指向的“绝对前方”
        // transform.right   是相机当前的“绝对右方”
        // 这样 W 就会带你向镜头中心飞，A 则是向镜头左侧平移
        Vector3 moveDir = (transform.forward * v) + (transform.right * h);

        transform.position += moveDir * speed * Time.deltaTime;

        Debug.Log("called wasd clicked");
    }

    // --- 3. 缩放逻辑：滚轮变换缩放 (FOV) ---
    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                // 通过修改 Field of View 实现缩放效果
                cam.fieldOfView -= scroll * zoomSpeed * 10f;
                cam.fieldOfView = Mathf.Clamp(cam.fieldOfView, 10f, 90f);
            }
        }
    }
}