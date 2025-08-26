using System;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace PathfindingLagFix.Utilities;

internal static class NativeArrayUtils
{
    internal static void SetAllElements<T>(this NativeArray<T> array, T value) where T : unmanaged
    {
        unsafe
        {
            var ptr = (T*)array.m_Buffer;
            var count = array.Length;

            for (var i = 0; i < count; i++)
                ptr[i] = value;
        }
    }

    internal static unsafe void CopyFrom<T>(this NativeArray<T> array, Span<T> span) where T : struct
    {
        if (array.Length < span.Length)
            throw new InvalidOperationException($"NativeArray size {array.Length} is smaller than span size {span.Length}.");
        UnsafeUtility.MemCpy(array.GetUnsafePtr(), UnsafeUtility.AddressOf(ref span[0]), span.Length);
    }
}
