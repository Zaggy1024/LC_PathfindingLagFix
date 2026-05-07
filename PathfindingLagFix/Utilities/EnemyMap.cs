using System;
using System.Collections.Generic;

namespace PathfindingLagFix.Utilities;

public class EnemyMap<T>(Func<T> elementConstructor)
{
    private readonly Func<T> constructor = elementConstructor;
    private Dictionary<int, T> backingDictionary = [];

    public T GetItem(EnemyAI enemy)
    {
        var index = enemy.GetInstanceID();
        if (backingDictionary.TryGetValue(index, out var value))
            return value;
        value = constructor();
        backingDictionary[index] = value;
        return value;
    }

    public T this[EnemyAI enemy] => GetItem(enemy);

    public int Count => backingDictionary.Count;

    public void Clear()
    {
        backingDictionary = [];
    }

    public bool Remove(EnemyAI enemy)
    {
        return backingDictionary.Remove(enemy.GetInstanceID());
    }

    public bool Contains(EnemyAI enemy)
    {
        return backingDictionary.ContainsKey(enemy.GetInstanceID());
    }
}
