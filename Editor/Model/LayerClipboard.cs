using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>Holds one layer so it can go through Unity serialization (deep copies).</summary>
    internal sealed class LayerClipboard : ScriptableObject
    {
        [SerializeReference] public Layer layer;

        /// <summary>Deep copy of a layer and its children, with new ids.</summary>
        public static Layer Clone(Layer layer)
        {
            var from = CreateInstance<LayerClipboard>();
            var to = CreateInstance<LayerClipboard>();
            try
            {
                from.layer = layer;
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), to);
                var copy = to.layer;
                copy.RegenerateIds();
                return copy;
            }
            finally
            {
                DestroyImmediate(from);
                DestroyImmediate(to);
            }
        }
    }
}
