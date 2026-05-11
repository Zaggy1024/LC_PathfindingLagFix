using UnityEngine;

namespace PathfindingLagFix.Utilities;

internal class NodeOrdering
{
    private int[] sortedToSourceIndex = [];
    private int count;

    public int Count => count;

    public int this[int sortedPosition] => sortedToSourceIndex[sortedPosition];

    public void Sort(Vector3[] sourcePositions, int sourceCount, Vector3 target, NodeSortOrder sortOrder)
    {
        if (sortedToSourceIndex.Length < sourceCount)
            sortedToSourceIndex = new int[sourceCount];

        if (count != sourceCount)
        {
            for (int i = 0; i < sourceCount; i++)
                sortedToSourceIndex[i] = i;
            count = sourceCount;
        }

        if (sortOrder == NodeSortOrder.None)
            return;

        var farthestFirst = sortOrder == NodeSortOrder.FarthestFirst;

        for (int i = 1; i < sourceCount; i++)
        {
            var currentIndex = sortedToSourceIndex[i];
            var currentDist = (sourcePositions[currentIndex] - target).sqrMagnitude;
            for (int j = i; j > 0; j--)
            {
                var neighborIndex = sortedToSourceIndex[j - 1];
                var neighborDist = (sourcePositions[neighborIndex] - target).sqrMagnitude;
                if ((neighborDist <= currentDist) ^ farthestFirst)
                    break;
                (sortedToSourceIndex[j - 1], sortedToSourceIndex[j]) = (sortedToSourceIndex[j], sortedToSourceIndex[j - 1]);
            }
        }
    }
}
