using System.Collections.Generic;
using UnityEngine;

namespace KampungRun.EditorTools.Anim
{
    /// <summary>
    /// Actions, reactions and street life: jumps and air poses, the flip and the ground-pound, landings and
    /// skids, the three-hit combo and the punt, flinches and knockdowns, and the things people do while they
    /// stand about (wave, chat, cheer, shake a fist, cross their arms, film it on their phone, fan the satay,
    /// shade their eyes, wipe their brow...). Key poses with holds and breakdowns, the way an animator blocks
    /// a shot: strong extremes, quick snaps into them, overshoot and settle.
    /// </summary>
    public static partial class AnimLibrary
    {
        public static IEnumerable<ClipDef> Actions()
        {
            // ---------------------------------------------------------------- air (a blend by vertical speed)
            yield return new ClipDef("jump", 0.5f, true, AirRise);
            yield return new ClipDef("air_apex", 0.6f, true, AirApex);
            yield return new ClipDef("air_fall", 0.5f, true, AirFall);
            yield return new ClipDef("air_flail", 0.44f, true, AirFlail);
            yield return new ClipDef("flip", 0.42f, false, Flip().At);
            yield return new ClipDef("pound", 0.36f, false, Pound().At);
            yield return new ClipDef("pound_land", 0.5f, false, PoundLand().At);
            yield return new ClipDef("land", 0.42f, false, Land().At);
            yield return new ClipDef("skid", 0.3f, true, Skid);
            // ---------------------------------------------------------------- fighting
            yield return new ClipDef("punch", 0.34f, false, Jab().At);
            yield return new ClipDef("punch2", 0.36f, false, Cross().At);
            yield return new ClipDef("punch3", 0.56f, false, Uppercut().At);
            yield return new ClipDef("kick", 0.52f, false, Punt().At);
            yield return new ClipDef("hit", 0.46f, false, Flinch().At);
            yield return new ClipDef("knockdown", 2.2f, false, Knockdown().At);
            // ---------------------------------------------------------------- people
            yield return new ClipDef("wave", 1.0f, true, Wave);
            yield return new ClipDef("talk", 2.4f, true, Talk().At);
            yield return new ClipDef("talk2", 2.0f, true, Talk2);
            yield return new ClipDef("cheer", 0.7f, true, Cheer().At);
            yield return new ClipDef("angry", 1.0f, true, Angry);
            yield return new ClipDef("watch_cross", 3.0f, true, WatchCross);
            yield return new ClipDef("watch_hips", 3.0f, true, WatchHips);
            yield return new ClipDef("film", 2.0f, true, Film);
            yield return new ClipDef("point_laugh", 1.2f, true, PointLaugh);
            yield return new ClipDef("fan", 0.5f, true, Fan);
            yield return new ClipDef("panic", Run().cycle, true, u => Panic(u), Run().speed);
            // ---------------------------------------------------------------- fidgets (one-shots from idle)
            yield return new ClipDef("fidget_look", 2.4f, false, FidgetLook().At);
            yield return new ClipDef("fidget_stretch", 2.8f, false, FidgetStretch().At);
            yield return new ClipDef("fidget_wipe", 2.2f, false, FidgetWipe().At);
            yield return new ClipDef("fidget_scratch", 2.0f, false, FidgetScratch().At);
            yield return new ClipDef("fidget_watch", 2.2f, false, FidgetWatch().At);
        }

        // ================================================================== building blocks
        static Pose P(System.Action<Pose> f) { var p = Stand(); f(p); return p; }
        static Pose P(Pose from, System.Action<Pose> f) { var p = from.Clone(); f(p); return p; }
        static Pose Idle0 => Idle(0f);

        /// <summary>A point relative to the middle of the shoulder line (carried by the chest).</summary>
        static Vector3 Chest(float x, float y, float z) => new Vector3(0f, R.ShoulderRest(1).y, 0f) + new Vector3(x, y, z);
        static Vector3 Head(float x, float y, float z) => R.HeadRest + new Vector3(x, y, z);
        static Vector3 Hip(float x, float y, float z) => R.HipsRest + new Vector3(x, y, z);

        /// <summary>Put a hand on a point (space 0 hips, 1 chest, 2 head), the elbow pointing along pole.</summary>
        static void Reach(Side d, int space, Vector3 target, Vector3 pole, float w = 1f)
        {
            d.armIK = w;
            d.handSpace = space;
            d.hand = target;
            d.elbowPole = pole;
        }

        static void Arms(Pose p, float down, float fwd, float elbow, float twist = 8f, float wrist = 10f)
        {
            foreach (int s in Sides)
            {
                var d = p[s];
                d.armDown = down; d.armFwd = fwd; d.elbow = elbow; d.armTwist = twist; d.wrist = wrist;
            }
        }

        static void Feet(Pose p, float width, float lift, float zL, float zR, float pitch = 0f)
        {
            p.L.foot = new Vector3(-width, lift, zL);
            p.R.foot = new Vector3(width, lift, zR);
            p.L.footPitch = p.R.footPitch = pitch;
        }

        static void Airborne(Pose p)
        {
            p.autoHip = 0f;
            foreach (int s in Sides) { p[s].footFree = 1f; p[s].footYaw = 6f; }
        }

