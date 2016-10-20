using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpChannels
{
    public struct ChannelReceiveAwaiter<T> : INotifyCompletion
    {
        private class AsyncState
        {
            public T _value;
            public bool _completed;
            public Action _onCompleted;
            public ExecutionContext _executionContext;
        }
        private AsyncState _asyncState;
        private T _value;
        private IUniqueOportunity _uniqueOportunity;

        internal static ChannelReceiveAwaiter<T> Done(T value)
        {
            return new ChannelReceiveAwaiter<T>
            {
                _value = value
            };
        }
        internal static ChannelReceiveAwaiter<T> Waiting(IUniqueOportunity oportunity)
        {
            return new ChannelReceiveAwaiter<T>
            {
                _asyncState = new AsyncState(),
                _uniqueOportunity = oportunity
            };
        }

        internal bool LockForSelection()
        {
            if (_uniqueOportunity == null)
            {
                return true;
            }
            return _uniqueOportunity.TryAcquire();
        }
        internal void CancelSelection()
        {
            _uniqueOportunity?.Release(true);
        }
        internal void ConfirmSelection()
        {
            _uniqueOportunity?.Release(false);
        }

        internal void Receive(T value)
        {
            lock (_asyncState)
            {
                _asyncState._value = value;
                _asyncState._completed = true;

                var callback = _asyncState._onCompleted;
                if (callback == null)
                {
                    return;
                }
                if (_asyncState._executionContext == null)
                {
                    Task.Run(callback);
                }
                else
                {
                    Thread callingThread = Thread.CurrentThread;
                    ExecutionContext.Run(_asyncState._executionContext, _ => callback(), null);
                }
            }
        }

        public ChannelReceiveAwaiter<T> GetAwaiter()
        {
            return this;
        }

        public void OnCompleted(Action continuation)
        {
            lock (_asyncState)
            {
                _asyncState._executionContext = ExecutionContext.Capture();
                _asyncState._onCompleted = continuation;
                if (_asyncState._completed)
                {
                    var callback = _asyncState._onCompleted;
                    if (callback == null)
                    {
                        return;
                    }
                    if (_asyncState._executionContext == null)
                    {
                        Task.Run(callback);
                    }
                    else
                    {
                        ExecutionContext.Run(_asyncState._executionContext, _ => callback(), null);
                    }
                }
            }
        }

        public bool IsCompleted
        {
            get
            {
                if (_asyncState == null)
                {
                    return true;
                }
                lock (_asyncState)
                {
                    return _asyncState._completed;
                }
            }
        }

        public T GetResult()
        {
            if (_asyncState == null)
            {
                return _value;
            }
            lock (_asyncState)
            {
                return _asyncState._value;
            }
        }

        internal static ChannelReceiveAwaiter<T> Never()
        {
            throw new NotImplementedException();
        }
    }
    public struct ChannelSendAwaiter<T> : INotifyCompletion
    {
        private class AsyncState
        {
            public readonly T _value;
            public bool _completed;
            internal ExecutionContext _executionContext;
            internal Action _onCompleted;

            public AsyncState(T value) { _value = value; }
        }
        bool _completed;
        private IUniqueOportunity _uniqueOportunity;
        private AsyncState _asyncState;
        internal static ChannelSendAwaiter<T> Done()
        {
            return new ChannelSendAwaiter<T>
            {
                _completed = true
            };
        }

        internal static ChannelSendAwaiter<T> Never()
        {
            return new ChannelSendAwaiter<T>
            {
                _completed = false
            };
        }
        internal static ChannelSendAwaiter<T> Waiting(T value, IUniqueOportunity oportunity)
        {
            return new ChannelSendAwaiter<T>
            {
                _asyncState = new AsyncState(value),
                _uniqueOportunity = oportunity
            };
        }

        internal T Consume()
        {
            lock (_asyncState)
            {
                var value = _asyncState._value;
                _asyncState._completed = true;
                var callback = _asyncState._onCompleted;
                if (callback == null)
                {
                    return value;
                }
                if (_asyncState._executionContext == null)
                {
                    Task.Run(callback);
                }
                else
                {
                    ExecutionContext.Run(_asyncState._executionContext, _ => callback(), null);
                }
                return value;
            }
        }


        internal bool LockForSelection()
        {
            if (_uniqueOportunity == null)
            {
                return true;
            }
            return _uniqueOportunity.TryAcquire();
        }
        internal void CancelSelection()
        {
            _uniqueOportunity?.Release(true);
        }
        internal void ConfirmSelection()
        {
            _uniqueOportunity?.Release(false);
        }

        public ChannelSendAwaiter<T> GetAwaiter()
        {
            return this;
        }

        public void OnCompleted(Action continuation)
        {
            if(_asyncState == null)
            {
                return;
            }
            lock (_asyncState)
            {
                _asyncState._executionContext = ExecutionContext.Capture();
                _asyncState._onCompleted = continuation;
                if (_asyncState._completed)
                {
                    var callback = _asyncState._onCompleted;
                    if (callback == null)
                    {
                        return;
                    }
                    if (_asyncState._executionContext == null)
                    {
                        Task.Run(callback);
                    }
                    else
                    {
                        ExecutionContext.Run(_asyncState._executionContext, _ => callback(), null);
                    }
                }
            }
        }

        public bool IsCompleted
        {
            get
            {
                if (_asyncState == null)
                {
                    return _completed;
                }
                lock (_asyncState)
                {
                    return _asyncState._completed;
                }
            }
        }

        public void GetResult()
        {
        }
    }
}
