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

        public static readonly MethodInfo m_RoundManager_get_Instance = typeof(RoundManager).GetMethod($"get_{nameof(RoundManager.Instance)}");

        public static readonly MethodInfo m_StartOfRound_get_Instance = typeof(StartOfRound).GetMethod($"get_{nameof(StartOfRound.Instance)}");
        public static readonly FieldInfo f_StartOfRound_allPlayerScripts = typeof(StartOfRound).GetField(nameof(StartOfRound.allPlayerScripts));

        public static readonly MethodInfo m_EnemyAI_DoAIInterval = typeof(EnemyAI).GetMethod(nameof(EnemyAI.DoAIInterval), []);
        public static readonly MethodInfo m_EnemyAI_TargetClosestPlayer = typeof(EnemyAI).GetMethod(nameof(EnemyAI.TargetClosestPlayer), [typeof(float), typeof(bool), typeof(float)]);
        public static readonly MethodInfo m_EnemyAI_ChooseFarthestNodeFromPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.ChooseFarthestNodeFromPosition), [typeof(Vector3), typeof(bool), typeof(int), typeof(bool), typeof(int), typeof(bool)]);
        public static readonly MethodInfo m_EnemyAI_ChooseClosestNodeToPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.ChooseClosestNodeToPosition), [typeof(Vector3), typeof(bool), typeof(int)]);
        public static readonly MethodInfo m_EnemyAI_SetDestinationToPosition = typeof(EnemyAI).GetMethod(nameof(EnemyAI.SetDestinationToPosition), [typeof(Vector3), typeof(bool)]);
        public static readonly MethodInfo m_EnemyAI_CheckLineOfSightForPlayer = typeof(EnemyAI).GetMethod(nameof(EnemyAI.CheckLineOfSightForPlayer), [typeof(float), typeof(int), typeof(int)]);
        public static readonly MethodInfo m_EnemyAI_PathIsIntersectedByLineOfSight = typeof(EnemyAI).GetMethod(nameof(EnemyAI.PathIsIntersectedByLineOfSight), [typeof(Vector3), typeof(bool), typeof(bool), typeof(bool)]);
        public static readonly MethodInfo m_EnemyAI_GetPathDistance = typeof(EnemyAI).GetMethod(nameof(EnemyAI.GetPathDistance), [typeof(Vector3), typeof(Vector3)]);
        public static readonly MethodInfo m_EnemyAI_EliminateNodeFromSearch = typeof(EnemyAI).GetMethod(nameof(EnemyAI.EliminateNodeFromSearch), BindingFlags.NonPublic | BindingFlags.Instance, [typeof(int)]);
        public static readonly FieldInfo f_EnemyAI_targetNode = typeof(EnemyAI).GetField(nameof(EnemyAI.targetNode));
        public static readonly FieldInfo f_EnemyAI_targetPlayer = typeof(EnemyAI).GetField(nameof(EnemyAI.targetPlayer));
        public static readonly FieldInfo f_EnemyAI_favoriteSpot = typeof(EnemyAI).GetField(nameof(EnemyAI.favoriteSpot));
        public static readonly FieldInfo f_EnemyAI_searchCoroutine = typeof(EnemyAI).GetField(nameof(EnemyAI.searchCoroutine));
        public static readonly FieldInfo f_EnemyAI_currentSearch = typeof(EnemyAI).GetField(nameof(EnemyAI.currentSearch));
        public static readonly FieldInfo f_EnemyAI_currentBehaviourStateIndex = typeof(EnemyAI).GetField(nameof(EnemyAI.currentBehaviourStateIndex));

        public static readonly MethodInfo m_MonoBehaviour_StartCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StartCoroutine), [typeof(IEnumerator)]);
        public static readonly MethodInfo m_MonoBehaviour_StopCoroutine = typeof(MonoBehaviour).GetMethod(nameof(MonoBehaviour.StopCoroutine), [typeof(Coroutine)]);

        public static readonly MethodInfo m_Component_get_transform = typeof(Component).GetMethod("get_transform", []);

        public static readonly MethodInfo m_Transform_get_position = typeof(Transform).GetMethod("get_position", []);

        public static readonly MethodInfo m_Object_op_Equality = typeof(Object).GetMethod("op_Equality", [typeof(Object), typeof(Object)]);
        public static readonly MethodInfo m_Object_op_Implicit = typeof(Object).GetMethod("op_Implicit", [typeof(Object)]);

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingFlags, Type[] parameters)
        {
            return type.GetMethod(name, bindingFlags, null, parameters, null);
        }

        public static MethodInfo GetGenericMethod(this Type type, string name, Type[] parameters, Type[] genericArgs)
        {
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                if (method.Name != name)
                    continue;
                if (!method.IsGenericMethodDefinition)
                    continue;

                var candidateParameters = method.GetParameters();
                if (parameters.Length != candidateParameters.Length)
                    continue;
                var parametersEqual = true;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i] != candidateParameters[i].ParameterType)
                    {
                        parametersEqual = false;
                        break;
                    }
                }
                if (!parametersEqual)
                    continue;

                return method.MakeGenericMethod(genericArgs);
            }

            return null;
        }
    }
}
