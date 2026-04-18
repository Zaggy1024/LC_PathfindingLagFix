using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PathfindingLagFix.Utilities;

internal struct AssignGroundCastPointsAsDestinationsJob : IJobFor
{
    [ReadOnly] private NativeArray<RaycastHit> Hits;

    [WriteOnly] private NativeArray<Vector3> Destinations;

    public void Initialize(NativeArray<RaycastHit> hits, NativeArray<Vector3> points)
    {
        Hits = hits;
        Destinations = points;
    }

#if BENCHMARKING
    private static readonly ProfilerMarkerWithMetadata<int> AssignGroundCastDestinationMarker = new("AssignGroundCastDestination", "Iteration");
#endif

    public void Execute(int index)
    {
#if BENCHMARKING
        AssignGroundCastDestinationMarker.Auto(index);
#endif

        if (Hits[index].colliderInstanceID == 0)
        {
            Destinations[index] = FindPathsToNodesJob.INVALID_DESTINATION;
            return;
        }

        Destinations[index] = Hits[index].point;
    }
}
