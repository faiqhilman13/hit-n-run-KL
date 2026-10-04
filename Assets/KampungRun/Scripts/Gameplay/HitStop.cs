using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Hit-stop: the world all but freezes for a few hundredths of a second when a blow lands, so hits feel
    /// heavy (every good brawler does it). Time slows to a crawl rather than stopping, and it never touches a
    /// paused game: if something else has changed the time scale (the pause menu), it is left alone.
    /// </summary>
    public static class HitStop
    {
        const float Crawl = 0.05f;
        static float _until;
        static bool _active;
        static Runner _runner;

        public static void Do(float seconds)
        {
            if (Time.timeScale <= 0f || seconds <= 0f) return;            // paused (or nothing to do)
            if (!_active)
            {
                if (!Mathf.Approximately(Time.timeScale, 1f)) return;    // someone else owns the clock
                Time.timeScale = Crawl;
                _active = true;
            }
            _until = Mathf.Max(_until, Time.unscaledTime + seconds);
            if (_runner == null) _runner = new GameObject("HitStop").AddComponent<Runner>();
        }

        /// <summary>Drop any freeze at once (scene changes, cutscenes).</summary>
        public static void Clear()
        {
            if (_active && Mathf.Approximately(Time.timeScale, Crawl)) Time.timeScale = 1f;
            _active = false;
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                if (!_active || Time.unscaledTime < _until) return;
                Clear();
            }

            void OnDestroy() => Clear();
        }
    }
}
