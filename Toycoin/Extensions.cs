namespace Toycoin;

public static class Extensions {
    public static bool IsLessThan<T>(this ReadOnlySpan<T> first, ReadOnlySpan<T> second) where T : IComparable<T> {
        for (int i = 0, length = Math.Min(first.Length, second.Length); i < length; i++) {
            int c = first[i].CompareTo(second[i]);
            if (c != 0)
                return c < 0;
        }
        return true;
    }
}