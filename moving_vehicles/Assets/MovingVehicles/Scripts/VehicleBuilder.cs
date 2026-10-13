// VehicleBuilder.cs
// Procedurally creates low-poly vehicle GameObjects at runtime.
// No external assets required – all geometry is built from code.
//
// Vehicle forward = +Z   (matches Quaternion.LookRotation on the path direction)
// Vehicle right   = +X   (driver's left / US passenger side)
// Vehicle up      = +Y
// Y = 0 is ground level.

using System.Collections.Generic;
using UnityEngine;

namespace MovingVehicles
{
    public static class VehicleBuilder
    {
        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        public enum VehicleType { Sedan = 0, Hatchback = 1, PickupTruck = 2, TractorTrailer = 3 }

        public static GameObject Build(VehicleType type, Color bodyColor)
        {
            switch (type)
            {
                case VehicleType.Sedan:         return BuildSedan(bodyColor);
                case VehicleType.Hatchback:     return BuildHatchback(bodyColor);
                case VehicleType.PickupTruck:   return BuildPickupTruck(bodyColor);
                case VehicleType.TractorTrailer:return BuildTractorTrailer(bodyColor);
                default:                        return BuildSedan(bodyColor);
            }
        }

        // ---------------------------------------------------------------
        // Shared mesh cache – created once, reused every spawn
        // ---------------------------------------------------------------
        private static Mesh _cubeMesh;
        private static Mesh _cylinderMesh8;    // 8-sided – car/truck wheels
        private static Mesh _cylinderMesh6;    // 6-sided – semi exhaust stacks

        // Full getter syntax for Unity 2018.2 / C# 4 compatibility
        private static Mesh CubeMesh
        {
            get { return _cubeMesh    ?? (_cubeMesh    = CreateCubeMesh()); }
        }
        private static Mesh Cylinder8
        {
            get { return _cylinderMesh8 ?? (_cylinderMesh8 = CreateCylinderMesh(1f, 1f, 8)); }
        }
        private static Mesh Cylinder6
        {
            get { return _cylinderMesh6 ?? (_cylinderMesh6 = CreateCylinderMesh(1f, 1f, 6)); }
        }

        // ---------------------------------------------------------------
        // Shared material helpers – one instance per role (lazy-init)
        // ---------------------------------------------------------------
        private static Material _windowMat;
        private static Material _wheelMat;
        private static Material _darkMat;       // undercarriage, chassis
        private static Material _exhaustMat;    // exhaust stacks

        private static Material WindowMat
        {
            get
            {
                if (_windowMat == null)
                {
                    _windowMat = new Material(Shader.Find("Standard"));
                    _windowMat.name = "MV_Window";
                    _windowMat.color = new Color(0.22f, 0.30f, 0.42f);
                    _windowMat.SetFloat("_Metallic",   0.1f);
                    _windowMat.SetFloat("_Glossiness", 0.7f);
                }
                return _windowMat;
            }
        }

        private static Material WheelMat
        {
            get
            {
                if (_wheelMat == null)
                {
                    _wheelMat = new Material(Shader.Find("Standard"));
                    _wheelMat.name = "MV_Wheel";
                    _wheelMat.color = new Color(0.12f, 0.12f, 0.12f);
                    _wheelMat.SetFloat("_Metallic",   0f);
                    _wheelMat.SetFloat("_Glossiness", 0.08f);
                }
                return _wheelMat;
            }
        }

        private static Material DarkMat
        {
            get
            {
                if (_darkMat == null)
                {
                    _darkMat = new Material(Shader.Find("Standard"));
                    _darkMat.name = "MV_Dark";
                    _darkMat.color = new Color(0.10f, 0.10f, 0.10f);
                    _darkMat.SetFloat("_Metallic",   0.1f);
                    _darkMat.SetFloat("_Glossiness", 0.2f);
                }
                return _darkMat;
            }
        }

