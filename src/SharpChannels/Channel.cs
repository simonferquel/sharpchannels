using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

        internal static ChannelReceiveAwaiter<T> Done(T value)
        {
            return new ChannelReceiveAwaiter<T>
            {
                _value = value
            };
        }
        internal static ChannelReceiveAwaiter<T> Waiting()
        {
            return new ChannelReceiveAwaiter<T>
            {
                _asyncState = new AsyncState()
            };
        }

        internal bool LockForSelection()
        {
            return true;
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
        private AsyncState _asyncState;
        internal static ChannelSendAwaiter<T> Done()
        {
            return new ChannelSendAwaiter<T>
            {
            };
        }
        internal static ChannelSendAwaiter<T> Waiting(T value)
        {
            return new ChannelSendAwaiter<T>
            {
                _asyncState = new AsyncState(value)
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
            return true;
        }

        public ChannelSendAwaiter<T> GetAwaiter()
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

        public void GetResult()
        {
        }
    }
    public class Channel<T>
    {
        private readonly ReaderWriterLockSlim _rwl = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private readonly T[] _buffer;
        private readonly LinkedList<ChannelReceiveAwaiter<T>> _activeReceivers = new LinkedList<ChannelReceiveAwaiter<T>>();
        private readonly LinkedList<ChannelSendAwaiter<T>> _activeSenders = new LinkedList<ChannelSendAwaiter<T>>();
        private int _bufferHead;
        private int _bufferedCount;
        private bool _closed;
        private bool _closing;

        public Channel(int bufferSize = 0)
        {
            if (bufferSize < 0)
                throw new ArgumentException("bufferSize should be >= 0");
            if (bufferSize > 0)
            {
                _buffer = new T[bufferSize];
            }
        }

        public void Close()
        {
            List<Action> cleanup = new List<Action>();
            _rwl.EnterWriteLock();
            try
            {
                _closing = true;
                T value = default(T);

                if (_activeSenders.Count == 0 && _bufferedCount == 0)
                {
                    while (_activeReceivers.Count > 0)
                    {
                        var receiver = _activeReceivers.First.Value;
                        _activeReceivers.RemoveFirst();
                        cleanup.Add(() => receiver.Receive(value));
                    }
                    _closed = true;
                }
            }
            finally
            {
                _rwl.ExitWriteLock();
                foreach (var a in cleanup)
                {
                    a();
                }
            }
        }
        public bool IsClosed
        {
            get
            {
                _rwl.EnterReadLock();
                try
                {
                    return _closed;
                }
                finally
                {
                    _rwl.ExitReadLock();
                }
            }
        }
        public ChannelSendAwaiter<T> SendAsync(T value)
        {
            Action afterLock = null;
            _rwl.EnterWriteLock();
            try
            {
                if (_closing)
                {
                    throw new InvalidOperationException("channel is closed");
                }
                while (_activeReceivers.Count > 0)
                {
                    var receiver = _activeReceivers.First.Value;
                    _activeReceivers.RemoveFirst();
                    if (receiver.LockForSelection())
                    {
                        afterLock = () => receiver.Receive(value);
                        return ChannelSendAwaiter<T>.Done();
                    }
                }
                if (_buffer != null && _bufferedCount < _buffer.Length)
                {
                    _buffer[(_bufferHead + _bufferedCount++) % _buffer.Length] = value;
                    return ChannelSendAwaiter<T>.Done();
                }
                var waiter = ChannelSendAwaiter<T>.Waiting(value);
                _activeSenders.AddLast(waiter);
                return waiter;
            }
            finally
            {
                _rwl.ExitWriteLock();
                afterLock?.Invoke();
            }
        }

        public bool TrySend(T value)
        {
            Action afterLock = null;
            _rwl.EnterWriteLock();
            try
            {
                if (_closing)
                {
                    throw new InvalidOperationException("channel is closed");
                }
                while (_activeReceivers.Count > 0)
                {
                    var receiver = _activeReceivers.First.Value;
                    _activeReceivers.RemoveFirst();
                    if (receiver.LockForSelection())
                    {
                        afterLock = () => receiver.Receive(value);
                        return true;
                    }
                }
                if (_buffer != null && _bufferedCount < _buffer.Length)
                {
                    _buffer[(_bufferHead + _bufferedCount++) % _buffer.Length] = value;
                    return true;
                }
                return false;
            }
            finally
            {
                _rwl.ExitWriteLock();
                afterLock?.Invoke();
            }
        }

        public ChannelReceiveAwaiter<T> ReceiveAsync()
        {
            ChannelReceiveAwaiter<T> result = default(ChannelReceiveAwaiter<T>);
            Func<ChannelReceiveAwaiter<T>> indirectResult = null;
            _rwl.EnterWriteLock();
            try
            {
                ReceiveAsyncInternal(ref result, ref indirectResult);
            }
            finally
            {
                _rwl.ExitWriteLock();
            }
            if (indirectResult != null)
            {
                return indirectResult();
            }
            return result;
        }

        private void ReceiveAsyncInternal(ref ChannelReceiveAwaiter<T> result, ref Func<ChannelReceiveAwaiter<T>> indirectResult)
        {
            if (_buffer != null && _bufferedCount > 0)
            {
                result = ChannelReceiveAwaiter<T>.Done(_buffer[_bufferHead]);
                _buffer[_bufferHead] = default(T);
                _bufferHead = (_bufferHead + 1) % _buffer.Length;
                --_bufferedCount;
                if (_closing && _bufferedCount == 0 && _activeSenders.Count == 0)
                {
                    _closed = true;
                }
                return;
            }
            while (_activeSenders.Count > 0)
            {

                var sender = _activeSenders.First.Value;
                _activeSenders.RemoveFirst();
                if (sender.LockForSelection())
                {
                    indirectResult = () => ChannelReceiveAwaiter<T>.Done(sender.Consume());
                    if (_closing && _bufferedCount == 0 && _activeSenders.Count == 0)
                    {
                        _closed = true;
                    }
                    return;
                }
            }
            if (_closed)
            {
                result = ChannelReceiveAwaiter<T>.Done(default(T));
                return;
            }

            var waiter = ChannelReceiveAwaiter<T>.Waiting();
            _activeReceivers.AddLast(waiter);
            result = waiter;

        }

        private bool TryReceiveInternal(ref T value, ref Func<T> indirectResult)
        {
            if (_buffer != null && _bufferedCount > 0)
            {
                value = _buffer[_bufferHead];
                _buffer[_bufferHead] = default(T);
                _bufferHead = (_bufferHead + 1) % _buffer.Length;
                --_bufferedCount;
                if (_closing && _bufferedCount == 0 && _activeSenders.Count == 0)
                {
                    _closed = true;
                }
                return true;
            }
            while (_activeSenders.Count > 0)
            {
                var sender = _activeSenders.First.Value;
                _activeSenders.RemoveFirst();
                if (sender.LockForSelection())
                {
                    indirectResult = () => sender.Consume();
                    if (_closing && _bufferedCount == 0 && _activeSenders.Count == 0)
                    {
                        _closed = true;
                    }
                    return true;
                }
            }
            if (_closed)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryReceive(out T value)
        {
            value = default(T);
            Func<T> indirectResult = null;
            bool succeeded;
            _rwl.EnterWriteLock();
            try
            {
                succeeded = TryReceiveInternal(ref value, ref indirectResult);
            }
            finally
            {
                _rwl.ExitWriteLock();
            }
            if (indirectResult != null)
            {
                value = indirectResult();
                return true;
            }
            return succeeded;
        }
    }
}