        // ================================================================== air
        // The air blend runs from the launch (rising fast) through the tuck at the top to reaching for the ground.
        static Pose AirRise(float u)
        {
            var p = Stand();
            Airborne(p);
            float k = Sin(u) * 0.5f;
            p.hips.y = 0.03f;
            p.hipPitch = -4f;
            p.spinePitch = -5f; p.chestPitch = -8f; p.neckPitch = -4f; p.headPitch = -12f;
            // lead knee driven up, the push leg trailing long and pointed
            p.L.foot = new Vector3(-0.01f, 0.24f + 0.02f * k, 0.14f + 0.02f * k); p.L.footRel = -25f;
            p.R.foot = new Vector3(0.02f, 0.06f, -0.13f - 0.02f * k); p.R.footRel = -48f;
            // one arm thrown up, the other swept back
            p.R.armDown = -48f + 4f * k; p.R.armFwd = 38f; p.R.elbow = 22f; p.R.wrist = 4f;
            p.L.armDown = 46f; p.L.armFwd = -38f - 4f * k; p.L.elbow = 34f; p.L.wrist = 18f;
            p.headRoll = 3f * k;
            return p;
        }

        static Pose AirApex(float u)
        {
            var p = Stand();
            Airborne(p);
            float k = Sin(u);
            p.hips.y = 0.04f + 0.005f * k;
            p.spinePitch = 6f; p.chestPitch = 5f; p.headPitch = -8f;
            // both knees up, arms out for balance
            p.L.foot = new Vector3(-0.03f, 0.3f, 0.07f + 0.015f * k); p.L.footRel = -30f;
            p.R.foot = new Vector3(0.03f, 0.25f, -0.02f - 0.015f * k); p.R.footRel = -35f;
            foreach (int s in Sides)
            {
                var d = p[s];
                d.armDown = 18f + 5f * k * s; d.armFwd = 28f; d.elbow = 48f; d.wrist = 18f; d.armTwist = 14f;
            }
            return p;
        }

        static Pose AirFall(float u)
        {
            var p = Stand();
            Airborne(p);
            float k = Sin(u * 2f);
            p.hips.y = 0.02f;
            p.spinePitch = 5f; p.chestPitch = 2f; p.neckPitch = 4f; p.headPitch = 8f;      // eyes on the landing
            // legs reaching down, one a little ahead; toes up for the touchdown
            p.L.foot = new Vector3(-0.02f, 0.07f, 0.1f); p.L.footRel = 4f; p.L.footFree = 0.7f;
            p.R.foot = new Vector3(0.03f, 0.11f, -0.05f); p.R.footRel = -6f; p.R.footFree = 0.7f;
            // arms up and out, flapping a little
            foreach (int s in Sides)
            {
                var d = p[s];
                d.armDown = -34f + 9f * Sin(u * 2f + (s > 0 ? 0.25f : 0f)); d.armFwd = 12f; d.elbow = 36f;
                d.wrist = 22f + 14f * Sin(u * 2f - 0.15f + (s > 0 ? 0.25f : 0f));
                d.shrug = 6f;
            }
            p.headRoll = 3f * k;
            return p;
        }

        /// <summary>A long drop: running on air, arms windmilling, head darting about.</summary>
        static Pose AirFlail(float u)
        {
            var p = Stand();
            Airborne(p);
            p.hips.y = 0.03f;
            p.spinePitch = -4f; p.chestPitch = -6f; p.headPitch = 6f;
            foreach (int s in Sides)
            {
                var d = p[s];
                float ph = u + (s > 0 ? 0.5f : 0f);
                d.foot = new Vector3(s * 0.03f, 0.18f + 0.1f * Sin(ph), 0.12f * Cos(ph));
                d.footRel = -20f + 15f * Cos(ph);
                float a = u * 2f + (s > 0 ? 0.25f : 0f);
                d.armDown = -18f + 34f * Sin(a);
                d.armFwd = 40f + 40f * Cos(a);
                d.elbow = 30f + 20f * Sin(a + 0.2f);
                d.wrist = 30f * Sin(a - 0.15f);
                d.shrug = 10f + 8f * Pos(-Sin(a));
            }
            p.headYaw = 22f * Sin(u * 2f);
            p.headRoll = 8f * Sin(u * 2f + 0.2f);
            p.chestRoll = 5f * Sin(u * 2f);
            return p;
        }

        /// <summary>A cannonball: knees to the chest, arms round the shins, head tucked.</summary>
        static Pose Tuck()
        {
            var p = Stand();
            Airborne(p);
            p.hips.y = 0.06f;
            p.spinePitch = 22f; p.chestPitch = 22f; p.neckPitch = 10f; p.headPitch = 18f;
            foreach (int s in Sides)
            {
                var d = p[s];
                d.foot = new Vector3(s * 0.01f, 0.36f, 0.1f);
                d.footRel = -35f;
                d.kneeOut = 10f;
                Reach(d, 0, Hip(s * 0.1f, 0.0f, 0.3f), new Vector3(1f, -0.2f, -0.3f));
                d.wrist = 20f;
            }
            return p;
        }

        static Keys Flip()
        {
            var tuck = Tuck();
            var open = AirApex(0f);
            return new Keys()
                .Pass(0f, AirRise(0f))
                .Hold(0.14f, tuck)
                .Hold(0.72f, P(tuck, p => { p.spinePitch = 26f; p.headPitch = 22f; }))
                .Hold(1f, open);
        }

