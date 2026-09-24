using System.Collections.Generic;
using UnityEngine;

namespace KampungRun
{
    /// <summary>
    /// Registry of the Blender models + the LatInk shader. Lives at Resources/GameAssets
    /// and is filled by the "Kampung Run/Rebuild Game Assets" editor command.
    /// </summary>
    [CreateAssetMenu(menuName = "Kampung Run/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        public Shader latInk;
        public Texture2DArray surfaces;   // painted surface detail (Tools/gen_surfaces.py)
        public Shader beam;
        public Shader sky;
        public Shader text3d;
        public RuntimeAnimatorController humanController;
        public List<GameObject> models = new List<GameObject>();

        Dictionary<string, GameObject> _lookup;
        static GameAssets _instance;

        public static GameAssets I
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<GameAssets>("GameAssets");
                return _instance;
            }
        }

        public GameObject Model(string name)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, GameObject>();
                foreach (var m in models)
                    if (m != null) _lookup[m.name] = m;
            }
            _lookup.TryGetValue(name, out var go);
            if (go == null) Debug.LogWarning($"[GameAssets] missing model '{name}'");
            return go;
        }
    }
}
