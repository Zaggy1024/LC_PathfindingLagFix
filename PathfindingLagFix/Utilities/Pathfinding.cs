//
// Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
//
// This software is provided 'as-is', without any express or implied
// warranty.  In no event will the authors be held liable for any damages
// arising from the use of this software.
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it
// freely, subject to the following restrictions:
// 1. The origin of this software must not be misrepresented; you must not
//    claim that you wrote the original software. If you use this software
//    in a product, an acknowledgment in the product documentation would be
//    appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be
//    misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.
//

// The original source code has been modified by Unity Technologies
// and Zaggy1024.

using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.AI;
using Unity.Mathematics;

namespace PathfindingLagFix.Utilities
{
    public static class Pathfinding
    {
        public const int MAX_PATH_SIZE = 2048;
        public const int MAX_STRAIGHT_PATH = 128;

        private static float CrossMagnitude(float3 u, float3 v)
        {
            return u.z * v.x - u.x * v.z;
        }

        public static PathQueryStatus FindStraightPath(NavMeshQuery query,
            float3 startPos, float3 endPos,
            NativeSlice<PolygonId> path, int pathSize,
            NativeArray<NavMeshLocation> straightPath,
            NativeArray<int> straightPathCount)
        {
            if (!query.IsValid(path[0]))
                return PathQueryStatus.Failure;

            straightPath[0] = query.CreateLocation(startPos, path[0]);

            var apexIndex = 0;
            var n = 1;

            if (pathSize > 1)
            {
                var startPolyWorldToLocal = query.PolygonWorldToLocalMatrix(path[0]);

                var apex = startPolyWorldToLocal.MultiplyPoint(startPos);
                var left = new Vector3(0, 0, 0); // Vector3.zero accesses a static readonly which does not work in burst yet
                var right = new Vector3(0, 0, 0);
                var leftIndex = -1;
                var rightIndex = -1;

                for (var i = 1; i <= pathSize; ++i)
                {
                    var polyWorldToLocal = query.PolygonWorldToLocalMatrix(path[apexIndex]);

                    Vector3 vl, vr;
                    if (i == pathSize)
                    {
                        vl = vr = polyWorldToLocal.MultiplyPoint(endPos);
                    }
                    else
                    {
                        var success = query.GetPortalPoints(path[i - 1], path[i], out vl, out vr);
                        if (!success)
                            return PathQueryStatus.Failure;

                        vl = polyWorldToLocal.MultiplyPoint(vl);
                        vr = polyWorldToLocal.MultiplyPoint(vr);
                    }

                    vl -= apex;
                    vr -= apex;

                    // Ensure left/right ordering
                    if (CrossMagnitude(vl, vr) < 0)
                        (vl, vr) = (vr, vl);

                    // Terminate funnel by turning
                    if (CrossMagnitude(left, vr) < 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(path[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + left);

                        n = RetracePortals(query, apexIndex, leftIndex, path, n, termPos, ref straightPath);

                        if (n == MAX_STRAIGHT_PATH)
                        {
                            straightPathCount[0] = n;
                            return PathQueryStatus.Success | PathQueryStatus.OutOfNodes;
                        }

                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left = new(0, 0, 0);
                        right = new(0, 0, 0);
                        i = apexIndex = leftIndex;
                        continue;
                    }
                    if (CrossMagnitude(right, vl) > 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(path[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + right);

                        n = RetracePortals(query, apexIndex, rightIndex, path, n, termPos, ref straightPath);

                        if (n == MAX_STRAIGHT_PATH)
                        {
                            straightPathCount[0] = n;
                            return PathQueryStatus.Success | PathQueryStatus.OutOfNodes;
                        }

                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left = new(0, 0, 0);
                        right = new(0, 0, 0);
                        i = apexIndex = rightIndex;
                        continue;
                    }

                    // Narrow funnel
                    if (CrossMagnitude(left, vl) >= 0)
                    {
                        left = vl;
                        leftIndex = i;
                    }
                    if (CrossMagnitude(right, vr) <= 0)
                    {
                        right = vr;
                        rightIndex = i;
                    }
                }
            }

            // Remove the next to last if duplicate point - e.g. start and end positions are the same
            // (in which case we have get a single point)
            if (n > 0 && (straightPath[n - 1].position == (Vector3)endPos))
                n--;

            n = RetracePortals(query, apexIndex, pathSize - 1, path, n, endPos, ref straightPath);

            if (n == MAX_STRAIGHT_PATH)
            {
                straightPathCount[0] = n;
                return PathQueryStatus.Success; // | PathQueryStatus.BufferTooSmall;
            }

            straightPathCount[0] = n;
            return PathQueryStatus.Success;
        }

        // Retrace portals between corners and register if type of polygon changes
        private static int RetracePortals(NavMeshQuery query,
            int startIndex, int endIndex,
            NativeSlice<PolygonId> path, int n, float3 termPos,
            ref NativeArray<NavMeshLocation> straightPath)
        {
            for (var k = startIndex; k < endIndex - 1; ++k)
            {
                var type1 = query.GetPolygonType(path[k]);
                var type2 = query.GetPolygonType(path[k + 1]);
                if (type1 != type2)
                {
                    query.GetPortalPoints(path[k], path[k + 1], out var l, out var r);
                    SegmentSegmentCPA(out float3 cpa1, out float3 _, l, r, straightPath[n - 1].position, termPos);
                    straightPath[n] = query.CreateLocation(cpa1, path[k + 1]);

                    if (++n == MAX_STRAIGHT_PATH)
                        return MAX_STRAIGHT_PATH;
                }
            }
            straightPath[n] = query.CreateLocation(termPos, path[endIndex]);
            return n + 1;
        }

        // Calculate the closest point of approach for line-segment vs line-segment.
        private static bool SegmentSegmentCPA(out float3 c0, out float3 c1, float3 p0, float3 p1, float3 q0, float3 q1)
        {
            var u = p1 - p0;
            var v = q1 - q0;
            var w0 = p0 - q0;

            float a = math.dot(u, u);
            float b = math.dot(u, v);
            float c = math.dot(v, v);
            float d = math.dot(u, w0);
            float e = math.dot(v, w0);

            float den = (a * c - b * b);
            float sc, tc;

            if (den == 0)
            {
                sc = 0;
                tc = d / b;

                // todo: handle b = 0 (=> a and/or c is 0)
            }
            else
            {
                sc = (b * e - c * d) / (a * c - b * b);
                tc = (a * e - b * d) / (a * c - b * b);
            }

            c0 = math.lerp(p0, p1, sc);
            c1 = math.lerp(q0, q1, tc);

            return den != 0;
        }

        public static PathQueryStatus GetStatus(this PathQueryStatus status)
        {
            return status & ~PathQueryStatus.StatusDetailMask;
        }

        public static PathQueryStatus GetDetail(this PathQueryStatus status)
        {
            return status & PathQueryStatus.StatusDetailMask;
        }
    }
}
