using System;
using System.Threading;
using Infocat.Util;

namespace Infocat.Metrics
{
    /// <summary>
    /// This class represents the aggregation cycle.
    /// 
    /// We create a new dedicated thread rather than using the thread pool.
    /// The background loop uses a dedicated tread in order
    /// to prevent the processing done by this thread from being affected
    /// by potential thread pool starvation.
    /// So, MainLoop() is a very long running operation that occupies a thread forever.
    /// It uses synchronous waits / sleeps when it is idle and always keeps its thread afinity. 
    /// 
    /// It is preferable to use blocking IO (i.e. non-async-IO) directly on this
    /// thread to avoid threadpool interactions.
    /// 
    /// Notably, the thread must be initially created explicitly, instead of obtaining it from the thread pool.
    /// If we were to schedule MainLoop() on the thread pool, it would be possible that the thread chosen by the
    /// pool had run user code before. Such user code may be doing an asynchronous wait scheduled to
    /// continue on the same thread (e.g. this can occur when using a custom synchronization context or a
    /// custom task scheduler). If such case the waiting user code will never continue (deadlock).
    /// By creating our own thread, we guarantee no interactions with potentially incorrectly written async user code.
    /// 
    /// @ToDo: Deal with logging in this class.
    /// </summary>
    internal class AggregationCycle : IDisposable
    {
        private static class LogSource
        {
            public const string Moniker = nameof(AggregationCycle);
        }

        private static class State
        {
            public const int NotStarted = 1;
            public const int Running = 2;
            public const int ShutdownRequested = 3;
            public const int ShutdownCompleted = 4;
            public const int Disposed = 4;
        }

        private const string LoopThreadName = nameof(AggregationCycle) + "." + nameof(MainLoop);

#pragma warning disable IDE1006  // Static fields acting as semantic constants {

        // We wait for these periods (milliseconds) when waiting for a shutdown to complete.
        // Note that on Windows, unless the timer reslution was explicitly increased using the native API timeBeginPeriod(..),
        // the resolution of the clock is between 15 and 16 ms. Waits smaller than the timer resolution just result in
        // giving up the current time slice, i.e. the expected min wait time is approx. 15/2 = approx 7ms.
        // So anything below 15ms means as much as "do sleep, but sleep as little as possible".
        private static readonly int[] ShutdownWaitMs = new int[] { 1, 1, 1, 25, 50, 100, 500 };

#pragma warning restore IDE1006  // } static fields acting as semantic constants.

        private readonly int _aggregationPeriodLengthSecs;
        private readonly Action<DateTimeOffset> _onAggregationCycleIterationStartedListener;

        private int _loopState = State.NotStarted;
        private AutoResetEvent _loopSignal = null;
        private Thread _loopThread = null;


        public AggregationCycle(MetricCollectionConfiguration config,
                                Action<DateTimeOffset> onAggregationCycleIterationStartedListener)
        {
            Validate.NotNull(config, nameof(config));
            Validate.NotNull(onAggregationCycleIterationStartedListener, nameof(onAggregationCycleIterationStartedListener));

            _aggregationPeriodLengthSecs = GetValidAggregationPeriodLengthSecs(config);
            _onAggregationCycleIterationStartedListener = onAggregationCycleIterationStartedListener;
        }

        private int GetValidAggregationPeriodLengthSecs(MetricCollectionConfiguration config)
        {
            const int SecondsInMinute = 60;
            const int SecondsInDay = SecondsInMinute * 60 * 24;

            if (config.AggregationPeriodLengthSeconds < 5)
            {
                throw new ArgumentOutOfRangeException($"{nameof(config)}.{nameof(MetricCollectionConfiguration.AggregationPeriodLengthSeconds)}",
                                                      "The minimal supported aggregation period length is 5 seconds."
                                                   + $" However, {config.AggregationPeriodLengthSeconds} was specified in the configuration.");
            }


            if (config.AggregationPeriodLengthSeconds > SecondsInDay)
            {
                throw new ArgumentOutOfRangeException($"{nameof(config)}.{nameof(MetricCollectionConfiguration.AggregationPeriodLengthSeconds)}",
                                                     $"The maximum supported aggregation period length is 24 hours (={SecondsInDay} seconds)."
                                                   + $" However, {config.AggregationPeriodLengthSeconds} was specified in the configuration.");
            }

            if (config.AggregationPeriodLengthSeconds >= SecondsInMinute)
            {
                if (config.AggregationPeriodLengthSeconds % SecondsInMinute != 0)
                {
                    throw new ArgumentException("If the aggregation period length one minute or longer, it must be a whole multiple of a minute."
                                             + $" I.e., the value of {nameof(config)}.{nameof(MetricCollectionConfiguration.AggregationPeriodLengthSeconds)}"
                                             + $" must be a whole multiple of {SecondsInMinute}."
                                             + $" However, {config.AggregationPeriodLengthSeconds} was specified in the configuration.");
                }
            }
            else
            {
                if (5 != config.AggregationPeriodLengthSeconds
                            && 10 != config.AggregationPeriodLengthSeconds
                            && 15 != config.AggregationPeriodLengthSeconds
                            && 20 != config.AggregationPeriodLengthSeconds
                            && 30 != config.AggregationPeriodLengthSeconds
                            && 60 != config.AggregationPeriodLengthSeconds)
                {
                    throw new ArgumentException("If the aggregation period length is shorter than one minute, it must be one of:"
                                              + " 5 sec, 10 sec, 15 sec, 20 sec, 30 sec."
                                             + $" However, {config.AggregationPeriodLengthSeconds} was specified in the configuration.");
                }
            }

            return config.AggregationPeriodLengthSeconds;
        }

