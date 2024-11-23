using System.Collections;
using System.Runtime.InteropServices;

namespace Toycoin;

public class ArrayKeyDictionary<TKey, TValue> : IDictionary<TKey[], TValue> where TKey : IComparable {
    private readonly Dictionary<TKey[], TValue> dictionary;
    private readonly Dictionary<TKey[], TValue>.AlternateLookup<ReadOnlySpan<TKey>> lookup;

    public ArrayKeyDictionary() {
        dictionary = new(ArrayComparer<TKey>.Instance);
        lookup = dictionary.GetAlternateLookup<ReadOnlySpan<TKey>>();
    }

    public ArrayKeyDictionary(int capacity) {
        dictionary = new(capacity, ArrayComparer<TKey>.Instance);
        lookup = dictionary.GetAlternateLookup<ReadOnlySpan<TKey>>();
    }

    public ArrayKeyDictionary(IDictionary<TKey[], TValue> dictionary) {
        this.dictionary = new(dictionary, ArrayComparer<TKey>.Instance);
        lookup = this.dictionary.GetAlternateLookup<ReadOnlySpan<TKey>>();
    }

    public IEnumerator<KeyValuePair<TKey[], TValue>> GetEnumerator() => dictionary.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => dictionary.GetEnumerator();

    public void Add(KeyValuePair<TKey[], TValue> item) => dictionary.Add(item.Key, item.Value);

    public void Clear() => dictionary.Clear();

    public bool Contains(KeyValuePair<TKey[], TValue> item) => dictionary.Contains(item);

    public void CopyTo(KeyValuePair<TKey[], TValue>[] array, int arrayIndex) {
        foreach (var pair in dictionary)
            array[arrayIndex++] = pair;
    }

    public bool Remove(KeyValuePair<TKey[], TValue> item) => dictionary.Remove(item.Key);

    public int Count => dictionary.Count;
    public bool IsReadOnly => false;
    public void Add(TKey[] key, TValue value) => dictionary.Add(key, value);

    public bool ContainsKey(TKey[] key) => dictionary.ContainsKey(key);

    public bool ContainsKey(ReadOnlySpan<TKey> key) => lookup.ContainsKey(key);

    public bool Remove(TKey[] key) => dictionary.Remove(key);

    public bool Remove(ReadOnlySpan<TKey> key) => lookup.Remove(key);

    public bool TryGetValue(TKey[] key, out TValue value) => dictionary.TryGetValue(key, out value);

    public bool TryGetValue(ReadOnlySpan<TKey> key, out TValue value) => lookup.TryGetValue(key, out value);

    public TValue GetValueOrDefault(TKey[] key, TValue defaultValue = default) =>
        dictionary.GetValueOrDefault(key, defaultValue);

    public TValue GetValueOrDefault(ReadOnlySpan<TKey> key, TValue defaultValue = default) =>
        TryGetValue(key, out TValue value) ? value : defaultValue;

    public ref TValue GetValueRefOrAddDefault(TKey[] key, out bool added) =>
        ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out added);

    public ref TValue GetValueRefOrAddDefault(ReadOnlySpan<TKey> key, out bool added) =>
        ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, key, out added);

    public TValue this[TKey[] key] {
        get => dictionary[key];
        set => dictionary[key] = value;
    }

    public TValue this[ReadOnlySpan<TKey> key] {
        get => lookup[key];
        set => lookup[key] = value;
    }

    public ICollection<TKey[]> Keys => dictionary.Keys;
    public ICollection<TValue> Values => dictionary.Values;
}