        static Keys Pound()
        {
            var tuck = Tuck();
            var drop = P(p =>
            {
                Airborne(p);
                p.hips.y = 0.0f;
                p.spinePitch = -6f; p.chestPitch = -8f; p.neckPitch = 6f; p.headPitch = 16f;
                foreach (int s in Sides)
                {
                    var d = p[s];
                    d.foot = new Vector3(-s * 0.01f, 0.02f, 0.02f);
                    d.footRel = -30f;
                    d.armDown = -54f; d.armFwd = 20f; d.elbow = 12f; d.wrist = 0f; d.shrug = 22f;
                }
            });
            return new Keys().Pass(0f, AirApex(0f)).Hold(0.25f, tuck).Hold(0.55f, P(tuck, p => p.spinePitch = 28f)).Hold(0.75f, drop).Hold(1f, drop);
        }

        /// <summary>The slam: a crouch with both fists in the dirt, then springing back up.</summary>
        static Keys PoundLand()
        {
            var slam = P(p =>
            {
                p.hips.y = -0.2f;
                Feet(p, 0.07f, 0f, 0.06f, -0.06f, -14f);
                foreach (int s in Sides) { p[s].kneeOut = 16f; p[s].footYaw = 18f; }
                p.spinePitch = 26f; p.chestPitch = 12f; p.neckPitch = -10f; p.headPitch = -16f;
                foreach (int s in Sides) Reach(p[s], 0, Hip(s * 0.16f, -0.52f, 0.26f), new Vector3(1f, 0.2f, -0.6f));
            });
            return new Keys()
                .Hold(0f, slam)
                .Hold(0.3f, P(slam, p => { p.hips.y = -0.21f; p.spinePitch = 28f; }))
                .Pass(0.62f, P(p => { p.hips.y = -0.04f; Feet(p, 0.05f, 0f, 0.02f, -0.02f); p.spinePitch = 4f; Arms(p, 40f, 30f, 30f); }))
                .Hold(1f, AirRise(0f));
        }

        static Keys Land()
        {
            var absorb = P(p =>
            {
                p.hips.y = -0.15f;
                Feet(p, 0.045f, 0f, 0.04f, -0.03f, -6f);
                foreach (int s in Sides) { p[s].kneeOut = 10f; p[s].footYaw = 12f; }
                p.spinePitch = 18f; p.chestPitch = 8f; p.neckPitch = -6f; p.headPitch = -10f;
                Arms(p, 58f, 36f, 34f, 12f, 24f);
            });
            return new Keys()
                .Pass(0f, P(p => { p.hips.y = -0.03f; Arms(p, 10f, 20f, 40f); p.headPitch = 6f; }))
                .Hold(0.16f, absorb)
                .Pass(0.45f, P(absorb, p => { p.hips.y = -0.05f; p.spinePitch = 6f; p.chestPitch = 2f; Arms(p, 74f, 10f, 20f, 8f, 14f); }))
                .Hold(1f, Idle0);
        }

        /// <summary>Braking hard: sat back, the front heel digging in, arms flung back, the whole body shuddering.</summary>
        static Pose Skid(float u)
        {
            var p = Stand();
            float shake = Sin(u * 2f);
            p.hips = new Vector3(0f, -0.06f + 0.006f * shake, -0.07f);
            p.hipPitch = -8f;
            p.spinePitch = -14f + 1.5f * shake; p.chestPitch = -10f; p.neckPitch = 8f; p.headPitch = 10f;
            p.R.foot = new Vector3(0.03f, 0f, 0.27f); p.R.footPitch = 26f; p.R.kneeOut = 2f;
            p.L.foot = new Vector3(-0.02f, 0f, -0.1f); p.L.footPitch = -22f;
            foreach (int s in Sides)
            {
                var d = p[s];
                d.armDown = 22f + 6f * Sin(u * 2f + (s > 0 ? 0.3f : 0f)); d.armFwd = -48f; d.elbow = 34f; d.wrist = 34f; d.shrug = 8f;
            }
            p.headRoll = 2f * shake;
            return p;
        }

        // ================================================================== fighting
        static Pose Guard()
        {
            var p = Stand(0.05f);
            p.hips.y = -0.045f;
            p.L.foot = new Vector3(-0.05f, 0f, 0.08f); p.L.footYaw = 8f;
            p.R.foot = new Vector3(0.05f, 0f, -0.07f); p.R.footYaw = 26f;
            foreach (int s in Sides) p[s].kneeOut = 8f;
            p.hipYaw = 12f; p.spineYaw = 4f; p.chestYaw = 4f; p.spinePitch = 6f; p.chestPitch = 3f;
            p.neckYaw = -8f; p.headYaw = -10f; p.headPitch = -4f;
            p.L.armDown = 56f; p.L.armFwd = 58f; p.L.elbow = 116f; p.L.armTwist = 20f; p.L.wrist = 0f;
            p.R.armDown = 60f; p.R.armFwd = 50f; p.R.elbow = 124f; p.R.armTwist = 24f; p.R.wrist = 0f;
            return p;
        }

        static Keys Jab()
        {
            var g = Guard();
            var hit = P(g, p =>
            {
                p.hips.z = 0.04f;
                p.L.foot = new Vector3(-0.05f, 0f, 0.14f);
                p.hipYaw = 18f; p.chestYaw = 12f; p.spineYaw = 6f; p.spinePitch = 9f;
                p.headYaw = -16f; p.neckYaw = -12f;
                Reach(p.L, 3, new Vector3(-0.05f, R.ShoulderRest(1).y + 0.02f, 0.5f), new Vector3(1f, -0.6f, -0.2f));
                p.L.wrist = -6f; p.L.shrug = 8f; p.L.protract = 12f;
                p.R.elbow = 130f; p.R.armFwd = 44f;
            });
            return new Keys()
                .Pass(0f, g)
                .Pass(0.08f, P(g, p => { p.chestYaw = 0f; p.L.elbow = 124f; }))
                .Hold(0.22f, hit)
                .Pass(0.42f, P(hit, p => { p.L.hand += new Vector3(0, 0, -0.03f); }))
                .Hold(1f, g);
        }

