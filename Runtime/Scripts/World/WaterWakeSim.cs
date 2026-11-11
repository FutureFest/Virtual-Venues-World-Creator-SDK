using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    /// <summary>
    /// Boat wakes: a small ripple simulation on a grid over one water surface (<see cref="WaterWaves"/> owns it).
    /// Each cell holds the water's height above the still line and its vertical speed; ripples spread by the 2D wave
    /// equation, the way Crest's dynamic waves do. A moving hull pushes water up ahead of it and pulls it down behind
    /// (<see cref="Disturb"/>), boats float on the result (<see cref="Sample"/>) and the water shader draws it
    /// (<see cref="WriteHalfs"/>). Plain arrays on the CPU: it runs the same on web, iOS and a dedicated server, the
    /// physics reads it with no GPU readback, and it only visits the cells that are moving (none = asleep, no cost).
    /// </summary>
    public class WaterWakeSim
    {
        private const float Quiet = 0.002f;         // below this a cell counts as still (m, m/s, foam)
        private const int EdgeFeather = 6;          // cells over which ripples die at the grid edge (no reflections)
        private const float InnerOffset = 0.109f;   // Crest's sphere interaction shape
        private const float InnerMultiplier = 1.55f;

        private readonly int _width;
        private readonly int _height;
        private readonly float _cell;
        private readonly float _originX;
        private readonly float _originZ;
        private readonly float[] _h;
        private readonly float[] _v;
        private readonly float[] _foam;

        // The cells that may be moving, inclusive. Empty (asleep) when _minX > _maxX.
        private int _minX = 1;
        private int _maxX = 0;
        private int _minY = 1;
        private int _maxY = 0;

        public int Width => _width;
        public int Height => _height;
        /// <summary>Cell size, metres.</summary>
        public float Cell => _cell;
        /// <summary>World x and z of cell (0, 0)'s centre.</summary>
        public Vector2 Origin => new Vector2(_originX, _originZ);
        /// <summary>False while nothing is moving: <see cref="Step"/> and <see cref="Sample"/> cost nothing.</summary>
        public bool Awake => _minX <= _maxX;

        /// <summary>A grid over the footprint of <paramref name="area"/>, at most <paramref name="maxCells"/> on its longer side.</summary>
        public WaterWakeSim(Bounds area, int maxCells = 256, float minCell = 0.5f)
        {
            float longSide = Mathf.Max(area.size.x, area.size.z);
            _cell = Mathf.Max(minCell, longSide / Mathf.Max(8, maxCells));
            _width = Mathf.Max(8, Mathf.CeilToInt(area.size.x / _cell));
            _height = Mathf.Max(8, Mathf.CeilToInt(area.size.z / _cell));
            _originX = area.min.x + _cell * 0.5f;
            _originZ = area.min.z + _cell * 0.5f;
            _h = new float[_width * _height];
            _v = new float[_width * _height];
            _foam = new float[_width * _height];
        }

        /// <summary>
        /// A hull of this <paramref name="radius"/> at world xz <paramref name="position"/> moving at
        /// <paramref name="velocity"/> (m/s) for <paramref name="dt"/> seconds: water rises ahead of it, drops behind
        /// it, and foam is left under it. Crest's sphere interaction, on one grid.
        /// </summary>
        public void Disturb(Vector2 position, Vector2 velocity, float radius, float strength, float dt)
        {
            float speed = velocity.magnitude;
            if (speed < 0.05f || strength <= 0f) { return; }

            float minWavelength = 2f * _cell;
            float a = 1.43f / minWavelength;
            float reach = radius * 3f + minWavelength;
            int x0 = Mathf.Max(1, Mathf.FloorToInt((position.x - reach - _originX) / _cell));
            int x1 = Mathf.Min(_width - 2, Mathf.CeilToInt((position.x + reach - _originX) / _cell));
            int y0 = Mathf.Max(1, Mathf.FloorToInt((position.y - reach - _originZ) / _cell));
            int y1 = Mathf.Min(_height - 2, Mathf.CeilToInt((position.y + reach - _originZ) / _cell));
            if (x0 > x1 || y0 > y1) { return; }

            float gain = strength * 0.2f / minWavelength * dt;
            float foam = Mathf.Clamp01(speed / 6f);
            for (int y = y0; y <= y1; y++)
            {
                float dz = _originZ + y * _cell - position.y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = _originX + x * _cell - position.x;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    float sdf = distance - radius;
                    int i = y * _width + x;
                    if (sdf < radius * 0.2f && foam > _foam[i]) { _foam[i] = foam; }
                    if (sdf <= 0f && sdf >= -radius * InnerOffset) { continue; }

                    float along = distance > 0.0001f ? (dx * velocity.x + dz * velocity.y) / distance : 0f;
                    float ax = a * Mathf.Abs(sdf);
                    float ax2 = ax * ax;
                    float push = along * ax / (1f + ax2 * ax2 * ax2);
                    _v[i] += gain * (sdf > 0f ? push : -push * InnerMultiplier);
                }
            }
            Grow(x0, x1, y0, y1);
        }

        /// <summary>
        /// Advance the ripples by <paramref name="dt"/> seconds. <paramref name="speed"/> = how fast they travel (m/s),
        /// <paramref name="fade"/> = how quickly they die (per second), <paramref name="foamFade"/> the same for foam,
        /// <paramref name="maxHeight"/> = the tallest a ripple may get (m).
        /// </summary>
        public void Step(float dt, float speed, float fade, float foamFade, float maxHeight)
        {
            if (!Awake) { return; }
            // Ripples reach one more cell per step at most (Courant limit below).
            Grow(_minX - 1, _maxX + 1, _minY - 1, _maxY + 1);
            int x0 = _minX, x1 = _maxX, y0 = _minY, y1 = _maxY;

            float c = Mathf.Min(speed, 0.7f * _cell / dt);
            float k = dt * c * c / (_cell * _cell);
            float damp = 1f - Mathf.Min(1f, fade * dt);
            float foamDamp = 1f - Mathf.Min(1f, foamFade * dt);
            int w = _width;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    _v[i] = (_v[i] + k * (_h[i - 1] + _h[i + 1] + _h[i - w] + _h[i + w] - 4f * _h[i])) * damp;
                }
            }

            int nx0 = _width, nx1 = -1, ny0 = _height, ny1 = -1;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                float edgeY = Mathf.Min(y, _height - 1 - y);
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    float h = _h[i] + dt * _v[i];
                    float edge = Mathf.Min(edgeY, Mathf.Min(x, _width - 1 - x));
                    if (edge < EdgeFeather) { float f = edge / EdgeFeather; h *= f; _v[i] *= f; }
                    if (h > maxHeight) { h = maxHeight; } else if (h < -maxHeight) { h = -maxHeight; }
                    float foam = _foam[i] * foamDamp;
                    // NaN never passes these tests, so a bad value is dropped here instead of spreading.
                    bool moving = h > Quiet || h < -Quiet || _v[i] > Quiet || _v[i] < -Quiet;
                    if (!moving) { h = 0f; _v[i] = 0f; }
                    if (!(foam > Quiet)) { foam = 0f; }
                    _h[i] = h;
                    _foam[i] = foam;
                    if (!moving && foam == 0f) { continue; }
                    if (x < nx0) { nx0 = x; }
                    if (x > nx1) { nx1 = x; }
                    if (y < ny0) { ny0 = y; }
                    if (y > ny1) { ny1 = y; }
                }
            }
            _minX = nx0; _maxX = nx1; _minY = ny0; _maxY = ny1;
        }

        /// <summary>Ripple height above the still line at world (x, z), metres, and its slope (dh/dx, dh/dz).</summary>
        public float Sample(float x, float z, out Vector2 slope)
        {
            slope = Vector2.zero;
            if (!Awake) { return 0f; }
            float gx = (x - _originX) / _cell;
            float gz = (z - _originZ) / _cell;
            int ix = Mathf.FloorToInt(gx);
            int iz = Mathf.FloorToInt(gz);
            if (ix < _minX - 1 || ix > _maxX || iz < _minY - 1 || iz > _maxY || ix < 0 || iz < 0 || ix >= _width - 1 || iz >= _height - 1) { return 0f; }
            float fx = gx - ix;
            float fz = gz - iz;
            int i = iz * _width + ix;
            float h00 = _h[i], h10 = _h[i + 1], h01 = _h[i + _width], h11 = _h[i + _width + 1];
            slope = new Vector2(Mathf.Lerp(h10 - h00, h11 - h01, fz), Mathf.Lerp(h01 - h00, h11 - h10, fx)) / _cell;
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
        }

        /// <summary>
        /// Fill an RGHalf pixel buffer (2 half floats per cell, row by row): R = height, G = foam. Returns false when
        /// nothing changed since the last call (asleep and already written flat).
        /// </summary>
        public bool WriteHalfs(ushort[] pixels)
        {
            if (!Awake)
            {
                if (!_pixelsDirty) { return false; }
                System.Array.Clear(pixels, 0, pixels.Length);
                _pixelsDirty = false;
                return true;
            }
            // Cells that went still since the last write must be flattened too: rewrite the union of both rects.
            int x0 = Mathf.Min(_minX, _writtenMinX), x1 = Mathf.Max(_maxX, _writtenMaxX);
            int y0 = Mathf.Min(_minY, _writtenMinY), y1 = Mathf.Max(_maxY, _writtenMaxY);
            for (int y = y0; y <= y1; y++)
            {
                int row = y * _width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    pixels[i * 2] = Mathf.FloatToHalf(_h[i]);
                    pixels[i * 2 + 1] = Mathf.FloatToHalf(_foam[i]);
                }
            }
            _writtenMinX = _minX; _writtenMaxX = _maxX; _writtenMinY = _minY; _writtenMaxY = _maxY;
            _pixelsDirty = true;
            return true;
        }

        private bool _pixelsDirty = false;
        private int _writtenMinX = int.MaxValue;
        private int _writtenMaxX = -1;
        private int _writtenMinY = int.MaxValue;
        private int _writtenMaxY = -1;

        private void Grow(int x0, int x1, int y0, int y1)
        {
            x0 = Mathf.Max(1, x0); y0 = Mathf.Max(1, y0);
            x1 = Mathf.Min(_width - 2, x1); y1 = Mathf.Min(_height - 2, y1);
            if (!Awake) { _minX = x0; _maxX = x1; _minY = y0; _maxY = y1; return; }
            if (x0 < _minX) { _minX = x0; }
            if (x1 > _maxX) { _maxX = x1; }
            if (y0 < _minY) { _minY = y0; }
            if (y1 > _maxY) { _maxY = y1; }
        }
    }
}
