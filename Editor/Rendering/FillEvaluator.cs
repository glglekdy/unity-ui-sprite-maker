using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>Thread-safe fill sampler. Gradients are baked to a lookup table on the main thread.</summary>
    internal readonly struct FillEvaluator
    {
        const int LutSize = 512;

        readonly FillMode _mode;
        readonly Color _solid;
        readonly Color[] _lut;
        readonly float _dirX, _dirY, _extent;
        readonly float _centerX, _centerY, _radius;

        /// <param name="hw">Half width of the filled box in pixels.</param>
        /// <param name="hh">Half height of the filled box in pixels.</param>
        public FillEvaluator(FillSettings f, float hw, float hh)
        {
            _mode = f.mode;
            _solid = f.color;
            _lut = null;
            _dirX = _dirY = _extent = _centerX = _centerY = _radius = 0f;

            if (_mode == FillMode.Solid) return;

            var gradient = f.gradient ?? new Gradient();
            _lut = new Color[LutSize];
            for (int i = 0; i < LutSize; i++)
                _lut[i] = gradient.Evaluate(i / (LutSize - 1f));

            float rad = f.angle * Mathf.Deg2Rad;
            _dirX = Mathf.Cos(rad);
            _dirY = Mathf.Sin(rad);
            _extent = Mathf.Max(1e-4f, Mathf.Abs(hw * _dirX) + Mathf.Abs(hh * _dirY));

            _centerX = (f.radialCenter.x - 0.5f) * hw * 2f;
            _centerY = (f.radialCenter.y - 0.5f) * hh * 2f;
            _radius = Mathf.Max(1e-4f, f.radialRadius * Mathf.Sqrt(hw * hw + hh * hh));
        }

        /// <summary>Color at (<paramref name="lx"/>, <paramref name="ly"/>) pixels from the box center, y up.</summary>
        public Color Evaluate(float lx, float ly)
        {
            float t;
            switch (_mode)
            {
                case FillMode.LinearGradient:
                    t = (lx * _dirX + ly * _dirY) / _extent * 0.5f + 0.5f;
                    break;
                case FillMode.RadialGradient:
                    float dx = lx - _centerX, dy = ly - _centerY;
                    t = Mathf.Sqrt(dx * dx + dy * dy) / _radius;
                    break;
                default:
                    return _solid;
            }
            return _lut[(int)(Mathf.Clamp01(t) * (LutSize - 1) + 0.5f)];
        }
    }
}