        static Keys Cross()
        {
            var g = Guard();
            var hit = P(g, p =>
            {
                p.hips.z = 0.06f;
                p.hipYaw = -14f; p.spineYaw = -10f; p.chestYaw = -14f; p.spinePitch = 10f;
                p.headYaw = 18f; p.neckYaw = 10f;
                p.R.foot = new Vector3(0.05f, 0f, -0.05f); p.R.footPitch = -26f; p.R.footYaw = 50f;     // pivot on the back toes
                Reach(p.R, 3, new Vector3(0.02f, R.ShoulderRest(1).y + 0.03f, 0.53f), new Vector3(1f, -0.4f, -0.2f));
                p.R.wrist = -6f; p.R.shrug = 10f; p.R.protract = 12f;
                p.L.elbow = 132f; p.L.armFwd = 46f; p.L.armDown = 50f;
            });
            return new Keys()
                .Pass(0f, g)
                .Pass(0.08f, P(g, p => { p.chestYaw = 12f; p.R.elbow = 132f; }))
                .Hold(0.24f, hit)
                .Pass(0.44f, P(hit, p => p.R.hand += new Vector3(0, -0.02f, -0.03f)))
                .Hold(1f, g);
        }

        static Keys Uppercut()
        {
            var g = Guard();
            var load = P(g, p =>
            {
                p.hips.y = -0.12f;
                p.spinePitch = 18f; p.chestPitch = 6f; p.chestYaw = 20f; p.spineYaw = 8f; p.headPitch = -12f;
                Reach(p.R, 0, Hip(0.2f, -0.04f, 0.04f), new Vector3(1f, 0f, -0.4f));
                p.L.armFwd = 64f; p.L.elbow = 120f;
            });
            var launch = P(g, p =>
            {
                p.hips = new Vector3(0f, 0.04f, 0.05f);
                p.autoHip = 0f;
                foreach (int s in Sides) { p[s].foot.y = 0.035f; p[s].footPitch = -26f; }
                p.hipYaw = -10f; p.spineYaw = -12f; p.chestYaw = -18f; p.spinePitch = -10f; p.chestPitch = -8f;
                p.neckPitch = -8f; p.headPitch = -22f; p.headYaw = 12f;
                p.R.armIK = 0f; p.R.armDown = -50f; p.R.armFwd = 30f; p.R.elbow = 16f; p.R.wrist = -4f; p.R.shrug = 24f;
                p.L.armDown = 66f; p.L.armFwd = -30f; p.L.elbow = 44f; p.L.wrist = 20f;
            });
            return new Keys()
                .Pass(0f, g)
                .Hold(0.14f, load)
                .Hold(0.3f, launch)
                .Pass(0.45f, P(launch, p => { p.hips.y = 0.05f; p.R.armDown = -54f; p.R.shrug = 26f; p.spinePitch = -12f; }))
                .Pass(0.7f, P(g, p => { p.hips.y = -0.07f; p.spinePitch = 10f; }))
                .Hold(1f, g);
        }

        /// <summary>A big punt: wind the leg back, swing through and up past the head, arms flung wide.</summary>
        static Keys Punt()
        {
            var stand = Idle0;
            var wind = P(p =>
            {
                p.hips.y = -0.05f;
                p.L.foot = new Vector3(-0.02f, 0f, 0.06f); p.L.kneeOut = 6f;
                p.R.foot = new Vector3(0.02f, 0.17f, -0.24f); p.R.footFree = 1f; p.R.footRel = -40f;
                p.spinePitch = 12f; p.chestPitch = 4f; p.hipYaw = -6f; p.headPitch = 6f;
                p.L.armDown = 34f; p.L.armFwd = 48f; p.L.elbow = 30f;
                p.R.armDown = 40f; p.R.armFwd = -40f; p.R.elbow = 28f;
            });
            var strike = P(p =>
            {
                p.hips.y = -0.03f;
                p.L.foot = new Vector3(-0.02f, 0f, 0.06f); p.L.footPitch = -18f;
                p.R.foot = new Vector3(0.0f, 0.26f, 0.43f); p.R.footFree = 1f; p.R.footRel = -34f;
                p.spinePitch = -6f; p.chestPitch = -3f; p.hipYaw = 8f; p.hipPitch = -6f; p.headPitch = 6f;
                p.L.armDown = 34f; p.L.armFwd = -46f; p.L.elbow = 30f; p.L.wrist = 20f;
                p.R.armDown = 30f; p.R.armFwd = 64f; p.R.elbow = 26f; p.R.wrist = 18f;
            });
            var follow = P(strike, p =>
            {
                p.R.foot = new Vector3(0.0f, 0.52f, 0.36f); p.R.footRel = -30f;
                p.L.footPitch = -26f;
                p.spinePitch = -14f; p.chestPitch = -8f; p.hipPitch = -14f; p.headPitch = 12f;
                p.L.armDown = 12f; p.L.armFwd = -30f; p.R.armDown = 12f; p.R.armFwd = 40f;
            });
            var down = P(p =>
            {
                p.hips.y = -0.04f;
                p.R.foot = new Vector3(0.03f, 0.06f, 0.1f); p.R.footFree = 0.6f; p.R.footRel = 0f;
                p.spinePitch = 2f; p.headPitch = 0f;
                Arms(p, 60f, 10f, 26f);
            });
            return new Keys()
                .Pass(0f, stand)
                .Hold(0.14f, wind)
                .Pass(0.26f, strike)
                .Hold(0.42f, follow)
                .Pass(0.7f, down)
                .Hold(1f, stand);
        }

