// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lumen.Core.Settings;

namespace Lumen.Platform.Windows.Security;

/// <summary>
/// DPAPI with CurrentUser scope (CryptProtectData without CRYPTPROTECT_LOCAL_MACHINE): only this Windows
/// user on this machine can decrypt. Calls crypt32 directly, so no extra package is needed.
/// </summary>
public sealed partial class DpapiProtector : ISecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    // Extra entropy so other DPAPI consumers of this user cannot decrypt Lumen's blobs by accident.
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("Lumen.Secrets.v1");

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Transform(plaintext, protect: true);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return Transform(ciphertext, protect: false);
    }

    private static unsafe byte[] Transform(byte[] input, bool protect)
    {
        fixed (byte* inputPtr = input)
        fixed (byte* entropyPtr = _entropy)
        {
            var inBlob = new DataBlob { Size = input.Length, Data = (nint)inputPtr };
            var entropyBlob = new DataBlob { Size = _entropy.Length, Data = (nint)entropyPtr };
            var outBlob = default(DataBlob);
            try
            {
                var ok = protect
                    ? CryptProtectData(ref inBlob, null, ref entropyBlob, 0, 0, CryptProtectUiForbidden, ref outBlob)
                    : CryptUnprotectData(ref inBlob, 0, ref entropyBlob, 0, 0, CryptProtectUiForbidden, ref outBlob);
                if (!ok)
                {
                    throw new CryptographicException("DPAPI operation failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
                }

                var result = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                return result;
            }
            finally
            {
                if (outBlob.Data != 0)
                {
                    // Wipe the plaintext/ciphertext copy owned by Windows before freeing it.
                    new Span<byte>((void*)outBlob.Data, outBlob.Size).Clear();
                    LocalFree(outBlob.Data);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy, nint reserved, nint prompt, int flags, ref DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob dataIn, nint description, ref DataBlob entropy, nint reserved, nint prompt, int flags, ref DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
