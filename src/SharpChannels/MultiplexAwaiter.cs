﻿using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpChannels
{
    public struct MultiplexAwaiter : INotifyCompletion
    {
        class AsyncState : IUniqueOportunity
        {
            public bool _completed;
            public Action _onCompleted;
            public ExecutionContext _executionContext;
            private bool _oportunityTaken = false;
            object _oportunityLock = new object();
            public bool TryAcquire()
            {
                Monitor.Enter(_oportunityLock);
                if (_oportunityTaken)
                {
                    Monitor.Exit(_oportunityLock);
                    return false;
                }
                _oportunityTaken = true;
                return true;
            }

            public void Release(bool rollback)
            {
                if (rollback)
                {
                    _oportunityTaken = false;
                    Monitor.Exit(_oportunityLock);
                }
            }
        }

        AsyncState _asyncState;
        public MultiplexAwaiter(bool completed)
        {
            if (!completed)
            {
                _asyncState = new AsyncState();
            }
            else
            {
                _asyncState = null;
            }
        }
        internal void Complete()
        {
            lock (_asyncState)
            {
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
                    ExecutionContext.Run(_asyncState._executionContext, _ => callback(), null);
                }
            }
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
        internal IUniqueOportunity Oportunity { get { return _asyncState; } }
        public void GetResult()
        {
        }
    }
}
