using System;
using System.Runtime.CompilerServices;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Scripting;

namespace PathfindingLagFix.Utilities;

[IgnoredByDeepProfiler]
[UsedByNativeCode]
internal struct ProfilerMarkerWithMetadata<T> where T : unmanaged
{
    [IgnoredByDeepProfiler]
    [UsedByNativeCode]
    public struct AutoScope : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        internal readonly ProfilerMarkerWithMetadata<T> marker;
        internal T value;
        internal bool on;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AutoScope(in ProfilerMarkerWithMetadata<T> marker, T value)
        {
            this.marker = marker;
            this.value = value;
            Resume();
        }

        public void Resume()
        {
            if (!on)
            {
                on = true;
                marker.Begin(value);
            }
        }

        public void Pause()
        {
            if (on)
            {
                marker.End();
                on = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Pause();
        }
    }

    private readonly IntPtr ptr;
    private ProfilerMarkerData data;

    public ProfilerMarkerWithMetadata(string name, string metadata)
    {
        ptr = ProfilerUnsafeUtility.CreateMarker(name, 1, MarkerFlags.Default, 0);

        if (typeof(T) == typeof(int))
            data.Type = (byte)ProfilerMarkerDataType.Int32;
        else if (typeof(T) == typeof(long))
            data.Type = (byte)ProfilerMarkerDataType.Int64;
        else if (typeof(T) == typeof(uint))
            data.Type = (byte)ProfilerMarkerDataType.UInt32;
        else if (typeof(T) == typeof(ulong))
            data.Type = (byte)ProfilerMarkerDataType.UInt64;
        else if (typeof(T) == typeof(float))
            data.Type = (byte)ProfilerMarkerDataType.Float;
        else if (typeof(T) == typeof(double))
            data.Type = (byte)ProfilerMarkerDataType.Double;

        data.Size = (uint)UnsafeUtility.SizeOf<T>();

        ProfilerUnsafeUtility.SetMarkerMetadata(ptr, 0, metadata, data.Type, (byte)ProfilerMarkerDataUnit.Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Begin(in T value)
    {
        unsafe
        {
            fixed (void* dataPtr = &data, valuePtr = &value)
            {
                data.Ptr = valuePtr;
                ProfilerUnsafeUtility.BeginSampleWithMetadata(ptr, 1, dataPtr);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void End()
    {
        ProfilerUnsafeUtility.EndSample(ptr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AutoScope Auto(T value)
    {
        return new AutoScope(this, value);
    }
}
