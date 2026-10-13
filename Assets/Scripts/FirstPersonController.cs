using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public Transform cam;
    public float moveSpeed = 5f;
    public float lookSensitivity = 0.1f;
    public float gravity = -9.81f;

    CharacterController cc;
    float pitch, yVel;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
        transform.Rotate(0f, look.x, 0f);
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        cam.localEulerAngles = new Vector3(pitch, 0f, 0f);

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;

        Vector3 move = (transform.right * input.x + transform.forward * input.y).normalized * moveSpeed;

        if (cc.isGrounded && yVel < 0) yVel = -2f;
        yVel += gravity * Time.deltaTime;
        move.y = yVel;
        cc.Move(move * Time.deltaTime);

        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}