        private static Material ExhaustMat
        {
            get
            {
                if (_exhaustMat == null)
                {
                    _exhaustMat = new Material(Shader.Find("Standard"));
                    _exhaustMat.name = "MV_Exhaust";
                    _exhaustMat.color = new Color(0.30f, 0.28f, 0.25f);
                    _exhaustMat.SetFloat("_Metallic",   0.6f);
                    _exhaustMat.SetFloat("_Glossiness", 0.4f);
                }
                return _exhaustMat;
            }
        }

        private static Material MakeBodyMat(Color c)
        {
            var m = new Material(Shader.Find("Standard"));
            m.name = "MV_Body";
            m.color = c;
            m.SetFloat("_Metallic",   0.25f);
            m.SetFloat("_Glossiness", 0.55f);
            return m;
        }

        // ---------------------------------------------------------------
        // Vehicle definitions
        // ---------------------------------------------------------------

        // ── Sedan (Car Type 1) ──────────────────────────────────────────
        // Classic 3-box saloon, ~4.4 m long
        private static GameObject BuildSedan(Color bodyColor)
        {
            var root = new GameObject("Vehicle_Sedan");
            var body = MakeBodyMat(bodyColor);

            // Lower body
            AddBox(root, "Body_Lower",
                new Vector3(1.76f, 0.60f, 4.20f),
                new Vector3(0f, 0.55f, 0f), body);

            // Upper cabin (shifted slightly toward rear for boot/trunk look)
            AddBox(root, "Body_Upper",
                new Vector3(1.65f, 0.58f, 2.20f),
                new Vector3(0f, 1.14f, -0.18f), body);

            // Hood (flat, front)
            AddBox(root, "Hood",
                new Vector3(1.74f, 0.06f, 0.98f),
                new Vector3(0f, 0.88f, 1.60f), body);

            // Trunk lid (flat, rear)
            AddBox(root, "Trunk",
                new Vector3(1.74f, 0.06f, 0.82f),
                new Vector3(0f, 0.85f, -1.52f), body);

            // Windows – front & rear
            AddBox(root, "Win_Front",
                new Vector3(1.56f, 0.46f, 0.04f),
                new Vector3(0f, 1.13f, 0.92f), WindowMat);
            AddBox(root, "Win_Rear",
                new Vector3(1.56f, 0.42f, 0.04f),
                new Vector3(0f, 1.11f, -1.22f), WindowMat);

            // Windows – side (long strip)
            AddBox(root, "Win_Left",
                new Vector3(0.04f, 0.40f, 1.92f),
                new Vector3( 0.845f, 1.11f, -0.25f), WindowMat);
            AddBox(root, "Win_Right",
                new Vector3(0.04f, 0.40f, 1.92f),
                new Vector3(-0.845f, 1.11f, -0.25f), WindowMat);

            // Wheels  (R=0.30, W=0.20, track=1.86)
            AddWheel(root, "Wheel_FL", 0.30f, 0.20f, new Vector3( 0.93f, 0.30f,  1.28f));
            AddWheel(root, "Wheel_FR", 0.30f, 0.20f, new Vector3(-0.93f, 0.30f,  1.28f));
            AddWheel(root, "Wheel_RL", 0.30f, 0.20f, new Vector3( 0.93f, 0.30f, -1.28f));
            AddWheel(root, "Wheel_RR", 0.30f, 0.20f, new Vector3(-0.93f, 0.30f, -1.28f));

            return root;
        }

