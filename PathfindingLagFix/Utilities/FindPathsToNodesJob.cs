using System.Collections.Generic;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLib.API;
using PathfindingLib.Jobs;
using PathfindingLib.Utilities;

#if BENCHMARKING
using System;
using Unity.Profiling;
#endif

namespace PathfindingLagFix.Utilities;

internal struct FindPathsToNodesJob : IJobFor
{
    [NativeDisableContainerSafetyRestriction] private static NativeArray<NavMeshQuery> StaticThreadQueries;

    private const float MAX_ORIGIN_DISTANCE = 5;
    private const float MAX_ENDPOINT_DISTANCE = 1.5f;
    private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

    [ReadOnly, NativeSetThreadIndex] internal int ThreadIndex;

    [ReadOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshQuery> ThreadQueriesRef;

    [ReadOnly] private int AgentTypeID;
    [ReadOnly] private int AreaMask;
    [ReadOnly] private Vector3 Origin;
    [ReadOnly, NativeDisableContainerSafetyRestriction] private NativeArray<Vector3> Destinations;
    [ReadOnly] private bool CalculateDistance;

    [ReadOnly, NativeDisableContainerSafetyRestriction] private NativeArray<bool> Canceled;

    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<PathQueryStatus> Statuses;
    [WriteOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshLocation> Paths;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<int> PathSizes;
    [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<float> PathDistances;

#if BENCHMARKING
    private static readonly ProfilerMarker InitializeJobMarker = new("Initialize Job");
#endif

    public void Initialize(int agentTypeID, int areaMask, Vector3 origin, Vector3[] candidates, int count, bool calculateDistance = false)
    {
#if BENCHMARKING
        using var markerAuto = InitializeJobMarker.Auto();
#endif

        ThreadQueriesRef = PathfindingJobSharedResources.GetPerThreadQueriesArray();

        CreateFixedArrays();

        AgentTypeID = agentTypeID;
        AreaMask = areaMask;
        Origin = origin;
        CalculateDistance = calculateDistance;

        EnsureCount(count);

        Statuses.SetAllElements(PathQueryStatus.InProgress);

        if (calculateDistance)
            PathDistances.SetAllElements(0);

        Canceled[0] = false;

        NativeArray<Vector3>.Copy(candidates, Destinations, count);
    }

    public void Initialize(int agentTypeID, int areaMask, Vector3 origin, Vector3[] candidates, bool calculateDistance = false)
    {
        Initialize(agentTypeID, areaMask, origin, candidates, candidates.Length, calculateDistance);
    }

    public void Initialize(int agentTypeID, int areaMask, Vector3 origin, List<Vector3> candidates, bool calculateDistance = false)
    {
        Initialize(agentTypeID, areaMask, origin, NoAllocHelpers.ExtractArrayFromListT(candidates), candidates.Count, calculateDistance);
    }

    private void CreateFixedArrays()
    {
        if (Canceled.Length == 1)
            return;

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
        Paths = new(count * NavMeshQueryUtils.RecommendedCornerCount, Allocator.Persistent);
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
        return Paths.GetSubArray(index * NavMeshQueryUtils.RecommendedCornerCount, NavMeshQueryUtils.RecommendedCornerCount);
    }

    public NativeArray<NavMeshLocation> GetPath(int index)
    {
        return Paths.GetSubArray(index * NavMeshQueryUtils.RecommendedCornerCount, PathSizes[index]);
    }

#if BENCHMARKING
    private static readonly ProfilerMarkerWithMetadata<int> UpdateFindPathMarker = new("UpdateFindPath", "Iteration");
    private static readonly ProfilerMarkerWithMetadata<int> FindStraightPathMarker = new("FindStraightPath", "Iteration");
    private static readonly ProfilerMarkerWithMetadata<int> FinalizeIterationMarker = new("Finalize", "Iteration");
#endif

    public void Execute(int index)
    {
        if (Canceled[0])
        {
            Statuses[index] = PathQueryStatus.Failure;
            return;
        }

        // Lock the navmesh to ensure that we don't crash while updating the path here.
        using var readLocker = new NavMeshReadLocker();

#if BENCHMARKING
        using var markerAuto = UpdateFindPathMarker.Auto(index);
#endif

        var query = ThreadQueriesRef[ThreadIndex];

        var originExtents = new Vector3(MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE);
        var origin = query.MapLocation(Origin, originExtents, AgentTypeID, AreaMask);

        if (!query.IsValid(origin))
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

        // Find the shortest path through the polygons of the navmesh.
        var status = query.BeginFindPath(origin, destinationLocation, AreaMask);
        if (status.GetResult() == PathQueryStatus.Failure)
        {
            Statuses[index] = status;
            return;
        }

        while (status.GetResult() == PathQueryStatus.InProgress)
        {
            status = query.UpdateFindPath(NavMeshLock.RecommendedUpdateFindPathIterationCount, out int _);

#if BENCHMARKING
            markerAuto.Pause();
#endif
            readLocker.Yield();
#if BENCHMARKING
            markerAuto.Resume();
#endif
        }

        status = query.EndFindPath(out var pathNodesSize);

        if (status.GetResult() != PathQueryStatus.Success)
        {
            Statuses[index] = status;
            return;
        }

        var pathNodes = new NativeArray<PolygonId>(pathNodesSize, Allocator.Temp);
        query.GetPathResult(pathNodes);

#if BENCHMARKING
        markerAuto.Pause();
        using var findStraightPathMarkerAuto = FindStraightPathMarker.Auto(index);
#endif

        // Calculate straight path from polygons.
        status = NavMeshQueryUtils.FindStraightPath(query, Origin, destination, pathNodes, pathNodesSize, GetPathBuffer(index), out var pathSize) | status.GetDetail();

        // Now that we have a copy of all the navmesh data we need, release the navmesh lock.
        readLocker.Dispose();

#if BENCHMARKING
        findStraightPathMarkerAuto.Pause();
        using var finalizeMarkerAuto = FinalizeIterationMarker.Auto(index);
#endif

        PathSizes[index] = pathSize;
        pathNodes.Dispose();

        if (status.GetResult() != PathQueryStatus.Success)
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
