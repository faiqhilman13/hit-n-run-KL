using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// The natural speeds of the motion library's gaits (Assets/KampungRun/Animation/Clips), for the reference
    /// character it was authored on (chr_aiman, 0.515 m from hip to ankle). Other characters' strides scale with
    /// their legs, so their speed is normalised by leg length before it drives the blend.
    /// </summary>
    public static class Gaits
    {
        public const float WalkFrom = 0.3f, Walk = 1.15f, Jog = 3.0f, Run = 5.5f, Sprint = 8.5f, RefLeg = 0.515f;
    }

    /// <summary>
    /// Drives a character's animation. KL humanoids play the shared motion library through the humanoid
    /// controller: locomotion matched to the ground speed (planted feet, no skating) in the character's own style
    /// (a heavy waddle, an aunty's neat steps, a kid's bounce, a pakcik's stroll), an air blend by vertical speed,
    /// the flip, the ground-pound, landings, skids, the punch combo, gestures and idle fidgets. The hero also gets
    /// a physical layer on top: the body leans into a start and rocks back on a stop, the arms carry on swinging,
    /// landings nod the head and bags swing about.
    /// The legacy branch is the old "cartoon strip" rig for separate-limb Blender models: limbs are separate
    /// pieces pivoting at their joints, so we just swing them.
    /// </summary>
    public class CharacterRig : MonoBehaviour
    {
        public float speed;          // m/s, set by the controller
        public bool grounded = true;
        public bool sitting;         // hidden in car etc.
        public bool waving;
        public bool panicking;       // arms up, running away
        public bool riding;
        /// <summary>Vertical speed while airborne (m/s), for the air blend.</summary>
        public float velY;
        /// <summary>Braking hard (the skid pose).</summary>
        public bool skidding;
        /// <summary>A looping gesture while standing (see <see cref="Gesture"/>), or -1.</summary>
        public int gesture = -1;
        /// <summary>Lean, overlap and swinging bags on top of the clips (the hero).</summary>
        public bool lively;
        /// <summary>Fidget when left standing (look about, stretch, mop the brow...).</summary>
        public bool fidgets = true;

        public enum Gesture { Talk, Talk2, Cheer, Angry, WatchCross, WatchHips, Film, PointLaugh, Fan }

        /// <summary>0 default, 1 heavy, 2 lady, 3 kid, 4 elder.</summary>
        public int Style { get; private set; }

        Transform _torso, _head, _armL, _armR, _legL, _legR;
        Quaternion _tR, _hR, _aLR, _aRR, _lLR, _lRR;
        Vector3 _torsoPos, _headPos;
        float _phase, _kickT, _tumble;
        float _stride = 1f;

        // ---------------------------------------------------------------- humanoid (KL) mode
        Animator _anim;
        SkinnedMeshRenderer _face;
        int _bsGrin = -1, _bsAlarm = -1, _bsDetermined = -1, _bsBlink = -1;
        float _grin, _alarm, _determined, _blinkT = 2f, _blink;
        float _pace = 1f, _still, _fidgetAt, _talkT, _talkSeed;
        public bool Humanoid => _anim != null;

        /// <summary>Flap the mouth for a line of speech (a speech bubble).</summary>
        public void Speak(float seconds) { _talkT = Mathf.Max(_talkT, seconds); }

        static readonly int PSpeed = Animator.StringToHash("Speed"), PLoco = Animator.StringToHash("LocoSpeed"),
            PStyle = Animator.StringToHash("Style"), PVelY = Animator.StringToHash("VelY"), PGround = Animator.StringToHash("Grounded"),
            PRiding = Animator.StringToHash("Riding"), PSitting = Animator.StringToHash("Sitting"), PWave = Animator.StringToHash("Wave"),
            PPanic = Animator.StringToHash("Panic"), PSkid = Animator.StringToHash("Skid"), PGesture = Animator.StringToHash("Gesture"),
            PGestureId = Animator.StringToHash("GestureId"), PFidgetId = Animator.StringToHash("FidgetId"), PCombo = Animator.StringToHash("Combo");

        void SetupHumanoid()
        {
            var a = GetComponentInChildren<Animator>();
            if (a == null || !a.isHuman || GameAssets.I.humanController == null) return;
            _anim = a;
            _anim.runtimeAnimatorController = GameAssets.I.humanController;
            _anim.applyRootMotion = false;
            // the full-detail mesh carries the face keys (the _LOD1 distance mesh has none)
            foreach (var sk in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sk.sharedMesh && sk.sharedMesh.blendShapeCount > 0) { _face = sk; break; }
            if (_face == null) _face = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_face && _face.sharedMesh)
            {
                // each KL character names its three expressions after its own sheet; they map onto
                // the same three gameplay moods: happy / shocked / intent
                var m = _face.sharedMesh;
                _bsGrin = Shape(m, "Grin", "Smile", "Amused");
                _bsAlarm = Shape(m, "Alarm", "Exasperation", "Alarmed");
                _bsDetermined = Shape(m, "Determined", "Focus", "Suspicious");
                _bsBlink = m.GetBlendShapeIndex("Blink");
            }
            // stride: the clips were authored on 0.515 m legs; a child's are half that
            var up = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var lo = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var ft = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (up && lo && ft)
                _pace = Mathf.Clamp((Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, ft.position)) /
                                    Mathf.Max(0.01f, transform.lossyScale.y) / Gaits.RefLeg, 0.3f, 2f);
            Style = StyleOf(a.avatar ? a.avatar.name : name);
            _anim.SetFloat(PStyle, Style);
            Ground();
            _anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            _fidgetAt = Random.Range(5f, 12f);
            _talkSeed = Random.Range(0f, 10f);
        }

        /// <summary>Who moves how: the two big men (Pak Mat, Datuk Mega), the aunties, the kids, the pakcik and
        /// everyone else (the town man walks normally: half the street waddling would be a bit much).</summary>
        public static int StyleOf(string id)
        {
            id = id.ToLowerInvariant();
            if (id.Contains("pakmat") || id.Contains("datukmega")) return 1;
            if (id.Contains("maksom") || id.Contains("townaunty") || id.Contains("mei")) return 2;
            if (id.Contains("adik") || id.Contains("chr_kid") || id.Contains("along")) return 3;
            if (id.Contains("pakcik")) return 4;
            return 0;
        }

        static int Shape(Mesh m, params string[] names)
        {
            foreach (var n in names) { int i = m.GetBlendShapeIndex(n); if (i >= 0) return i; }
            return -1;
        }

        // ---------------------------------------------------------------- feet on the floor
        // Humanoid retargeting places the body by the avatar's overall size, which on these big-headed cartoon
        // proportions can leave the feet a few centimetres in the ground (Along sank 12 cm). Measure it once on
        // the idle pose and lift the hips by that much whenever the animator poses them.
        Transform _hips;
        float _groundFix;
        Vector3 _hipsWritten;

        void Ground()
        {
            _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            if (_hips == null) return;
            var bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
            var feet = new Transform[4];
            var contact = new Vector3[4];
            float floor = transform.position.y;
            float s = Mathf.Max(0.01f, transform.lossyScale.y);
            for (int i = 0; i < 4; i++)
            {
                feet[i] = _anim.GetBoneTransform(bones[i]);
                if (feet[i] == null) return;
                var p = feet[i].position;
                // under the heel (behind the ankle) and under the ball, on the bind pose's floor
                contact[i] = feet[i].InverseTransformPoint(new Vector3(p.x, floor, p.z) - transform.forward * (i < 2 ? 0.04f * s : 0f));
            }
            var cull = _anim.cullingMode;
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _anim.Update(0f);
            _anim.cullingMode = cull;
            float low = float.MaxValue;
            for (int i = 0; i < 4; i++) low = Mathf.Min(low, transform.InverseTransformPoint(feet[i].TransformPoint(contact[i])).y);
            _groundFix = Mathf.Abs(low) > 0.004f ? Mathf.Clamp(-low, -0.05f, 0.16f) : 0f;
            _hipsWritten = _hips.localPosition;
        }

        void ApplyGroundFix()
        {
            if (_groundFix == 0f || _hips == null) return;
            var lp = _hips.localPosition;
            if ((lp - _hipsWritten).sqrMagnitude < 1e-12f) return;     // not posed this frame (culled, throttled)
            _hips.position += transform.up * (_groundFix * transform.lossyScale.y);
            _hipsWritten = _hips.localPosition;
        }

        public void Trigger(string name) { if (_anim) _anim.SetTrigger(name); }

        // one-off cues that only make sense in the moment (a landing, a slam): dropped if not taken up at once,
        // so they can't fire late from some other state
        string _cue;
        float _cueT;

        void Cue(string name)
        {
            if (!_anim) return;
            if (_cue != null) _anim.ResetTrigger(_cue);
            _anim.SetTrigger(name);
            _cue = name;
            _cueT = 0.15f;
        }

        void HumanoidUpdate(float dt)
        {
            if (_cue != null && (_cueT -= dt) <= 0f) { _anim.ResetTrigger(_cue); _cue = null; }
            // stride matching: the blend picks the gait by speed (normalised to the reference legs), and the
            // playback rate makes up the difference so the planted foot doesn't skate. People walking briskly
            // stay in the walk a little longer, just quicker, instead of breaking into a jog.
            float n = speed / _pace;
            float x = n <= Gaits.Walk ? n
                : n < 2f ? Gaits.Walk + (n - Gaits.Walk) * 0.35f
                : n < Gaits.Jog ? Mathf.Lerp(Gaits.Walk + 0.85f * 0.35f, Gaits.Jog, (n - 2f) / (Gaits.Jog - 2f))
                : n;
            float natural = x <= Gaits.Walk ? Gaits.Walk : Mathf.Min(x, Gaits.Sprint);
            // (a child sprinting at a grown-up's pace is all legs: the stride plays up to 2.4x)
            float loco = n < Gaits.WalkFrom ? Mathf.Lerp(1f, Gaits.WalkFrom / Gaits.Walk, n / Gaits.WalkFrom) : Mathf.Clamp(n / natural, 0.25f, 2.4f);
            if (panicking) loco = Mathf.Clamp(n / Gaits.Run, 0.5f, 1.6f);
            _anim.SetFloat(PSpeed, x, 0.08f, dt);
            _anim.SetFloat(PLoco, loco, 0.06f, dt);
            _anim.SetFloat(PVelY, velY, 0.05f, dt);
            _anim.SetBool(PGround, grounded || riding || sitting);
            _anim.SetBool(PRiding, riding);
            _anim.SetBool(PSitting, sitting && !riding);
            _anim.SetBool(PWave, waving);
            _anim.SetBool(PPanic, panicking);
            _anim.SetBool(PSkid, skidding && grounded);
            _anim.SetBool(PGesture, gesture >= 0 && !waving);
            if (gesture >= 0) _anim.SetFloat(PGestureId, gesture);

            // left standing about: fidget now and then
            bool idle = fidgets && speed < 0.1f && grounded && !sitting && !riding && !waving && !panicking && gesture < 0 && _tumble <= 0f;
            _still = idle ? _still + dt : 0f;
            if (_still > _fidgetAt)
            {
                _still = 0f;
                _fidgetAt = Random.Range(7f, 15f);
                Fidget(Random.Range(0, 5));
            }

            if (_face == null) return;
            // expressions: grin when punching/waving/cheering, alarm when panicking or knocked about,
            // gritted determination when going fast; blink every few seconds
            bool hurt = _tumble > 0;
            if (_tumble > 0) _tumble -= dt;
            if (_punchT > 0) _punchT -= dt;
            if (_kickT > 0) _kickT -= dt;
            bool happy = _punchT > 0 || _kickT > 0 || waving || gesture == (int)Gesture.Cheer || gesture == (int)Gesture.PointLaugh || gesture == (int)Gesture.Talk2;
            float g = happy ? 1 : 0.25f;
            float al = (panicking || hurt || velY < -14f) ? 1 : 0;
            float de = !panicking && !hurt && (speed > 5.5f || (riding && speed > 8f) || gesture == (int)Gesture.Angry) ? 1 : 0;
            _grin = Mathf.MoveTowards(_grin, al > 0 || de > 0 ? 0 : g, dt * 5);
            _alarm = Mathf.MoveTowards(_alarm, al, dt * 6);
            _determined = Mathf.MoveTowards(_determined, de, dt * 4);
            _blinkT -= dt;
            if (_blinkT <= 0) { _blink = 1; _blinkT = Random.Range(2.5f, 5f); }
            _blink = Mathf.MoveTowards(_blink, 0, dt * 8);
            if (_bsGrin >= 0) _face.SetBlendShapeWeight(_bsGrin, _grin * 100);
            // talking: the open-mouthed face flaps a few syllables a second (and tails off at the end of the line)
            float flap = 0f;
            if (_talkT > 0f)
            {
                _talkT -= dt;
                float syll = Mathf.Sin(Time.time * 33f + _talkSeed) * 0.6f + Mathf.Sin(Time.time * 13f + _talkSeed * 2f) * 0.4f;
                flap = Mathf.Clamp01(syll) * Mathf.Clamp01(_talkT * 5f);
            }
            if (_bsAlarm >= 0) _face.SetBlendShapeWeight(_bsAlarm, Mathf.Max(_alarm, flap * 0.55f) * 100);
            if (_bsDetermined >= 0) _face.SetBlendShapeWeight(_bsDetermined, _determined * 100);
            if (_bsBlink >= 0) _face.SetBlendShapeWeight(_bsBlink, (_blink > 0.5f ? 1 : 0) * 100);
        }

        // ---------------------------------------------------------------- actions
        public void Kick() { _kickT = 0.35f; Trigger("Kick"); }

        float _punchT;
        int _punchSide;

        public void Punch(int combo)
        {
            _punchT = combo == 3 ? 0.4f : 0.22f;
            _punchSide = combo;
            if (_anim) _anim.SetInteger(PCombo, Mathf.Clamp(combo, 1, 3));
            Trigger("Punch");
        }

        /// <summary>Knocked over: whole body tips back, then gets up.</summary>
        public void Tumble(float time) { _tumble = time; Trigger("Knock"); }

        /// <summary>The double jump's tuck (the body's flip is turned by the controller).</summary>
        public void Flip() => Trigger("Flip");
        /// <summary>The ground-pound: tuck and spin, then drop with fists up.</summary>
        public void Pound() => Trigger("Pound");
        /// <summary>The ground-pound lands: a crouch with both fists in the dirt.</summary>
        public void PoundLand() => Cue("PoundLand");
        /// <summary>A hard landing: absorb it in the knees.</summary>
        public void Land() { if (_anim) { _anim.ResetTrigger("Flip"); Cue("Land"); } }

        /// <summary>A little idle business: 0 look about, 1 stretch, 2 mop the brow, 3 stroke the chin, 4 check the time.</summary>
        public void Fidget(int which)
        {
            if (!_anim) return;
            _anim.SetFloat(PFidgetId, which);
            Trigger("Fidget");
        }

        /// <summary>Dropped into a seat: cancel any pending punch / knockdown so the body doesn't play it
        /// out at the wheel (legs flailing through the floor), and blend straight into the seated pose.</summary>
        public void SettleInSeat(bool ride)
        {
            _tumble = 0; _punchT = 0; _kickT = 0;
            if (!_anim) return;
            foreach (var t in new[] { "Punch", "Kick", "Knock", "Hit", "Mount", "Dismount", "Flip", "Pound", "PoundLand", "Land", "Fidget" }) _anim.ResetTrigger(t);
            _anim.SetBool(PRiding, ride);
            _anim.SetBool(PSitting, !ride);
            _anim.CrossFadeInFixedTime(ride ? "Ride" : "Sit", 0.2f);
        }

        // ---------------------------------------------------------------- the hero's physical layer
        // Springs on top of the clips, like the secondary layer of a classic platformer hero: the spine leans
        // into a start and rocks back past upright on a stop, the arms carry on swinging after the body stops,
        // a landing nods the chest and head, and bags (Aiman's satchel, Mei's towel) swing about.
        Transform _spine, _chest, _neck, _headB, _uArmL, _uArmR, _satchel, _cord;
        Quaternion _satchelRest, _cordRest;
        Vector3 _lastPos, _lastVel, _acc;
        float _lean, _leanV, _swing, _swingV, _nod, _nodV, _bagX, _bagXV, _bagZ, _bagZV, _lastVelY;
        bool _liveInit;

        void LiveInit()
        {
            _liveInit = true;
            _spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
            _neck = _anim.GetBoneTransform(HumanBodyBones.Neck);
            _headB = _anim.GetBoneTransform(HumanBodyBones.Head);
            _uArmL = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _uArmR = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            foreach (var t in _anim.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Satchel") { _satchel = t; _satchelRest = t.localRotation; }
                else if (t.name == "KeyCord") { _cord = t; _cordRest = t.localRotation; }
            }
            _lastPos = transform.position;
        }

        static void Spring(ref float x, ref float v, float target, float freq, float damp, float dt)
        {
            // semi-implicit damped spring (stable at game frame rates)
            float w = freq * Mathf.PI * 2f;
            v += (-(x - target) * w * w - 2f * damp * w * v) * dt;
            x += v * dt;
        }

        void Secondary(float dt)
        {
            if (!_liveInit) LiveInit();
            if (dt <= 0f || sitting || riding) { _lastPos = transform.position; _lastVel = Vector3.zero; return; }
            var vel = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            var acc = (vel - _lastVel) / dt;
            _lastVel = vel;
            if (acc.sqrMagnitude > 2500f) acc = Vector3.zero;               // teleports, respawns
            _acc = Vector3.Lerp(_acc, acc, 1f - Mathf.Exp(-dt * 18f));
            var local = transform.InverseTransformDirection(_acc);
            var flatVel = transform.InverseTransformDirection(vel);

            // lean into a start, rock back on a stop (overshooting a little past upright, then settling)
            float leanTarget = grounded ? Mathf.Clamp(local.z * 0.9f, -12f, 10f) : 0f;
            Spring(ref _lean, ref _leanV, leanTarget, 2.2f, 0.42f, dt);
            // the arms lag the body: braking throws them forward, a burst of speed pulls them back
            Spring(ref _swing, ref _swingV, grounded ? Mathf.Clamp(-local.z * 1.6f, -22f, 28f) : 0f, 2.6f, 0.3f, dt);
            // landings nod the chest and head (a kick on the spring, then it settles)
            if (!grounded) _lastVelY = velY;
            else if (_lastVelY < -4f) { _nodV += Mathf.Clamp(-_lastVelY * 16f, 0f, 320f); _lastVelY = 0f; }
            Spring(ref _nod, ref _nodV, 0f, 3.2f, 0.38f, dt);

            var right = transform.right;
            float total = _lean + _nod;
            if (_spine) _spine.rotation = Quaternion.AngleAxis(total * 0.45f, right) * _spine.rotation;
            if (_chest) _chest.rotation = Quaternion.AngleAxis(total * 0.55f, right) * _chest.rotation;
            // the head keeps its eyes up (most of the lean taken back), and a stop nods it a beat later
            if (_headB) _headB.rotation = Quaternion.AngleAxis(-_lean * 0.7f + _nod * 0.3f, right) * _headB.rotation;
            if (_uArmL) _uArmL.rotation = Quaternion.AngleAxis(-_swing, right) * _uArmL.rotation;
            if (_uArmR) _uArmR.rotation = Quaternion.AngleAxis(-_swing, right) * _uArmR.rotation;

            // bags swing against the body's acceleration and bounce with the stride
            float bounce = grounded && flatVel.magnitude > 1f ? Mathf.Sin(Time.time * 18f) * Mathf.Min(1f, flatVel.magnitude / 6f) * 6f : 0f;
            Spring(ref _bagX, ref _bagXV, Mathf.Clamp(-local.z * 3f, -50f, 50f) + bounce, 1.6f, 0.18f, dt);
            Spring(ref _bagZ, ref _bagZV, Mathf.Clamp(local.x * 3f, -40f, 40f) + (grounded ? 0f : velY * 2f), 1.6f, 0.18f, dt);
            if (_satchel) _satchel.localRotation = _satchelRest * Quaternion.Euler(_bagX, 0f, _bagZ);
            if (_cord) _cord.localRotation = _cordRest * Quaternion.Euler(_bagX * 1.3f, 0f, _bagZ * 1.3f);
        }

        // ---------------------------------------------------------------- legacy rig
        void Awake()
        {
            SetupHumanoid();
            _torso = ModelFactory.Find(gameObject, "Torso");
            _head = ModelFactory.Find(gameObject, "Head");
            _armL = ModelFactory.Find(gameObject, "ArmL");
            _armR = ModelFactory.Find(gameObject, "ArmR");
            _legL = ModelFactory.Find(gameObject, "LegL");
            _legR = ModelFactory.Find(gameObject, "LegR");
            if (_torso) { _tR = Rel(_torso); _torsoPos = _torso.localPosition; }
            if (_head) { _hR = Rel(_head); _headPos = _head.localPosition; }
            if (_armL) _aLR = Rel(_armL);
            if (_armR) _aRR = Rel(_armR);
            if (_legL) _lLR = Rel(_legL);
            if (_legR) _lRR = Rel(_legR);
            // shorter legs -> quicker steps
            if (_legL) _stride = Mathf.Clamp(_legL.localPosition.y / 0.72f, 0.5f, 1.3f);
        }

        Quaternion Rel(Transform t) => Quaternion.Inverse(transform.rotation) * t.rotation;

        void Pose(Transform t, Quaternion rest, float pitch, float roll = 0f, float yaw = 0f)
        {
            if (!t) return;
            var r = transform.rotation;
            t.rotation = Quaternion.AngleAxis(yaw, transform.up) * Quaternion.AngleAxis(pitch, transform.right) *
                         Quaternion.AngleAxis(roll, transform.forward) * r * rest;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (_anim != null)
            {
                HumanoidUpdate(dt);
                ApplyGroundFix();
                if (lively) Secondary(dt);
                return;
            }
            float s = Mathf.Clamp01(speed / 7f);
            _phase += dt * (3.2f + speed * 1.5f) / _stride;
            float sw = Mathf.Sin(_phase);
            float bob = Mathf.Abs(Mathf.Cos(_phase));

            float legAmp = Mathf.Lerp(0f, 55f, s);
            float armAmp = Mathf.Lerp(4f, 60f, s);
            float breathe = Mathf.Sin(Time.time * 2.1f) * 2f;

            float legL = sw * legAmp, legR = -sw * legAmp;
            float armL = -sw * armAmp, armR = sw * armAmp;
            float armRollL = -8f, armRollR = 8f;
            float torsoPitch = s * 12f + breathe * 0.3f;
            float headPitch = -s * 6f + breathe;

            if (!grounded)
            {
                // Lat's jump: knees up, arms flung out
                legL = -50f; legR = -20f;
                armL = -150f; armR = -150f;
                armRollL = -35f; armRollR = 35f;
            }
            if (panicking)
            {
                armL = -170f + sw * 20f; armR = -170f - sw * 20f;
                armRollL = -20f; armRollR = 20f;
            }
            if (waving)
            {
                armR = -160f;
                armRollR = 30f + Mathf.Sin(Time.time * 12f) * 25f;
            }
            if (_kickT > 0)
            {
                _kickT -= dt;
                float k = Mathf.Sin(Mathf.Clamp01(1f - _kickT / 0.35f) * Mathf.PI);
                legR = -95f * k;
                armL = -40f * k; armR = 40f * k;
                torsoPitch = -12f * k;
            }
            if (_punchT > 0)
            {
                _punchT -= dt;
                float k = Mathf.Sin(Mathf.Clamp01(1f - _punchT / (_punchSide == 3 ? 0.4f : 0.22f)) * Mathf.PI);
                if (_punchSide == 1) { armR = -95f * k; armRollR = 0; }
                else if (_punchSide == 2) { armL = -95f * k; armRollL = 0; }
                else { armL = armR = -110f * k; armRollL = armRollR = 0; torsoPitch = 14f * k; }
                torsoPitch += 6f * k;
            }
            if (_tumble > 0)
            {
                _tumble -= dt;
                armL = armR = -160f; armRollL = -40f; armRollR = 40f;
                legL = -70f; legR = -30f;
            }
            if (sitting)
            {
                legL = legR = -85f;
                armL = armR = -60f;
            }

            Pose(_legL, _lLR, legL);
            Pose(_legR, _lRR, legR);
            Pose(_armL, _aLR, armL, armRollL);
            Pose(_armR, _aRR, armR, armRollR);
            Pose(_torso, _tR, torsoPitch, sw * s * 6f, sw * s * 8f);
            Pose(_head, _hR, headPitch, -sw * s * 5f);

            float lift = grounded ? bob * s * 0.12f : 0f;
            if (_torso) _torso.localPosition = _torsoPos + Vector3.up * lift;
            if (_head) _head.localPosition = _headPos + Vector3.up * lift * 1.2f;
        }
    }
}