        // ── Hatchback / Crossover (Car Type 2) ─────────────────────────
        // Taller, more upright, longer roofline, ~4.0 m long
        private static GameObject BuildHatchback(Color bodyColor)
        {
            var root = new GameObject("Vehicle_Hatchback");
            var body = MakeBodyMat(bodyColor);

            // Lower body – slightly shorter and taller than sedan
            AddBox(root, "Body_Lower",
                new Vector3(1.80f, 0.68f, 3.90f),
                new Vector3(0f, 0.59f, 0f), body);

            // Upper cabin – long, covers most of the body (hatchback style)
            AddBox(root, "Body_Upper",
                new Vector3(1.70f, 0.62f, 2.90f),
                new Vector3(0f, 1.21f, -0.10f), body);

            // Windscreen angled cap
            AddBox(root, "Hood",
                new Vector3(1.78f, 0.06f, 0.70f),
                new Vector3(0f, 0.94f, 1.58f), body);

            // Windows – front & rear
            AddBox(root, "Win_Front",
                new Vector3(1.60f, 0.50f, 0.04f),
                new Vector3(0f, 1.20f, 1.35f), WindowMat);
            AddBox(root, "Win_Rear",
                new Vector3(1.60f, 0.50f, 0.04f),
                new Vector3(0f, 1.20f, -1.52f), WindowMat);

            // Side windows
            AddBox(root, "Win_Left",
                new Vector3(0.04f, 0.46f, 2.62f),
                new Vector3( 0.855f, 1.19f, -0.18f), WindowMat);
            AddBox(root, "Win_Right",
                new Vector3(0.04f, 0.46f, 2.62f),
                new Vector3(-0.855f, 1.19f, -0.18f), WindowMat);

            // Wheels (R=0.31, W=0.22)
            AddWheel(root, "Wheel_FL", 0.31f, 0.22f, new Vector3( 0.95f, 0.31f,  1.22f));
            AddWheel(root, "Wheel_FR", 0.31f, 0.22f, new Vector3(-0.95f, 0.31f,  1.22f));
            AddWheel(root, "Wheel_RL", 0.31f, 0.22f, new Vector3( 0.95f, 0.31f, -1.22f));
            AddWheel(root, "Wheel_RR", 0.31f, 0.22f, new Vector3(-0.95f, 0.31f, -1.22f));

            return root;
        }

        // ── Pickup Truck ────────────────────────────────────────────────
        // Classic American pickup, ~5.5 m long
        private static GameObject BuildPickupTruck(Color bodyColor)
        {
            var root = new GameObject("Vehicle_Pickup");
            var body = MakeBodyMat(bodyColor);

            // Full-length lower body / chassis rail
            AddBox(root, "Body_Lower",
                new Vector3(1.90f, 0.72f, 5.40f),
                new Vector3(0f, 0.62f, 0f), body);

            // Cab (front half, tall)
            AddBox(root, "Cab",
                new Vector3(1.82f, 0.82f, 2.10f),
                new Vector3(0f, 1.30f, 1.05f), body);

            // Truck bed walls – left, right, front bulkhead
            AddBox(root, "Bed_WallL",
                new Vector3(0.06f, 0.40f, 1.80f),
                new Vector3( 0.95f, 1.04f, -1.40f), DarkMat);
            AddBox(root, "Bed_WallR",
                new Vector3(0.06f, 0.40f, 1.80f),
                new Vector3(-0.95f, 1.04f, -1.40f), DarkMat);
            AddBox(root, "Bed_Front",
                new Vector3(1.90f, 0.40f, 0.06f),
                new Vector3(0f, 1.04f, -0.50f), DarkMat);
            // Bed floor
            AddBox(root, "Bed_Floor",
                new Vector3(1.90f, 0.06f, 1.84f),
                new Vector3(0f, 0.87f, -1.40f), DarkMat);

            // Cab windows
            AddBox(root, "Win_Front",
                new Vector3(1.70f, 0.56f, 0.04f),
                new Vector3(0f, 1.36f, 2.04f), WindowMat);
            AddBox(root, "Win_Rear",
                new Vector3(1.70f, 0.46f, 0.04f),
                new Vector3(0f, 1.30f, 0.01f), WindowMat);
            AddBox(root, "Win_Left",
                new Vector3(0.04f, 0.46f, 1.84f),
                new Vector3( 0.92f, 1.30f, 1.04f), WindowMat);
            AddBox(root, "Win_Right",
                new Vector3(0.04f, 0.46f, 1.84f),
                new Vector3(-0.92f, 1.30f, 1.04f), WindowMat);

            // Wheels (slightly larger, R=0.34, W=0.24)
            AddWheel(root, "Wheel_FL", 0.34f, 0.24f, new Vector3( 1.00f, 0.34f,  1.70f));
            AddWheel(root, "Wheel_FR", 0.34f, 0.24f, new Vector3(-1.00f, 0.34f,  1.70f));
            AddWheel(root, "Wheel_RL", 0.34f, 0.24f, new Vector3( 1.00f, 0.34f, -1.58f));
            AddWheel(root, "Wheel_RR", 0.34f, 0.24f, new Vector3(-1.00f, 0.34f, -1.58f));

            return root;
        }

