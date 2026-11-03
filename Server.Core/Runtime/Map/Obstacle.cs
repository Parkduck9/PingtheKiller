namespace PingTheKiller.ServerCore.Map
{
    // A low movement blocker. It deliberately never participates in visibility queries.
    public readonly struct Obstacle
    {
        private readonly Wall _box;
        public Obstacle(float minX, float minZ, float maxX, float maxZ)
        { _box = new Wall(minX, minZ, maxX, maxZ); }
        public float MinX => _box.MinX;
        public float MinZ => _box.MinZ;
        public float MaxX => _box.MaxX;
        public float MaxZ => _box.MaxZ;
        public bool IntersectsCircle(float x, float z, float radius) => _box.IntersectsCircle(x, z, radius);
    }
}
