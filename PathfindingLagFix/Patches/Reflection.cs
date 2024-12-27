using System;
using System.Collections;
using System.Reflection;

using UnityEngine;

using Object = UnityEngine.Object;

namespace PathfindingLagFix.Patches
{
    public static class Reflection
    {
        public static readonly MethodInfo m_Debug_Log = typeof(Debug).GetMethod("Log", [ typeof(object) ]);

        public static readonly MethodInfo m_EnemyAI_DoAIInterval = typeof(EnemyAI).GetMethod(nameof(EnemyAI.DoAIInterval), []);
        public static readonly MethodInfo m_EnemyAI_TargetClosestPlayer = typeof(EnemyAI).GetMethod(nameof(EnemyAI.TargetClosestPlayer), [ typeof(float), typeof(bool), typeof(float) ]);
        public static readonly MethodInfo m_EnemyAI_ChooseFarthestNodeFromPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.ChooseFarthestNodeFromPosition), [typeof(Vector3), typeof(bool), typeof(int), typeof(bool), typeof(int), typeof(bool)]);
        public static readonly MethodInfo m_EnemyAI_SetDestinationToPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.SetDestinationToPosition), [ typeof(Vector3), typeof(bool) ]);
        public static readonly FieldInfo f_EnemyAI_targetNode = typeof(EnemyAI).GetField(nameof(EnemyAI.targetNode));
        public static readonly FieldInfo f_EnemyAI_targetPlayer = typeof(EnemyAI).GetField(nameof(EnemyAI.targetPlayer));
        public static readonly FieldInfo f_EnemyAI_favoriteSpot = typeof(EnemyAI).GetField(nameof(EnemyAI.favoriteSpot));
        public static readonly FieldInfo f_EnemyAI_searchCoroutine = typeof(EnemyAI).GetField(nameof(EnemyAI.searchCoroutine));
        public static readonly FieldInfo f_EnemyAI_currentSearch = typeof(EnemyAI).GetField(nameof(EnemyAI.currentSearch));

        public static readonly MethodInfo m_MonoBehaviour_StartCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StartCoroutine), [ typeof(IEnumerator) ]);
        public static readonly MethodInfo m_MonoBehaviour_StopCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StopCoroutine), [ typeof(Coroutine) ]);

        public static readonly MethodInfo m_Component_get_transform = typeof(Component).GetMethod("get_transform", []);

        public static readonly MethodInfo m_Transform_get_position = typeof(Transform).GetMethod("get_position", []);

        public static readonly MethodInfo m_Object_op_Equality = typeof(Object).GetMethod("op_Equality", [ typeof(Object), typeof(Object) ]);

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingFlags, Type[] parameters)
        {
            return type.GetMethod(name, bindingFlags, null, parameters, null);
        }
    }
}
