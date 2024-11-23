namespace Toycoin;

public class ArrayComparer<T> : EqualityComparer<T[]>, IAlternateEqualityComparer<ReadOnlySpan<T>, T[]>
    where T : IComparable {
    public static readonly ArrayComparer<T> Instance = new();

    private ArrayComparer() {
    }

    public override bool Equals(T[] obj1, T[] obj2) =>
        obj1 == null || obj2 == null ? obj1 == obj2 : ReferenceEquals(obj1, obj2) || obj1.SequenceEqual(obj2);

    public override int GetHashCode(T[] obj) => GetHashCode(obj.AsSpan());

    public bool Equals(ReadOnlySpan<T> alternate, T[] other) => other != null && alternate.SequenceEqual(other);

    public int GetHashCode(ReadOnlySpan<T> alternate) {
        int hash = 0;
        if (alternate != null)
            foreach (var item in alternate)
                hash = hash * 13 + item.GetHashCode();
        return hash;
    }

    public T[] Create(ReadOnlySpan<T> alternate) => alternate.ToArray();
}