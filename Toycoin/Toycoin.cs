namespace Toycoin;

public struct Toycoin(ulong value) : IComparable<Toycoin>, IEquatable<Toycoin>, IFormattable {
    private readonly ulong value = value;

    public const int Size = sizeof(ulong);
    
    private const decimal OneToycoin = 1_000_000m;

    public static implicit operator Toycoin(ulong value) => new(value);
    
    public static implicit operator ulong(Toycoin toycoin) => toycoin.value;
    
    public override string ToString() {
        decimal value = this.value;
        value /= OneToycoin;
        return value.ToString();
    }

    public string ToString(string format, IFormatProvider formatProvider) {
        decimal value = this.value;
        value /= OneToycoin;
        return value.ToString(format, formatProvider);
    }

    public static Toycoin MinValue => (Toycoin)(ulong.MinValue / OneToycoin);
    
    public static Toycoin MaxValue => (Toycoin)(ulong.MaxValue / OneToycoin);
    
    public static Toycoin Parse(string s) {
        checked {
            return new Toycoin((ulong)(decimal.Parse(s) * OneToycoin));
        }
    }

    public static bool TryParse(string s, out Toycoin result) {
        if (decimal.TryParse(s, out decimal value)) {
            checked {
                result = new((ulong)(value * OneToycoin));
            }
            return true;
        }
        result = default;
        return false;
    }
    
    public static Toycoin FromBytes(ReadOnlySpan<byte> bytes) => new(BitConverter.ToUInt64(bytes));
    
    public byte[] ToBytes() => BitConverter.GetBytes(value);
    
    public void WriteBytesTo(Span<byte> span) => BitConverter.TryWriteBytes(span, value);
    
    public int CompareTo(Toycoin other) => value.CompareTo(other.value);
    
    public bool Equals(Toycoin other) => value == other.value;
    
    public override bool Equals(object obj) => obj is Toycoin toycoin && value == toycoin.value;
    
    public override int GetHashCode() => value.GetHashCode();
    
    public static bool operator ==(Toycoin left, Toycoin right) => left.Equals(right);
    
    public static bool operator !=(Toycoin left, Toycoin right) => !left.Equals(right);
    
    public static Toycoin operator +(Toycoin left, Toycoin right) {
        checked {
            return new Toycoin(left.value + right.value);
        }
    }
    
    public static Toycoin operator -(Toycoin left, Toycoin right) {
        checked {
            return new Toycoin(left.value - right.value);
        }
    }
    
    public static bool operator <(Toycoin left, Toycoin right) => left.value < right.value;
    
    public static bool operator >(Toycoin left, Toycoin right) => left.value > right.value;
    
    public static bool operator <=(Toycoin left, Toycoin right) => left.value <= right.value;
    
    public static bool operator >=(Toycoin left, Toycoin right) => left.value >= right.value;
}