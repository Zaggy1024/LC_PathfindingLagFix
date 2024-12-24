using System;
using System.Collections;
using System.Collections.Generic;

namespace PathfindingLagFix.Utilities;

public class IDMap<T> : IEnumerable<T>
{
    private readonly Func<T> constructor;
    private T[] backingArray;

    public IDMap(Func<T> elementConstructor, int capacity)
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

    public int IndexOf(T item)
    {
        return Array.IndexOf(backingArray, item);
    }

    public void Clear()
    {
        backingArray = [];
    }

    public bool Clear(T item)
    {
        var index = IndexOf(item);
        if (index == -1)
            return false;
        this[index] = default;
        return true;
    }

    public bool Contains(T item)
    {
        return Array.IndexOf(backingArray, item) != -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(backingArray, 0, array, arrayIndex, backingArray.Length);
    }

    public void CopyTo(int sourceIndex, T[] array, int arrayIndex, int count)
    {
        Array.Copy(backingArray, sourceIndex, array, arrayIndex, count);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)backingArray).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return backingArray.GetEnumerator();
    }
}
