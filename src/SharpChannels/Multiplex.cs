using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpChannels
{
    internal struct MultiplexOperation
    {
        Func<bool> _synchronousPass;
        Action<IUniqueOportunity, Action> _asyncPass;
        internal MultiplexOperation(Func<bool> synchronousPass, Action<IUniqueOportunity, Action> asyncPass)
        {
            _synchronousPass = synchronousPass;
            _asyncPass = asyncPass;
        }

        internal bool SynchronousPass()
        {
            return _synchronousPass();
        }
        internal void AsyncPass(IUniqueOportunity oportunity, Action callback)
        {
            _asyncPass(oportunity, callback);
        }
    }

    public struct Multiplex
    {
        List<MultiplexOperation> _ops;
        internal IEnumerable<MultiplexOperation> Ops
        {
            get
            {
                return _ops ?? Enumerable.Empty<MultiplexOperation>();
            }
        }
        public Multiplex CaseReceive<T>(Channel<T> channel, Action<T> operation)
        {
            Func<bool> synchronousPass = () =>
            {
                T value;
                if (channel.TryReceive(out value))
                {
                    operation(value);
                    return true;
                }
                return false;
            };
            Action<IUniqueOportunity, Action> asyncPass = async (oportunity, callback) =>
            {
                var value = await channel.ReceiveAsync(oportunity);
                operation(value);
                callback();
            };
            if (_ops == null)
            {
                var ops = new List<MultiplexOperation>(1);
                ops.Add(new MultiplexOperation(synchronousPass, asyncPass));
                return new Multiplex { _ops = ops };
            }
            else
            {
                var ops = new List<MultiplexOperation>(_ops.Count + 1);
                ops.AddRange(_ops);
                ops.Add(new MultiplexOperation(synchronousPass, asyncPass));
                return new Multiplex { _ops = ops };
            }
        }
        public Multiplex CaseSend<T>(Channel<T> channel, T value, Action todo = null)
        {
            Func<bool> synchronousPass = () =>
            {
                if (channel.TrySend(value))
                {
                    todo?.Invoke();
                    return true;
                }
                return false;
            };
            Action<IUniqueOportunity, Action> asyncPass = async (oportunity, callback) =>
            {
                await channel.SendAsync(value, oportunity);
                todo?.Invoke();
                callback();
            };
            if (_ops == null)
            {
                var ops = new List<MultiplexOperation>(1);
                ops.Add(new MultiplexOperation(synchronousPass, asyncPass));
                return new Multiplex { _ops = ops };
            }
            else
            {
                var ops = new List<MultiplexOperation>(_ops.Count + 1);
                ops.AddRange(_ops);
                ops.Add(new MultiplexOperation(synchronousPass, asyncPass));
                return new Multiplex { _ops = ops };
            }
        }
        public void Default(Action operation)
        {
            foreach (var op in Ops)
            {
                if (op.SynchronousPass())
                {
                    return;
                }
            }
            operation();
        }
        public MultiplexAwaiter GetAwaiter()
        {
            foreach (var op in Ops)
            {
                if (op.SynchronousPass())
                {
                    return new MultiplexAwaiter(true);
                }
            }
            var awaiter = new MultiplexAwaiter(false);
            foreach (var op in Ops)
            {
                op.AsyncPass(awaiter.Oportunity, awaiter.Complete);
            }
            return awaiter;
        }
    }
}
