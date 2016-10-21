namespace SharpChannels
{
    internal interface IUniqueOportunity
    {
        bool TryAcquire();
        void Release(bool rollback);

        bool IsStillAvailable { get; }
    }
}