        ~AggregationCycle()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public bool Start()
        {
            // See the doc-comment to this class for info on why we start the thread this particular way.

            int prevState = Interlocked.CompareExchange(ref _loopState, State.Running, State.NotStarted);
            if (prevState != State.NotStarted)
            {
                return false;
            }

            _loopSignal = new AutoResetEvent(false);

            _loopThread = new Thread(this.MainLoop);
            _loopThread.Name = LoopThreadName;
            _loopThread.IsBackground = true;

            //Log.Info(LogSource.Moniker,
            //         "Starting loop thread",
            //         $"{nameof(_loopThread)}.{nameof(Thread.Name)}", _loopThread.Name,
            //         $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread.ManagedThreadId,
            //         $"{nameof(_loopThread)}.{nameof(Thread.GetHashCode)}", _loopThread.GetHashCode());

            _loopThread.Start();
            return true;
        }

        public void Shutdown()
        {
            // If we already shut down, all is good:
            int loopState = Volatile.Read(ref _loopState);
            if (loopState == State.ShutdownCompleted || loopState == State.Disposed)
            {
                return;
            }

            // If we were not started, we can transition directly to shut down:
            int prevState = Interlocked.CompareExchange(ref _loopState, State.ShutdownCompleted, State.NotStarted);
            if (prevState == State.NotStarted)
            {
                OnShutdownRequested();

                //Log.Info(LogSource.Moniker,
                //         "Shutting down main loop before it started",
                //         $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId);

                OnShutdownCompleted();
                return;
            }

            // Request shutdown:
            prevState = Interlocked.CompareExchange(ref _loopState, State.ShutdownRequested, State.Running);
            bool isShutdownRequestRaceWinner = (prevState == State.Running);

            // If shutdown is requested concurrently, only the race-winning thread invoked the notification callbacks.
            // (But all threads must wait for the shutdown to complete.)
            if (isShutdownRequestRaceWinner)
            {
                OnShutdownRequested();
            }

            // Signal main loop to wake up:
            AutoResetEvent loopSignal = _loopSignal;
            loopSignal?.Set();

            // If _loopState was NotStarted, we have transitioned to ShutdownCompleted and returned.
            // If _loopState was Running, we have transitioned to ShutdownRequested.
            // We will now loop and pull the _loopState until the mail loop completes and we transition to ShutdownCompleted or Disposed.

            int shutdownWaitMsIndex = 0;
            loopState = Volatile.Read(ref _loopState);
            while (loopState != State.ShutdownCompleted && loopState != State.Disposed)
            {
                Thread.Sleep(ShutdownWaitMs[shutdownWaitMsIndex]);
                shutdownWaitMsIndex = (shutdownWaitMsIndex + 1) % ShutdownWaitMs.Length;

                loopState = Volatile.Read(ref _loopState);
            }

            //Log.Info(LogSource.Moniker,
            //         "Main loop shut down",
            //         $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId);

            if (isShutdownRequestRaceWinner)
            {
                OnShutdownCompleted();
            }
        }

