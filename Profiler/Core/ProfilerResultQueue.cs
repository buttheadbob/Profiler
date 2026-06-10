using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Utils.General;
using VRage.Collections;

namespace Profiler.Core
{
    /// <summary>
    /// Receives ProfilerResults from patched methods & distributes them to multiple observers in a separate thread
    /// </summary>
    public static class ProfilerResultQueue
    {
        static readonly ILogger Log = LogManager.GetCurrentClassLogger();
        static readonly ConcurrentQueue<ProfilerResult> _profilerResults;
        static readonly ConcurrentCachingList<IProfiler> _profilers;

        static ProfilerResultQueue()
        {
            _profilerResults = new ConcurrentQueue<ProfilerResult>();
            _profilers = new ConcurrentCachingList<IProfiler>();
        }

        /// <summary>
        /// Add an profiler and, when the returned IDisposable object is disposed, remove the profiler from the profiler.
        /// </summary>
        /// <param name="observer">Observer to add/remove.</param>
        /// <returns>IDisposable object that, when disposed, removes the profiler from the profiler.</returns>
        public static IDisposable Profile(IProfiler observer)
        {
            AddProfiler(observer);
            return new ActionDisposable(() => RemoveProfiler(observer));
        }

        internal static void Enqueue(in ProfilerResult result)
        {
            _profilerResults.Enqueue(result);
        }

        internal static async Task Start(CancellationToken canceller)
        {
            while (!canceller.IsCancellationRequested)
            {
                try
                {
                    _profilers.ApplyChanges();

                    var dequeued = false;
                    while (_profilerResults.TryDequeue(out var result))
                    {
                        dequeued = true;
                        foreach (var profiler in _profilers)
                        {
                            try
                            {
                                profiler.ReceiveProfilerResult(result);
                            }
                            catch (Exception e)
                            {
                                Log.Error($"{profiler}: {e.Message}");
                            }
                        }
                    }

                    if (!dequeued)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(.1f), canceller);
                    }
                }
                catch (OperationCanceledException) when (canceller.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    Log.Warn(e, "Profiler consumer loop crashed; restarting after delay");
                    await Task.Delay(TimeSpan.FromSeconds(1), canceller);
                }
            }
        }

        static void AddProfiler(IProfiler profiler)
        {
            _profilers.Add(profiler);
        }

        static void RemoveProfiler(IProfiler profiler)
        {
            _profilers.Remove(profiler);
        }
    }
}
