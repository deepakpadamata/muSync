namespace AudioCreation
{
    internal interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
    }
}