        private void OnAggregationCycleIterationStarted(DateTimeOffset targetCycleStartTime, DateTimeOffset actualCycleStartTime)
        {
            Action<DateTimeOffset> onAggregationCycleIterationStartedListener = _onAggregationCycleIterationStartedListener;
            if (onAggregationCycleIterationStartedListener == null)
            {
                return;
            }

            // GetNextCycleStartTargetTime(..) tries to snap cycles to whole multiple of the aggregation cycle length.
            // However, the timer is not precise and it wakes up " only approximately" at the requested time.
            // If we are "close" to the intended cycle start time, we will publish the cycle start as if it hit the intended time exactly.
            // If we are further away, we will just snap to a whole second.
            // That way downstream systems do not need to worry about sub-second timestamp resolution.
            // Note that due to this, the timestamp of the metric aggregate is not exactly the timestamp where all data is collected,
            // but represents a rounded value. This is an explicit design decisin to make post-processing of aggregates more uniform.
            // The MetricCollectionManager must use a different, more precise timer, to calculate exact time periods that are relevant for 
            // precise duration, rate and other calculations.

            const int MaxDriftToRound = 1500;  // 1.5 sec
            int driftMs = Math.Abs((int) (actualCycleStartTime - targetCycleStartTime).TotalMilliseconds);

            DateTimeOffset roundCycleStartTime = (driftMs <= MaxDriftToRound)
                                                        ? targetCycleStartTime
                                                        : targetCycleStartTime.RoundDownToSecond();

            onAggregationCycleIterationStartedListener(roundCycleStartTime);
        }

        private void OnShutdownRequested()
        {
            ;   // No-op for now.
        }

        private void OnShutdownCompleted()
        {
            ;   // No-op for now.
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value", Justification = "@ToDo this suppression once we figure out the logging.")]
        private void LogMainLoopEntry()
        {
            int osThreadId;
            Thread currentThread;
            try
            {
#pragma warning disable CS0618  // GetCurrentThreadId is obsolete but we can still use it for logging purposes (see respective docs)
                osThreadId = AppDomain.GetCurrentThreadId();
#pragma warning restore CS0618  // Type or member is obsolete

                currentThread = Thread.CurrentThread;
            }
            catch
            {
                osThreadId = -1;
                currentThread = null;
            }

            //Log.Info(LogSource.Moniker,
            //         "Entering main loop",
            //         $"{nameof(currentThread)} == {nameof(_loopThread)}", (currentThread == _loopThread).ToString(),
            //         $"{nameof(_loopThread)}.{nameof(Thread.Name)}", _loopThread?.Name,
            //         $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId,
            //         $"{nameof(_loopThread)}.{nameof(Thread.GetHashCode)}", _loopThread?.GetHashCode(),
            //         $"{nameof(_loopThread)}.<{nameof(osThreadId)}>", osThreadId,
            //         $"{nameof(_loopThread)}.{nameof(Thread.IsThreadPoolThread)}", _loopThread?.IsThreadPoolThread,
            //         $"{nameof(_loopThread)}.{nameof(Thread.IsBackground)}", _loopThread?.IsBackground,
            //         $"{nameof(_loopThread)}.{nameof(Thread.Priority)}", _loopThread?.Priority,
            //         $"{nameof(_loopThread)}.{nameof(Thread.ThreadState)}", _loopThread?.ThreadState);
        }

        private void MainLoop()
        {
            LogMainLoopEntry();

            DateTimeOffset actualCycleStartTime = DateTimeOffset.Now;

            while (Volatile.Read(ref _loopState) == State.Running)
            {
                try
                {
                    DateTimeOffset targetCycleStartTime = GetNextCycleStartTargetTime(actualCycleStartTime);
                    actualCycleStartTime = WaitForTargetTime(targetCycleStartTime);

                    // Note that currentCycleStartTime has been set to the time where we completed the wait.
                    // Therefore, the current aggregation period's timespan actually starts now.
                    // I.e., the workload of fetching and submitting metrics that were aggregated during the previous
                    // period is, essentially, performed during this aggregation period.
                    // This is, of course, imprecise, as a few milliseconds will pass between now and the moment where
                    // we actually rotate the aggregators.
                    // It is an explicit design decision that the actual timestamps in the metric aggregates represent
                    // the rounded bounds of aggregation periods, rather than precise real time stamps.

                    OnAggregationCycleIterationStarted(targetCycleStartTime, actualCycleStartTime);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);  // @ToDo: Figure out logging and remove Console writes.
                    //Log.Error(LogSource.Moniker, ex,
                    //          $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId);
                }
            }

            int prevState = Interlocked.CompareExchange(ref _loopState, State.ShutdownCompleted, State.ShutdownRequested);
            if (prevState != State.ShutdownRequested)
            {
                //Log.Error(LogSource.Moniker,
                //          $"The value of {nameof(_loopState)} at the end of {nameof(MainLoop)} was unexpected."
                //         + " There is likely a bug in the state transition logic.",
                //          $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId,
                //          $"Expected value of {nameof(_loopThread)}", State.ShutdownRequested,
                //          $"Actual value of {nameof(_loopThread)}", prevState);
            }
        }

