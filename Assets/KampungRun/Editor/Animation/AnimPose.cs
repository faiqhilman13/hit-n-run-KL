using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// One side of the body (arm and leg). Angles are degrees, distances metres, in body space
    /// (x right, y up, z forward). Mirrored channels mean the same thing on either side: "out" is away
    /// from the body, "twist" is inward rotation, so a symmetric pose is written once.
    /// </summary>
    public class Side
    {
        // collarbone: raise the shoulder, pull it forward
        public float shrug, protract;
        // arm, forward kinematics from the T-pose: lower it to the side, swing it forward (flexion, so a
        // hanging arm swings forward and then up), twist it inward; bend the elbow; droop the wrist
        public float armDown = 78f, armFwd, armTwist, elbow = 12f, foreTwist, wrist = 8f, wristSide;
        // arm, inverse kinematics (blended in by armIK): the hand's target in body space (handSpace says
        // which bone carries it: 0 hips, 1 chest, 2 head, 3 nothing - fixed in body space) and the way the elbow points
        public float armIK;
        public Vector3 hand;
        public float handSpace = 1f;
        public Vector3 elbowPole = new Vector3(0.4f, -0.4f, -1f);
        // leg, inverse kinematics: where the foot's ground point sits relative to its rest spot (y lifts it),
        // pitch (+ toes up, rolling on the heel; - heel up, rolling on the ball), toe-out, the toes' bend
        // (+ up) and how far the knee points outward
        public Vector3 foot;
        public float footPitch, footYaw, footRoll, toe, kneeOut;
        // a foot in the air hangs off its shin instead (footFree 0..1), pointed by footRel (+ toes up)
        public float footFree, footRel = -12f;
    }

    /// <summary>A whole-body pose. Spine-type rotations: pitch + bends forward, yaw + turns right, roll + leans right.</summary>
    public class Pose
    {
        public Vector3 hips;
        public float hipPitch, hipYaw, hipRoll;
        public float spinePitch, spineYaw, spineRoll;
        public float chestPitch, chestYaw, chestRoll;
        public float neckPitch, neckYaw, neckRoll;
        public float headPitch, headYaw, headRoll;
        public Side L = new Side(), R = new Side();
        /// <summary>1: lower the hips so a planted foot can always reach the ground (0 = leave them).</summary>
        public float autoHip = 1f;

        /// <summary>s = -1 left, +1 right.</summary>
        public Side this[int s] => s < 0 ? L : R;

        // ------------------------------------------------------------------ arithmetic on every channel
        static readonly FieldInfo[] PoseFloats = typeof(Pose).GetFields().Where(f => f.FieldType == typeof(float)).ToArray();
        static readonly FieldInfo[] PoseVecs = typeof(Pose).GetFields().Where(f => f.FieldType == typeof(Vector3)).ToArray();
        static readonly FieldInfo[] SideFloats = typeof(Side).GetFields().Where(f => f.FieldType == typeof(float)).ToArray();
        static readonly FieldInfo[] SideVecs = typeof(Side).GetFields().Where(f => f.FieldType == typeof(Vector3)).ToArray();

        public Pose Clone() => Combine((this, 1f));

        /// <summary>Weighted sum of poses (weights needn't add to one: Hermite blends use this).</summary>
        public static Pose Combine(params (Pose p, float w)[] terms)
        {
            var r = new Pose();
            foreach (var f in PoseFloats) f.SetValue(r, terms.Sum(t => (float)f.GetValue(t.p) * t.w));
            foreach (var f in PoseVecs)
            {
                var v = Vector3.zero;
                foreach (var t in terms) v += (Vector3)f.GetValue(t.p) * t.w;
                f.SetValue(r, v);
            }
            CombineSide(r.L, terms.Select(t => (t.p.L, t.w)).ToArray());
            CombineSide(r.R, terms.Select(t => (t.p.R, t.w)).ToArray());
            return r;
        }

        static void CombineSide(Side r, (Side s, float w)[] terms)
        {
            foreach (var f in SideFloats) f.SetValue(r, terms.Sum(t => (float)f.GetValue(t.s) * t.w));
            foreach (var f in SideVecs)
            {
                var v = Vector3.zero;
                foreach (var t in terms) v += (Vector3)f.GetValue(t.s) * t.w;
                f.SetValue(r, v);
            }
        }

        public static Pose Lerp(Pose a, Pose b, float t) => Combine((a, 1f - t), (b, t));

        /// <summary>Add the difference (b - a) of another pair on top of this pose (layering a gesture).</summary>
        public Pose Plus(Pose b, Pose a, float w = 1f) => Combine((this, 1f), (b, w), (a, -w));
    }

    /// <summary>
    /// Key poses through time, the way an animator sets them: extremes hold (they ease in and out),
    /// breakdowns pass through (smooth Catmull-Rom tangents). Times are 0..1 of the clip.
    /// </summary>
    public class Keys
    {
        readonly List<(float t, Pose p, bool hold)> _k = new List<(float, Pose, bool)>();
        readonly bool _loop;
        public Keys(bool loop = false) { _loop = loop; }

        public Keys Hold(float t, Pose p) { _k.Add((t, p, true)); return this; }
        public Keys Pass(float t, Pose p) { _k.Add((t, p, false)); return this; }

        public Pose At(float t)
        {
            var k = _k;
            int n = k.Count;
            if (n == 1) return k[0].p.Clone();
            if (_loop) t = Mathf.Repeat(t, 1f);
            if (!_loop && t <= k[0].t) return k[0].p.Clone();
            if (!_loop && t >= k[n - 1].t) return k[n - 1].p.Clone();
            int i = 0;
            if (_loop && (t < k[0].t || t >= k[n - 1].t)) i = n - 1;
            else while (i < n - 2 && t >= k[i + 1].t) i++;
            int j = (i + 1) % n;
            float t0 = k[i].t, t1 = k[j].t;
            if (j == 0) t1 += 1f;
            float tt = t < t0 ? t + 1f : t;
            float span = Mathf.Max(1e-5f, t1 - t0);
            float u = Mathf.Clamp01((tt - t0) / span);
            var m0 = Tangent(i, span);
            var m1 = Tangent(j, span);
            // cubic Hermite through the two keys with their tangents (scaled to this span)
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
            var terms = new List<(Pose, float)> { (k[i].p, h00), (k[j].p, h01) };
            if (m0.w != 0) { terms.Add((m0.b, h10 * m0.w)); terms.Add((m0.a, -h10 * m0.w)); }
            if (m1.w != 0) { terms.Add((m1.b, h11 * m1.w)); terms.Add((m1.a, -h11 * m1.w)); }
            return Pose.Combine(terms.ToArray());
        }

        /// <summary>The tangent at key i as (b - a) * w for this span: zero at a hold, Catmull-Rom otherwise.</summary>
        (Pose a, Pose b, float w) Tangent(int i, float span)
        {
            var k = _k;
            int n = k.Count;
            if (k[i].hold) return (null, null, 0f);
            int prev = i - 1, next = i + 1;
            float tp, tn;
            if (_loop)
            {
                prev = (i - 1 + n) % n; next = (i + 1) % n;
                tp = k[prev].t - (i == 0 ? 1f : 0f);
                tn = k[next].t + (i == n - 1 ? 1f : 0f);
            }
            else
            {
                if (prev < 0 || next >= n) return (null, null, 0f);
                tp = k[prev].t; tn = k[next].t;
            }
            float dt = Mathf.Max(1e-5f, tn - tp);
            return (k[prev].p, k[next].p, span / dt);
        }
    }

    /// <summary>A clip to bake: a pose for each moment (0..1 of its length).</summary>
    public class ClipDef
    {
        public string name;
        public float length;
        public bool loop;
        public Func<float, Pose> pose;
        /// <summary>How fast the body travels when the clip plays at its own pace (m/s, reference character).</summary>
        public float speed;
        public ClipDef(string name, float length, bool loop, Func<float, Pose> pose, float speed = 0f)
        { this.name = name; this.length = length; this.loop = loop; this.pose = pose; this.speed = speed; }
    }
}
