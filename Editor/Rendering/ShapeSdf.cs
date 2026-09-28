using UnityEngine;

namespace UISpriteMaker.Editor
{
    internal static class ShapeSdf
    {
        /// <summary>
        /// Signed distance from <paramref name="px"/>,<paramref name="py"/> (relative to the rect center, y-up)
        /// to a rounded rect with half extents <paramref name="hw"/>,<paramref name="hh"/>.
        /// Radii are (topLeft, topRight, bottomRight, bottomLeft). Negative inside.
        /// </summary>
        public static float RoundedRect(float px, float py, float hw, float hh, Vector4 radii)
        {
            float r = px > 0f
                ? (py > 0f ? radii.y : radii.z)
                : (py > 0f ? radii.x : radii.w);

            float qx = Mathf.Abs(px) - hw + r;
            float qy = Mathf.Abs(py) - hh + r;
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            return Mathf.Min(Mathf.Max(qx, qy), 0f) + Mathf.Sqrt(ox * ox + oy * oy) - r;
        }

        /// <summary>Anti-aliased coverage of the region where distance &lt; 0.</summary>
        public static float Coverage(float d) => Mathf.Clamp01(0.5f - d);
    }
}
