using System;

using Unity.Collections;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities
{
    public sealed class NavMeshQueryPool(int capacity)
    {
        private NavMeshQuery[] freeQueries = new NavMeshQuery[capacity];
        private int currentIndex = 0;

        public int FreeCount => currentIndex;

        public void Take(NativeArray<NavMeshQuery> destination, int count)
        {
            var copiedItemCount = 0;

            if (currentIndex > 0)
            {
                copiedItemCount = Math.Min(count, currentIndex);
                currentIndex -= copiedItemCount;
                freeQueries.AsSpan().Slice(currentIndex, copiedItemCount).CopyTo(destination.AsSpan()[..count]);
            }

            for (int i = copiedItemCount; i < count; i++)
                destination[i] = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.Persistent, Pathfinding.MAX_PATH_SIZE);
        }

        private void GrowArray()
        {
            var newSize = freeQueries.Length;
            while (newSize < currentIndex)
                newSize *= 2;
            var newBackingArray = new NavMeshQuery[newSize];
            Array.Copy(freeQueries, newBackingArray, freeQueries.Length);
            freeQueries = newBackingArray;
        }

        public void Free(NativeArray<NavMeshQuery> queries)
        {
            var destinationIndex = currentIndex;
            currentIndex += queries.Length;
            if (currentIndex > freeQueries.Length)
                GrowArray();

            queries.AsSpan().CopyTo(freeQueries.AsSpan().Slice(destinationIndex, queries.Length));
        }

        ~NavMeshQueryPool()
        {
            for (int i = 0; i < currentIndex; i++)
                freeQueries[i].Dispose();
        }
    }
}
