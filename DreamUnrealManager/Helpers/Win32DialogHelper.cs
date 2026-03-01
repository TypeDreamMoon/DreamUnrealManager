using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DreamUnrealManager.Helpers;

public static class Win32DialogHelper
{
    public static string? PickSingleFile(IntPtr ownerHwnd, string title, string filter)
    {
        return ShowFileDialog(
            ownerHwnd,
            title,
            filter,
            suggestedFileName: null,
            OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST,
            isSaveDialog: false);
    }

    public static string? PickFolder(IntPtr ownerHwnd, string description, string? initialPath = null)
    {
        IntPtr displayNamePtr = IntPtr.Zero;
        IntPtr titlePtr = IntPtr.Zero;
        IntPtr pidl = IntPtr.Zero;
        try
        {
            displayNamePtr = Marshal.AllocHGlobal(260 * sizeof(char));
            var emptyBuffer = new byte[260 * sizeof(char)];
            Marshal.Copy(emptyBuffer, 0, displayNamePtr, emptyBuffer.Length);
            titlePtr = Marshal.StringToHGlobalUni(description ?? string.Empty);

            var browseInfo = new BROWSEINFO
            {
                hwndOwner = ownerHwnd,
                pszDisplayName = displayNamePtr,
                lpszTitle = titlePtr,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_USENEWUI
            };

            pidl = SHBrowseForFolder(ref browseInfo);
            if (pidl == IntPtr.Zero)
            {
                return null;
            }

            var path = new StringBuilder(260);
            return SHGetPathFromIDList(pidl, path) ? path.ToString() : null;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                CoTaskMemFree(pidl);
            }

            if (displayNamePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(displayNamePtr);
            }

            if (titlePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titlePtr);
            }
        }
    }

    public static string? SaveFile(IntPtr ownerHwnd, string title, string filter, string suggestedFileName)
    {
        return ShowFileDialog(
            ownerHwnd,
            title,
            filter,
            suggestedFileName,
            OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT,
            isSaveDialog: true);
    }

    private static string? ShowFileDialog(
        IntPtr ownerHwnd,
        string title,
        string filter,
        string? suggestedFileName,
        int flags,
        bool isSaveDialog)
    {
        const int maxFileChars = 4096;
        IntPtr filterPtr = IntPtr.Zero;
        IntPtr titlePtr = IntPtr.Zero;
        IntPtr filePtr = IntPtr.Zero;

        try
        {
            var filterString = BuildFilter(filter);
            filterPtr = Marshal.StringToHGlobalUni(filterString);
            titlePtr = Marshal.StringToHGlobalUni(title ?? string.Empty);

            filePtr = Marshal.AllocHGlobal(maxFileChars * sizeof(char));
            var emptyBuffer = new byte[maxFileChars * sizeof(char)];
            Marshal.Copy(emptyBuffer, 0, filePtr, emptyBuffer.Length);

            if (!string.IsNullOrWhiteSpace(suggestedFileName))
            {
                var trimmed = suggestedFileName.Length >= maxFileChars
                    ? suggestedFileName[..(maxFileChars - 1)]
                    : suggestedFileName;
                var chars = trimmed.ToCharArray();
                Marshal.Copy(chars, 0, filePtr, chars.Length);
                Marshal.WriteInt16(filePtr, chars.Length * sizeof(char), 0);
            }

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = filterPtr,
                lpstrFile = filePtr,
                nMaxFile = maxFileChars,
                lpstrTitle = titlePtr,
                Flags = flags
            };

            var ok = isSaveDialog ? GetSaveFileName(ref ofn) : GetOpenFileName(ref ofn);
            if (!ok)
            {
                return null;
            }

            var selectedPath = Marshal.PtrToStringUni(filePtr);
            return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
        }
        finally
        {
            if (filterPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterPtr);
            }

            if (titlePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titlePtr);
            }

            if (filePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }

    private static string BuildFilter(string filter)
    {
        // Win32 对话框要求使用 '\0' 分隔，最后双 '\0' 结束。
        if (string.IsNullOrWhiteSpace(filter))
        {
            return "All Files (*.*)\0*.*\0\0";
        }

        var normalized = filter.Replace('|', '\0');
        return normalized.EndsWith("\0\0", StringComparison.Ordinal) ? normalized : $"{normalized}\0\0";
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint BIF_EDITBOX = 0x0010;
    private const uint BIF_USENEWUI = BIF_NEWDIALOGSTYLE | BIF_EDITBOX;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public IntPtr lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OPENFILENAME lpofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName(ref OPENFILENAME lpofn);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);
}
