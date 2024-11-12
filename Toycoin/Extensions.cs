namespace Toycoin;

public static class Extensions {
    public static byte[] Coalesce(this IList<byte[]> arrays) {
        if (arrays.Count == 0) return [];
        if (arrays.Count == 1) return arrays[0];
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var a in arrays) {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    public static bool IsLessThan(this byte[] first, byte[] second) => IsLessThan(first.AsSpan(), second.AsSpan());
    public static bool IsLessThan(this ReadOnlySpan<byte> first, byte[] second) => IsLessThan(first, second.AsSpan());
    public static bool IsLessThan(this byte[] first, ReadOnlySpan<byte> second) => IsLessThan(first.AsSpan(), second);

    public static bool IsLessThan(this ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
        for (int i = 0, length = Math.Min(first.Length, second.Length); i < length; i++)
            if (first[i] != second[i])
                return first[i] < second[i];
        return true;
    }
}