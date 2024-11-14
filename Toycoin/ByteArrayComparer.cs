namespace Toycoin;

public class ByteArrayComparer : EqualityComparer<byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { // don't instantiate - use static ByteArrayComparer.Instance
    }

    public override bool Equals(byte[] first, byte[] second) =>
        first == null || second == null
            ? first == second
            : ReferenceEquals(first, second) || first.Length == second.Length && first.SequenceEqual(second);

    public override int GetHashCode(byte[] obj) => obj.Length;
}