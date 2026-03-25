using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LumenLike.Demo
{
    [AddComponentMenu("LumenLike Demo/Light Rotator")]
    public class DemoLightRotator : MonoBehaviour
    {
        public bool avoid90Deg;
        public float speed = 5.0f;

        Vector2 _lastPointerPosition;
        bool _hasPointerPosition;

        void Update()
        {
            if (!TryGetPointerState(out Vector2 pointerPosition, out bool rotateRequested))
            {
                _hasPointerPosition = false;
                return;
            }

            if (_hasPointerPosition && rotateRequested)
            {
                Vector2 delta = (_lastPointerPosition - pointerPosition) * speed * Time.deltaTime;
                transform.Rotate(new Vector3(-delta.y, -delta.x, 0.0f), Space.Self);
            }

            if (avoid90Deg && Mathf.Approximately(transform.forward.x, 0.0f) && Mathf.Approximately(transform.forward.z, 0.0f))
            {
                transform.Rotate(new Vector3(0.01f, 0.01f, 0.01f), Space.Self);
            }

            _lastPointerPosition = pointerPosition;
            _hasPointerPosition = true;
        }

        static bool TryGetPointerState(out Vector2 pointerPosition, out bool rotateRequested)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null)
            {
                pointerPosition = default;
                rotateRequested = false;
                return false;
            }

            pointerPosition = mouse.position.ReadValue();
            rotateRequested = mouse.leftButton.isPressed && keyboard != null && keyboard.leftCtrlKey.isPressed;
            return true;
#else
            pointerPosition = Input.mousePosition;
            rotateRequested = Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftControl);
            return true;
#endif
        }
    }
}