        static Keys Flinch()
        {
            var stand = Idle0;
            var recoil = P(p =>
            {
                p.hips = new Vector3(0f, -0.04f, -0.05f);
                p.hipPitch = -4f;
                p.R.foot = new Vector3(0.04f, 0f, -0.11f); p.R.footPitch = -10f;
                p.spinePitch = -8f; p.chestPitch = -14f; p.chestRoll = 6f;
                p.neckPitch = -10f; p.headPitch = -26f; p.headRoll = 12f; p.headYaw = -10f;
                Arms(p, 36f, 48f, 42f, 10f, 40f);
                p.L.shrug = p.R.shrug = 12f;
            });
            var over = P(recoil, p =>
            {
                p.hips.z = -0.03f;
                p.spinePitch = 6f; p.chestPitch = 6f; p.neckPitch = 4f; p.headPitch = 10f; p.headRoll = -5f;
                Arms(p, 66f, 20f, 30f, 10f, 26f);
                p.L.shrug = p.R.shrug = 0f;
            });
            return new Keys().Pass(0f, stand).Hold(0.1f, recoil).Pass(0.32f, over).Hold(1f, stand);
        }

        /// <summary>Thrown flat: up and back, land on the back with a bounce, starfish for a beat, then up again.</summary>
        static Keys Knockdown()
        {
            var stand = Idle0;
            var hit = Flinch().At(0.1f / 1f);
            var flying = P(p =>
            {
                p.autoHip = 0f;
                p.hips = new Vector3(0f, -0.06f, -0.18f);
                p.hipPitch = -48f; p.spinePitch = -10f; p.chestPitch = -8f; p.headPitch = 20f;
                foreach (int s in Sides) { p[s].footFree = 1f; p[s].footRel = -10f; }
                p.L.foot = new Vector3(-0.04f, 0.32f, 0.28f); p.R.foot = new Vector3(0.05f, 0.24f, 0.36f);
                Arms(p, -20f, 30f, 30f, 10f, 30f);
                p.L.shrug = p.R.shrug = 16f;
            });
            var flat = P(p =>
            {
                p.autoHip = 0f;
                p.hips = new Vector3(0f, -0.56f, -0.42f);
                p.hipPitch = -88f; p.spinePitch = -2f; p.chestPitch = 0f; p.neckPitch = 4f; p.headPitch = 8f;
                foreach (int s in Sides) { p[s].footFree = 1f; p[s].footRel = -18f; p[s].kneeOut = 14f; }
                p.L.foot = new Vector3(-0.1f, 0.03f, 0.1f); p.R.foot = new Vector3(0.14f, 0.1f, 0.18f);
                Arms(p, 18f, 0f, 22f, 0f, 20f);
            });
            var bounce = P(flat, p => { p.hips.y = -0.49f; p.hipPitch = -80f; p.L.foot.y = 0.14f; p.R.foot.y = 0.2f; Arms(p, 0f, 10f, 30f, 8f, 20f); });
            var daze = P(flat, p => { p.headYaw = 24f; p.headRoll = 10f; p.L.armDown = 10f; });
            var daze2 = P(flat, p => { p.headYaw = -20f; p.headRoll = -8f; p.R.foot.y = 0.16f; });
            var situp = P(p =>
            {
                p.autoHip = 0f;
                p.hips = new Vector3(0f, -0.54f, -0.38f);
                p.hipPitch = -40f; p.spinePitch = 22f; p.chestPitch = 14f; p.headPitch = 6f; p.headRoll = 8f;
                foreach (int s in Sides) { p[s].kneeOut = 12f; }
                p.L.foot = new Vector3(-0.06f, 0f, 0.08f); p.R.foot = new Vector3(0.08f, 0f, 0.04f);
                p.L.footPitch = p.R.footPitch = 0f;
                foreach (int s in Sides) { p[s].footFree = 0.5f; p[s].footRel = 0f; }
                foreach (int s in Sides) Reach(p[s], 0, Hip(s * 0.24f, -0.6f, -0.04f), new Vector3(1f, 0.3f, -0.2f));
            });
            var crouch = P(p =>
            {
                p.hips = new Vector3(0f, -0.26f, -0.12f);
                p.hipPitch = 10f; p.spinePitch = 26f; p.chestPitch = 10f; p.headPitch = -6f;
                Feet(p, 0.05f, 0f, -0.06f, -0.1f, -18f);
                foreach (int s in Sides) { p[s].kneeOut = 12f; Reach(p[s], 0, Hip(s * 0.14f, -0.2f, 0.16f), new Vector3(1f, -0.3f, -0.3f), 0.7f); }
            });
            var shake1 = P(Idle0, p => { p.headYaw = 22f; p.headRoll = 6f; });
            var shake2 = P(Idle0, p => { p.headYaw = -22f; p.headRoll = -6f; });
            return new Keys()
                .Pass(0f, stand)
                .Hold(0.025f, hit)
                .Pass(0.09f, flying)
                .Hold(0.17f, flat)
                .Pass(0.215f, bounce)
                .Hold(0.26f, flat)
                .Pass(0.36f, daze)
                .Pass(0.46f, daze2)
                .Hold(0.55f, flat)
                .Hold(0.66f, situp)
                .Hold(0.78f, crouch)
                .Pass(0.86f, shake1)
                .Pass(0.92f, shake2)
                .Hold(1f, stand);
        }

