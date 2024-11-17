namespace Toycoin;

public class ByteArrayComparer : EqualityComparer<byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { // don't instantiate - use static ByteArrayComparer.Instance
    }

    public override bool Equals(byte[] obj1, byte[] obj2) =>
        obj1 == null || obj2 == null ? obj1 == obj2 : ReferenceEquals(obj1, obj2) || obj1.SequenceEqual(obj2);

    public override int GetHashCode(byte[] obj) {
        HashCode hashCode = new();
        hashCode.AddBytes(obj);
        return hashCode.ToHashCode();
    }
}