using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ZestDrop.Engines.Data;

internal class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly List<TKey> _keys = [];
    private readonly Dictionary<TKey, TValue> _dict;

    public OrderedDictionary() : this(EqualityComparer<TKey>.Default) { }

    public OrderedDictionary(IEqualityComparer<TKey> comparer)
    {
        _dict = new Dictionary<TKey, TValue>(comparer);
    }

    public KeyValuePair<TKey, TValue> GetAt(int index) => new(_keys[index], _dict[_keys[index]]);

    public TValue this[TKey key]
    {
        get => _dict[key];
        set
        {
            if (!_dict.ContainsKey(key))
                _keys.Add(key);
            _dict[key] = value;
        }
    }

    public ICollection<TKey> Keys => _keys.AsReadOnly();
    public ICollection<TValue> Values
    {
        get
        {
            var list = new List<TValue>(_keys.Count);
            foreach (var k in _keys)
                list.Add(_dict[k]);
            return list.AsReadOnly();
        }
    }

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    public int Count => _keys.Count;
    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value)
    {
        _dict.Add(key, value);
        _keys.Add(key);
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        _dict.Clear();
        _keys.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item) =>
        _dict.TryGetValue(item.Key, out var v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);

    public bool ContainsKey(TKey key) => _dict.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        for (int i = 0; i < _keys.Count; i++)
        {
            var k = _keys[i];
            array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(k, _dict[k]);
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var k in _keys)
            yield return new KeyValuePair<TKey, TValue>(k, _dict[k]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(TKey key)
    {
        if (_dict.Remove(key))
        {
            _keys.Remove(key);
            return true;
        }
        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (Contains(item))
            return Remove(item.Key);
        return false;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) =>
        _dict.TryGetValue(key, out value);
}
