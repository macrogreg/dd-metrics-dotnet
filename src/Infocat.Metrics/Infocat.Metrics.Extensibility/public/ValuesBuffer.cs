using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Infocat.Metrics.Extensibility
{
    /// <summary>
    /// The metric values buffer is a key component of the (mostly) lock-free aggregation logic.
    /// If allows to quickly collect metric values and to update the running aggregate at regular intervals.
    /// This is required becasue aggregates for some aggregation kinds are expensive to update and/or require a lock (e.g.
    /// some percentile algorithms). By collecting a some values first, the expensive/locked operation can occur less frequently.
    /// </summary>
    /// <typeparam name="T">The type of values to be stored in the buffer.</typeparam>
    /// <remarks>ValuesBuffer{T} is used with structs, make sure that the specified capacity does does not place the
    /// buffer onto the large object heap.</remarks>
    public class ValuesBuffer<T>
    {
        internal const int MaxCapacity = 5000;

        private const int IsLocked = 1;
        private const int IsNotLocked = 0;

        private readonly T[] _values;
        private readonly int _capacity;
        private int _prevAddIndex;
        private int _isLocked;

        private ValuesBuffer()
        {
            throw new NotSupportedException("Please use other ctor overloads.");
        }

        internal ValuesBuffer(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"{nameof(capacity)} may not be negative, but {capacity} was specified.");
            }

            if (capacity > MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"{nameof(capacity)} may not be larger than {MaxCapacity}, but {capacity} was specified.");
            }

            _capacity = capacity;
            _values = new T[capacity];
            _prevAddIndex = -1;
            _isLocked = IsNotLocked;
        }

        internal bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Volatile.Read(ref _prevAddIndex) < 0; }
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _values[index]; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryAdd(T value)
        {
            int index = Interlocked.Increment(ref _prevAddIndex);

            if (index < _capacity)
            {
                _values[index] = value;
                return true;
            }
            else
            {
                _prevAddIndex = _capacity;
                return false;
            }
        }

        internal bool TryCountValuesAndLock(out int valuesCount)
        {
            int prevLocked = Interlocked.Exchange(ref _isLocked, IsLocked);
            if (IsLocked == prevLocked)
            {
                valuesCount = 0;
                return false;
            }

            int prevAddIndex = Interlocked.Exchange(ref _prevAddIndex, _capacity);
            valuesCount = Math.Min(prevAddIndex + 1, _capacity);
            return true;
        }

        internal void Reset()
        {
            Interlocked.Exchange(ref _isLocked, IsLocked);
            Interlocked.Exchange(ref _prevAddIndex, _capacity);

            for (int i = 0; i < _capacity; i++)
            {
                _values[i] = default(T);
            }

            Interlocked.Exchange(ref _prevAddIndex, 0);
            Interlocked.Exchange(ref _isLocked, IsNotLocked);
        }
    }
}
