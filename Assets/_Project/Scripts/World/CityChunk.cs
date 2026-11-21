using UnityEngine;

namespace CityDriver.World
{
    /// <summary>One 240 m square of the city (roads, buildings, props). Created by the world builder; toggled by <see cref="ChunkStreamer"/>.</summary>
    public sealed class CityChunk : MonoBehaviour
    {
        public Vector2Int index;
        public Vector3 center;
        public float radius;
        public int triangleCount;
        public int buildingCount;
        public int rendererCount;
    }
}