        // ── Tractor / Trailer (Semi) ────────────────────────────────────
        // 18-wheeler, ~16 m long total
        // Vehicle origin is at the geometric center of the whole rig.
        // Front of cab  = Z +8.0
        // Rear of trailer = Z -8.0
        private static GameObject BuildTractorTrailer(Color bodyColor)
        {
            var root = new GameObject("Vehicle_Semi");
            var body  = MakeBodyMat(bodyColor);
            var white = MakeBodyMat(new Color(0.92f, 0.92f, 0.92f));   // trailer is white

            // ── TRACTOR (front half of rig, center ~Z +5.0) ──
            // Chassis / lower body
            AddBox(root, "Tractor_Lower",
                new Vector3(2.20f, 1.10f, 5.00f),
                new Vector3(0f, 0.85f, 5.00f), body);

            // Cab upper (sleeping berth included, very boxy for low-poly)
            AddBox(root, "Tractor_Cab",
                new Vector3(2.10f, 1.30f, 3.20f),
                new Vector3(0f, 2.40f, 5.50f), body);

            // Aerodynamic roof fairing (sits on top of cab, slightly wider)
            AddBox(root, "Tractor_Fairing",
                new Vector3(2.20f, 0.40f, 2.60f),
                new Vector3(0f, 3.35f, 5.20f), DarkMat);

            // Cab windows
            AddBox(root, "Tractor_Win_Front",
                new Vector3(1.96f, 0.72f, 0.05f),
                new Vector3(0f, 2.50f, 7.10f), WindowMat);
            AddBox(root, "Tractor_Win_Left",
                new Vector3(0.05f, 0.62f, 2.60f),
                new Vector3( 1.06f, 2.44f, 5.50f), WindowMat);
            AddBox(root, "Tractor_Win_Right",
                new Vector3(0.05f, 0.62f, 2.60f),
                new Vector3(-1.06f, 2.44f, 5.50f), WindowMat);

            // Exhaust stacks (6-sided cylinders rotated upright)
            AddExhaustStack(root, "Stack_L", new Vector3( 0.88f, 2.50f, 4.50f));
            AddExhaustStack(root, "Stack_R", new Vector3(-0.88f, 2.50f, 4.50f));

            // Tractor wheels – front steer axle
            AddWheel(root, "T_Wheel_FL", 0.50f, 0.26f, new Vector3( 1.18f, 0.50f,  6.80f));
            AddWheel(root, "T_Wheel_FR", 0.50f, 0.26f, new Vector3(-1.18f, 0.50f,  6.80f));

            // Tractor wheels – rear drive tandem axles
            AddWheel(root, "T_Wheel_DL1", 0.50f, 0.26f, new Vector3( 1.18f, 0.50f,  3.50f));
            AddWheel(root, "T_Wheel_DR1", 0.50f, 0.26f, new Vector3(-1.18f, 0.50f,  3.50f));
            AddWheel(root, "T_Wheel_DL2", 0.50f, 0.26f, new Vector3( 1.18f, 0.50f,  4.30f));
            AddWheel(root, "T_Wheel_DR2", 0.50f, 0.26f, new Vector3(-1.18f, 0.50f,  4.30f));

            // ── TRAILER (rear half of rig, center ~Z -2.5) ──
            AddBox(root, "Trailer_Box",
                new Vector3(2.44f, 2.70f, 11.00f),
                new Vector3(0f, 2.35f, -2.50f), white);

            // Trailer chassis below floor
            AddBox(root, "Trailer_Chassis",
                new Vector3(2.10f, 0.30f, 11.00f),
                new Vector3(0f, 0.85f, -2.50f), DarkMat);

            // Trailer rear doors indication (subtle dark panel)
            AddBox(root, "Trailer_Rear",
                new Vector3(2.44f, 2.70f, 0.06f),
                new Vector3(0f, 2.35f, -8.03f), DarkMat);

            // Trailer wheels – tandem axle near rear
            AddWheel(root, "Trailer_WL1", 0.44f, 0.22f, new Vector3( 1.24f, 0.44f, -6.20f));
            AddWheel(root, "Trailer_WR1", 0.44f, 0.22f, new Vector3(-1.24f, 0.44f, -6.20f));
            AddWheel(root, "Trailer_WL2", 0.44f, 0.22f, new Vector3( 1.24f, 0.44f, -7.10f));
            AddWheel(root, "Trailer_WR2", 0.44f, 0.22f, new Vector3(-1.24f, 0.44f, -7.10f));

            // Clean up the extra body material we made for the white trailer
            // (white is kept alive by the renderer; no need to manually destroy here)

            return root;
        }

