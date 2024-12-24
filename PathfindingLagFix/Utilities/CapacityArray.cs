using System;

namespace PathfindingLagFix.Utilities;

public class CapacityArray<T>
{
    private T[] array = [];

    public T[] Get(int capacity)
    {
        if (capacity > array.Length)
        {
            var newArray = new T[capacity];
            Array.Copy(array, newArray, array.Length);
            array = newArray;
            return newArray;
        }

        return array;
    }
}