        // ================================================================== people
        static Pose Wave(float u)
        {
            var p = Idle0;
            float w = Sin(u * 2f);
            p.hips.x = 0.018f * Sin(u);
            p.hips.y += 0.008f * Sin(u * 2f + 0.2f);
            p.hipRoll = -3f * Sin(u);
            p.spineRoll = 3f * Sin(u * 2f - 0.1f) + 2f;
            p.chestRoll = 2.5f * Sin(u * 2f - 0.2f) + 2f;
            p.chestYaw = 6f;
            p.headRoll = -7f * Sin(u) - 4f;
            p.headPitch = -6f + 2f * Sin(u * 2f);
            p.headYaw = -6f;
            var d = p.R;
            d.armIK = 0f;
            d.armDown = -46f + 4f * w; d.armFwd = 22f; d.elbow = 40f + 32f * w; d.foreTwist = 30f;
            d.wrist = 16f * Sin(u * 2f - 0.12f); d.wristSide = 20f * Sin(u * 2f - 0.1f); d.shrug = 18f;
            p.L.armDown = 78f; p.L.armFwd = 6f; p.L.elbow = 18f;
            return p;
        }

        static Keys Talk()
        {
            var a = P(p =>
            {
                p.hips.x = 0.02f;
                p.hipRoll = -2f; p.spinePitch = 3f;
                foreach (int s in Sides) Reach(p[s], 1, Chest(s * 0.11f, -0.22f, 0.2f), new Vector3(1f, -0.6f, -0.3f));
                p.headPitch = -2f;
            });
            var b = P(a, p =>
            {
                Reach(p.R, 1, Chest(0.3f, -0.14f, 0.16f), new Vector3(0.6f, -1f, -0.3f));
                p.R.wrist = -10f; p.chestYaw = 8f; p.headYaw = 10f; p.headPitch = 4f; p.headRoll = -4f;
            });
            var c = P(a, p =>
            {
                p.hips.x = -0.02f; p.hipRoll = 2f;
                foreach (int s in Sides) { Reach(p[s], 1, Chest(s * 0.28f, -0.18f, 0.14f), new Vector3(0.5f, -1f, -0.2f)); p[s].shrug = 12f; p[s].wrist = -18f; }
                p.headRoll = 10f; p.headPitch = -6f; p.spinePitch = -2f;
            });
            var d = P(a, p =>
            {
                p.spinePitch = 8f; p.chestPitch = 3f;
                p.R.armIK = 0f; p.R.armDown = 72f; p.R.armFwd = 76f; p.R.elbow = 12f; p.R.wrist = -6f;
                p.headPitch = 4f; p.headYaw = -4f;
            });
            var e = P(a, p => { p.headPitch = 6f; p.hips.x = 0.01f; Reach(p.L, 1, Chest(-0.13f, -0.16f, 0.24f), new Vector3(1f, -0.6f, -0.3f)); });
            return new Keys(true).Hold(0f, a).Pass(0.18f, b).Hold(0.3f, b).Pass(0.45f, e).Hold(0.58f, c).Pass(0.7f, a).Hold(0.82f, d).Pass(0.92f, e);
        }

        static Pose Talk2(float u)
        {
            var p = Idle(u);
            Reach(p.L, 0, Hip(-0.21f, 0.2f, 0.0f), new Vector3(1f, 0.1f, -0.3f));
            p.L.wrist = 30f;
            // the free hand circles as they explain, then they laugh at their own story
            float laugh = Smooth((u - 0.55f) / 0.08f) * (1f - Smooth((u - 0.85f) / 0.1f));
            float c = u * 2f;
            Reach(p.R, 1, Chest(0.17f + 0.06f * Cos(c), -0.16f + 0.05f * Sin(c), 0.22f), new Vector3(1f, -0.7f, -0.2f));
            p.R.wrist = -12f + 10f * Sin(c - 0.1f);
            p.headYaw = 6f * Sin(u) - 6f;
            p.headPitch = -2f - 14f * laugh + 4f * laugh * Sin(u * 12f);
            p.chestPitch = -6f * laugh;
            foreach (int s in Sides) p[s].shrug = 6f * laugh * Pos(Sin(u * 12f));
            p.hipRoll = 3f;
            p.hips.x = -0.02f;
            return p;
        }

        static Keys Cheer()
        {
            var crouch = P(p =>
            {
                p.hips.y = -0.09f;
                Feet(p, 0.04f, 0f, 0f, 0f);
                p.spinePitch = 12f; p.headPitch = -6f;
                Arms(p, 60f, -28f, 30f, 8f, 20f);
            });
            var up = P(p =>
            {
                p.autoHip = 0f;
                p.hips.y = 0.07f;
                Feet(p, 0.03f, 0.08f, 0f, 0f, -20f);
                foreach (int s in Sides) { p[s].footFree = 1f; p[s].footRel = -35f; }
                p.spinePitch = -8f; p.chestPitch = -6f; p.headPitch = -18f;
                foreach (int s in Sides) { var d = p[s]; d.armDown = -52f; d.armFwd = 22f; d.elbow = 18f; d.wrist = -4f; d.shrug = 24f; }
            });
            var top = P(up, p => { p.hips.y = 0.09f; foreach (int s in Sides) { p[s].foot.y = 0.1f; p[s].elbow = 34f; } });
            return new Keys(true).Hold(0f, crouch).Pass(0.3f, up).Hold(0.48f, top).Pass(0.7f, P(crouch, p => { p.hips.y = -0.05f; Arms(p, 20f, 20f, 40f); }));
        }