        // ---------------------------------------------------------------
        // Helper: add a scaled cube child
        // ---------------------------------------------------------------
        private static void AddBox(GameObject root, string childName,
                                   Vector3 size, Vector3 localPos, Material mat)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = size;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh     = CubeMesh;
            mr.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------
        // Helper: add a wheel (8-sided cylinder, rotated so axis = X)
        // ---------------------------------------------------------------
        private static void AddWheel(GameObject root, string childName,
                                     float radius, float width, Vector3 localPos)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            // Scale XYZ: X = wheel width (axle direction), Y/Z = radius
            go.transform.localScale    = new Vector3(width, radius, radius);
            // Euler(0,0,90) rotates the cylinder's height-axis (local Y) onto world -X
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh     = Cylinder8;
            mr.sharedMaterial = WheelMat;
        }

        // ---------------------------------------------------------------
        // Helper: add a vertical exhaust stack (6-sided cylinder)
        // ---------------------------------------------------------------
        private static void AddExhaustStack(GameObject root, string childName, Vector3 localPos)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = new Vector3(0.12f, 1.40f, 0.12f); // height along Y
            // No rotation needed – cylinder height is already along Y

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh     = Cylinder6;
            mr.sharedMaterial = ExhaustMat;
        }

        // ---------------------------------------------------------------
        // Procedural cube mesh (flat normals – low-poly look)
        // ---------------------------------------------------------------
        private static Mesh CreateCubeMesh()
        {
            // 6 faces × 4 unique vertices = 24 vertices (flat normals per face)
            var verts   = new Vector3[24];
            var norms   = new Vector3[24];
            var tris    = new int[36];

            // Front (+Z)
            SetCubeFace(verts, norms, tris, 0, Vector3.forward,
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f));
            // Back (-Z)
            SetCubeFace(verts, norms, tris, 1, Vector3.back,
                new Vector3( 0.5f,-0.5f,-0.5f), new Vector3(-0.5f,-0.5f,-0.5f),
                new Vector3(-0.5f, 0.5f,-0.5f), new Vector3( 0.5f, 0.5f,-0.5f));
            // Left (-X)
            SetCubeFace(verts, norms, tris, 2, Vector3.left,
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(-0.5f,-0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f,-0.5f));
            // Right (+X)
            SetCubeFace(verts, norms, tris, 3, Vector3.right,
                new Vector3( 0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3( 0.5f, 0.5f, 0.5f));
            // Top (+Y)
            SetCubeFace(verts, norms, tris, 4, Vector3.up,
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3( 0.5f, 0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3(-0.5f, 0.5f,-0.5f));
            // Bottom (-Y)
            SetCubeFace(verts, norms, tris, 5, Vector3.down,
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f,-0.5f, 0.5f), new Vector3(-0.5f,-0.5f, 0.5f));

            var mesh = new Mesh();
            mesh.name      = "MV_Cube";
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.triangles = tris;
            return mesh;
        }

        // ---------------------------------------------------------------
        // Static helper used by CreateCubeMesh
        // (Extracted as a static method so it compiles under C# 4 / .NET 3.5)
        // ---------------------------------------------------------------
        private static void SetCubeFace(
            Vector3[] verts, Vector3[] norms, int[] tris,
            int faceIndex, Vector3 normal,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int vi = faceIndex * 4;
            verts[vi] = v0; verts[vi+1] = v1; verts[vi+2] = v2; verts[vi+3] = v3;
            norms[vi] = norms[vi+1] = norms[vi+2] = norms[vi+3] = normal;

            int ti = faceIndex * 6;
            // Two triangles: (v0,v1,v2) and (v0,v2,v3)
            // Verified: each triple produces a normal matching the 'normal' parameter.
            tris[ti]   = vi;   tris[ti+1] = vi+1; tris[ti+2] = vi+2;
            tris[ti+3] = vi;   tris[ti+4] = vi+2; tris[ti+5] = vi+3;
        }

        // ---------------------------------------------------------------
        // Procedural cylinder mesh (flat normals – low-poly look)
        // Axis is local Y.  Radius and height are each 1 unit; use
        // localScale to size the wheel / stack correctly.
        // ---------------------------------------------------------------
        private static Mesh CreateCylinderMesh(float radius, float height, int sides)
        {
            var verts  = new List<Vector3>();
            var norms  = new List<Vector3>();
            var tris   = new List<int>();

            float halfH = height * 0.5f;
            float step  = 2f * Mathf.PI / sides;

            // ── Bottom cap (normal = down) ──
            // Centre + ring of (sides+1) verts so index [0] == index [sides] position-wise
            int bBase = verts.Count;
            verts.Add(new Vector3(0, -halfH, 0)); norms.Add(Vector3.down);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * step;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, -halfH, Mathf.Sin(a) * radius));
                norms.Add(Vector3.down);
            }
            for (int i = 0; i < sides; i++)
            {
                // Winding: (centre, ring[i], ring[i+1]) → normal -Y ✓
                tris.Add(bBase);
                tris.Add(bBase + 1 + i);
                tris.Add(bBase + 2 + i);
            }

            // ── Top cap (normal = up) ──
            int tBase = verts.Count;
            verts.Add(new Vector3(0, halfH, 0)); norms.Add(Vector3.up);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * step;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, halfH, Mathf.Sin(a) * radius));
                norms.Add(Vector3.up);
            }
            for (int i = 0; i < sides; i++)
            {
                // Reversed winding: (centre, ring[i+1], ring[i]) → normal +Y ✓
                tris.Add(tBase);
                tris.Add(tBase + 2 + i);
                tris.Add(tBase + 1 + i);
            }

            // ── Side quads (flat face normal = outward) ──
            for (int i = 0; i < sides; i++)
            {
                float a0   = i * step;
                float a1   = (i + 1) * step;
                float aMid = (a0 + a1) * 0.5f;
                var   fn   = new Vector3(Mathf.Cos(aMid), 0, Mathf.Sin(aMid)).normalized;

                var bl = new Vector3(Mathf.Cos(a0) * radius, -halfH, Mathf.Sin(a0) * radius);
                var br = new Vector3(Mathf.Cos(a1) * radius, -halfH, Mathf.Sin(a1) * radius);
                var tr = new Vector3(Mathf.Cos(a1) * radius,  halfH, Mathf.Sin(a1) * radius);
                var tl = new Vector3(Mathf.Cos(a0) * radius,  halfH, Mathf.Sin(a0) * radius);

                int bi = verts.Count;
                verts.Add(bl); norms.Add(fn);   // 0
                verts.Add(br); norms.Add(fn);   // 1
                verts.Add(tr); norms.Add(fn);   // 2
                verts.Add(tl); norms.Add(fn);   // 3

                // Winding: (bl, tr, br) then (bl, tl, tr) → outward normal ✓
                tris.Add(bi + 0); tris.Add(bi + 2); tris.Add(bi + 1);
                tris.Add(bi + 0); tris.Add(bi + 3); tris.Add(bi + 2);
            }

            var mesh = new Mesh();
            mesh.name      = "MV_Cylinder_" + sides;
            mesh.vertices  = verts.ToArray();
            mesh.normals   = norms.ToArray();
            mesh.triangles = tris.ToArray();
            return mesh;
        }
    }
}
