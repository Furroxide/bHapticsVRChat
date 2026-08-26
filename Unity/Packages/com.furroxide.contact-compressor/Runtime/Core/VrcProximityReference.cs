using System;

namespace Furroxide.ContactCompressor
{
    /// <summary>
    /// A literal port of VRChat's own contact proximity maths, decompiled from
    /// <c>VRC.Dynamics.dll</c> in <c>com.vrchat.base</c> 3.10.4.
    ///
    /// This is not used to drive anything at runtime. It exists so that both the tests and the
    /// editor's validation pass can check the encoder against what the game actually computes,
    /// rather than against the algebra the encoder was derived from - a mistake in the derivation
    /// would otherwise be invisible, because both sides would share it.
    ///
    /// Ported from:
    ///   ContactManager.UpdateReceiversFunctions.CalcProximity  (ShapeType.Box branch)
    ///   ContactManager.UpdateReceivers.Execute                 (math.max across senders)
    ///   CollisionShapes.Sphere.ClosestPoint / Capsule.ClosestPoint
    ///   MathUtil.ClosestPointOnPlane / ClosestPointOnLineSegment
    ///
    /// Deliberately engine-free, like the rest of this assembly, so the same source compiles into
    /// an OSC consumer outside Unity.
    /// </summary>
    public static class VrcProximityReference
    {
        /// <summary>Minimal 3-vector so this stays free of <c>UnityEngine</c>.</summary>
        public struct Vec
        {
            public float X, Y, Z;

            public Vec(float x, float y, float z) { X = x; Y = y; Z = z; }

            public float this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;

            public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec operator *(Vec a, float s) => new Vec(a.X * s, a.Y * s, a.Z * s);
            public static Vec operator /(Vec a, float s) => new Vec(a.X / s, a.Y / s, a.Z / s);

            public static float Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public float Length => (float)Math.Sqrt(Dot(this, this));
            public float LengthSquared => Dot(this, this);

            public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
        }

        /// <summary>A contact sender. A sphere when <see cref="Pos0"/> equals <see cref="Pos1"/>, otherwise a capsule.</summary>
        public struct Sender
        {
            public Vec Pos0, Pos1;
            public float Radius;

            public static Sender Sphere(Vec at, float radius)
                => new Sender { Pos0 = at, Pos1 = at, Radius = radius };

            /// <param name="tiltDegrees">Rotation of the capsule axis within the XY plane; 0 is along +Y.</param>
            public static Sender Capsule(Vec centre, float length, float radius, float tiltDegrees)
            {
                float half = Math.Max(0f, length * 0.5f - radius);
                float a = tiltDegrees * (float)Math.PI / 180f;
                var dir = new Vec((float)Math.Sin(a), (float)Math.Cos(a), 0f);
                return new Sender { Pos0 = centre - dir * half, Pos1 = centre + dir * half, Radius = radius };
            }

            public bool IsSphere => (Pos1 - Pos0).LengthSquared < 1e-12f;

            /// <summary>ShapeData.GetMidpoint(): sphere returns its centre, capsule the midpoint of its segment.</summary>
            public Vec Midpoint => IsSphere ? Pos0 : (Pos0 + Pos1) * 0.5f;

            /// <summary>ShapeData.GetClosestPoint(point, onSurface: false).</summary>
            public Vec ClosestPoint(Vec point)
            {
                if (IsSphere)
                {
                    Vec d = point - Pos0;
                    float n = d.Length;
                    if (n > Radius)
                    {
                        Vec dir = n > 0f ? d / n : new Vec(0, 0, 1);
                        return Pos0 + dir * Radius;
                    }
                    return point;
                }

                Vec seg = ClosestPointOnLineSegment(Pos0, Pos1, point);
                Vec od = point - seg;
                float n2 = od.LengthSquared;
                if (n2 > Radius * Radius)
                {
                    Vec dir = n2 > 0f ? od / (float)Math.Sqrt(n2) : new Vec(0, 0, 1);
                    return seg + dir * Radius;
                }
                return point;
            }
        }

        /// <summary>A box contact receiver. Basis columns are its local axes expressed in region space.</summary>
        public struct BoxReceiver
        {
            public Vec Centre;
            public Vec BasisX, BasisY, BasisZ;
            public Vec Size;
            public bool UseFaceProximity;

            Vec Rotate(Vec v) => BasisX * v.X + BasisY * v.Y + BasisZ * v.Z;
            Vec InverseRotate(Vec v) => new Vec(Vec.Dot(v, BasisX), Vec.Dot(v, BasisY), Vec.Dot(v, BasisZ));

