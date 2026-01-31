using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Miningcore.Contracts;

namespace Miningcore.Native;

public static unsafe class ZanonoteBindings
{
    [DllImport("libzanonote", EntryPoint = "convert_blob_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool convert_blob(byte* input, int inputSize, byte* output, ref int outputSize);

    [DllImport("libzanonote", EntryPoint = "convert_block_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool convert_block(byte* input, int inputSize, byte* output, ref int outputSize, ulong nonce);

    [DllImport("libzanonote", EntryPoint = "get_blob_id_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool get_blob_id(byte* input, int inputSize, byte* output);

    [DllImport("libzanonote", EntryPoint = "get_block_id_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool get_block_id(byte* input, int inputSize, byte* output);

    public static byte[] ConvertBlob(ReadOnlySpan<byte> data, int size)
    {
        Contract.Requires<ArgumentException>(data.Length > 0);

        fixed (byte* input = data)
        {
            // provide reasonable large output buffer
            var outputBuffer = ArrayPool<byte>.Shared.Rent(0x100);

            try
            {
                var outputBufferLength = outputBuffer.Length;

                var success = false;
                fixed (byte* output = outputBuffer)
                {
                    success = convert_blob(input, size, output, ref outputBufferLength);
                }

                if(!success)
                {
                    // if we get false, the buffer might have been too small
                    if(outputBufferLength == 0)
                        return null; // nope, other error

                    // retry with correctly sized buffer
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                    outputBuffer = ArrayPool<byte>.Shared.Rent(outputBufferLength);

                    fixed (byte* output = outputBuffer)
                    {
                        success = convert_blob(input, size, output, ref outputBufferLength);
                    }

                    if(!success)
                        return null; // sorry
                }

                // build result buffer
                var result = new byte[outputBufferLength];
                Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

                return result;
            }

            finally
            {
                ArrayPool<byte>.Shared.Return(outputBuffer);
            }
        }
    }

    public static byte[] ConvertBlock(ReadOnlySpan<byte> data, int size, ulong nonce)
    {
        Contract.Requires<ArgumentException>(data.Length > 0);

        fixed (byte* input = data)
        {
            // provide reasonable large output buffer
            var outputBuffer = ArrayPool<byte>.Shared.Rent(0x100);

            try
            {
                var outputBufferLength = outputBuffer.Length;

                var success = false;
                fixed (byte* output = outputBuffer)
                {
                    success = convert_block(input, size, output, ref outputBufferLength, nonce);
                }

                if(!success)
                {
                    // if we get false, the buffer might have been too small
                    if(outputBufferLength == 0)
                        return null; // nope, other error

                    // retry with correctly sized buffer
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                    outputBuffer = ArrayPool<byte>.Shared.Rent(outputBufferLength);

                    fixed (byte* output = outputBuffer)
                    {
                        success = convert_block(input, size, output, ref outputBufferLength, nonce);
                    }

                    if(!success)
                        return null; // sorry
                }

                // build result buffer
                var result = new byte[outputBufferLength];
                Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

                return result;
            }

            finally
            {
                ArrayPool<byte>.Shared.Return(outputBuffer);
            }
        }
    }

    public static void GetBlobId(ReadOnlySpan<byte> data, Span<byte> result)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                get_blob_id(input, data.Length, output);
            }
        }
    }

    public static void GetBlockId(ReadOnlySpan<byte> data, Span<byte> result)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                get_block_id(input, data.Length, output);
            }
        }
    }
}
