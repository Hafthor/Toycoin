namespace Toycoin;

public struct Toycoin(ulong value) : IComparable<Toycoin>, IEquatable<Toycoin>, IFormattable {
    private readonly ulong value = value;

    public const string Symbol = "🧸";

    public const int Size = sizeof(ulong);
    
    private const decimal OneToycoin = 1_000_000m;

    public static implicit operator Toycoin(ulong value) => new(value);
    
    public static implicit operator ulong(Toycoin toycoin) => toycoin.value;
    
    public override string ToString() {
        decimal value = this.value;
        value /= OneToycoin;
        return Symbol + value;
    }

    public string ToString(string format, IFormatProvider formatProvider) {
        decimal value = this.value;
        value /= OneToycoin;
        return Symbol + value.ToString(format, formatProvider);
    }

    public static Toycoin MinValue => (Toycoin)(ulong.MinValue / OneToycoin);
    
    public static Toycoin MaxValue => (Toycoin)(ulong.MaxValue / OneToycoin);
    
    public static Toycoin Parse(string s) {
        checked {
            var d = decimal.Parse(s.TrimStart(Symbol.ToCharArray()));
            if (d < 0) throw new ArgumentOutOfRangeException(nameof(s), "Value cannot be negative");
            if (d > ulong.MaxValue / OneToycoin) throw new ArgumentOutOfRangeException(nameof(s), "Value is too large");
            return new Toycoin((ulong)(d * OneToycoin));
        }
    }

    public static bool TryParse(string s, out Toycoin result) {
        bool success = decimal.TryParse(s.TrimStart(Symbol.ToCharArray()), out decimal value);
        if (!success || value is < 0 or > ulong.MaxValue / OneToycoin) {
            result = 0; 
            return false;
        }
        result = new((ulong)(value * OneToycoin));
        return true;
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