        private DateTimeOffset GetNextCycleStartTargetTime(DateTimeOffset currentCycleStartTime)
        {
            // Next tick:
            //   (current time rounded down to a multiple of period length) + (period length) + (small offset).
            // The strategy here is to always "tick" at the same offset(s) within a minute.
            // Due to drift and imprecise timing this may conflict with the aggregation being EXACTLY the configured number of seconds.
            // In such cases we err on the side of keeping the same offset.
            // This will tend to straighten out the inmterval and to yield consistent timestamps.

            int secAtCycleStartSlot = (_aggregationPeriodLengthSecs < 60)
                                            ? (currentCycleStartTime.Second / _aggregationPeriodLengthSecs) * _aggregationPeriodLengthSecs
                                            : 0;

            DateTimeOffset timeAtCycleStartSlot = currentCycleStartTime.RoundDownToMinute(setSecond: secAtCycleStartSlot);
            DateTimeOffset nextCycleStartTarget = timeAtCycleStartSlot.AddSeconds(_aggregationPeriodLengthSecs);

            // If the next period is "unreasonably" short, we extend that period by 1 cycle length,
            // resulting in a total period that is somewhat longer than the configured length.
            // The "reasonable" shortness is a magic number based on best judgement. It is encoded into the IF table below.

            TimeSpan waitPeriod = nextCycleStartTarget - currentCycleStartTime;
            if (((_aggregationPeriodLengthSecs <= 5) && waitPeriod.TotalSeconds <= 1)
                    || ((5 < _aggregationPeriodLengthSecs && _aggregationPeriodLengthSecs <= 10) && waitPeriod.TotalSeconds <= 2)
                    || ((10 < _aggregationPeriodLengthSecs && _aggregationPeriodLengthSecs <= 60) && waitPeriod.TotalSeconds <= 5)
                    || ((60 < _aggregationPeriodLengthSecs) && waitPeriod.TotalSeconds <= 15))
            {
                nextCycleStartTarget = nextCycleStartTarget.AddSeconds(_aggregationPeriodLengthSecs);
            }

            return nextCycleStartTarget;
        }

        private DateTimeOffset WaitForTargetTime(DateTimeOffset nextCycleStartTarget)
        {
            bool hasSlept = false;

            while (true)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                TimeSpan remainingWaitInterval = nextCycleStartTarget - now;

                // We loop until the target time arrives, or antil the loop is aborted vie changing the state.
                // (However, we ALWAYS make sure to sleep at least 1 millisecond, to allow other threads to to make progress.)

                if (remainingWaitInterval <= TimeSpan.Zero || Volatile.Read(ref _loopState) != State.Running)
                {
                    if (!hasSlept)
                    {
                        Thread.Sleep(millisecondsTimeout: 1);
                    }

                    return now;
                }

                // Sleep until interval passes or until we are signaled:
                try
                {
                    _loopSignal.WaitOne(remainingWaitInterval);
                    hasSlept = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);  // @ToDo: Figure out logging and remove Console writes.
                    //Log.Error(LogSource.Moniker, ex,
                    //          $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", _loopThread?.ManagedThreadId);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            int loopState = Volatile.Read(ref _loopState);
            if (loopState == State.Disposed)
            {
                return;
            }

            // We need to signal the main loop to exit and then wait for it, before we can dispose.
            Shutdown();

            // We are shut down now. 

            // Make sure the main loop thread has finished:
            Thread loopThread = Interlocked.Exchange(ref _loopThread, null);
            if (loopThread != null)
            {
                try
                {
                    loopThread.Join();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);  // @ToDo: Figure out logging and remove Console writes.
                    //Log.Error(LogSource.Moniker, ex, $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", loopThread?.ManagedThreadId);
                }
            }

            // Dispose any disposable fields:
            AutoResetEvent loopSignal = Interlocked.Exchange(ref _loopSignal, null);
            if (loopSignal != null)
            {
                try
                {
                    loopSignal.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);  // @ToDo: Figure out logging and remove Console writes.
                    //Log.Error(LogSource.Moniker, ex, $"{nameof(_loopThread)}.{nameof(Thread.ManagedThreadId)}", loopThread?.ManagedThreadId);
                }
            }

            // Done.
            Interlocked.Exchange(ref _loopState, State.Disposed);
        }
    }
}