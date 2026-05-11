using System;

namespace PathfindingLagFix.Utilities;

public class SequentialMap<T>
{
    private readonly Func<T> constructor;
    private T[] backingArray;

    public SequentialMap(Func<T> elementConstructor, int capacity)
    {
        constructor = elementConstructor;
        backingArray = new T[capacity];
        for (var i = 0; i < capacity; i++)
            backingArray[i] = constructor();
    }

    private void EnsureCapacity(int capacity)
    {
        if (backingArray.Length >= capacity)
            return;
        var newArray = new T[capacity];
        Array.Copy(backingArray, newArray, Math.Min(backingArray.Length, newArray.Length));
        for (var i = backingArray.Length; i < capacity; i++)
            newArray[i] = constructor();
        backingArray = newArray;
    }

    public ref T GetItem(int index)
    {
        EnsureCapacity(index + 1);
        return ref backingArray[index];
    }

    public ref T this[int index] => ref GetItem(index);

    public int Count => backingArray.Length;

    public void Clear()
    {
        backingArray = [];
    }
}