        static Pose Angry(float u)
        {
            var p = Idle0;
            float shake = Sin(u * 3f);
            p.spinePitch = 10f; p.chestPitch = 4f; p.headPitch = -6f + 4f * shake; p.neckPitch = -4f;
            Reach(p.L, 0, Hip(-0.21f, 0.2f, 0.0f), new Vector3(1f, 0.1f, -0.3f));
            p.L.wrist = 30f;
            var d = p.R;
            d.armIK = 0f; d.armDown = -18f; d.armFwd = 72f; d.elbow = 74f + 26f * shake; d.wrist = -8f; d.shrug = 8f;
            p.chestYaw = -10f + 4f * shake;
            // a stamp of the foot
            float stamp = Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.4f) / 0.12f));
            p.R.foot = new Vector3(0.02f, 0.07f * stamp, 0.03f);
            p.R.footFree = stamp;
            return p;
        }

        static Pose WatchCross(float u)
        {
            var p = Idle(u);
            p.hips.x = -0.03f + 0.008f * Sin(u);
            p.hipRoll = 3.5f;
            p.R.kneeOut = 12f;
            p.R.foot = new Vector3(0.04f, 0f, 0.04f); p.R.footYaw = 18f;
            Reach(p.R, 1, Chest(-0.08f, -0.22f, 0.18f), new Vector3(1f, -0.8f, -0.2f));
            Reach(p.L, 1, Chest(0.09f, -0.19f, 0.16f), new Vector3(1f, -0.8f, -0.2f));
            p.R.wrist = 10f; p.L.wrist = 10f;
            p.chestPitch = -3f;
            p.headYaw = 14f * Sin(u) + 4f; p.headRoll = 6f; p.headPitch = -2f + 3f * Sin(u * 2f);
            return p;
        }

        static Pose WatchHips(float u)
        {
            var p = Idle(u);
            foreach (int s in Sides) { Reach(p[s], 0, Hip(s * 0.21f, 0.2f, 0.0f), new Vector3(1f, 0.1f, -0.3f)); p[s].wrist = 32f; }
            p.chestPitch = -6f; p.spinePitch = -2f; p.neckPitch = -3f;
            p.headYaw = -16f * Sin(u + 0.1f); p.headPitch = -4f;
            return p;
        }

        /// <summary>Holding a phone up to film it ("Viral la ni!"), panning slowly to follow the action.</summary>
        static Pose Film(float u)
        {
            var p = Idle(u);
            float pan = Sin(u);
            foreach (int s in Sides) { Reach(p[s], 1, Chest(s * 0.05f, 0.16f, 0.3f), new Vector3(1f, -1f, 0f)); p[s].wrist = -10f; }
            p.chestYaw = 10f * pan; p.spineYaw = 4f * pan;
            p.headPitch = 6f; p.headYaw = 4f * pan; p.neckPitch = 2f;
            p.spinePitch = -2f;
            return p;
        }

        static Pose PointLaugh(float u)
        {
            var p = Idle0;
            float ha = Pos(Sin(u * 3f));
            p.spinePitch = 10f + 6f * ha; p.chestPitch = -4f; p.neckPitch = -6f; p.headPitch = -12f + 6f * ha;
            Reach(p.L, 0, Hip(-0.05f, 0.18f, 0.14f), new Vector3(1f, -0.4f, -0.3f));
            var d = p.R;
            d.armIK = 0f; d.armDown = 74f; d.armFwd = 78f; d.elbow = 6f + 8f * ha; d.wrist = -8f;
            foreach (int s in Sides) p[s].shrug = 8f * ha;
            p.hips.y -= 0.01f * ha;
            return p;
        }

        /// <summary>The satay man fanning his coals: bent over the grill, forearm flapping.</summary>
        static Pose Fan(float u)
        {
            var p = Idle0;
            float f = Sin(u * 2f);
            p.spinePitch = 14f; p.chestPitch = 6f; p.headPitch = 16f; p.neckPitch = 6f;
            p.hips.z = -0.02f;
            Reach(p.R, 1, Chest(0.14f, -0.2f + 0.025f * f, 0.24f + 0.05f * f), new Vector3(1f, -0.3f, -0.4f));
            p.R.wrist = 30f * Sin(u * 2f - 0.12f);
            p.R.foreTwist = 50f;
            Reach(p.L, 1, Chest(-0.12f, -0.22f, 0.24f), new Vector3(1f, -0.6f, -0.3f));
            p.headYaw = 6f;
            return p;
        }

        /// <summary>Running for their lives: the run's legs, arms flailing over the head.</summary>
        static Pose Panic(float u)
        {
            var p = GaitPose(Run(), u);
            p.spinePitch = 2f; p.chestPitch = -6f; p.headPitch = -6f;
            foreach (int s in Sides)
            {
                var d = p[s];
                float a = u * 2f + (s > 0 ? 0.5f : 0f);
                d.armIK = 0f;
                d.armDown = -36f + 20f * Sin(a); d.armFwd = 24f + 20f * Cos(a); d.elbow = 34f + 20f * Sin(a + 0.15f);
                d.wrist = 30f * Sin(a - 0.2f); d.shrug = 18f + 6f * Sin(a + 0.1f);
            }
            p.headYaw = 20f * Sin(u * 2f);
            p.headRoll = 6f * Sin(u * 2f + 0.2f);
            return p;
        }

        // ================================================================== fidgets
        static Keys FidgetLook()
        {
            var i = Idle0;
            var shade = P(i, p =>
            {
                Reach(p.R, 2, Head(0.08f, 0.15f, 0.12f), new Vector3(1f, -0.5f, 0.1f));
                p.R.wrist = -10f; p.R.foreTwist = -30f;
                p.chestYaw = -14f; p.spineYaw = -6f; p.hipYaw = -4f; p.neckYaw = -14f; p.headYaw = -26f; p.headPitch = -4f;
            });
            var right = P(shade, p => { p.chestYaw = 12f; p.spineYaw = 6f; p.hipYaw = 3f; p.neckYaw = 14f; p.headYaw = 26f; p.headRoll = -3f; });
            return new Keys().Hold(0f, i).Hold(0.2f, shade).Hold(0.42f, P(shade, p => p.headYaw = -32f))
                .Hold(0.62f, right).Hold(0.8f, P(right, p => p.headYaw = 30f)).Hold(1f, i);
        }

        static Keys FidgetStretch()
        {
            var i = Idle0;
            var up = P(i, p =>
            {
                foreach (int s in Sides) { var d = p[s]; d.armIK = 0f; d.armDown = -54f; d.armFwd = 14f; d.elbow = 14f; d.wrist = -20f; d.shrug = 26f; }
                p.chestPitch = -12f; p.spinePitch = -6f; p.neckPitch = -8f; p.headPitch = -14f;
                foreach (int s in Sides) p[s].footPitch = -18f;
                p.hips.y = 0.0f;
            });
            var lean = P(up, p => { p.spineRoll = 10f; p.chestRoll = 8f; p.hips.x = -0.02f; p.headRoll = 8f; });
            var lean2 = P(up, p => { p.spineRoll = -10f; p.chestRoll = -8f; p.hips.x = 0.02f; p.headRoll = -8f; });
            return new Keys().Hold(0f, i).Hold(0.22f, up).Hold(0.4f, lean).Hold(0.6f, lean2)
                .Pass(0.8f, P(i, p => { Arms(p, 60f, 10f, 30f); p.spinePitch = 6f; })).Hold(1f, i);
        }

        static Keys FidgetWipe()
        {
            var i = Idle0;
            var at = P(i, p => { Reach(p.R, 2, Head(0.12f, 0.18f, 0.16f), new Vector3(1f, -0.5f, 0.1f)); p.R.wrist = 10f; p.R.foreTwist = -20f; p.headPitch = 6f; p.headRoll = -4f; });
            var across = P(at, p => { p.R.hand = Head(0.01f, 0.17f, 0.17f); p.headRoll = 4f; p.headPitch = 8f; });
            var flick = P(i, p =>
            {
                p.R.armIK = 0f; p.R.armDown = 64f; p.R.armFwd = 26f; p.R.elbow = 30f; p.R.wrist = 50f; p.R.foreTwist = 40f;
                p.chestYaw = 8f; p.headYaw = 16f; p.headPitch = 6f;
            });
            var flick2 = P(flick, p => { p.R.wrist = -20f; p.R.armDown = 70f; });
            return new Keys().Hold(0f, i).Hold(0.2f, at).Hold(0.38f, across).Pass(0.52f, flick).Pass(0.6f, flick2).Pass(0.68f, flick).Pass(0.76f, flick2).Hold(1f, i);
        }

        static Keys FidgetScratch()
        {
            var i = Idle0;
            Pose scratch(float dx) => P(i, p =>
            {
                Reach(p.R, 2, Head(0.03f + dx * 0.5f, 0.06f - dx, 0.2f), new Vector3(1f, -0.6f, -0.1f));
                p.R.wrist = 20f;
                p.headRoll = 10f; p.headPitch = -8f; p.headYaw = 8f; p.chestRoll = -3f; p.neckPitch = -4f;
                Reach(p.L, 0, Hip(-0.21f, 0.2f, 0.0f), new Vector3(1f, 0.1f, -0.3f));
            });
            var k = new Keys().Hold(0f, i).Pass(0.18f, scratch(0f));
            for (int n = 0; n < 6; n++) k.Pass(0.22f + n * 0.07f, scratch(n % 2 == 0 ? 0.03f : -0.02f));
            return k.Hold(0.68f, scratch(0f)).Hold(1f, i);
        }

        static Keys FidgetWatch()
        {
            var i = Idle0;
            var look = P(i, p =>
            {
                Reach(p.L, 1, Chest(-0.04f, -0.17f, 0.25f), new Vector3(1f, -0.3f, -0.6f));
                p.L.foreTwist = -40f; p.L.wrist = -10f;
                p.neckPitch = 10f; p.headPitch = 16f; p.headYaw = -14f; p.chestYaw = -6f;
            });
            var tap = P(look, p => Reach(p.R, 1, Chest(-0.02f, -0.13f, 0.26f), new Vector3(1f, -0.5f, -0.4f)));
            var sigh = P(i, p => { foreach (int s in Sides) p[s].shrug = -4f; p.chestPitch = 6f; p.headPitch = 8f; p.spinePitch = 5f; });
            return new Keys().Hold(0f, i).Hold(0.2f, look).Pass(0.36f, tap).Pass(0.44f, look).Pass(0.52f, tap).Hold(0.6f, look).Hold(0.8f, sigh).Hold(1f, i);
        }
    }
}
