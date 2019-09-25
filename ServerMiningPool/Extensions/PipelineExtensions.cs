using System;
using System.Buffers;

namespace MPS.Extensions
{
    public static class PipelineExtensions
    {
        public static ReadOnlySpan<byte> ToSpan(this ReadOnlySequence<byte> buffer)
        {
            if(buffer.IsSingleSegment)
                return buffer.First.Span;

            return buffer.ToArray();
        }
    }
}
