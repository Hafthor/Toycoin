namespace Toycoin;

public class ByteArrayComparer : EqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { // don't instantiate - use static ByteArrayComparer.Instance
    }

    public override bool Equals(byte[] obj1, byte[] obj2) =>
        obj1 == null || obj2 == null ? obj1 == obj2 : ReferenceEquals(obj1, obj2) || obj1.SequenceEqual(obj2);

    public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) =>
        other != null && alternate.SequenceEqual(other);

    public override int GetHashCode(byte[] obj) => GetHashCode(obj.AsSpan());

    public int GetHashCode(ReadOnlySpan<byte> alternate) {
        HashCode hashCode = new();
        foreach (var b in alternate)
            hashCode.Add(b);
        return hashCode.ToHashCode();
    }

    public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
}