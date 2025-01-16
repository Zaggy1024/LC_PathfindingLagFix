using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities;

//[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
internal struct FindPathsToNodesJob : IJobFor
{
    [NativeDisableContainerSafetyRestriction] private static NativeArray<NavMeshQuery> StaticThreadQueries;

    private const float MAX_ORIGIN_DISTANCE = 5;
    private const float MAX_ENDPOINT_DISTANCE = 1.5f;
    private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

    [ReadOnly, NativeSetThreadIndex] internal int ThreadIndex;

    [ReadOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshQuery> ThreadQueriesRef;

    [ReadOnly] internal int AgentTypeID;
    [ReadOnly] internal int AreaMask;
    [ReadOnly] internal Vector3 Origin;
    [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<Vector3> Destinations;
    [ReadOnly] internal bool CalculateDistance;

    [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<bool> Canceled;

    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<PathQueryStatus> Statuses;
    [WriteOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshLocation> Paths;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<int> PathSizes;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<float> PathDistances;

    public void Initialize(int agentTypeID, int areaMask, Vector3 origin, Vector3[] candidates, bool calculateDistance = false)
    {
        CreateQueries();
        ThreadQueriesRef = StaticThreadQueries;

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

        Canceled[0] = false;

        NativeArray<Vector3>.Copy(candidates, Destinations, count);
    }

    private static void CreateQueries()
    {
        var threadCount = JobsUtility.ThreadIndexCount;
        if (StaticThreadQueries.Length >= threadCount)
            return;

        Application.quitting -= DisposeQueries;

        var newQueries = new NativeArray<NavMeshQuery>(threadCount, Allocator.Persistent);
        for (var i = 0; i < StaticThreadQueries.Length; i++)
            newQueries[i] = StaticThreadQueries[i];
        for (var i = StaticThreadQueries.Length; i < threadCount; i++)
            newQueries[i] = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.Persistent, Pathfinding.MAX_PATH_SIZE);
        StaticThreadQueries.Dispose();
        StaticThreadQueries = newQueries;

        Application.quitting += DisposeQueries;
    }

    private static void DisposeQueries()
    {
        foreach (var query in StaticThreadQueries)
            query.Dispose();

        StaticThreadQueries.Dispose();

        Application.quitting -= DisposeQueries;
    }

    private void CreateFixedArrays()
    {
        Canceled = new(1, Allocator.Persistent);
    }

    private void DisposeFixedArrays()
    {
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

        // Lock the navmesh to ensure that we don't crash while updating the path here.
        NavMeshLock.BeginNavMeshRead();

        var query = ThreadQueriesRef[ThreadIndex];

        var originExtents = new Vector3(MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE);
        var origin = query.MapLocation(Origin, originExtents, AgentTypeID, AreaMask);

        if (!query.IsValid(origin))
        {
            Statuses[index] = PathQueryStatus.Failure;
            NavMeshLock.EndNavMeshRead();
            return;
        }

        var destination = Destinations[index];
        var destinationExtents = new Vector3(MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE);
        var destinationLocation = query.MapLocation(destination, destinationExtents, AgentTypeID, AreaMask);
        if (!query.IsValid(destinationLocation))
        {
            Statuses[index] = PathQueryStatus.Failure;
            NavMeshLock.EndNavMeshRead();
            return;
        }

        // Find the shortest path through the polygons of the navmesh.
        var status = query.BeginFindPath(origin, destinationLocation, AreaMask);
        if (status.GetStatus() == PathQueryStatus.Failure)
        {
            Statuses[index] = status;
            NavMeshLock.EndNavMeshRead();
            return;
        }

        while (status.GetStatus() == PathQueryStatus.InProgress)
            status = query.UpdateFindPath(int.MaxValue, out int _);

        status = query.EndFindPath(out var pathNodesSize);

        if (status.GetStatus() != PathQueryStatus.Success)
        {
            Statuses[index] = status;
            NavMeshLock.EndNavMeshRead();
            return;
        }

        var pathNodes = new NativeArray<PolygonId>(pathNodesSize, Allocator.Temp);
        query.GetPathResult(pathNodes);

        // Calculate straight path from polygons.
        status = Pathfinding.FindStraightPath(query, Origin, destination, pathNodes, pathNodesSize, GetPathBuffer(index), out var pathSize) | status.GetDetail();

        // Now that we have a copy of all the navmesh data we need, release the navmesh lock.
        NavMeshLock.EndNavMeshRead();

        PathSizes[index] = pathSize;
        pathNodes.Dispose();

        if (status.GetStatus() != PathQueryStatus.Success)
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
            Statuses[index] = PathQueryStatus.Failure | status.GetDetail();
            return;
        }

        if (CalculateDistance)
        {
            var distance = 0f;
            for (var i = 1; i < pathCorners.Length; i++)
                distance += Vector3.Distance(pathCorners[i - 1].position, pathCorners[i].position);
            PathDistances[index] = distance;
        }

        Statuses[index] = status;
    }

    internal void FreeAllResources()
    {
        DisposeResizeableArrays();
        DisposeFixedArrays();
    }
}
