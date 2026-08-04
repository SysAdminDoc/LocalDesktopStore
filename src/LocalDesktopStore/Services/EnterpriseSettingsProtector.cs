using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LocalDesktopStore.Services;

public static class EnterpriseSettingsProtector
{
    private const int CryptProtectLocalMachine = 0x4;

    public static string ProtectForMachine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty value is required.", nameof(value));

        var inputBytes = Encoding.UTF8.GetBytes(value);
        var input = CreateBlob(inputBytes);
        var output = default(DataBlob);
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "LocalDesktopStore enterprise settings",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectLocalMachine,
                    ref output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI could not protect the enterprise setting.");
            }

            return Convert.ToBase64String(ReadBlob(output));
        }
        finally
        {
            FreeInputBlob(input);
            FreeOutputBlob(output);
        }
    }

    public static string UnprotectForMachine(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            throw new ArgumentException("A protected value is required.", nameof(protectedValue));

        byte[] inputBytes;
        try
        {
            inputBytes = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The enterprise setting is not valid DPAPI data.", ex);
        }

        var input = CreateBlob(inputBytes);
        var output = default(DataBlob);
        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    ref output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI could not unprotect the enterprise setting on this machine.");
            }

            return Encoding.UTF8.GetString(ReadBlob(output));
        }
        finally
        {
            FreeInputBlob(input);
            FreeOutputBlob(output);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    private static byte[] ReadBlob(DataBlob blob)
    {
        if (blob.Size < 0 || blob.Data == IntPtr.Zero)
            throw new CryptographicException("Windows DPAPI returned an empty protected value.");

        var bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void FreeInputBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
            Marshal.FreeHGlobal(blob.Data);
    }

    private static void FreeOutputBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
            LocalFree(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
