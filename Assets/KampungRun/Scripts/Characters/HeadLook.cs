using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// People notice things: the head (and a little of the neck) turns toward whatever they're looking at -
    /// you walking past, the person they're chatting to, a crash across the road. It is the cheapest way to
    /// make a crowd feel alive. Runs after the animator has posed the body and adds the turn on top, within
    /// a natural range; it eases in and out, and only works while the body is being animated this frame
    /// (on screen and close), so it never stacks up on a frozen pose.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class HeadLook : MonoBehaviour
    {
        public Transform target;            // looked at (head height is added)
        public Vector3 point;               // or a spot in the world
        public bool usePoint;
        public float maxDistance = 9f;
        public float weightScale = 1f;      // 0..1, e.g. half-hearted glances

        Transform _head, _neck;
        Renderer _renderer;
        float _w;
        Transform _eyeOf;
        float _eye;

        void Awake()
        {
            if (!Bind()) enabled = false;
        }

        /// <summary>Find the body's head (again, when the body has been swapped: the old one is destroyed a frame later).</summary>
        bool Bind()
        {
            Animator a = null;
            foreach (var x in GetComponentsInChildren<Animator>())
                if (x != null && x.isHuman && x.gameObject.activeInHierarchy) a = x;    // the newest body is the last one
            if (a == null) return false;
            _head = a.GetBoneTransform(HumanBodyBones.Head);
            _neck = a.GetBoneTransform(HumanBodyBones.Neck);
            _renderer = a.GetComponentInChildren<SkinnedMeshRenderer>();
            return _head != null;
        }

        public void LookAt(Transform t) { target = t; usePoint = false; }
        public void LookAt(Vector3 p) { point = p; usePoint = true; target = null; }
        public void Clear() { target = null; usePoint = false; }

        void LateUpdate()
        {
            if (_head == null && !Bind()) return;
            Vector3 aim = default;
            bool has = false;
            if (target != null)
            {
                if (_eyeOf != target) { _eyeOf = target; _eye = EyeHeight(target); }
                aim = target.position + Vector3.up * _eye;
                has = true;
            }
            else if (usePoint) { aim = point; has = true; }

            float want = 0f;
            Vector3 local = Vector3.forward;
            if (has)
            {
                var to = aim - _head.position;
                if (to.sqrMagnitude < maxDistance * maxDistance && to.sqrMagnitude > 0.04f)
                {
                    local = transform.InverseTransformDirection(to.normalized);
                    // over the shoulder is too far: only look round to about 100 degrees either side
                    if (local.z > -0.2f) want = weightScale;
                }
            }
            _w = Mathf.MoveTowards(_w, want, Time.deltaTime * 2.8f);
            if (_w < 0.01f) return;
            // only on a pose the animator wrote this frame (off screen the culled animator leaves the last one)
            if (_renderer != null && !_renderer.isVisible) return;

            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -30f, 35f);
            // the turn, about the body's own axes, as a world-space rotation laid on top of the animated pose
            var body = transform.rotation;
            var turn = body * Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Inverse(body);
            if (_neck != null) _neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, _w * 0.35f) * _neck.rotation;
            // the neck already carried part of it to the head
            _head.rotation = Quaternion.Slerp(Quaternion.identity, turn, _w * (_neck != null ? 0.65f : 1f)) * _head.rotation;
        }

        static float EyeHeight(Transform t)
        {
            // people and the player look at faces, everything else at its middle
            if (t.GetComponent<PlayerController>() != null) return 1.5f;
            if (t.GetComponent<Pedestrian>() != null || t.GetComponent<NPC>() != null) return 1.5f;
            return 0.5f;
        }
    }
}
