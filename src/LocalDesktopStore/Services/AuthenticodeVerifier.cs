using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LocalDesktopStore.Services;

public sealed record AuthenticodeVerificationResult(
    bool IsTrusted,
    string? Thumbprint,
    string? Subject,
    string Detail);

public sealed record PublisherChangeWarning(
    string Repo,
    string? PreviousThumbprint,
    string? PreviousSubject,
    string CurrentThumbprint,
    string CurrentSubject);

/// <summary>
/// Verifies Windows PE/MSI Authenticode signatures with the native Windows trust
/// policy and exposes the signer identity used for the per-repository pin.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyAction = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChainExcludeRoot = 0x80;

    public static AuthenticodeVerificationResult Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new(false, null, null, "The downloaded installer was not found.");

        try
        {
            var status = VerifyTrust(filePath);
            if (status != 0)
            {
                var detail = new Win32Exception(unchecked((int)status)).Message;
                return new(false, null, null, $"Windows trust verification failed ({status:X8}): {detail}");
            }

#pragma warning disable SYSLIB0057 // Authenticode exposes the signer through the signed PE container.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
            if (string.IsNullOrEmpty(thumbprint))
                return new(false, null, null, "Windows accepted the signature but no signer certificate was returned.");

            return new(true, thumbprint, certificate.Subject, "Authenticode signature is trusted by Windows.");
        }
        catch (CryptographicException ex)
        {
            return new(false, null, null, $"The installer does not contain a readable Authenticode signer certificate: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(false, null, null, $"Authenticode verification could not be completed: {ex.Message}");
        }
    }

    public static string NormalizeThumbprint(string? thumbprint)
        => string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private static uint VerifyTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = filePath
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            var trustData = new WinTrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPtr,
                StateAction = WtdStateActionVerify,
                ProviderFlags = WtdRevocationCheckChainExcludeRoot
            };

            try
            {
                var action = GenericVerifyAction;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            }
            finally
            {
                trustData.StateAction = WtdStateActionClose;
                var action = GenericVerifyAction;
                _ = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        ref Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
