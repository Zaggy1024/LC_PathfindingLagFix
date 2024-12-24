using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities
{
    public sealed class NavMeshQueryPool(int capacity)
    {
        private NavMeshQuery[] freeArrays = new NavMeshQuery[capacity];
        private int currentIndex = 0;

        public int FreeCount => freeArrays.Length;

        public void Take(NavMeshQuery[] destination)
        {
            var count = destination.Length;
            var copiedItemCount = 0;

            if (currentIndex > 0)
            {
                copiedItemCount = Math.Min(count, currentIndex);
                currentIndex -= copiedItemCount;
                Array.Copy(freeArrays, currentIndex, destination, 0, copiedItemCount);
            }

            for (int i = copiedItemCount; i < count; i++)
                destination[i] = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.Persistent, Pathfinding.MAX_PATH_SIZE);
        }

        private void GrowArray()
        {
            var newSize = freeArrays.Length;
            while (newSize < currentIndex)
                newSize *= 2;
            var newBackingArray = new NavMeshQuery[newSize];
            Array.Copy(freeArrays, newBackingArray, freeArrays.Length);
            freeArrays = newBackingArray;
        }

        public void Free(NavMeshQuery[] queries)
        {
            var destinationIndex = currentIndex;
            currentIndex += queries.Length;
            if (currentIndex > freeArrays.Length)
                GrowArray();

            Array.Copy(queries, 0, freeArrays, destinationIndex, queries.Length);
        }
    }
}
