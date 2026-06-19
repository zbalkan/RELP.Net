namespace Relp;

public sealed class TxId : IComparable, IComparable<TxId>, IEquatable<TxId>, IFormattable
{
    public const int MaxValue = 999_999_999;
    public const int MinValue = 1;
    private int _transactionIdentifier;

    public TxId() : this(MinValue)
    {
    }

    internal TxId(int initialTransactionIdentifier)
    {
        if (initialTransactionIdentifier < MinValue || initialTransactionIdentifier > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTransactionIdentifier), $"RELP transaction id must be between {MinValue} and {MaxValue}.");
        }

        _transactionIdentifier = initialTransactionIdentifier;
    }

    public uint UnsignedValue => (uint)Value;
    public int Value => Volatile.Read(ref _transactionIdentifier);

    public static explicit operator TxId(int value) => new(value);

    public static explicit operator TxId(uint value)
    {
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"RELP transaction id must be between {MinValue} and {MaxValue}.");
        }

        return new TxId((int)value);
    }

    public static implicit operator int(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return txId.Value;
    }

    public static implicit operator uint(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return txId.UnsignedValue;
    }

    public static TxId operator -(TxId txId, int offset)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return new TxId(Shift(txId.Value, -(long)offset));
    }

    public static TxId operator --(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        txId.Move(-1);
        return txId;
    }

    public static TxId operator +(TxId txId, int offset)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return new TxId(Shift(txId.Value, offset));
    }

    public static TxId operator ++(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        txId.Move(1);
        return txId;
    }

    public static TxId Parse(string value)
    {
        if (!TryParse(value, out var txId))
        {
            throw new FormatException($"Value must be a RELP transaction id between {MinValue} and {MaxValue}.");
        }

        return txId!;
    }

    public static bool TryParse(string? value, out TxId? txId)
    {
        txId = null;
        if (!uint.TryParse(value, out var parsed) || parsed < MinValue || parsed > MaxValue)
        {
            return false;
        }

        txId = new TxId((int)parsed);
        return true;
    }

    public int CompareTo(object? obj) => obj switch {
        null => 1,
        TxId txId => CompareTo(txId),
        int value => Value.CompareTo(value),
        uint value => UnsignedValue.CompareTo(value),
        _ => throw new ArgumentException("Object must be a TxId, int, or uint.", nameof(obj))
    };

    public int CompareTo(TxId? other) => other is null ? 1 : UnsignedValue.CompareTo(other.UnsignedValue);

    public bool Equals(TxId? other) => other is not null && UnsignedValue == other.UnsignedValue;

    public override bool Equals(object? obj) => obj switch {
        TxId txId => Equals(txId),
        int value => Value == value,
        uint value => UnsignedValue == value,
        _ => false
    };

    public override int GetHashCode() => UnsignedValue.GetHashCode();

    public int Next() => Move(1);

    public int Previous() => Move(-1);

    public override string ToString() => UnsignedValue.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => UnsignedValue.ToString(format, formatProvider);

    private static int Shift(int value, long offset)
    {
        var zeroBased = value - MinValue;
        var shifted = (zeroBased + offset) % MaxValue;
        if (shifted < 0)
        {
            shifted += MaxValue;
        }

        return (int)shifted + MinValue;
    }

    private int Move(long offset)
    {
        while (true)
        {
            var current = Volatile.Read(ref _transactionIdentifier);
            var next = Shift(current, offset);
            if (Interlocked.CompareExchange(ref _transactionIdentifier, next, current) == current)
            {
                return current;
            }
        }
    }
}