using System.Collections;

namespace Toycoin;

public class ArrayKeyHashSet<T> : ISet<T[]> where T : IComparable {
    private readonly HashSet<T[]> set;
    private readonly HashSet<T[]>.AlternateLookup<ReadOnlySpan<T>> lookup;

    public ArrayKeyHashSet() {
        set = new(ArrayComparer<T>.Instance);
        lookup = set.GetAlternateLookup<ReadOnlySpan<T>>();
    }

    public ArrayKeyHashSet(int capacity) {
        set = new(capacity, ArrayComparer<T>.Instance);
        lookup = set.GetAlternateLookup<ReadOnlySpan<T>>();
    }

    public ArrayKeyHashSet(IEnumerable<T[]> collection) {
        set = new(collection, ArrayComparer<T>.Instance);
        lookup = set.GetAlternateLookup<ReadOnlySpan<T>>();
    }

    public IEnumerator<T[]> GetEnumerator() => set.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => set.GetEnumerator();

    void ICollection<T[]>.Add(T[] item) => set.Add(item);

    public void ExceptWith(IEnumerable<T[]> other) => set.ExceptWith(other);

    public void IntersectWith(IEnumerable<T[]> other) => set.IntersectWith(other);

    public bool IsProperSubsetOf(IEnumerable<T[]> other) => set.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T[]> other) => set.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T[]> other) => set.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T[]> other) => set.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T[]> other) => set.Overlaps(other);

    public bool SetEquals(IEnumerable<T[]> other) => set.SetEquals(other);

    public void SymmetricExceptWith(IEnumerable<T[]> other) => set.SymmetricExceptWith(other);

    public void UnionWith(IEnumerable<T[]> other) => set.UnionWith(other);

    bool ISet<T[]>.Add(T[] item) => set.Add(item);
    
    public bool Add(ReadOnlySpan<T> item) => lookup.Add(item);

    public void Clear() => set.Clear();

    public bool Contains(T[] item) => set.Contains(item);

    public bool Contains(ReadOnlySpan<T> item) => lookup.Contains(item);

    public void CopyTo(T[][] array, int arrayIndex) {
        foreach (var item in set)
            array[arrayIndex++] = item;
    }

    public bool Remove(T[] item) => set.Remove(item);

    public int Count => set.Count;
    public bool IsReadOnly => false;
}