using System;
using System.Collections;
using System.Reflection;

using UnityEngine;

namespace PathfindingLagFix.Patches
{
    public static class Reflection
    {
        public static readonly MethodInfo m_Debug_Log = typeof(UnityEngine.Debug).GetMethod("Log", new Type[] { typeof(object) });

        public static readonly MethodInfo m_EnemyAI_DoAIInterval = typeof(EnemyAI).GetMethod(nameof(EnemyAI.DoAIInterval), new Type[0]);
        public static readonly MethodInfo m_EnemyAI_TargetClosestPlayer = typeof(EnemyAI).GetMethod(nameof(EnemyAI.TargetClosestPlayer), new Type[] { typeof(float), typeof(bool), typeof(float) });
        public static readonly MethodInfo m_EnemyAI_ChooseFarthestNodeFromPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.ChooseFarthestNodeFromPosition), new Type[] { typeof(Vector3), typeof(bool), typeof(int), typeof(bool) });
        public static readonly MethodInfo m_EnemyAI_SetDestinationToPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.SetDestinationToPosition), new Type[] { typeof(Vector3), typeof(bool) });
        public static readonly FieldInfo f_EnemyAI_targetNode = typeof(EnemyAI).GetField(nameof(EnemyAI.targetNode));
        public static readonly FieldInfo f_EnemyAI_targetPlayer = typeof(EnemyAI).GetField(nameof(EnemyAI.targetPlayer));
        public static readonly FieldInfo f_EnemyAI_favoriteSpot = typeof(EnemyAI).GetField(nameof(EnemyAI.favoriteSpot));
        public static readonly FieldInfo f_EnemyAI_searchCoroutine = typeof(EnemyAI).GetField(nameof(EnemyAI.searchCoroutine));

        public static readonly MethodInfo m_MonoBehaviour_StartCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StartCoroutine), new Type[] { typeof(IEnumerator) });
        public static readonly MethodInfo m_MonoBehaviour_StopCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StopCoroutine), new Type[] { typeof(Coroutine) });

        public static readonly MethodInfo m_Component_get_transform = typeof(Component).GetMethod("get_transform", new Type[0]);

        public static readonly MethodInfo m_Transform_get_position = typeof(Transform).GetMethod("get_position", new Type[0]);

        public static readonly MethodInfo m_Object_op_Equality = typeof(UnityEngine.Object).GetMethod("op_Equality", new Type[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) });
    }
}
