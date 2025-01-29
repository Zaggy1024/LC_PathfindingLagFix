using Unity.Collections;

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
}
