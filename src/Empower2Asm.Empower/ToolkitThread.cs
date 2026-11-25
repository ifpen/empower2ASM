using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// The one thread every Toolkit call runs on. The Toolkit keeps its login in thread-local storage and is not
    /// thread-safe, so all its COM objects are created and used on this single STA thread, one call at a time.
    /// </summary>
    internal sealed class ToolkitThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private bool _disposed;

        public ToolkitThread()
        {
            _thread = new Thread(() =>
            {
                foreach (var action in _work.GetConsumingEnumerable())
                {
                    action();
                }
            })
            {
                IsBackground = true,
                Name = "Empower Toolkit",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        /// <summary>Runs a function on the Toolkit thread and waits for its result, rethrowing its exception.</summary>
        public T Run<T>(Func<T> function)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(_thread.Name);
            }

            if (Thread.CurrentThread == _thread)
            {
                return function();
            }

            T result = default!;
            ExceptionDispatchInfo? error = null;
            using (var done = new ManualResetEventSlim())
            {
                _work.Add(() =>
                {
                    try
                    {
                        result = function();
                    }
                    catch (Exception e)
                    {
                        error = ExceptionDispatchInfo.Capture(e);
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                done.Wait();
            }

            error?.Throw();
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _work.CompleteAdding();
            _thread.Join();
            _work.Dispose();
        }
    }
}
