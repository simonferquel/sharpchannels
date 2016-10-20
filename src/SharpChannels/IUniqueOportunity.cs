namespace SharpChannels
{
    internal interface IUniqueOportunity
    {
        bool TryAcquire();
        void Release(bool rollback);
    }
}
