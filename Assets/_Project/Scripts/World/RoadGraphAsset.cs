using UnityEngine;

namespace CityDriver.World
{
    /// <summary>
    /// Asset wrapper so the generated network is stored in the project and loaded without parsing.
    /// (Lives in its own file: Unity can only serialise a ScriptableObject whose class name matches its file name.)
    /// </summary>
    [CreateAssetMenu(fileName = "CityRoadGraph", menuName = "City Driver/Road Graph")]
    public sealed class RoadGraphAsset : ScriptableObject
    {
        public RoadGraphData data = new RoadGraphData();
    }
}
