using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Procedural "cartoon strip" animation for the Blender characters: limbs are separate
    /// pieces pivoting at their joints, so we just swing them. Exaggerated and bouncy,
    /// like Lat's figures mid-stride.
    /// </summary>
    public class CharacterRig : MonoBehaviour
    {
        public float speed;          // m/s, set by the controller
        public bool grounded = true;
        public bool sitting;         // hidden in car etc.
        public bool waving;
        public bool panicking;       // arms up, running away

        Transform _torso, _head, _armL, _armR, _legL, _legR;
        Quaternion _tR, _hR, _aLR, _aRR, _lLR, _lRR;
        Vector3 _torsoPos, _headPos;
        float _phase, _kickT, _tumble;
        float _stride = 1f;

        // ---------------------------------------------------------------- humanoid (KL) mode
        public bool riding;
        Animator _anim;
        SkinnedMeshRenderer _face;
        int _bsGrin = -1, _bsAlarm = -1, _bsDetermined = -1, _bsBlink = -1;
        float _grin, _alarm, _determined, _blinkT = 2f, _blink;
        public bool Humanoid => _anim != null;

        void SetupHumanoid()
        {
            var a = GetComponentInChildren<Animator>();
            if (a == null || !a.isHuman || GameAssets.I.humanController == null) return;
            _anim = a;
            _anim.runtimeAnimatorController = GameAssets.I.humanController;
            _anim.applyRootMotion = false;
            _anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
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
        }

        static int Shape(Mesh m, params string[] names)
        {
            foreach (var n in names) { int i = m.GetBlendShapeIndex(n); if (i >= 0) return i; }
            return -1;
        }

        public void Trigger(string name) { if (_anim) _anim.SetTrigger(name); }

        void HumanoidUpdate(float dt)
        {
            _anim.SetFloat("Speed", speed, 0.08f, dt);
            // play the stride faster/slower outside the walk..run range so the feet stay planted
            const float walkRef = 2.55f, runRef = 6.5f;
            float loco = speed < 0.3f ? 1f : speed < walkRef ? Mathf.Max(0.6f, speed / walkRef) : speed > runRef ? speed / runRef : 1f;
            _anim.SetFloat("LocoSpeed", Mathf.Min(loco, 1.8f), 0.1f, dt);
            _anim.SetBool("Grounded", grounded || riding || sitting);
            _anim.SetBool("Riding", riding);
            _anim.SetBool("Sitting", sitting && !riding);
            _anim.SetBool("Wave", waving);
            _anim.SetBool("Panic", panicking);
            if (_face == null) return;
            // expressions: grin when punching/waving, alarm when panicking or knocked about,
            // gritted determination when going fast; blink every few seconds
            bool hurt = _tumble > 0;
            if (_tumble > 0) _tumble -= dt;
            if (_punchT > 0) _punchT -= dt;
            if (_kickT > 0) _kickT -= dt;
            float g = (_punchT > 0 || _kickT > 0 || waving) ? 1 : 0.25f;
            float al = (panicking || hurt) ? 1 : 0;
            float de = !panicking && !hurt && (speed > 5.5f || (riding && speed > 8f)) ? 1 : 0;
            _grin = Mathf.MoveTowards(_grin, al > 0 || de > 0 ? 0 : g, dt * 5);
            _alarm = Mathf.MoveTowards(_alarm, al, dt * 6);
            _determined = Mathf.MoveTowards(_determined, de, dt * 4);
            _blinkT -= dt;
            if (_blinkT <= 0) { _blink = 1; _blinkT = Random.Range(2.5f, 5f); }
            _blink = Mathf.MoveTowards(_blink, 0, dt * 8);
            if (_bsGrin >= 0) _face.SetBlendShapeWeight(_bsGrin, _grin * 100);
            if (_bsAlarm >= 0) _face.SetBlendShapeWeight(_bsAlarm, _alarm * 100);
            if (_bsDetermined >= 0) _face.SetBlendShapeWeight(_bsDetermined, _determined * 100);
            if (_bsBlink >= 0) _face.SetBlendShapeWeight(_bsBlink, (_blink > 0.5f ? 1 : 0) * 100);
        }

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

        public void Kick() { _kickT = 0.35f; Trigger("Kick"); }

        float _punchT;
        int _punchSide;

        public void Punch(int combo)
        {
            _punchT = combo == 3 ? 0.4f : 0.22f;
            _punchSide = combo;
            Trigger("Punch");
        }

        /// <summary>Knocked over: whole body tips back, then gets up.</summary>
        public void Tumble(float time) { _tumble = time; Trigger("Knock"); }

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
            if (_anim != null) { HumanoidUpdate(dt); return; }
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
