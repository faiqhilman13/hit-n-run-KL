using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// The reference skeleton the clips are authored on (chr_aiman, standing at the origin facing +Z in its
    /// T-pose). Applies a <see cref="Pose"/>: the spine and arms by forward kinematics (each rotation is
    /// about the rest-pose body axes, carried down the chain, so "bend forward 10 degrees" means the same on
    /// every bone), the legs and any reaching hands by two-bone IK - planted feet stay exactly where they
    /// are put, and roll heel-to-toe - then reads the result back as a Humanoid muscle pose, which retargets
    /// onto the whole cast.
    /// </summary>
    public class AnimRig
    {
        public readonly GameObject go;
        public readonly Animator animator;
        readonly HumanPoseHandler _handler;
        readonly Dictionary<Transform, (Vector3 lp, Quaternion lr)> _restLocal = new Dictionary<Transform, (Vector3, Quaternion)>();
        readonly Dictionary<HumanBodyBones, (Vector3 p, Quaternion r)> _rest = new Dictionary<HumanBodyBones, (Vector3, Quaternion)>();

        public float thigh, shin, upperArm, foreArm, hipHeight, footBack, legLength;
        public Vector3 HeadRest => _rest[HumanBodyBones.Head].p;
        public Vector3 ChestRest => _rest[HumanBodyBones.Chest].p;
        public Vector3 HipsRest => _rest[HumanBodyBones.Hips].p;
        public Vector3 ShoulderRest(int s) => _rest[s < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm].p;
        public Vector3 HipJointRest(int s) => _rest[s < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg].p;
        public Vector3 AnkleRest(int s) => _rest[s < 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot].p;
        public Vector3 BallRest(int s) => _rest[s < 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes].p;

        /// <summary>Lowest point of each foot (heel and toe tip) after the last Apply, for checks.</summary>
        public float LowestFoot { get; private set; }
        public string LowestWhat { get; private set; }

        static readonly Vector3 Right = Vector3.right, Up = Vector3.up, Fwd = Vector3.forward;

        public AnimRig(GameObject prefab)
        {
            go = Object.Instantiate(prefab);
            go.name = "AnimRig_" + prefab.name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            animator = go.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) _restLocal[t] = (t.localPosition, t.localRotation);
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = animator.GetBoneTransform(b);
                if (t != null) _rest[b] = (t.position, t.rotation);
            }
            thigh = Vector3.Distance(_rest[HumanBodyBones.LeftUpperLeg].p, _rest[HumanBodyBones.LeftLowerLeg].p);
            shin = Vector3.Distance(_rest[HumanBodyBones.LeftLowerLeg].p, _rest[HumanBodyBones.LeftFoot].p);
            upperArm = Vector3.Distance(_rest[HumanBodyBones.LeftUpperArm].p, _rest[HumanBodyBones.LeftLowerArm].p);
            foreArm = Vector3.Distance(_rest[HumanBodyBones.LeftLowerArm].p, _rest[HumanBodyBones.LeftHand].p);
            hipHeight = _rest[HumanBodyBones.Hips].p.y;
            legLength = thigh + shin + AnkleRest(-1).y;
            // the heel sticks out behind the ankle about a third of the ankle-to-ball length
            footBack = Mathf.Abs(BallRest(-1).z - AnkleRest(-1).z) * 0.4f;
            _handler = new HumanPoseHandler(animator.avatar, go.transform);
        }

        public void Dispose()
        {
            _handler.Dispose();
            Object.DestroyImmediate(go);
        }

        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

        public void ResetPose()
        {
            foreach (var kv in _restLocal) { kv.Key.localPosition = kv.Value.lp; kv.Key.localRotation = kv.Value.lr; }
        }

        /// <summary>Spine-type rotation about the rest axes: pitch first, then roll, then yaw.</summary>
        public static Quaternion Spin(float pitch, float yaw, float roll) =>
            Quaternion.AngleAxis(yaw, Up) * Quaternion.AngleAxis(-roll, Fwd) * Quaternion.AngleAxis(pitch, Right);

        void SetDelta(HumanBodyBones b, Quaternion delta)
        {
            var t = B(b);
            if (t != null) t.rotation = delta * _rest[b].r;
        }

        public void Apply(Pose p)
        {
            ResetPose();
            // hips: where they go, then (with autoHip) lower them so the planted feet still reach
            var dHips = Spin(p.hipPitch, p.hipYaw, p.hipRoll);
            var hips = B(HumanBodyBones.Hips);
            hips.SetPositionAndRotation(_rest[HumanBodyBones.Hips].p + p.hips, dHips * _rest[HumanBodyBones.Hips].r);
            if (p.autoHip > 0f)
            {
                float drop = 0f;
                foreach (int s in new[] { -1, 1 })
                {
                    var side = p[s];
                    // how firmly this foot is on the ground (fades out as it lifts, so the hips never pop)
                    float planted = 1f - Mathf.SmoothStep(0f, 1f, side.foot.y / 0.05f);
                    if (planted <= 0f) continue;
                    var a = AnkleTarget(side, s, out _, out _);
                    var h = B(s < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
                    float reach = (thigh + shin) * 0.985f;
                    var v = a - h;
                    if (v.sqrMagnitude <= reach * reach) continue;
                    // lower the hips by dy so |a - (h - up*dy)| = reach
                    float vy = v.y, disc = vy * vy - v.sqrMagnitude + reach * reach;
                    float dy = disc >= 0f ? -vy - Mathf.Sqrt(disc) : -vy;
                    drop = Mathf.Max(drop, Mathf.Clamp(dy, 0f, 0.4f) * planted);
                }
                if (drop > 0f) hips.position -= Up * drop * p.autoHip;
            }

            var dSpine = dHips * Spin(p.spinePitch, p.spineYaw, p.spineRoll);
            SetDelta(HumanBodyBones.Spine, dSpine);
            var dChest = dSpine * Spin(p.chestPitch, p.chestYaw, p.chestRoll);
            SetDelta(HumanBodyBones.Chest, dChest);
            var dNeck = dChest * Spin(p.neckPitch, p.neckYaw, p.neckRoll);
            SetDelta(HumanBodyBones.Neck, dNeck);
            var dHead = dNeck * Spin(p.headPitch, p.headYaw, p.headRoll);
            SetDelta(HumanBodyBones.Head, dHead);

            Arm(p, -1, dHips, dChest, dHead);
            Arm(p, 1, dHips, dChest, dHead);
            LowestFoot = 9f;
            Leg(p, -1, dHips);
            Leg(p, 1, dHips);
        }

        // ------------------------------------------------------------------ arms
        void Arm(Pose p, int s, Quaternion dHips, Quaternion dChest, Quaternion dHead)
        {
            var side = p[s];
            bool left = s < 0;
            var clav = left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            var up = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var lo = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;

            var dClav = dChest * Quaternion.AngleAxis(s * side.shrug, Fwd) * Quaternion.AngleAxis(-s * side.protract, Up);
            SetDelta(clav, dClav);
            // forward kinematics: twist (about the arm's own rest axis), lower, then swing forward
            var dUp = dClav * Quaternion.AngleAxis(-side.armFwd, Right) * Quaternion.AngleAxis(-s * side.armDown, Fwd) *
                      Quaternion.AngleAxis(side.armTwist, Right);
            var dLo = dUp * Quaternion.AngleAxis(-s * side.elbow, Up) * Quaternion.AngleAxis(side.foreTwist, Right);
            var dHandRel = Quaternion.AngleAxis(-s * side.wrist, Fwd) * Quaternion.AngleAxis(-s * side.wristSide, Up);
            if (side.armIK > 0.001f)
            {
                // the hand's target, carried by the bone it's relative to
                Vector3 target;
                int space = Mathf.RoundToInt(side.handSpace);
                if (space == 0) target = B(HumanBodyBones.Hips).position + dHips * (side.hand - _rest[HumanBodyBones.Hips].p);
                else if (space == 2) target = B(HumanBodyBones.Head).position + dHead * (side.hand - _rest[HumanBodyBones.Head].p);
                else if (space == 3) target = side.hand;                       // fixed in the world (a punch at what's in front)
                else target = B(HumanBodyBones.Chest).position + dChest * (side.hand - _rest[HumanBodyBones.Chest].p);
                // where the plain (forward-kinematics) arm would put the hand, and its elbow's hinge
                SetDelta(up, dUp);
                SetDelta(lo, dLo);
                var root = B(up).position;
                var fkHand = B(hand).position;
                var fkHinge = dUp * new Vector3(0f, -s, 0f);
                // blending: slide the hand from the plain arm's spot to the target and always solve the arm whole,
                // so the elbow bends the way an elbow does all the way through (never kinked backwards mid-blend)
                float w = Mathf.Clamp01(side.armIK);
                var aim = Vector3.Lerp(fkHand, target, w);
                var dir = (aim - root).normalized;
                var pole = dChest * new Vector3(s * side.elbowPole.x, side.elbowPole.y, side.elbowPole.z);
                var ikHinge = Vector3.Cross(pole, dir);
                if (ikHinge.sqrMagnitude < 1e-6f) ikHinge = fkHinge;
                var hinge = Vector3.Slerp(fkHinge.normalized, ikHinge.normalized, w);
                TwoBone(root, aim, hinge, upperArm, foreArm, _rest[up].p, _rest[lo].p, _rest[hand].p,
                    new Vector3(0f, -s, 0f), out dUp, out var ikLo);
                // the forearm's own twist rides on the solved arm
                dLo = ikLo * Quaternion.AngleAxis(side.foreTwist, Right);
            }
            SetDelta(up, dUp);
            SetDelta(lo, dLo);
            SetDelta(hand, dLo * dHandRel);
        }

        // ------------------------------------------------------------------ legs
        /// <summary>
        /// Where the ankle goes for this foot pose: the ground point moves (and lifts), the foot turns out, and
        /// it pitches about the heel (toes up) or the ball (heel up) so the contact point stays put.
        /// </summary>
        Vector3 AnkleTarget(Side side, int s, out Quaternion footDelta, out Vector3 groundPoint)
        {
            var a0 = AnkleRest(s);
            var g0 = new Vector3(a0.x, 0f, a0.z);
            var heel0 = g0 - Fwd * footBack;
            var b0 = BallRest(s);
            var ball0 = new Vector3(b0.x, 0f, b0.z);
            var yaw = Quaternion.AngleAxis(s * side.footYaw, Up);
            var pitch = Quaternion.AngleAxis(-side.footPitch, Right);
            var roll = Quaternion.AngleAxis(s * side.footRoll, Fwd);
            footDelta = yaw * pitch * roll;
            groundPoint = g0 + side.foot;
            var pivot0 = side.footPitch >= 0f ? heel0 : ball0;
            var pivot = groundPoint + yaw * (pivot0 - g0);
            var rolled = pivot + footDelta * (a0 - pivot0);
            // a foot up in the air just follows its path (the ankle over the ground point); near the ground it is
            // still rolling off (or onto) the ball and heel
            float air = Mathf.Clamp01(side.footFree) * Mathf.SmoothStep(0f, 1f, side.foot.y / 0.14f);
            return air > 0f ? Vector3.Lerp(rolled, groundPoint + (a0 - g0), air) : rolled;
        }

        void Leg(Pose p, int s, Quaternion dHips)
        {
            var side = p[s];
            bool left = s < 0;
            var up = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            var lo = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            var toes = left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;

            var ankle = AnkleTarget(side, s, out var footDelta, out _);
            // the knee hinges about the hips' side-to-side axis, turned with the foot (and outward by kneeOut):
            // a hinge, not a pole, so a knee driven up high never flips over
            var hinge = Quaternion.AngleAxis(s * (side.footYaw + side.kneeOut), Up) * Quaternion.Slerp(Quaternion.identity, dHips, 0.5f) * Right;
            var root = B(up).position;
            TwoBone(root, ankle, hinge, thigh, shin, _rest[up].p, _rest[lo].p, _rest[foot].p, Right, out var dUp, out var dLo);
            SetDelta(up, dUp);
            SetDelta(lo, dLo);
            // a foot on the ground is set square to the world; a foot in the air hangs off the shin (footFree)
            if (side.footFree > 0.001f)
            {
                var free = dLo * Quaternion.AngleAxis(-side.footRel, Right) * Quaternion.AngleAxis(s * side.footYaw * 0.5f, Up);
                footDelta = Quaternion.Slerp(footDelta, free, side.footFree);
                // a pointed foot just off the ground mustn't stab the toes into it: tip it up until they clear
                var ankleNow = B(foot).position;
                var toeVec = footDelta * (BallRest(s) + Fwd * footBack * 1.2f - AnkleRest(s));
                float tipY = ankleNow.y + toeVec.y;
                if (p.autoHip > 0f && tipY < 0.005f && toeVec.magnitude > 1e-3f)   // (only on the ground: not mid-jump)
                {
                    float lift = Mathf.Min(35f, Mathf.Asin(Mathf.Clamp((0.005f - tipY) / toeVec.magnitude, 0f, 1f)) * Mathf.Rad2Deg);
                    var axis = Vector3.Cross(Up, toeVec).normalized;           // tips the toes upward about the ankle
                    if (axis.sqrMagnitude > 0.5f) footDelta = Quaternion.AngleAxis(-lift, axis) * footDelta;
                }
            }
            SetDelta(foot, footDelta);
            // toes: flat on the floor while the heel is up on a planted foot (easing off as it lifts), plus any authored bend
            float onFloor = 1f - Mathf.SmoothStep(0f, 1f, side.foot.y / 0.05f);
            float toeUp = Mathf.Min(48f, side.toe + (side.footPitch < 0f ? -side.footPitch * onFloor : 0f));
            SetDelta(toes, footDelta * Quaternion.AngleAxis(-toeUp, Right));

            var heel = B(foot).position + (footDelta * (new Vector3(0, -AnkleRest(s).y, -footBack)));
            var tip = B(toes).position + footDelta * Quaternion.AngleAxis(-toeUp, Right) * (Fwd * footBack * 1.2f + Vector3.down * BallRest(s).y);
            if (heel.y < LowestFoot) { LowestFoot = heel.y; LowestWhat = (left ? "L" : "R") + " heel"; }
            if (tip.y < LowestFoot) { LowestFoot = tip.y; LowestWhat = (left ? "L" : "R") + " toe"; }
        }

        /// <summary>
        /// Two-bone IK: root at <paramref name="root"/>, end reaching for <paramref name="target"/>, the middle joint
        /// bending about <paramref name="hinge"/> (the joint folds the end bone toward cross(dir, hinge)). Returns the
        /// two bones' rotation deltas from their rest poses; restHinge is the hinge axis of the straight rest limb.
        /// </summary>
        static void TwoBone(Vector3 root, Vector3 target, Vector3 hinge, float l1, float l2,
            Vector3 r0, Vector3 m0, Vector3 e0, Vector3 restHinge, out Quaternion dRoot, out Quaternion dMid)
        {
            var toT = target - root;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * 0.9995f);
            var dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : Vector3.down;
            var n = hinge - Vector3.Dot(hinge, dir) * dir;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.Cross(Vector3.forward, dir));
            n.Normalize();
            var perp = Vector3.Cross(dir, n);
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            var knee = root + dir * a + perp * h;
            var end = root + dir * d;

            var u0 = (m0 - r0).normalized;
            var w0 = (e0 - m0).normalized;
            var nu = (restHinge - Vector3.Dot(restHinge, u0) * u0).normalized;
            var nw = (restHinge - Vector3.Dot(restHinge, w0) * w0).normalized;
            var u1 = (knee - root).normalized;
            var w1 = (end - knee).normalized;
            dRoot = Quaternion.LookRotation(u1, n) * Quaternion.Inverse(Quaternion.LookRotation(u0, nu));
            dMid = Quaternion.LookRotation(w1, n) * Quaternion.Inverse(Quaternion.LookRotation(w0, nw));
        }

        // ------------------------------------------------------------------ read back
        public HumanPose Capture()
        {
            var hp = new HumanPose();
            _handler.GetHumanPose(ref hp);
            return hp;
        }

        /// <summary>World positions of the main joints, for round-trip checks.</summary>
        public Dictionary<HumanBodyBones, Vector3> Joints()
        {
            var d = new Dictionary<HumanBodyBones, Vector3>();
            foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                         HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerArm })
                d[b] = B(b).position;
            return d;
        }
    }
}
