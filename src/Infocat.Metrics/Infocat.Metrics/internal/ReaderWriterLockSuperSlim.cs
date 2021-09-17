using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Infocat.Metrics
{
    /// <summary>
    /// A very simple version of a reader-writer lock.
    /// Any number or concurrent readers is possible. Only one writer is possible.
    /// </summary>
    /// <remarks>
    /// This class uses a <c>SemaphoreSlim</c> to contrill access. All readers act as a single group.
    /// E.e. the very first reader to enter acquires the semaphore, and very last reader to leave releases the semaphore.
    /// The writer always acquires / releases the semaphone.
    /// </remarks>
    internal class ReaderWriterLockSuperSlim : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private int _readersCount = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StartRead()
        {
            int readersCount = Interlocked.Increment(ref _readersCount);

            if (readersCount == 1)
            {
                _semaphore.Wait();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndRead()
        {
            int readersCount = Interlocked.Decrement(ref _readersCount);

            if (readersCount == 0)
            {
                _semaphore.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StartWrite()
        {
            _semaphore.Wait();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool StartWrite(int timeoutMs)
        {
            return _semaphore.Wait(timeoutMs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task StartWriteAsync()
        {
            return _semaphore.WaitAsync();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task StartWriteAsync(int timeoutMs)
        {
            return _semaphore.WaitAsync(timeoutMs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndWrite()
        {
            _semaphore.Release();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _semaphore.Dispose();
        }
    }
}
