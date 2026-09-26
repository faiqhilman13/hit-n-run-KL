using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Fits the seated presentation to a car's real roof while keeping the source
    /// character's proportions. Runs before SeatFit, which still owns hip and hand placement.
    /// Standing models and riders always retain their authored scale.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class SeatedCharacterScale : MonoBehaviour
    {
        Animator _animator;
        Transform _hips, _head, _seat;
        SeatFit _fit;
        Vector3 _authoredScale;
        float _cabinScale = 1f, _displayScale = 1f, _settledTime;
        bool _measured;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _authoredScale = _animator ? _animator.transform.localScale : Vector3.one;
            if (!_animator || !_animator.isHuman) { enabled = false; return; }
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        }

        void LateUpdate()
        {
            if (!_animator || !_hips || !_head) return;
            if (!_fit) _fit = GetComponentInParent<SeatFit>();
            if (!_fit || !_fit.seat || _fit.weight <= 0f)
            {
                Restore();
                return;
            }
            var vehicle = _fit.seat.GetComponentInParent<Vehicle>();
            if (!vehicle || vehicle.TwoWheeler) { Restore(); return; }
            if (_seat != _fit.seat)
            {
                _seat = _fit.seat;
                _measured = false;
                _settledTime = 0;
                _cabinScale = 1;
            }
            if (!_measured && _fit.weight > 0.9f &&
                _animator.GetCurrentAnimatorStateInfo(0).IsName("Sit") && !_animator.IsInTransition(0))
            {
                _settledTime += Time.deltaTime;
                if (_settledTime > 0.08f)
                {
                    _cabinScale = MeasureCabinScale(vehicle);
                    _measured = true;
                }
            }
            float target = Mathf.Lerp(1f, _cabinScale, Mathf.Clamp01(_fit.weight));
            _displayScale = Mathf.MoveTowards(_displayScale, target, Time.deltaTime * 2f);
            _animator.transform.localScale = _authoredScale * _displayScale;
        }

        float MeasureCabinScale(Vehicle vehicle)
        {
            Vector3 up = vehicle.transform.up;
            float crownHeight = 0;
            var baked = new Mesh();
            foreach (var renderer in _animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!renderer.sharedMesh || renderer.name.Contains("LOD1")) continue;
                renderer.BakeMesh(baked, true);
                foreach (var point in baked.vertices)
                {
                    var world = renderer.transform.TransformPoint(point);
                    crownHeight = Mathf.Max(crownHeight, Vector3.Dot(world - _hips.position, up));
                }
            }
            Destroy(baked);
            if (crownHeight < 0.25f) return 1f;

            // Sample around the crown and the seat to account for sloping roof skins.
            Vector3 hipAtSeat = _seat.position + up * 0.02f;
            Vector3 crownCentre = hipAtSeat + Vector3.ProjectOnPlane(_head.position - _hips.position, up);
            float roofHeight = float.PositiveInfinity;
            foreach (var offset in new[] { Vector3.zero, vehicle.transform.right * 0.07f,
                         -vehicle.transform.right * 0.07f, vehicle.transform.forward * 0.06f,
                         -vehicle.transform.forward * 0.06f })
            {
                float height = RoofHeight(vehicle, crownCentre + offset, up);
                if (height > 0.4f) roofHeight = Mathf.Min(roofHeight, height);
            }
            if (float.IsPositiveInfinity(roofHeight)) return 1f;
            // 3 cm roof thickness plus 6 cm visible clearance for suspension and idle motion.
            return Mathf.Clamp((roofHeight - 0.09f) / crownHeight, 0.65f, 1f);
        }

        static float RoofHeight(Vehicle vehicle, Vector3 origin, Vector3 up)
        {
            float highest = float.NegativeInfinity;
            foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "Body" || !filter.sharedMesh) continue;
                var vertices = filter.sharedMesh.vertices;
                var triangles = filter.sharedMesh.triangles;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                    Vector3 edge1 = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]) - a;
                    Vector3 edge2 = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]) - a;
                    Vector3 cross = Vector3.Cross(up, edge2);
                    float determinant = Vector3.Dot(edge1, cross);
                    if (Mathf.Abs(determinant) < 1e-9f) continue;
                    Vector3 relative = origin - a;
                    float u = Vector3.Dot(relative, cross) / determinant;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(relative, edge1);
                    float v = Vector3.Dot(up, q) / determinant;
                    if (v < 0 || u + v > 1) continue;
                    highest = Mathf.Max(highest, Vector3.Dot(edge2, q) / determinant);
                }
            }
            return highest;
        }

        void Restore()
        {
            // Ordinary pedestrians never acquire a SeatFit; avoid dirtying their
            // entire transform hierarchy with an identical scale write every frame.
            if (_animator && _displayScale != 1f) _animator.transform.localScale = _authoredScale;
            _seat = null;
            _measured = false;
            _settledTime = 0;
            _cabinScale = _displayScale = 1;
        }

        void OnDisable() => Restore();
    }
}
