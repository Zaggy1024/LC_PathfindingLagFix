using System;
using System.Collections;
using System.Collections.Generic;

namespace PathfindingLagFix.Utilities
{
    public class IDMap<T>(int capacity) : IEnumerable<T>
    {
        private T[] backingArray = new T[capacity];

        public IDMap() : this(0)
        {
        }

        private void EnsureCapacity(int capacity)
        {
            if (backingArray.Length >= capacity)
                return;
            backingArray = new T[capacity];
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
}
