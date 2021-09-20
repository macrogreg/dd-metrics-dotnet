using System;
using System.Threading;

namespace Infocat.Metrics
{
    internal class SimpleObjectPool<T> where T : class
    {
        /// <summary>
        /// We limit the max object pool size so that pools are never placed on the LOH.
        /// </summary>
        public const int MaxObjectPoolCapacity = 10000;

        private readonly int _capacity;
        private readonly T[] _pool;

        public SimpleObjectPool(int capacity)
        {
            // 'objectPoolSize' is OK. It means "do not use this pool".

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                                                     $"{nameof(capacity)} may not be negative, but \"{capacity}\" was specified.");
            }

            if (capacity > MaxObjectPoolCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                                                     $"{nameof(capacity)} may not be larger than {MaxObjectPoolCapacity},"
                                                   + $" but \"{capacity}\" was specified.");
            }

            _capacity = capacity;
            _pool = new T[_capacity];
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public bool TryAdd(T instance)
        {
            bool wasAdded = false;

            for (int i = 0; i < _capacity && false == wasAdded; i++)
            {
                if (_pool[i] == null)
                {
                    wasAdded = (null == Interlocked.CompareExchange(ref _pool[i], instance, null));
                }
            }

            return wasAdded;
        }

        public bool TryPull(out T instance)
        {
            instance = null;
            for (int i = 0; i < _capacity && instance == null; i++)
            {
                if (_pool[i] != null)
                {
                    instance = Interlocked.Exchange(ref _pool[i], null);
                }
            }

            return (instance != null);
        }
    }
}
