using UnityEngine;
using ARLab.Electronics;

namespace ARLab.Laboratory
{
    /// <summary>
    /// Central material construction so the whole lab shares a small set of URP materials.
    /// Built once and cached; nothing is created per frame.
    /// </summary>
    public static class LabMaterials
    {
        private static Material _standard;
        private static Shader _standardShader;
        private static Shader _unlitShader;
        private static Shader _emissiveShader;

        /// <summary>
        /// Removes a generated object. Object.Destroy throws in edit mode, and this project
        /// builds most of its scene from code that also runs in editor tooling and tests, so
        /// the mode-aware form is the one callers should use.
        /// </summary>
        public static void Discard(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        private static Shader FindShader(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Shader s = Shader.Find(names[i]);
                if (s != null) return s;
            }
            // Never return null: `new Material(null)` throws, and headless/EditMode runs can
            // resolve none of the pipeline shaders above.
            return Shader.Find("Hidden/InternalErrorShader");
        }

        private static Shader StandardShader()
        {
            if (_standardShader == null)
                _standardShader = FindShader("Universal Render Pipeline/Lit", "Standard", "Diffuse");
            return _standardShader;
        }

        public static Material Standard()
        {
            if (_standard == null)
            {
                _standard = new Material(StandardShader()) { name = "LabStandard" };
            }
            return _standard;
        }

        /// <summary>Fresnel-free unlit material for wire tubes and probes that read well in AR.</summary>
        public static Shader UnlitTransparent()
        {
            if (_unlitShader == null)
                _unlitShader = FindShader("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");
            return _unlitShader;
        }

        public static Material Emissive(Color c, float intensity = 2.0f)
        {
            if (_emissiveShader == null)
                _emissiveShader = FindShader("Universal Render Pipeline/Lit", "Standard");
            var m = new Material(_emissiveShader) { name = "LabEmissive" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
            m.EnableKeyword("_EMISSION");
            return m;
        }

        public static Material Colored(Color c, float metallic = 0f, float smoothness = 0.25f)
        {
            Material m = new Material(StandardShader()) { name = "LabColored" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Colour used for a logical level everywhere in the lab.</summary>
        public static Color ForLogic(LogicValue v)
        {
            switch (v)
            {
                case LogicValue.High: return new Color(0.22f, 0.95f, 0.42f);
                case LogicValue.Low: return new Color(0.24f, 0.36f, 0.78f);
                default: return new Color(0.95f, 0.62f, 0.12f);
            }
        }

        /// <summary>
        /// Translucent overlay material. Configures the URP transparent surface explicitly
        /// rather than relying on keyword guesses, because a plane overlay that silently
        /// renders opaque hides the whole point of it.
        /// </summary>
        public static Material TransparentOverlay(Color c, float alpha)
        {
            Shader shader = FindShader("Universal Render Pipeline/Unlit", "Unlit/Transparent", "Sprites/Default", "Unlit/Color");
            var m = new Material(shader) { name = "LabOverlay" };

            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);          // 1 = Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);               // 0 = Alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            Color tinted = c;
            tinted.a = alpha;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tinted);

            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Overlays are asked for repeatedly (reticle, plane visual) and are read-only, so
            // one instance per colour is enough. Caching also keeps the material out of any
            // scene it is created while, rather than leaking an asset per call.
            if (_overlayCache.TryGetValue(tinted, out Material cached)) return cached;
            _overlayCache[tinted] = m;
            return m;
        }

        private static readonly System.Collections.Generic.Dictionary<Color, Material> _overlayCache =
            new System.Collections.Generic.Dictionary<Color, Material>();
    }
}
