using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.TestTools;

namespace PingTheKiller.Client.Tests
{
    // U4h: the imported city fitted to the FixedMap v2 boxes (brief U4h-a1, "Retarget to map v2").
    public sealed class WorldArtTests
    {
        private const float WallTolerance = .05f, ObstacleTolerance = .1f;
        private const float MaxObstacleHeight = 1.5f, MaxFloorHeight = .15f, MaxBuildingHeight = 11f;
        // Above MapGeometry.WallHeight the Survivor camera can rise over a wall box up to about 0.8 m inside its footprint.
        private const float UpperSetback = .95f;
        private static readonly Color Ally = new Color32(63, 184, 175, 255);
        private static readonly Color Text = new Color32(232, 230, 225, 255);
        private static readonly Color Muted = new Color32(154, 152, 144, 255);

        // Material colours round-trip through linear space; compare to well within one 8-bit step.
        private sealed class NearColour : IEqualityComparer<Color>
        {
            public bool Equals(Color a, Color b) => Mathf.Abs(a.r - b.r) < .002f && Mathf.Abs(a.g - b.g) < .002f && Mathf.Abs(a.b - b.b) < .002f && Mathf.Abs(a.a - b.a) < .002f;
            public int GetHashCode(Color c) => 0;
        }
        private static readonly NearColour Near = new NearColour();

        private readonly List<Object> created = new List<Object>();
        private static FixedMap Map => SharedMap.Map;

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        private GameObject Spawn(GameObject prefab, Vector3 position)
        {
            GameObject item = Object.Instantiate(prefab);
            item.transform.position = position;
            created.Add(item);
            return item;
        }

        private static Bounds BoundsOf(IEnumerable<Renderer> renderers)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(any, Is.True, "has renderers");
            return bounds;
        }

        private static WorldArt Art()
        {
            WorldArt art = WorldArt.Load();
            Assert.That(art, Is.Not.Null, "Resources/WorldArt.asset (Ping the Killer/Setup World Art)");
            return art;
        }

        private static Vector3 WallCentre(Wall wall) => new Vector3((wall.MinX + wall.MaxX) * .5f, 0, (wall.MinZ + wall.MaxZ) * .5f);

        [Test]
        public void TheCatalogLoadsAndWasBuiltForThisMap()
        {
            WorldArt art = Art();
            Assert.That(art.Matches(Map), Is.True);
            Assert.That(art.Walls.Length, Is.EqualTo(Map.Walls.Count));
            Assert.That(art.Obstacles.Length, Is.EqualTo(Map.Obstacles.Count));
            Assert.That(art.Ground, Is.Not.Null);
            Assert.That(art.Backdrop, Is.Not.Null);
            Assert.That(art.Terminal, Is.Not.Null);
            Assert.That(art.ExitMarker, Is.Not.Null);
        }

        [Test]
        public void ACatalogForAnotherMapDoesNotMatch()
        {
            WorldArt art = Object.Instantiate(Art());
            created.Add(art);
            art.Walls = (WorldArt.Piece[])art.Walls.Clone();
            art.Walls[5].Box += new Vector4(.5f, 0, .5f, 0);
            Assert.That(art.Matches(Map), Is.False, "one moved box");
            art.Walls = new WorldArt.Piece[0];
            Assert.That(art.Matches(Map), Is.False, "wrong wall count");
        }

        [Test]
        public void EveryWallVisualFillsItsBoxFootprintWithinFiveCentimetres()
        {
            WorldArt art = Art();
            for (int i = 0; i < Map.Walls.Count; i++)
            {
                Wall wall = Map.Walls[i];
                Bounds b = BoundsOf(Spawn(art.Walls[i].Prefab, WallCentre(wall)).GetComponentsInChildren<Renderer>());
                string name = "wall " + i;
                Assert.That(b.min.x, Is.EqualTo(wall.MinX).Within(WallTolerance), name + " min x");
                Assert.That(b.max.x, Is.EqualTo(wall.MaxX).Within(WallTolerance), name + " max x");
                Assert.That(b.min.z, Is.EqualTo(wall.MinZ).Within(WallTolerance), name + " min z");
                Assert.That(b.max.z, Is.EqualTo(wall.MaxZ).Within(WallTolerance), name + " max z");
                Assert.That(b.min.y, Is.EqualTo(0).Within(WallTolerance), name + " stands on the floor");
                Assert.That(b.max.y, Is.GreaterThanOrEqualTo(MapGeometry.WallHeight), name + " at least as tall as the line-of-sight box");
                Assert.That(b.max.y, Is.LessThanOrEqualTo(MaxBuildingHeight), name + " about 10 m at most");
            }
        }

