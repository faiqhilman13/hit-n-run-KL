using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// The cast's motion library, written as poses through time. Locomotion comes from one gait generator
    /// (planted feet that roll heel to toe, swing feet on smooth arcs, hips that bob, sway and swivel,
    /// shoulders that counter the hips, arms and wrists that trail behind, a head that keeps looking ahead);
    /// actions are key poses with holds and breakdowns. All cycles start on the left heel strike.
    /// </summary>
    public static partial class AnimLibrary
    {
        static AnimRig R;
        const float Tau = Mathf.PI * 2f;
        static readonly int[] Sides = { -1, 1 };

        public static List<ClipDef> Build(AnimRig rig)
        {
            R = rig;
            var list = new List<ClipDef>
            {
                new ClipDef("idle", 4f, true, Idle),
                Gait("walk", Walk),
                Gait("jog", Jog),
                Gait("run", Run),
                Gait("sprint", Sprint),
            };
            list.AddRange(Actions());
            list.AddRange(StyleClips());
            return list;
        }

        // ================================================================== helpers
        static float Frac(float x) => x - Mathf.Floor(x);
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static float Cos(float cycles) => Mathf.Cos(Tau * cycles);
        static float Sin(float cycles) => Mathf.Sin(Tau * cycles);
        static float Pos(float x) => Mathf.Max(0f, x);

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>A relaxed standing pose: feet a little apart and turned out, arms hanging, soft elbows.</summary>
        static Pose Stand(float width = 0.02f)
        {
            var p = new Pose();
            foreach (int s in Sides)
            {
                var d = p[s];
                d.foot = new Vector3(s * width, 0f, 0f);
                d.footYaw = 9f;
                d.kneeOut = 4f;
                d.armDown = 80f;
                d.armFwd = 3f;
                d.elbow = 14f;
                d.wrist = 10f;
                d.armTwist = 8f;
            }
            p.hips.y = -0.012f;
            return p;
        }

        // ================================================================== idle
        /// <summary>
        /// Standing about: the weight drifts from foot to foot (the hips slide over the planted feet and the
        /// free knee softens), breathing lifts the chest and shoulders, the arms hang and swing a touch behind
        /// the body, the head wanders.
        /// </summary>
        static Pose Idle(float u)
        {
            var p = Stand();
            float shift = Sin(u);                       // one slow weight shift per loop (4 s)
            float breath = Sin(u * 2f + 0.1f);          // two breaths
            p.hips.x = 0.032f * shift;
            p.hips.y = -0.018f - 0.006f * Mathf.Abs(shift) + 0.003f * breath;
            p.hipRoll = -3.2f * shift;                  // the loaded hip rises
            p.hipYaw = 2.5f * Sin(u + 0.15f);
            p.spineRoll = 2.2f * shift;                 // the spine curves back over the feet
            p.chestRoll = 1.4f * Sin(u - 0.06f);
            p.spinePitch = 2f + 0.8f * breath;
            p.chestPitch = -1.5f * breath;              // the chest lifts on the in-breath
            p.chestYaw = -1.5f * Sin(u + 0.1f);
            p.neckPitch = -1f;
            p.headPitch = -1.5f + 1.6f * Sin(u * 2f + 0.35f);
            p.headYaw = 9f * Sin(u + 0.3f) + 3f * Sin(u * 3f);
            p.headRoll = -2.5f * Sin(u - 0.05f) + 1.5f * shift;
            foreach (int s in Sides)
            {
                var d = p[s];
                // the free leg's knee softens as the weight moves off it
                float free = Pos(-s * shift);
                d.footPitch = 0f;
                d.kneeOut = 4f + 6f * free;
                d.shrug = 1.6f * Pos(breath);
                d.armFwd = 3f + 3f * Sin(u - 0.12f) * s * 0.3f + 1.5f * Sin(u * 2f - 0.2f);
                d.armDown = 80f - 2.5f * Pos(breath) + 2f * s * Sin(u - 0.1f);
                d.elbow = 14f + 4f * Sin(u * 2f - 0.3f);
                d.wrist = 10f + 6f * Sin(u * 2f - 0.45f);
            }
            return p;
        }

        // ================================================================== locomotion
        /// <summary>Everything that shapes a walk or a run.</summary>
        public class GaitSpec
        {
            public float speed, cycle, duty;
            public float swingBack = 0.10f, swingFront = 0.08f;   // heights of the swing path's control points behind / in front
            public float strike = 16f, toeOff = 34f, swingToe = 0f; // foot pitch at heel strike, at toe-off, extra toes-down mid-swing
            public float rollIn = 0.18f, peel = 0.5f;              // stance fraction to roll flat / where the heel starts to peel
            public float width = 0.0f, toeOut = 8f, kneeOut = 3f;
            public float stanceShift = -0.04f;                     // stance centre relative to the hips (+ ahead)
            public float height = 0f, bob = 0.012f, bobPhase = 0.3f;
            public float sway = 0.018f;
            public float pelvisYaw = 7f, pelvisRoll = 4f, pelvisTilt = 3f;
            public float lean = 4f, twist = 1.3f, sideBend = 0.7f, chestDip = 1.5f;
            public float armSwing = 24f, armMid = 3f, armDown = 76f, armIn = 6f, armLag = 0.05f;
            public float elbow = 16f, elbowSwing = 26f, wrist = 12f, wristLag = 12f;
            public float shrug = 1.5f, protract = 7f;
            public float headBob = 2.5f, headCounter = 0.85f, headLag = 0.06f, headDown = 0f;
            public float relOff = -25f, relOn = 6f;                 // swing foot's pitch against the shin: after the push / before landing
            public float free = 1f, maxReach = 0.3f;                // how loosely the swing foot hangs; longest swing handle (m)
            public float waddle = 0f, handsBehind = 0f;             // shoulders rocking over each foot (deg); hands clasped at the back
            // running swing path (used when rearZ < 0): after the push the foot trails out behind (rearZ, rearLift),
            // the heel folds up under the seat (tuckZ, swingBack high), the knee drives through (driveZ, swingFront),
            // the foot reaches out in front (frontReach past the landing spot, reachLift) and paws back down
            public float rearZ = 0f, rearLift = 0.08f, tuckZ = -0.12f, driveZ = 0.1f, frontReach = 0.08f, reachLift = 0.1f;
            public float rearT = 0.2f, tuckT = 0.42f, driveT = 0.66f, reachT = 0.85f;
        }

        /// <summary>
        /// A runner's swing, through its key positions (z ahead of the hips, y up) with Catmull-Rom tangents: the
        /// path leaves the ground carrying on backwards (the push) and lands moving backwards (the paw).
        /// </summary>
        static Vector2 RunSwing(GaitSpec g, float zOn, float zOff, float swingDur, float sN)
        {
            float lead = g.speed * swingDur * 0.08f;
            var t = new[] { -0.08f, 0f, g.rearT, g.tuckT, g.driveT, g.reachT, 1f, 1.08f };
            var pt = new[]
            {
                new Vector2(zOff + lead, 0f),                       // (where it was a moment before toe-off)
                new Vector2(zOff, 0f),
                new Vector2(g.rearZ, g.rearLift),                   // trailing out behind: the push-off leg long
                new Vector2(g.tuckZ, g.swingBack),                  // heel up under the seat
                new Vector2(g.driveZ, g.swingFront),                // knee driving through
                new Vector2(zOn + g.frontReach, g.reachLift),       // reaching out in front
                new Vector2(zOn, 0f),                               // pawing back down onto the ground
                new Vector2(zOn - lead, 0f),
            };
            int i = 1;
            while (i < 5 && sN > t[i + 1]) i++;
            float h = t[i + 1] - t[i];
            float u = Mathf.Clamp01((sN - t[i]) / h);
            var m0 = (pt[i + 1] - pt[i - 1]) / (t[i + 1] - t[i - 1]) * h;
            var m1 = (pt[i + 2] - pt[i]) / (t[i + 2] - t[i]) * h;
            float u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * pt[i] + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * pt[i + 1] + (u3 - u2) * m1;
        }

        static ClipDef Gait(string name, GaitSpec g) =>
            new ClipDef(name, g.cycle, true, u => GaitPose(g, u), g.speed);

        static ClipDef Gait(string name, System.Func<GaitSpec> spec) => Gait(name, spec());

        public static Pose GaitPose(GaitSpec g, float u)
        {
            var p = new Pose();
            float v = g.speed, T = g.cycle;
            float stanceLen = v * g.duty * T;
            float zOn = stanceLen * 0.5f + g.stanceShift, zOff = -stanceLen * 0.5f + g.stanceShift;
            float swingDur = (1f - g.duty) * T;
            // Bezier handles that leave and land at the stance speed (short of that at a sprint, where the foot
            // flicks up behind rather than trailing a metre back)
            float reach = Mathf.Min(v * swingDur / 3f, g.maxReach);

            foreach (int s in Sides)
            {
                var d = p[s];
                float ph = Frac(u + (s < 0 ? 0f : 0.5f));          // this foot's phase: 0 = its heel strike
                float z, y, pitch, toe = 0f;
                if (ph < g.duty)
                {
                    // planted: it slides back under the moving body, rolling heel -> flat -> ball
                    float k = ph / g.duty;
                    z = Mathf.Lerp(zOn, zOff, k);
                    y = 0f;
                    if (k < g.rollIn) pitch = g.strike * (1f - Smooth(k / g.rollIn));
                    else if (k > g.peel) { float q = (k - g.peel) / (1f - g.peel); pitch = -g.toeOff * q * q * (1.6f - 0.6f * q); }
                    else pitch = 0f;
                }
                else
                {
                    // swinging through on an arc that leaves and lands at the stance speed
                    float sN = (ph - g.duty) / (1f - g.duty);
                    var b = g.rearZ < 0f ? RunSwing(g, zOn, zOff, swingDur, sN)
                        : Bezier(new Vector2(zOff, 0f), new Vector2(zOff - reach, g.swingBack),
                            new Vector2(zOn + reach, g.swingFront), new Vector2(zOn, 0f), sN);
                    z = b.x;
                    y = Mathf.Max(0f, b.y);
                    pitch = Mathf.Lerp(-g.toeOff, g.strike, Smooth((sN - 0.05f) / 0.75f)) - g.swingToe * Mathf.Sin(Mathf.PI * sN);
                    // in the air the foot hangs off the shin, pointed after the push and flexed for the landing
                    d.footFree = g.free * Smooth(sN / 0.18f) * (1f - Smooth((sN - 0.72f) / 0.24f));
                    d.footRel = Mathf.Lerp(g.relOff, g.relOn, Smooth((sN - 0.15f) / 0.6f));
                }
                d.foot = new Vector3(s * g.width, y, z);
                d.footPitch = pitch;
                d.footYaw = g.toeOut;
                d.kneeOut = g.kneeOut;
                d.toe = toe;
            }

            // hips: bob per step, over the stance foot, the forward leg's hip leads, the swing side drops
            float step = Frac(u * 2f);
            p.hips.y = g.height + g.bob * Cos(step - g.bobPhase);
            // a foot in the air rides up with the body as it flies (otherwise the legs dangle at the top of each bound)
            float rise = Mathf.Max(0f, g.bob * Cos(step - g.bobPhase));
            foreach (int s in Sides)
            {
                float ph = Frac(u + (s < 0 ? 0f : 0.5f));
                if (ph > g.duty) p[s].foot.y += rise * Mathf.Sin(Mathf.PI * (ph - g.duty) / (1f - g.duty));
            }
            p.hips.x = -g.sway * Cos(u - g.duty * 0.5f);
            p.hipYaw = g.pelvisYaw * Cos(u);
            p.hipRoll = g.pelvisRoll * Sin(u - 0.02f);
            p.hipPitch = g.pelvisTilt;

            // torso: lean into the pace, shoulders turn against the hips, the spine bends back over the hips
            float chestYawAbs = -g.twist * p.hipYaw;
            float yawRel = chestYawAbs - p.hipYaw;
            p.spineYaw = yawRel * 0.45f;
            p.chestYaw = yawRel * 0.55f;
            float dip = g.chestDip * Cos(step - g.bobPhase - 0.12f);     // the chest nods on each landing, a beat late
            p.spinePitch = g.lean * 0.45f - dip * 0.4f;
            p.chestPitch = g.lean * 0.55f - dip * 0.6f;
            p.spineRoll = -p.hipRoll * g.sideBend * 0.6f;
            p.chestRoll = -p.hipRoll * g.sideBend * 0.45f;
            // a waddle rocks the shoulders out over whichever foot is down
            float over = -Cos(u - g.duty * 0.5f);
            p.spineRoll += g.waddle * 0.5f * over;
            p.chestRoll += g.waddle * 0.5f * over;

            // head: holds its gaze forward against the twist and the lean, bobbing a touch behind the body
            float tilt = g.pelvisTilt + g.lean;
            p.neckYaw = -chestYawAbs * g.headCounter * 0.4f;
            p.headYaw = -chestYawAbs * g.headCounter * 0.6f;
            p.neckPitch = -tilt * 0.35f + g.headDown * 0.4f;
            p.headPitch = -tilt * 0.45f + g.headDown * 0.6f + g.headBob * Cos(step - g.bobPhase - g.headLag - 0.25f);
            p.headRoll = -(p.hipRoll + p.spineRoll + p.chestRoll) * 0.6f;

            // arms: swing with the opposite leg, a little behind it; elbows fold on the way forward, wrists trail
            foreach (int s in Sides)
            {
                var d = p[s];
                float ph = u - (s > 0 ? 0f : 0.5f) - g.armLag;           // right arm forward with the left leg (u = 0)
                float a = Cos(ph);
                d.armFwd = g.armMid + g.armSwing * a;
                d.armDown = g.armDown - 4f * Pos(a);
                d.armTwist = 6f + g.armIn * Pos(a);
                d.elbow = g.elbow + g.elbowSwing * Pos(Cos(ph - 0.05f));
                d.wrist = g.wrist + g.wristLag * Cos(ph - 0.12f);
                d.protract = g.protract * a;
                d.shrug = g.shrug * Cos(step - g.bobPhase - 0.15f);
                if (g.handsBehind > 0f) HandsBehind(d, s, g.handsBehind);
            }
            return p;
        }

        // The pace of each gait suits the reference character (chr_aiman: 0.52 m from hip to ankle, a short
        // cartoon leg). Contact covers about one leg length at every pace; the faster gaits get their distance
        // from longer flights, so a sprint is a string of bounds rather than a blur of legs. The game scales
        // these by each character's size, so a child takes quicker steps at the same speed.
        static GaitSpec Walk() => new GaitSpec
        {
            speed = 1.15f, cycle = 0.78f, duty = 0.58f,
            swingBack = 0.14f, swingFront = 0.07f, strike = 18f, toeOff = 46f, rollIn = 0.16f, peel = 0.44f,
            width = 0.005f, toeOut = 9f, stanceShift = -0.035f,
            height = -0.012f, bob = 0.02f, bobPhase = 0.58f, sway = 0.024f,
            pelvisYaw = 11f, pelvisRoll = 5.5f, pelvisTilt = 2f,
            lean = 3f, twist = 1.6f, sideBend = 0.8f, chestDip = 2f,
            armSwing = 34f, armMid = 6f, armDown = 76f, armIn = 9f, armLag = 0.06f,
            elbow = 14f, elbowSwing = 36f, wrist = 14f, wristLag = 16f, shrug = 2f, protract = 10f,
            headBob = 3.4f, headCounter = 0.85f, free = 0.45f, relOff = -12f, relOn = 14f,
        };

        static GaitSpec Jog() => new GaitSpec
        {
            speed = 3.0f, cycle = 0.62f, duty = 0.27f,
            swingBack = 0.22f, swingFront = 0.15f, strike = 10f, toeOff = 46f, swingToe = 10f, rollIn = 0.2f, peel = 0.4f,
            width = 0.0f, toeOut = 6f, stanceShift = -0.05f,
            rearZ = -0.26f, rearLift = 0.1f, tuckZ = -0.15f, driveZ = 0.07f, frontReach = 0.05f, reachLift = 0.07f,
            height = -0.03f, bob = 0.03f, bobPhase = 0.75f, sway = 0.016f,
            pelvisYaw = 9f, pelvisRoll = 4f, pelvisTilt = 5f,
            lean = 9f, twist = 1.4f, sideBend = 0.6f, chestDip = 2.4f,
            armSwing = 38f, armMid = 8f, armDown = 72f, armIn = 14f, armLag = 0.05f,
            elbow = 70f, elbowSwing = 22f, wrist = 8f, wristLag = 10f, shrug = 2.5f, protract = 10f,
            headBob = 3f, headCounter = 0.85f,
        };

        static GaitSpec Run() => new GaitSpec
        {
            speed = 5.5f, cycle = 0.52f, duty = 0.2f,
            swingBack = 0.3f, swingFront = 0.26f, strike = 6f, toeOff = 56f, swingToe = 16f, rollIn = 0.25f, peel = 0.3f,
            width = -0.01f, toeOut = 5f,
            rearZ = -0.29f, rearLift = 0.17f, tuckZ = -0.16f, driveZ = 0.12f, frontReach = 0.08f, reachLift = 0.1f,
            height = -0.04f, bob = 0.05f, bobPhase = 0.67f, sway = 0.012f,
            pelvisYaw = 11f, pelvisRoll = 4f, pelvisTilt = 2f, stanceShift = -0.07f,
            lean = 22f, twist = 1.35f, sideBend = 0.5f, chestDip = 3f,
            armSwing = 58f, armMid = 10f, armDown = 70f, armIn = 18f, armLag = 0.04f,
            elbow = 88f, elbowSwing = 18f, wrist = 4f, wristLag = 8f, shrug = 3.5f, protract = 12f,
            headBob = 3.5f, headCounter = 0.8f,
        };

        static GaitSpec Sprint() => new GaitSpec
        {
            speed = 8.5f, cycle = 0.46f, duty = 0.17f,
            swingBack = 0.31f, swingFront = 0.3f, strike = 4f, toeOff = 58f, swingToe = 16f, rollIn = 0.3f, peel = 0.25f,
            width = -0.015f, toeOut = 4f, stanceShift = -0.09f,
            rearZ = -0.29f, rearLift = 0.21f, tuckZ = -0.17f, driveZ = 0.16f, frontReach = 0.1f, reachLift = 0.12f,
            height = -0.045f, bob = 0.06f, bobPhase = 0.63f, sway = 0.01f,
            pelvisYaw = 12f, pelvisRoll = 4f, pelvisTilt = 0f, maxReach = 0.12f,
            lean = 26f, twist = 1.3f, sideBend = 0.4f, chestDip = 3.5f,
            armSwing = 64f, armMid = 12f, armDown = 68f, armIn = 20f, armLag = 0.035f,
            elbow = 92f, elbowSwing = 14f, wrist = 2f, wristLag = 6f, shrug = 5f, protract = 14f,
            headBob = 3.5f, headCounter = 0.75f, headDown = 2f,
        };
    }
}