            public float CalcProximity(Sender sender)
            {
                Vec half = Size * 0.5f;
                Vec reference;
                if (UseFaceProximity)
                {
                    Vec planeOrigin = Centre + Rotate(new Vec(0f, 0f, half.Z));
                    Vec planeNormal = Rotate(new Vec(0f, 0f, -1f));
                    reference = ClosestPointOnPlane(planeOrigin, planeNormal, sender.Midpoint);
                }
                else
                {
                    reference = Centre;
                }

                Vec d = InverseRotate(sender.ClosestPoint(reference) - reference);

                float num = UseFaceProximity
                    ? Unlerp(0f, -half.Z * 2f, d.Z)
                    : Math.Max(Math.Abs(d.X / half.X), Math.Max(Math.Abs(d.Y / half.Y), Math.Abs(d.Z / half.Z)));

                return 1f - Clamp01(num);
            }
        }

        /// <summary>
        /// One of the six receivers of a fully encoded region: the one whose local +Z points along
        /// region axis <paramref name="axis"/> in the given direction.
        /// </summary>
        public static BoxReceiver MakeReceiver(Vec centre, Vec boxSize, int axis, int sign)
        {
            var x = new Vec(1, 0, 0);
            var z = new Vec(0, 0, 1);

            Vec bz = axis == 0 ? x : axis == 1 ? new Vec(0, 1, 0) : z;
            Vec bx = axis == 2 ? x : z;
            if (sign < 0) { bz = bz * -1f; bx = bx * -1f; }
            Vec by = Cross(bz, bx);

            float lz = axis == 0 ? boxSize.X : axis == 1 ? boxSize.Y : boxSize.Z;
            float lx = axis == 2 ? boxSize.X : boxSize.Z;
            float ly = axis == 0 ? boxSize.Y : axis == 1 ? boxSize.X : boxSize.Y;

            return new BoxReceiver
            {
                Centre = centre,
                BasisX = bx,
                BasisY = by,
                BasisZ = bz,
                Size = new Vec(lx, ly, lz),
                UseFaceProximity = true
            };
        }

        /// <summary>
        /// What the six receivers of a region would report, as (plus, minus) arrays indexed X, Y, Z.
        /// Multiple senders combine with <c>math.max</c>, matching <c>UpdateReceivers.Execute</c>.
        /// </summary>
        public static void ReadRegion(Vec centre, Vec boxSize, Sender[] senders, float[] plus, float[] minus)
        {
            if (senders == null) throw new ArgumentNullException(nameof(senders));
            if (plus == null || plus.Length < 3) throw new ArgumentException("Expected three entries.", nameof(plus));
            if (minus == null || minus.Length < 3) throw new ArgumentException("Expected three entries.", nameof(minus));

            for (int axis = 0; axis < 3; axis++)
            {
                var rp = MakeReceiver(centre, boxSize, axis, +1);
                var rn = MakeReceiver(centre, boxSize, axis, -1);

                plus[axis] = 0f;
                minus[axis] = 0f;

                foreach (var sender in senders)
                {
                    plus[axis] = Math.Max(plus[axis], rp.CalcProximity(sender));
                    minus[axis] = Math.Max(minus[axis], rn.CalcProximity(sender));
                }
            }
        }

        /// <summary>
        /// Convenience overload that allocates its own result arrays. Prefer the array-filling
        /// version when sweeping many points, such as the editor's validation pass.
        /// </summary>
        public static (float[] plus, float[] minus) ReadRegion(Vec centre, Vec boxSize, params Sender[] senders)
        {
            var plus = new float[3];
            var minus = new float[3];
            ReadRegion(centre, boxSize, senders, plus, minus);
            return (plus, minus);
        }

        // ---- MathUtil ----

        public static Vec ClosestPointOnPlane(Vec planeOrigin, Vec planeNormal, Vec point)
            => point + planeNormal * Vec.Dot(planeNormal, planeOrigin - point);

        public static Vec ClosestPointOnLineSegment(Vec a, Vec b, Vec p)
        {
            Vec ab = b - a;
            float num = Vec.Dot(p - a, ab);
            if (num <= 0f) return a;
            float den = Vec.Dot(ab, ab);
            if (den <= num) return b;
            return a + ab * (num / den);
        }

        static Vec Cross(Vec a, Vec b)
            => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        static float Unlerp(float a, float b, float v) => (v - a) / (b - a);
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
