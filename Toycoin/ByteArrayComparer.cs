namespace Toycoin;

public class ByteArrayComparer : EqualityComparer<byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { // don't instantiate - use static ByteArrayComparer.Instance
    }

    public override bool Equals(byte[] first, byte[] second) {
        if (first == null || second == null) return first == second;
        return ReferenceEquals(first, second) || first.Length == second.Length && first.SequenceEqual(second);
    }

    public override int GetHashCode(byte[] obj) {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Length;
    }
}