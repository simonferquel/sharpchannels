using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpChannels
{

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
        class NeverBlockOportunity : IUniqueOportunity
        {
            public void Release(bool rollback)
            {
            }

            public bool TryAcquire()
            {
                return true;
            }
        }

        private static NeverBlockOportunity _neverBlockOportunity = new NeverBlockOportunity();

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
        public int BufferedCount
        {
            get
            {
                _rwl.EnterReadLock();
                try
                {
                    return _bufferedCount;
                }
                finally
                {
                    _rwl.ExitReadLock();
                }
            }
        }

        public ChannelSendAwaiter<T> SendAsync(T value)
        {
            return SendAsync(value, _neverBlockOportunity);
        }
        internal ChannelSendAwaiter<T> SendAsync(T value, IUniqueOportunity oportunity)
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
                        if (oportunity.TryAcquire())
                        {
                            afterLock = () => receiver.Receive(value);
                            oportunity.Release(false);
                            receiver.ConfirmSelection();
                            return ChannelSendAwaiter<T>.Done();
                        }
                        else
                        {
                            receiver.CancelSelection();
                            _activeReceivers.AddFirst(receiver);
                            return ChannelSendAwaiter<T>.Never();
                        }
                    }
                }
                if (_buffer != null && _bufferedCount < _buffer.Length)
                {
                    if (oportunity.TryAcquire())
                    {
                        oportunity.Release(false);
                        _buffer[(_bufferHead + _bufferedCount++) % _buffer.Length] = value;
                        return ChannelSendAwaiter<T>.Done();
                    }
                    else
                    {
                        return ChannelSendAwaiter<T>.Never();
                    }
                }
                var waiter = ChannelSendAwaiter<T>.Waiting(value, oportunity);
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
                        receiver.ConfirmSelection();
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
            return ReceiveAsync(_neverBlockOportunity);
        }
        internal ChannelReceiveAwaiter<T> ReceiveAsync(IUniqueOportunity oportunity)
        {
            ChannelReceiveAwaiter<T> result = default(ChannelReceiveAwaiter<T>);
            Func<ChannelReceiveAwaiter<T>> indirectResult = null;
            _rwl.EnterWriteLock();
            try
            {
                ReceiveAsyncInternal(ref result, ref indirectResult, oportunity);
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

        private void ReceiveAsyncInternal(ref ChannelReceiveAwaiter<T> result, ref Func<ChannelReceiveAwaiter<T>> indirectResult, IUniqueOportunity oportunity)
        {
            if (_buffer != null && _bufferedCount > 0)
            {
                if (oportunity.TryAcquire())
                {
                    oportunity.Release(false);
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
                else
                {
                    result = ChannelReceiveAwaiter<T>.Never();
                }
            }
            while (_activeSenders.Count > 0)
            {

                var sender = _activeSenders.First.Value;
                _activeSenders.RemoveFirst();
                if (sender.LockForSelection())
                {
                    if (oportunity.TryAcquire())
                    {
                        sender.ConfirmSelection();
                        oportunity.Release(false);
                        indirectResult = () => ChannelReceiveAwaiter<T>.Done(sender.Consume());
                        if (_closing && _bufferedCount == 0 && _activeSenders.Count == 0)
                        {
                            _closed = true;
                        }
                        return;
                    }
                    else
                    {
                        sender.CancelSelection();
                        _activeSenders.AddFirst(sender);
                        result = ChannelReceiveAwaiter<T>.Never();
                        return;
                    }
                }
            }
            if (_closed)
            {
                if (oportunity.TryAcquire())
                {
                    oportunity.Release(false);
                    result = ChannelReceiveAwaiter<T>.Done(default(T));
                    return;
                }
                else
                {
                    result = ChannelReceiveAwaiter<T>.Never();
                }
            }

            var waiter = ChannelReceiveAwaiter<T>.Waiting(oportunity);
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
                    sender.ConfirmSelection();
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
