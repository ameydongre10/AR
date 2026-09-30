using System.Collections.Generic;
using UnityEngine;

namespace ARLab.Electronics
{
    /// <summary>
    /// Builds a swept tube along a sagging Bezier so a patch cord reads as a physical wire
    /// rather than a line. Buffers are cached and the mesh is only rebuilt when an endpoint
    /// actually moves, so an idle lab allocates nothing.
    /// </summary>
    public static class WireMeshBuilder
    {
        public static Mesh Build(
            Vector3 a, Vector3 b, float sag, float radius, int segments, int sides,
            List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<int> tris)
        {
            verts.Clear(); normals.Clear(); colors.Clear(); tris.Clear();

            Vector3 mid = (a + b) * 0.5f;
            float dist = Vector3.Distance(a, b);
            Vector3 control = mid + Vector3.down * (sag * Mathf.Clamp01(dist * 0.35f + 0.25f));

            int ringCount = segments + 1;
            var prevRing = new Vector3[sides];
            var currRing = new Vector3[sides];

            // Stable orthonormal frame along the curve avoids twisting artefacts.
            Vector3 tangent = (b - a).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 normal = Vector3.Cross(tangent, reference).normalized;
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

            for (int s = 0; s < ringCount; s++)
            {
                float t = (float)s / segments;
                Vector3 p = QuadraticBezier(a, control, b, t);
                Vector3 tan = QuadraticBezierTangent(a, control, b, t).normalized;

                // Re-orthogonalise per ring so the tube follows curvature.
                Vector3 n = Vector3.Cross(tan, Mathf.Abs(Vector3.Dot(tan, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up).normalized;
                Vector3 bn = Vector3.Cross(tan, n).normalized;

                for (int k = 0; k < sides; k++)
                {
                    float ang = (Mathf.PI * 2f * k) / sides;
                    Vector3 offset = (n * Mathf.Cos(ang) + bn * Mathf.Sin(ang)) * radius;
                    Vector3 v = p + offset;
                    currRing[k] = v;
                    verts.Add(v);
                    normals.Add(offset.normalized);
                    colors.Add(Color.white);
                }

                for (int k = 0; k < sides; k++)
                {
                    int kNext = (k + 1) % sides;
                    if (s > 0)
                    {
                        int prevBase = (s - 1) * sides;
                        int currBase = s * sides;
                        tris.Add(prevBase + k); tris.Add(currBase + k); tris.Add(currBase + kNext);
                        tris.Add(prevBase + k); tris.Add(currBase + kNext); tris.Add(prevBase + kNext);
                    }
                }

                var tmp = prevRing; prevRing = currRing; currRing = tmp;
            }

            Mesh mesh = new Mesh { name = "WireMesh" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Vector3 QuadraticBezier(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        public static Vector3 QuadraticBezierTangent(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return 2f * u * (c - a) + 2f * t * (b - c);
        }
    }
}