        // Rays from half a metre outside each face toward it, from just above the floor to just below 3 m: the first visual
        // surface is the box face (no gap, no hole, nothing sticking out).
        [Test]
        public void EveryWallFaceIsSolidFromTheFloorToWallHeight()
        {
            WorldArt art = Art();
            for (int i = 0; i < Map.Walls.Count; i++)
            {
                Wall wall = Map.Walls[i];
                GameObject item = Spawn(art.Walls[i].Prefab, WallCentre(wall));
                var triangles = new List<Vector3>();
                foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
                    if (filter.name == "Podium") Collect(filter, triangles);
                Assert.That(triangles.Count, Is.GreaterThan(0), "wall " + i + " has a podium");
                foreach (var face in Faces(wall))
                    for (float along = .05f; along < face.length; along += face.length > 20 ? 1.3f : .37f)
                        for (float y = .05f; y < MapGeometry.WallHeight - .02f; y += .29f)
                        {
                            Vector3 onFace = face.origin + face.right * along + Vector3.up * y;
                            Vector3 from = onFace + face.outward * .5f;
                            float hit = Raycast(triangles, from, -face.outward);
                            Assert.That(hit, Is.EqualTo(.5f).Within(WallTolerance), "wall " + i + " at " + onFace);
                        }
            }
        }

