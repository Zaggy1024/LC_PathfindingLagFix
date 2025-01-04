using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities;

//[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
internal struct FindPathsToNodesJob : IJobFor
{
    private const float MAX_ORIGIN_DISTANCE = 5;
    private const float MAX_ENDPOINT_DISTANCE = 1.5f;
    private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

    private static readonly NavMeshQueryPool QueryPool = new(256);

    [ReadOnly] internal int AgentTypeID;
    [ReadOnly] internal int AreaMask;
    [ReadOnly] internal Vector3 Origin;
    [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<Vector3> Destinations;
    [ReadOnly] internal bool CalculateDistance;

    [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<NavMeshQuery> Queries;
    [ReadOnly] internal NativeArray<int> OwnedQueryCount;

    [ReadOnly] internal NativeArray<bool> Canceled;

    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<PathQueryStatus> Statuses;
    [WriteOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshLocation> Paths;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<int> PathSizes;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<float> PathDistances;

    public void Initialize(int agentTypeID, int areaMask, Vector3 origin, Vector3[] candidates, bool calculateDistance = false)
    {
        CreateFixedArrays();

        AgentTypeID = agentTypeID;
        AreaMask = areaMask;
        Origin = origin;
        CalculateDistance = calculateDistance;

        var count = candidates.Length;
        EnsureCount(count);

        for (var i = 0; i < count; i++)
            Statuses[i] = PathQueryStatus.InProgress;

        if (calculateDistance)
        {
            for (var i = 0; i < count; i++)
                PathDistances[i] = 0;
        }

        QueryPool.Take(Queries.AsSpan()[..count]);
        OwnedQueryCount[0] = count;

        Canceled[0] = false;

        Destinations.CopyFrom(candidates);
    }

    private void CreateFixedArrays()
    {
        if (OwnedQueryCount != default)
            return;

        OwnedQueryCount = new(1, Allocator.Persistent);
        Canceled = new(1, Allocator.Persistent);
    }

    private void DisposeFixedArrays()
    {
        OwnedQueryCount.Dispose();
        Canceled.Dispose();
    }

    private void EnsureCount(int count)
    {
        if (Destinations.Length >= count)
            return;

        DisposeResizeableArrays();

        if (count == 0)
            return;

        Destinations = new(count, Allocator.Persistent);
        Queries = new(count, Allocator.Persistent);

        Statuses = new(count, Allocator.Persistent);
        Paths = new(count * Pathfinding.MAX_STRAIGHT_PATH, Allocator.Persistent);
        PathSizes = new(count, Allocator.Persistent);
        PathDistances = new(count, Allocator.Persistent);
    }

    private void DisposeResizeableArrays()
    {
        if (!Destinations.IsCreated)
            return;

        Destinations.Dispose();

        Queries.Dispose();

        Statuses.Dispose();
        Paths.Dispose();
        PathSizes.Dispose();
        PathDistances.Dispose();
    }

    public void Cancel()
    {
        if (Canceled.IsCreated)
            Canceled[0] = true;
    }

    public NativeArray<NavMeshLocation> GetPathBuffer(int index)
    {
        return Paths.GetSubArray(index * Pathfinding.MAX_STRAIGHT_PATH, Pathfinding.MAX_STRAIGHT_PATH);
    }

    public NativeArray<NavMeshLocation> GetPath(int index)
    {
        return Paths.GetSubArray(index * Pathfinding.MAX_STRAIGHT_PATH, PathSizes[index]);
    }

    public void Execute(int index)
    {
        if (Canceled[0])
        {
            Statuses[index] = PathQueryStatus.Failure;
            return;
        }

        var query = Queries[index];
        var originExtents = new Vector3(MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE);
        var origin = query.MapLocation(Origin, originExtents, AgentTypeID, AreaMask);

        if (!query.IsValid(origin.polygon))
        {
            Statuses[index] = PathQueryStatus.Failure;
            return;
        }

        var destination = Destinations[index];
        var destinationExtents = new Vector3(MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE);
        var destinationLocation = query.MapLocation(destination, destinationExtents, AgentTypeID, AreaMask);
        if (!query.IsValid(destinationLocation))
        {
            Statuses[index] = PathQueryStatus.Failure;
            return;
        }

        query.BeginFindPath(origin, destinationLocation, AreaMask);

        PathQueryStatus status = PathQueryStatus.InProgress;
        while (status.GetStatus() == PathQueryStatus.InProgress)
            status = query.UpdateFindPath(int.MaxValue, out int _);

        if (status.GetStatus() != PathQueryStatus.Success)
        {
            Statuses[index] = status;
            return;
        }

        var pathNodes = new NativeArray<PolygonId>(Pathfinding.MAX_PATH_SIZE, Allocator.Temp);
        status = query.EndFindPath(out var pathNodesSize);
        query.GetPathResult(pathNodes);

        var straightPathStatus = Pathfinding.FindStraightPath(query, Origin, destination, pathNodes, pathNodesSize, GetPath(index), out var pathSize);
        PathSizes[index] = pathSize;
        pathNodes.Dispose();

        if (straightPathStatus.GetStatus() != PathQueryStatus.Success)
        {
            Statuses[index] = status;
            return;
        }

        // Check if the end of the path is close enough to the target.
        var pathCorners = GetPath(index);
        var endPosition = pathCorners[^1].position;
        var endDistance = (endPosition - destination).sqrMagnitude;
        if (endDistance > MAX_ENDPOINT_DISTANCE_SQR)
        {
            Statuses[index] = PathQueryStatus.Failure;
            return;
        }

        if (CalculateDistance)
        {
            var firstCorner = pathCorners[0];
            var distance = 0f;
            for (var i = 1; i < pathCorners.Length; i++)
                distance += Vector3.Distance(pathCorners[i].position, firstCorner.position);
            PathDistances[index] = distance;
        }

        Statuses[index] = PathQueryStatus.Success;
    }

    internal void FreeNonReusableResources()
    {
        if (!OwnedQueryCount.IsCreated)
            return;
        var ownedQueryCount = OwnedQueryCount[0];
        if (ownedQueryCount <= 0)
            return;
        QueryPool.Free(Queries.AsSpan()[..ownedQueryCount]);
        OwnedQueryCount[0] = 0;
    }

    internal void FreeAllResources()
    {
        FreeNonReusableResources();
        DisposeResizeableArrays();
        DisposeFixedArrays();
    }
}
