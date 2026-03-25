using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LumenLike
{
    [AddComponentMenu("LumenLike Demo/Free Camera")]
    public class FreeCameraLumenLike : MonoBehaviour
    {
        const float MouseLookScale = 0.1f;

        public float m_LookSpeedController = 120f;
        public float m_LookSpeedMouse = 4.0f;
        public float m_MoveSpeed = 10.0f;
        public float m_MoveSpeedIncrement = 2.5f;
        public float m_Turbo = 10.0f;

        float _pitch;
        float _yaw;

        void OnEnable()
        {
            Vector3 euler = transform.localEulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);
        }

        void Update()
        {
            Vector2 lookDelta = ReadLookDelta();
            Vector3 moveInput = ReadMoveInput();
            float speedAdjustment = ReadSpeedAdjustment();
            bool turbo = IsTurboActive();

            if (!Mathf.Approximately(speedAdjustment, 0.0f))
            {
                m_MoveSpeed = Mathf.Max(0.1f, m_MoveSpeed + speedAdjustment * m_MoveSpeedIncrement * Time.deltaTime);
            }

            _yaw += lookDelta.x;
            _pitch = Mathf.Clamp(_pitch - lookDelta.y, -89.0f, 89.0f);
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0.0f);

            if (moveInput.sqrMagnitude > 0.0f)
            {
                float moveSpeed = m_MoveSpeed * (turbo ? m_Turbo : 1.0f) * Time.deltaTime;
                Vector3 localMotion = moveInput.normalized * moveSpeed;
                transform.position += transform.forward * localMotion.z;
                transform.position += transform.right * localMotion.x;
                transform.position += Vector3.up * localMotion.y;
            }
        }

        Vector2 ReadLookDelta()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Vector2 look = Vector2.zero;
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                look += Mouse.current.delta.ReadValue() * m_LookSpeedMouse * MouseLookScale;
            }
            if (Gamepad.current != null)
            {
                look += Gamepad.current.rightStick.ReadValue() * m_LookSpeedController * Time.deltaTime;
            }
            return look;
#else
            if (!Input.GetMouseButton(1))
            {
                return Vector2.zero;
            }
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * m_LookSpeedMouse;
#endif
        }

        Vector3 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Vector3 move = Vector3.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move.z += keyboard.wKey.isPressed ? 1.0f : 0.0f;
                move.z -= keyboard.sKey.isPressed ? 1.0f : 0.0f;
                move.x += keyboard.dKey.isPressed ? 1.0f : 0.0f;
                move.x -= keyboard.aKey.isPressed ? 1.0f : 0.0f;
                move.y += keyboard.eKey.isPressed ? 1.0f : 0.0f;
                move.y -= keyboard.qKey.isPressed ? 1.0f : 0.0f;
            }
            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                move.x += stick.x;
                move.z += stick.y;
                move.y += Gamepad.current.rightTrigger.ReadValue();
                move.y -= Gamepad.current.leftTrigger.ReadValue();
            }
            return move;
#else
            Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"), 0.0f, Input.GetAxisRaw("Vertical"));
            move.y += Input.GetKey(KeyCode.E) ? 1.0f : 0.0f;
            move.y -= Input.GetKey(KeyCode.Q) ? 1.0f : 0.0f;
            return move;
#endif
        }

        float ReadSpeedAdjustment()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y * 0.01f : 0.0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        bool IsTurboActive()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
                   (Mouse.current != null && Mouse.current.rightButton.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetMouseButton(1);
#endif
        }

        static float NormalizePitch(float pitch)
        {
            if (pitch > 180.0f)
            {
                pitch -= 360.0f;
            }
            return pitch;
        }
    }
}