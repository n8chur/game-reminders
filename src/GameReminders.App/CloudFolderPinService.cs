using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameReminders.App;

internal interface ICloudFolderPinService
{
    bool TryEnsurePinned(string path, out string? error);
}

internal sealed class CloudFolderPinService : ICloudFolderPinService
{
    private const int FileAttributePinned = 0x00080000;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly Func<string, int> _setPinned;

    public CloudFolderPinService()
        : this(File.GetAttributes, SetPinned)
    {
    }

    internal CloudFolderPinService(
        Func<string, FileAttributes> getAttributes,
        Func<string, int> setPinned)
    {
        _getAttributes = getAttributes;
        _setPinned = setPinned;
    }

    public bool TryEnsurePinned(string path, out string? error)
    {
        try
        {
            if (((int)_getAttributes(path) & FileAttributePinned) != 0)
            {
                error = null;
                return true;
            }

            var result = _setPinned(path);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or Win32Exception or COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            error =
                $"Windows could not set Always keep on this device: {exception.Message} " +
                "In File Explorer, right-click the Game Reminders folder and choose Always keep on this device, then try again.";
            return false;
        }
    }

    private static int SetPinned(string path)
    {
        using var handle = CreateFile(
            path,
            desiredAccess: 0x80000000,
            shareMode: FileShare.ReadWrite | FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            creationDisposition: FileMode.Open,
            flagsAndAttributes: 0x02000000,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return CfSetPinState(handle, pinState: 1, pinFlags: 1, overlapped: IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("cldapi.dll")]
    private static extern int CfSetPinState(
        SafeFileHandle fileHandle,
        int pinState,
        int pinFlags,
        IntPtr overlapped);
}
