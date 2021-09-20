using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Infocat.Metrics.Extensibility
{
    /// <summary>
    /// This is a powerfull base class for metrics with (mostly) lock-free aggregation logic.
    /// If allows to quickly collect metric values and to update the running aggregate at regular intervals.
    /// This is required becasue aggregates for some aggregation kinds are expensive to update and/or require a lock (e.g.
    /// some percentile algorithms). By collecting a some values first, the expensive/locked operation can occur less frequently.
    /// </summary>    
    public abstract class BufferedMetricAggregatorBase<TBufferedValue> : MetricAggregatorBase
    {
        // 100 is permitted, but typicaly, a much smaller number should be used.
        private const int MaxSpareBuffersObjectPoolCapacity = 50;

        private readonly int _valuesBufferCapacity;
        private readonly int _spareBuffersObjectPoolCapacity;
        private readonly bool _isCollectSynchronized;

        private ValuesBuffer<TBufferedValue> _currentValuesBuffer;
        private readonly SimpleObjectPool<ValuesBuffer<TBufferedValue>> _spareBuffersObjectPool;

        private readonly ReaderWriterLockSuperSlim _finishAggregationPeriodLock = new ReaderWriterLockSuperSlim();


        /// <summary>
        /// Initializes a <c>BufferedMetricAggregatorBase</c> instance.
        /// </summary>
        /// <param name="owner">The owner aggregator.</param>
        /// <param name="bufferCapacity">The size of a single value buffer. Values on the order of 10 - 1000 is recommended.
        /// Max permitted value is <see cref="ValuesBuffer{T}.MaxCapacity"/></param>
        /// <param name="spareBuffersObjectPoolCapacity">Size of the value buffer object pool. A value on the order 1 - 5 is recommeneded.
        /// Max permitted value is <see cref="MaxSpareBuffersObjectPoolCapacity"/>.</param>
        /// <param name="isCollectSynchronized">Specifies whether additional synchrinization is to be used to ensure that the return
        /// value of the <see cref="Collect(Double)"/> method truthfully describes whether the metric value was collected.<br />
        /// <para>If <c>isCollectSynchronized</c> is <c>false</c>, there is a rare, but possible race where Collect(..) will return <c>true</c>,
        /// but the value will not be collected. That can happen if the <c>MetricCollectionManager</c> completes the aggregation cycle and
        /// the <c>OnFinishAggregationPeriod()</c> method of this aggregator starts and finishes completely (including its loop) when
        /// the <c>Collect(..)</c> chain was already invoked, but the first <c>.TryAdd(..)</c> attempt on the buffer has not yet occurred.</para>
        /// <para>Setting <c>isCollectSynchronized</c> to <c>true</c> ensures that additional checks and synchronization are performed, and the 
        /// value returned by the <c>Collect(..)</c> method of this instance always correctly describes whether the value was collected.</para>
        /// <para>In is recommended that Metric Aggregator implementations consider opting into setting this value to <c>false</c>, becasue the
        /// race may occur very rarely in practive, and the consequences of dropping a small number of metric values may be benign.
        /// In comparison, the increased synchronisation costs occur at every metric value connection.</para></param>
        protected BufferedMetricAggregatorBase(Metric owner, int bufferCapacity, int spareBuffersObjectPoolCapacity, bool isCollectSynchronized)
            : base(owner)
        {
            if (bufferCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferCapacity),
                                                     $"{nameof(bufferCapacity)} may not be negative, but {bufferCapacity} was specified.");
            }

            if (bufferCapacity > ValuesBuffer<TBufferedValue>.MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferCapacity),
                                                     $"{nameof(bufferCapacity)} may not be larger than {ValuesBuffer<TBufferedValue>.MaxCapacity},"
                                                   + $" but {bufferCapacity} was specified.");
            }

            if (spareBuffersObjectPoolCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spareBuffersObjectPoolCapacity),
                                                     $"{nameof(spareBuffersObjectPoolCapacity)} may not be negative,"
                                                   + $" but {spareBuffersObjectPoolCapacity} was specified.");
            }

            if (spareBuffersObjectPoolCapacity > MaxSpareBuffersObjectPoolCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(spareBuffersObjectPoolCapacity),
                                                     $"{nameof(spareBuffersObjectPoolCapacity)} may not be larger than {MaxSpareBuffersObjectPoolCapacity},"
                                                   + $" but {spareBuffersObjectPoolCapacity} was specified.");
            }

            _valuesBufferCapacity = bufferCapacity;
            _spareBuffersObjectPoolCapacity = spareBuffersObjectPoolCapacity;
            _isCollectSynchronized = isCollectSynchronized;

            _currentValuesBuffer = new ValuesBuffer<TBufferedValue>(_valuesBufferCapacity);
            _spareBuffersObjectPool = new SimpleObjectPool<ValuesBuffer<TBufferedValue>>(_spareBuffersObjectPoolCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool CollectValue(TBufferedValue value)
        {
            return _isCollectSynchronized ? CollectValueSynchronized(value) : CollectValueImplementation(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool CanCollectValue(TBufferedValue _)
        {
            return true;
        }

        protected abstract void OnFlushBuffer(ValuesBuffer<TBufferedValue> lockedValuesBuffer, int valuesInBufferCount);

        protected override void OnFinishAggregationPeriod(IMetricAggregate _)
        {
            FlushBuffersOnAggregationFinish();
        }

        private bool CollectValueSynchronized(TBufferedValue value)
        {
            _finishAggregationPeriodLock.StartRead();

            try
            {
                return IsActive
                            ? CollectValueImplementation(value)
                            : false;
            }
            finally
            {
                _finishAggregationPeriodLock.EndRead();
            }
        }

        private bool CollectValueImplementation(TBufferedValue value)
        {
            ValuesBuffer<TBufferedValue> valuesBuffer = _currentValuesBuffer;

            // Try adding value to buffer:
            bool isValueCollected = valuesBuffer.TryAdd(value);

            // We will re-try adding the value to whichever is the current buffer until we either succeed or until the aggregation period finishes.
            while (false == isValueCollected && IsActive)
            {
                // Could not add value to buffer => buffer is full.

                // Set up a fresh buffer and add the value to it:
                ValuesBuffer<TBufferedValue> freshBuffer = GetOrCreateFreshValuesBuffer();
                if (!freshBuffer.TryAdd(value))
                {
                    // We failed adding to a fresh buffer. Something is very wrong. Give up.
                    return false;
                }

                // Try setting the fresh buffer to be the current buffer:
                isValueCollected = (valuesBuffer == Interlocked.CompareExchange(ref _currentValuesBuffer, freshBuffer, valuesBuffer));
                if (isValueCollected)
                {
                    // We have won the race for the asignment and set '_currentValuesBuffer' to 'freshBuffer',
                    // i.e. the above CompareExchange DID assign.
                    // Other threads can continue collecting metrics into this aggregate using the buffer we just put in place.
                    // This thread will now flush the fuller and then return it to the cache.

                    FlushBuffer(valuesBuffer);
                    RecycleBuffer(valuesBuffer);
                }
                else
                {
                    // We have lost the race for the asignment and set '_currentValuesBuffer' to 'freshBuffer',
                    // i.e. the above CompareExchange did NOT assign.
                    // Another thread installed a its own fresh buffer.                        
                    // We will return our 'freshBuffer' instance to the cache, and then update 'valuesBuffer' to point to the current buffer.

                    RecycleBuffer(freshBuffer);
                    valuesBuffer = Volatile.Read(ref _currentValuesBuffer);
                }
            }

            return isValueCollected;
        }

        private void FlushBuffersOnAggregationFinish()
        {
            // If _isCollectSynchronized is False, this will be always uncontended.
            _finishAggregationPeriodLock.StartWrite();

            try
            {
                ValuesBuffer<TBufferedValue> valuesBuffer = _currentValuesBuffer;

                while (false == valuesBuffer.IsEmpty)
                {
                    ValuesBuffer<TBufferedValue> freshBuffer = GetOrCreateFreshValuesBuffer();
                    bool isFeshBufferSet = (valuesBuffer == Interlocked.CompareExchange(ref _currentValuesBuffer, freshBuffer, valuesBuffer));

                    if (isFeshBufferSet)
                    {
                        FlushBuffer(valuesBuffer);
                        RecycleBuffer(valuesBuffer);
                    }
                    else
                    {
                        RecycleBuffer(freshBuffer);
                    }

                    valuesBuffer = Volatile.Read(ref _currentValuesBuffer);
                }
            }
            finally
            {
                _finishAggregationPeriodLock.EndWrite();
            }
        }

        private ValuesBuffer<TBufferedValue> GetOrCreateFreshValuesBuffer()
        {
            if (!_spareBuffersObjectPool.TryPull(out ValuesBuffer<TBufferedValue> freshBuffer))
            {
                freshBuffer = new ValuesBuffer<TBufferedValue>(_valuesBufferCapacity);
            }

            return freshBuffer;
        }

        private void RecycleBuffer(ValuesBuffer<TBufferedValue> buffer)
        {
            buffer.Reset();
            _spareBuffersObjectPool.TryAdd(buffer);
        }

        private void FlushBuffer(ValuesBuffer<TBufferedValue> valuesBuffer)
        {
            bool canLock = valuesBuffer.TryCountValuesAndLock(out int valuesCount);
            if (valuesCount > 0 || canLock)
            {
                OnFlushBuffer(valuesBuffer, valuesCount);
            }
        }
    }
}