        [Test]
        public void UpperStoreysStandBackFromTheBoxEdgesSoTheRaisedSurvivorCameraNeverEntersThem()
        {
            WorldArt art = Art();
            for (int i = 0; i < Map.Walls.Count; i++)
            {
                Wall wall = Map.Walls[i];
                GameObject item = Spawn(art.Walls[i].Prefab, WallCentre(wall));
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = renderer.bounds;
                    if (b.max.y <= MapGeometry.WallHeight + .15f) continue; // the podium and its parapet
                    string name = "wall " + i + " / " + renderer.name;
                    Assert.That(b.min.x, Is.GreaterThanOrEqualTo(wall.MinX + UpperSetback), name);
                    Assert.That(b.max.x, Is.LessThanOrEqualTo(wall.MaxX - UpperSetback), name);
                    Assert.That(b.min.z, Is.GreaterThanOrEqualTo(wall.MinZ + UpperSetback), name);
                    Assert.That(b.max.z, Is.LessThanOrEqualTo(wall.MaxZ - UpperSetback), name);
                }
            }
        }

        [Test]
        public void ObstaclesFillTheirBoxAndStayLowerThanAStandingCharacter()
        {
            WorldArt art = Art();
            for (int i = 0; i < Map.Obstacles.Count; i++)
            {
                Obstacle obstacle = Map.Obstacles[i];
                var centre = new Vector3((obstacle.MinX + obstacle.MaxX) * .5f, 0, (obstacle.MinZ + obstacle.MaxZ) * .5f);
                Bounds b = BoundsOf(Spawn(art.Obstacles[i].Prefab, centre).GetComponentsInChildren<Renderer>());
                string name = "obstacle " + i;
                Assert.That(b.min.x, Is.EqualTo(obstacle.MinX).Within(ObstacleTolerance), name);
                Assert.That(b.max.x, Is.EqualTo(obstacle.MaxX).Within(ObstacleTolerance), name);
                Assert.That(b.min.z, Is.EqualTo(obstacle.MinZ).Within(ObstacleTolerance), name);
                Assert.That(b.max.z, Is.EqualTo(obstacle.MaxZ).Within(ObstacleTolerance), name);
                Assert.That(b.min.y, Is.EqualTo(0).Within(ObstacleTolerance), name);
                Assert.That(b.max.y, Is.LessThanOrEqualTo(MaxObstacleHeight), name + " lower than 1.5 m (a character is 1.8 m)");
            }
        }

        [Test]
        public void NoPieceHasACollider()
        {
            WorldArt art = Art();
            var prefabs = new List<GameObject> { art.Ground, art.Backdrop, art.Terminal, art.ExitMarker };
            foreach (WorldArt.Piece piece in art.Walls) prefabs.Add(piece.Prefab);
            foreach (WorldArt.Piece piece in art.Obstacles) prefabs.Add(piece.Prefab);
            foreach (GameObject prefab in prefabs)
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, prefab.name);
        }

        [Test]
        public void EveryMaterialIsAUrpOrParticleShaderThatCompiles()
        {
            WorldArt art = Art();
            var prefabs = new List<GameObject> { art.Ground, art.Backdrop, art.Terminal, art.ExitMarker };
            foreach (WorldArt.Piece piece in art.Walls) prefabs.Add(piece.Prefab);
            foreach (WorldArt.Piece piece in art.Obstacles) prefabs.Add(piece.Prefab);
            foreach (GameObject prefab in prefabs)
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        string name = prefab.name + " / " + renderer.name;
                        Assert.That(material, Is.Not.Null, name + " has a missing material");
                        Assert.That(material.shader.isSupported, Is.True, name + ": " + material.shader.name);
                        Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/").Or.StartWith("Shader Graphs/HS_"), name);
                    }
        }

        [Test]
        public void TheGroundIsFlat()
        {
            Bounds b = BoundsOf(Spawn(Art().Ground, Vector3.zero).GetComponentsInChildren<Renderer>());
            Assert.That(b.max.y, Is.LessThanOrEqualTo(MaxFloorHeight));
            Assert.That(b.min.x, Is.LessThanOrEqualTo(0));
            Assert.That(b.max.x, Is.GreaterThanOrEqualTo(FixedMap.Size));
        }

        [Test]
        public void TheTerminalFitsInOnePointTwoMetresAndHasAScreen()
        {
            GameObject item = Spawn(Art().Terminal, new Vector3(10, 0, 10));
            Bounds b = BoundsOf(item.GetComponentsInChildren<Renderer>());
            Assert.That(b.size.x, Is.LessThanOrEqualTo(1.2f));
            Assert.That(b.size.z, Is.LessThanOrEqualTo(1.2f));
            Assert.That(b.max.y, Is.LessThanOrEqualTo(1.2f));
            Assert.That(b.min.y, Is.EqualTo(0).Within(.02f));
            Assert.That(Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(10, 10)), Is.LessThan(.15f), "centred on the server position");
            Assert.That(item.transform.Find(WorldArt.ScreenName), Is.Not.Null);
        }

        // The whole city as WorldView builds it: apart from the floor, the terminals and the Exits, every renderer is inside a
        // wall box, inside an obstacle box, or outside the border.
        [Test]
        public void NothingButFloorTerminalsAndExitsStandsInTheWalkableArea()
        {
            GameObject root = World(Role.Survivor, Art(), out WorldProps props);
            Assert.That(props.UsesArt, Is.True);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(renderer.transform, root.transform);
                if (path.StartsWith("Terminal ") || path.StartsWith("Exit")) continue;
                Bounds b = renderer.bounds;
                if (path.StartsWith("City/Ground")) { Assert.That(b.max.y, Is.LessThanOrEqualTo(MaxFloorHeight), path); continue; }
                bool outside = b.max.x <= -1 || b.min.x >= FixedMap.Size + 1 || b.max.z <= -1 || b.min.z >= FixedMap.Size + 1;
                bool inWall = false, inObstacle = false;
                foreach (Wall wall in Map.Walls)
                    inWall |= b.min.x >= wall.MinX - WallTolerance && b.max.x <= wall.MaxX + WallTolerance && b.min.z >= wall.MinZ - WallTolerance && b.max.z <= wall.MaxZ + WallTolerance;
                foreach (Obstacle obstacle in Map.Obstacles)
                    inObstacle |= b.min.x >= obstacle.MinX - ObstacleTolerance && b.max.x <= obstacle.MaxX + ObstacleTolerance && b.min.z >= obstacle.MinZ - ObstacleTolerance && b.max.z <= obstacle.MaxZ + ObstacleTolerance;
                Assert.That(outside || inWall || inObstacle, Is.True, path + " " + b);
            }
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty, "positions and collisions are server-owned");
        }

        [Test]
        public void WithoutACatalogTheMapFallsBackToPrimitives()
        {
            LogAssert.ignoreFailingMessages = true; // edit mode: the primitives' collider Destroy() logs
            GameObject root = World(Role.Survivor, null, out WorldProps props);
            Assert.That(props.UsesArt, Is.False);
            Assert.That(root.transform.Find("City"), Is.Null);
            int walls = 0;
            foreach (Transform child in root.transform) if (child.name == "Wall") walls++;
            Assert.That(walls, Is.EqualTo(Map.Walls.Count));
            Assert.That(root.transform.Find("Floor"), Is.Not.Null);
            Assert.That(root.transform.Find("Terminal 0/Panel"), Is.Not.Null, "the primitive terminal");
            Assert.That(root.transform.Find("Exit"), Is.Not.Null);
        }

        [Test]
        public void ACatalogForAnotherMapAlsoFallsBackToPrimitives()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldArt other = ScriptableObject.CreateInstance<WorldArt>();
            created.Add(other);
            World(Role.Killer, other, out WorldProps props);
            Assert.That(props.UsesArt, Is.False);
        }

        [TestCase(Role.Survivor)]
        [TestCase(Role.Killer)]
        public void TheTerminalScreenShowsIdleAndDoneByRole(Role role)
        {
            GameObject root = World(role, Art(), out WorldProps props);
            Material screen = root.transform.Find("Terminal 0/" + WorldArt.ScreenName).GetComponent<Renderer>().sharedMaterial;
            Material other = root.transform.Find("Terminal 1/" + WorldArt.ScreenName).GetComponent<Renderer>().sharedMaterial;
            Assert.That(screen, Is.Not.SameAs(other), "each terminal has its own screen material");
            props.Accept(State(0, 0, 0));
            Assert.That(screen.color, Is.EqualTo(Muted).Using(Near), "idle");
            props.Accept(State(100, 0, 0));
            Assert.That(screen.color, Is.EqualTo(role == Role.Survivor ? Ally : Text).Using(Near), "done: ally for Survivors, text for the Killer");
            Assert.That(other.color, Is.EqualTo(Muted).Using(Near), "terminal 1 is still idle");
        }

        [TestCase(Role.Survivor)]
        [TestCase(Role.Killer)]
        public void TheExitShowsItsMarkerOnlyWhileOpenTintedByRole(Role role)
        {
            GameObject root = World(role, Art(), out WorldProps props);
            Material ring = root.transform.Find("Exit").GetComponent<Renderer>().sharedMaterial;
            GameObject marker = root.transform.Find("Exit marker").gameObject;
            Assert.That(ring.color, Is.EqualTo(Muted).Using(Near));
            Assert.That(marker.activeSelf, Is.False, "not open: ring only");
            props.Accept(State(0, 1, 0));
            Assert.That(props.ExitLookOf(0), Is.EqualTo(ExitLook.Open));
            Color tint = role == Role.Survivor ? Ally : Text;
            Assert.That(ring.color, Is.EqualTo(tint).Using(Near));
            Assert.That(marker.activeSelf, Is.True);
            foreach (ParticleSystem system in marker.GetComponentsInChildren<ParticleSystem>(true))
            {
                Color start = system.main.startColor.color;
                Assert.That(new Color(start.r, start.g, start.b), Is.EqualTo(new Color(tint.r, tint.g, tint.b)).Using(Near), system.name);
            }
            ParticleSystem[] systems = marker.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(systems, Is.Not.Empty);
            foreach (ParticleSystem system in systems) Assert.That(system.isPlaying, Is.True, system.name + " plays while open");
            int particles = 0;
            foreach (ParticleSystem system in systems) { system.Simulate(1.5f, false, false); particles += system.particleCount; }
            Assert.That(particles, Is.GreaterThan(0), "the open marker emits");
            props.Accept(State(0, 1, 1));
            Assert.That(ring.color, Is.EqualTo(Muted).Using(Near), "Locked reads as closed");
            Assert.That(marker.activeSelf, Is.False);
            foreach (ParticleSystem system in systems)
            {
                Assert.That(system.isPlaying, Is.False, system.name + " stopped when closed");
                Assert.That(system.particleCount, Is.EqualTo(0), system.name + " cleared when closed");
            }
            props.Accept(State(0, 1, 0));
            foreach (ParticleSystem system in systems) Assert.That(system.isPlaying, Is.True, system.name + " plays again on reopening");
            Assert.That(root.transform.Find("Exit 2 marker").gameObject.activeSelf, Is.False, "the second Exit stays closed");
        }

        [Test]
        public void TheExitMarkerPrefabLoopsAndNeverPlaysOnItsOwn()
        {
            ParticleSystem[] systems = Art().ExitMarker.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(systems, Is.Not.Empty);
            foreach (ParticleSystem system in systems)
            {
                Assert.That(system.main.loop, Is.True, system.name + " stays on while the Exit is open");
                Assert.That(system.main.playOnAwake, Is.False, system.name + " is started by SetExitLook only");
            }
        }

        [Test]
        public void AnExitIndexOutOfRangeIsIgnored()
        {
            World(Role.Survivor, Art(), out WorldProps props);
            Assert.DoesNotThrow(() => props.SetExitLook(2, ExitLook.Open));
            Assert.DoesNotThrow(() => props.SetExitLook(-1, ExitLook.Open));
            Assert.That(props.ExitLookOf(2), Is.EqualTo(ExitLook.Closed));
            Assert.That(props.ExitLookOf(0), Is.EqualTo(ExitLook.Closed), "the real Exits are untouched");
            Assert.That(props.ExitLookOf(1), Is.EqualTo(ExitLook.Closed));
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------

        private GameObject World(Role role, WorldArt art, out WorldProps props)
        {
            var root = new GameObject("World art test");
            created.Add(root);
            var materials = new WorldMaterials(root.transform);
            props = new WorldProps(materials, role, art);
            props.Initialize();
            return root;
        }

        private static S2C_WorldState State(byte progress, byte open, byte locked)
        {
            // Protocol v0.4: Exit 0 carries the state (a Firewall-locked switch keeps the Exit closed); Exit 1 stays closed.
            var state = new S2C_WorldState();
            byte flags = (byte)ExitFlags.Powered;
            if (locked != 0) flags |= (byte)ExitFlags.Locked;
            else if (open != 0) flags |= (byte)ExitFlags.Open;
            state.Exits.Add(new ExitState { Id = 0, X = Map.Exits[0].X, Z = Map.Exits[0].Z, Flags = flags });
            state.Exits.Add(new ExitState { Id = 1, X = Map.Exits[1].X, Z = Map.Exits[1].Z, Flags = (byte)ExitFlags.Powered });
            for (byte i = 0; i < Map.TerminalPositions.Count; i++)
                state.Terminals.Add(new TerminalState { Id = i, X = Map.TerminalPositions[i].X, Z = Map.TerminalPositions[i].Z, Progress = i == 0 ? progress : (byte)0 });
            return state;
        }

        private static string PathOf(Transform item, Transform root)
        {
            string path = item.name;
            for (Transform t = item.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        private static IEnumerable<(Vector3 origin, Vector3 right, Vector3 outward, float length)> Faces(Wall w)
        {
            yield return (new Vector3(w.MinX, 0, w.MinZ), Vector3.right, Vector3.back, w.MaxX - w.MinX);
            yield return (new Vector3(w.MaxX, 0, w.MinZ), Vector3.forward, Vector3.right, w.MaxZ - w.MinZ);
            yield return (new Vector3(w.MaxX, 0, w.MaxZ), Vector3.left, Vector3.forward, w.MaxX - w.MinX);
            yield return (new Vector3(w.MinX, 0, w.MaxZ), Vector3.back, Vector3.left, w.MaxZ - w.MinZ);
        }

        private static void Collect(MeshFilter filter, List<Vector3> triangles)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            foreach (int index in indices) triangles.Add(m.MultiplyPoint3x4(vertices[index]));
        }

        // Nearest hit distance along a unit direction, either side of a triangle (Moller-Trumbore); infinity on a miss.
        private static float Raycast(List<Vector3> triangles, Vector3 origin, Vector3 direction)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 e1 = triangles[i + 1] - triangles[i], e2 = triangles[i + 2] - triangles[i];
                Vector3 p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector3 s = origin - triangles[i];
                float u = Vector3.Dot(s, p) / det;
                if (u < -1e-5f || u > 1 + 1e-5f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(direction, q) / det;
                if (v < -1e-5f || u + v > 1 + 1e-5f) continue;
                float t = Vector3.Dot(e2, q) / det;
                if (t > 0 && t < best) best = t;
            }
            return best;
        }
    }
}
