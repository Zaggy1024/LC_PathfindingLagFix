using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(BlobAI))]
internal static class PatchBlobAI
{
    private static bool limitBlobUpdates = true;

    private struct BlobUpdateData()
    {
        internal int index = 0;
        internal RaycastHit[] hits = new RaycastHit[8];
        internal Vector3[] navmeshPositions = new Vector3[8];

#if VISUALIZERS
        internal Transform serverPositionVisualizer;
        internal Transform[] boneTargetVisualizers;
#endif
    }

    private static IDMap<BlobUpdateData> blobUpdateIndices = new(() => new(), 0);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlobAI.Update))]
    private static void UpdatePostfix(BlobAI __instance)
    {
#if VISUALIZERS
        ref var data = ref blobUpdateIndices[__instance.thisEnemyIndex];

        if (data.boneTargetVisualizers == null)
        {
            static Transform CreateVisualizer(string name, Transform parent, Material material)
            {
                var serverPositionVisualizer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                serverPositionVisualizer.name = name;
                serverPositionVisualizer.transform.SetParent(parent, worldPositionStays: false);
                serverPositionVisualizer.transform.localPosition = Vector3.zero;
                serverPositionVisualizer.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                Object.Destroy(serverPositionVisualizer.GetComponent<Collider>());
                serverPositionVisualizer.GetComponent<Renderer>().sharedMaterial = material;
                return serverPositionVisualizer.transform;
            }

            var blobVisualizerRoot = new GameObject("Blob Visualizers").transform;

            var shader = Shader.Find("HDRP/Lit");
            data.serverPositionVisualizer = CreateVisualizer("Server Position Visualizer", blobVisualizerRoot, new Material(shader)
            {
                color = Color.white,
            });

            data.boneTargetVisualizers = new Transform[8];

            for (var i = 0; i < __instance.SlimeBonePositions.Length; i++)
            {
                data.boneTargetVisualizers[i] = CreateVisualizer("Target Visualizer", blobVisualizerRoot, new Material(shader)
                {
                    color = Color.green,
                });

                CreateVisualizer("Bone Visualizer", __instance.SlimeBones[i].transform, new Material(shader)
                {
                    color = Color.red,
                });

                CreateVisualizer("Raycast Visualizer", __instance.SlimeRaycastTargets[i], new Material(shader)
                {
                    color = Color.blue,
                });
            }
        }

        data.serverPositionVisualizer.position = __instance.serverPosition;

        for (var i = 0; i < __instance.SlimeBonePositions.Length; i++)
            data.boneTargetVisualizers[i].position = __instance.SlimeBonePositions[i];
#endif
    }

    private static int GetBlobUpdateIndex(BlobAI blob)
    {
        if (!limitBlobUpdates)
            return -1;
        ref var index = ref blobUpdateIndices[blob.thisEnemyIndex].index;
        var result = index;
        index = (index + 1) % blob.SlimeRaycastTargets.Length;
        return result;
    }

    private static void SetBlobRayHit(BlobAI blob, int index)
    {
        blobUpdateIndices[blob.thisEnemyIndex].hits[index] = blob.slimeRayHit;
    }

    private static RaycastHit GetBlobRayHit(BlobAI blob, int index)
    {
        return blobUpdateIndices[blob.thisEnemyIndex].hits[index];
    }

    private static Vector3 GetBlobNavMeshPosition(BlobAI blob, int index)
    {
        return blobUpdateIndices[blob.thisEnemyIndex].navmeshPositions[index];
    }

    private static void SetBlobNavMeshPosition(BlobAI blob, int index, Vector3 position)
    {
        blobUpdateIndices[blob.thisEnemyIndex].navmeshPositions[index] = position;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(BlobAI.Update))]
    private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + var updateIndex = PatchBlobAI.GetBlobUpdateIndex(this);
        //   for (int i = 0; i < SlimeRaycastTargets.Length; i++)
        //   {
        //       var direction = SlimeRaycastTargets[i].position - centerPoint.position;
        //       slimeRay = new Ray(vector, direction);
        // -     RaycastCollisionWithPlayers(Vector3.Distance(vector, SlimeBones[i].transform.position));
        // +     var shouldUpdate = updateIndex == -1 || updateIndex == i;
        // +     if (shouldUpdate) {
        // +       RaycastCollisionWithPlayers(Vector3.Distance(vector, SlimeBones[i].transform.position));
        // +       Physics.Raycast(slimeRay, out slimeRayHit, maxDistanceForSlimeRays[i], slimeMask, QueryTriggerInteraction.Ignore);
        // +       PatchBlobAI.SetBlobRayHit(this, i);
        // +     } else {
        // +       slimeRayHit = PatchBlobAI.GetBlobRayHit(this, i);
        // +     }
        // -     if (Physics.Raycast(slimeRay, out slimeRayHit, maxDistanceForSlimeRays[i], slimeMask, QueryTriggerInteraction.Ignore)) {
        // +     if (slimeRayHit.colliderInstanceID != 0) {
        //           MoveSlimeBoneToRaycastHit(0f, i);
        //           continue;
        //       }
        // -     var extendedPosition = RoundManager.Instance.GetNavMeshPosition(slimeRay.GetPoint(maxDistanceForSlimeRays[i]));
        // +     var extendedPosition = PatchBlobAI.GetBlobNavMeshPosition(this, i);
        // +     if (shouldUpdate) {
        // +       extendedPosition = RoundManager.Instance.GetNavMeshPosition(slimeRay.GetPoint(maxDistanceForSlimeRays[i]));
        // +       PatchBlobAI.SetBlobNavMeshPosition(this, i, extendedPosition);
        // +     }
        //       SlimeBonePositions[i] = Vector3.Lerp(SlimeBonePositions[i], extendedPosition, 1f * Time.deltaTime);
        //       distanceOfRaysLastFrame[i] = maxDistanceForSlimeRays[i];
        //   }
        var updateIndexLocal = generator.DeclareLocal(typeof(int));
        var injector = new ILInjector(instructions)
            .Insert([
                // var updateIndex = PatchBlobAI.GetBlobUpdateIndex(this);
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchBlobAI).GetMethod(nameof(GetBlobUpdateIndex), BindingFlags.NonPublic | BindingFlags.Static, [typeof(BlobAI)])),
                new(OpCodes.Stloc, updateIndexLocal),
            ])
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldloc(),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(BlobAI).GetField(nameof(BlobAI.SlimeBones))),
                ILMatcher.Ldloc().CaptureAs(out var loadIndex),
                ILMatcher.Opcode(OpCodes.Ldelem_Ref),
                ILMatcher.Callvirt(Reflection.m_Component_get_transform),
                ILMatcher.Callvirt(Reflection.m_Transform_get_position),
                ILMatcher.Call(typeof(Vector3).GetMethod(nameof(Vector3.Distance), [typeof(Vector3), typeof(Vector3)])),
                ILMatcher.Call(typeof(BlobAI).GetMethod(nameof(BlobAI.RaycastCollisionWithPlayers), BindingFlags.NonPublic | BindingFlags.Instance, [typeof(float)])),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to {nameof(BlobAI.RaycastCollisionWithPlayers)} in {nameof(BlobAI)}.{nameof(BlobAI.Update)}().");
            return instructions;
        }

        var shouldUpdateLocal = generator.DeclareLocal(typeof(bool));

        var skipPhysicsLabel = generator.DefineLabel();
        injector
            .Insert([
                // var shouldUpdateLocal = updateIndex == -1 || updateIndex == i;
                new(OpCodes.Ldloc, updateIndexLocal),
                new(OpCodes.Ldc_I4_M1),
                new(OpCodes.Ceq),
                new(OpCodes.Ldloc, updateIndexLocal),
                loadIndex,
                new(OpCodes.Ceq),
                new(OpCodes.Or),
                new(OpCodes.Stloc, shouldUpdateLocal),

                // if (shouldUpdate) {
                new(OpCodes.Ldloc, shouldUpdateLocal),
                new(OpCodes.Brfalse, skipPhysicsLabel),
            ])
            .Find([
                ILMatcher.Call(typeof(Physics).GetMethod(nameof(Physics.Raycast), [typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction)])),
                ILMatcher.Opcode(OpCodes.Brfalse).CaptureOperandAs(out Label noRaycastHitLabel),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the raycast in {nameof(BlobAI)}.{nameof(BlobAI.Update)}().");
            return instructions;
        }

        var skipGetCachedHitLabel = generator.DefineLabel();
        var slimeRayHitField = typeof(BlobAI).GetField(nameof(BlobAI.slimeRayHit), BindingFlags.NonPublic | BindingFlags.Instance);
        injector
            .SetRelativeInstruction(1, new(OpCodes.Pop))
            .GoToMatchEnd()
            .Insert([
                // PatchBlobAI.SetBlobRayHit(this, i);
                new(OpCodes.Ldarg_0),
                loadIndex,
                new(OpCodes.Call, typeof(PatchBlobAI).GetMethod(nameof(SetBlobRayHit), BindingFlags.NonPublic | BindingFlags.Static, [typeof(BlobAI), typeof(int)])),

                // } else {
                new(OpCodes.Br, skipGetCachedHitLabel),

                // slimeRayHit = PatchBlobAI.GetBlobRayHit(this, i);
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(skipPhysicsLabel),
                new(OpCodes.Ldarg_0),
                loadIndex,
                new(OpCodes.Call, typeof(PatchBlobAI).GetMethod(nameof(GetBlobRayHit), BindingFlags.NonPublic | BindingFlags.Static, [typeof(BlobAI), typeof(int)])),
                new(OpCodes.Stfld, slimeRayHitField),

                // }

                // if (slimeRayHit.collider != null) {
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(skipGetCachedHitLabel),
                new(OpCodes.Ldflda, slimeRayHitField),
                new(OpCodes.Call, typeof(RaycastHit).GetMethod($"get_{nameof(RaycastHit.colliderInstanceID)}")),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Beq_S, noRaycastHitLabel),
            ])
            .Find([
                ILMatcher.Callvirt(typeof(RoundManager).GetMethod(nameof(RoundManager.GetNavMeshPosition), [typeof(Vector3), typeof(NavMeshHit), typeof(float), typeof(int)])),
                ILMatcher.Stloc().CaptureAs(out var storeExtendedPosition),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the navmesh sample in {nameof(BlobAI)}.{nameof(BlobAI.Update)}().");
            return instructions;
        }

        var skipGetNavMeshPositionLabel = generator.DefineLabel();
        return injector
            .GoToMatchEnd()
            .AddLabel(skipGetNavMeshPositionLabel)
            .InsertInPlace([
                // PatchBlobAI.SetBlobNavMeshPosition(this, i, extendedPosition);
                new(OpCodes.Ldarg_0),
                new(loadIndex),
                new(storeExtendedPosition.StlocToLdloc()),
                new(OpCodes.Call, typeof(PatchBlobAI).GetMethod(nameof(SetBlobNavMeshPosition), BindingFlags.NonPublic | BindingFlags.Static, [typeof(BlobAI), typeof(int), typeof(Vector3)])),
            ])
            .Back(2)
            .GoToPush(5)
            .InsertAfterBranch([
                // var extendedPosition = PatchBlobAI.GetBlobNavMeshPosition(this, i);
                new(OpCodes.Ldarg_0),
                loadIndex,
                new(OpCodes.Call, typeof(PatchBlobAI).GetMethod(nameof(GetBlobNavMeshPosition), BindingFlags.NonPublic | BindingFlags.Static, [typeof(BlobAI), typeof(int)])),
                storeExtendedPosition,

                // if (shouldUpdate) {
                new(OpCodes.Ldloc, shouldUpdateLocal),
                new(OpCodes.Brfalse_S, skipGetNavMeshPositionLabel),
            ])
            .ReleaseInstructions();
    }

    internal static void RemoveStatus(EnemyAI enemy)
    {
        blobUpdateIndices[enemy.thisEnemyIndex] = new();
    }
}
