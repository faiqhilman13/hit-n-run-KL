using UnityEngine;
using UnityEngine.InputSystem;

namespace KampungRun
{
    /// <summary>
    /// Keyboard/mouse + gamepad input, read straight from devices so there is no
    /// asset wiring to break. Layout is modelled on Hit &amp; Run:
    ///   Move  WASD / left stick      Look   mouse / right stick
    ///   Jump / handbrake  Space / A  Kick   F / RMB / X   Punch Q / LMB / B
    ///   Enter/exit car, talk  E / Y  Horn   H / L3
    ///   Pause  Esc / Start           Sprint Shift / LB   Reset car  R / Back
    /// </summary>
    public static class GameInput
    {
        static bool _locked;
        static int _releaseFrame = -1;

        /// <summary>Dialogue / menu / cutscene. Stays locked one extra frame after release so
        /// the button that closed a box does not also trigger gameplay.</summary>
        public static bool Locked
        {
            get => _locked || Time.frameCount <= _releaseFrame;
            set
            {
                if (_locked && !value) _releaseFrame = Time.frameCount + 1;
                _locked = value;
            }
        }

        static Keyboard Kb => Keyboard.current;
        // while the touch overlay is up the mouse is just a finger (emulators, ?touch=1), not buttons
        static Mouse Ms => TouchInput.Active ? null : Mouse.current;
        static Gamepad Gp => Gamepad.current;

        public static Vector2 Move
        {
            get
            {
                if (Locked) return Vector2.zero;
                Vector2 v = Vector2.zero;
                if (Kb != null)
                {
                    if (Kb.wKey.isPressed || Kb.upArrowKey.isPressed) v.y += 1;
                    if (Kb.sKey.isPressed || Kb.downArrowKey.isPressed) v.y -= 1;
                    if (Kb.dKey.isPressed || Kb.rightArrowKey.isPressed) v.x += 1;
                    if (Kb.aKey.isPressed || Kb.leftArrowKey.isPressed) v.x -= 1;
                }
                if (Gp != null) v += Gp.leftStick.ReadValue();
                v += TouchInput.Move;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static Vector2 Look
        {
            get
            {
                Vector2 v = Vector2.zero;
                if (Ms != null && Cursor.lockState == CursorLockMode.Locked) v += Ms.delta.ReadValue() * 0.08f;
                if (Gp != null) v += Gp.rightStick.ReadValue() * 2.2f;
                v += TouchInput.Look;
                return v;
            }
        }

        /// <summary>Car throttle -1..1 (W/S, or triggers).</summary>
        public static float Throttle
        {
            get
            {
                if (Locked) return 0;
                float t = 0;
                if (Kb != null)
                {
                    if (Kb.wKey.isPressed || Kb.upArrowKey.isPressed) t += 1;
                    if (Kb.sKey.isPressed || Kb.downArrowKey.isPressed) t -= 1;
                }
                if (Gp != null) t += Gp.rightTrigger.ReadValue() - Gp.leftTrigger.ReadValue();
                t += TouchInput.Move.y;
                return Mathf.Clamp(t, -1, 1);
            }
        }

        public static float Steer
        {
            get
            {
                if (Locked) return 0;
                float s = 0;
                if (Kb != null)
                {
                    if (Kb.dKey.isPressed || Kb.rightArrowKey.isPressed) s += 1;
                    if (Kb.aKey.isPressed || Kb.leftArrowKey.isPressed) s -= 1;
                }
                if (Gp != null) s += Gp.leftStick.ReadValue().x;
                s += TouchInput.Move.x;
                return Mathf.Clamp(s, -1, 1);
            }
        }

        public static bool JumpDown => !Locked && ((Kb?.spaceKey.wasPressedThisFrame ?? false) || (Gp?.buttonSouth.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Jump));
        public static bool Handbrake => !Locked && ((Kb?.spaceKey.isPressed ?? false) || (Gp?.buttonSouth.isPressed ?? false) || TouchInput.IsHeld(TouchButton.Jump));
        public static bool KickDown => !Locked && ((Kb?.fKey.wasPressedThisFrame ?? false) || (Ms?.rightButton.wasPressedThisFrame ?? false) || (Gp?.buttonWest.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Kick));
        public static bool PunchDown => !Locked && ((Kb?.qKey.wasPressedThisFrame ?? false) || (Ms?.leftButton.wasPressedThisFrame ?? false) || (Gp?.buttonEast.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Punch));
        public static bool InteractDown => !Locked && ((Kb?.eKey.wasPressedThisFrame ?? false) || (Gp?.buttonNorth.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Interact));
        public static bool HornDown => !Locked && ((Kb?.hKey.wasPressedThisFrame ?? false) || (Gp?.leftStickButton.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Horn));
        // on touch, pushing the stick all the way is a run (the LARI button also works)
        public static bool Sprint => (Kb?.leftShiftKey.isPressed ?? false) || (Gp?.leftShoulder.isPressed ?? false) ||
                                     TouchInput.IsHeld(TouchButton.Sprint) || (TouchInput.Active && TouchInput.Move.magnitude > 0.96f);
        public static bool ResetCarDown => !Locked && ((Kb?.rKey.wasPressedThisFrame ?? false) || (Gp?.selectButton.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Reset));

        /// <summary>Advance dialogue / confirm menus (works while Locked).</summary>
        public static bool ConfirmDown =>
            (Kb?.eKey.wasPressedThisFrame ?? false) || (Kb?.enterKey.wasPressedThisFrame ?? false) ||
            (Kb?.spaceKey.wasPressedThisFrame ?? false) || (Gp?.buttonSouth.wasPressedThisFrame ?? false) ||
            (Gp?.buttonNorth.wasPressedThisFrame ?? false) || (Ms?.leftButton.wasPressedThisFrame ?? false) || TouchInput.Tap;

        public static bool PauseDown => (Kb?.escapeKey.wasPressedThisFrame ?? false) || (Gp?.startButton.wasPressedThisFrame ?? false) || TouchInput.Pressed(TouchButton.Pause);
    }
}
