using System.Runtime.InteropServices;

namespace WINHELP;

/// <summary>
/// 基于 Windows VfW (avifil32.dll) 的 AVI 视频写入器。
/// 使用系统自带的 "Microsoft Video 1" (MSVC) 压缩编码，
/// 若 MSVC 不可用则回退到无压缩 RGB（DIB），保证在所有 Windows 上都能写出可播放的 .avi。
/// 仅依赖系统组件，无需外部库。
/// </summary>
internal sealed class AviWriter : IDisposable
{
    private IntPtr _pfile;
    private IntPtr _pstream;
    private IntPtr _pcomp;
    private int _frame;
    private bool _initialized;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AVISTREAMINFO
    {
        public int fccType;
        public int fccHandler;
        public int dwFlags;
        public int dwCaps;
        public short wPriority;
        public short wLanguage;
        public int dwScale;
        public int dwRate;
        public int dwStart;
        public int dwLength;
        public int dwInitialBuffers;
        public int dwSuggestedBufferSize;
        public int dwQuality;
        public int dwSampleSize;
        public RECT rcFrame;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AVICOMPRESSOPTIONS
    {
        public int fccType;
        public int fccHandler;
        public int dwKeyFrameEvery;
        public int dwQuality;
        public int dwBytesPerSecond;
        public int dwFlags;
        public int dwCaps;
        public IntPtr lpFormat;
        public IntPtr lpParms;
        public int cbFormat;
        public int cbParms;
        public int dwInterleaveEvery;
    }

    private static int FourCC(char a, char b, char c, char d) =>
        (byte)a | ((byte)b << 8) | ((byte)c << 16) | ((byte)d << 24);

    [DllImport("avifil32.dll", EntryPoint = "AVIFileInit", PreserveSig = true)]
    private static extern void AVIFileInit();

    [DllImport("avifil32.dll", EntryPoint = "AVIFileExit", PreserveSig = true)]
    private static extern void AVIFileExit();

    [DllImport("avifil32.dll", EntryPoint = "AVIFileOpenW", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int AVIFileOpen(ref IntPtr ppfile, string szFile, int uMode, int pclsid);

    [DllImport("avifil32.dll", EntryPoint = "AVIFileRelease", PreserveSig = true)]
    private static extern int AVIFileRelease(IntPtr pfile);

    [DllImport("avifil32.dll", EntryPoint = "AVIFileCreateStream", PreserveSig = true)]
    private static extern int AVIFileCreateStream(IntPtr pfile, out IntPtr ppavi, ref AVISTREAMINFO psi);

    [DllImport("avifil32.dll", EntryPoint = "AVIMakeCompressedStream", PreserveSig = true)]
    private static extern int AVIMakeCompressedStream(out IntPtr ppsCompressed, IntPtr pavi, ref AVICOMPRESSOPTIONS lpOptions, int pclsid);

    [DllImport("avifil32.dll", EntryPoint = "AVIStreamSetFormat", PreserveSig = true)]
    private static extern int AVIStreamSetFormat(IntPtr pavi, int lPos, ref BITMAPINFOHEADER lpFormat, int cbFormat);

    [DllImport("avifil32.dll", EntryPoint = "AVIStreamWrite", PreserveSig = true)]
    private static extern int AVIStreamWrite(IntPtr pavi, int lStart, int lSamples, IntPtr lpBuffer, int cbBuffer, int dwFlags, out int plSampWritten, out int plBytesWritten);

    [DllImport("avifil32.dll", EntryPoint = "AVIStreamRelease", PreserveSig = true)]
    private static extern int AVIStreamRelease(IntPtr pavi);

    private const int OF_WRITE = 0x00000001;
    private const int OF_CREATE = 0x00001000;
    private const int BI_RGB = 0;

    /// <summary>
    /// 打开 AVI 文件并创建（压缩）视频流。
    /// </summary>
    /// <param name="path">输出 .avi 路径</param>
    /// <param name="width">帧宽（建议为 4 的倍数）</param>
    /// <param name="height">帧高</param>
    /// <param name="fps">帧率</param>
    public AviWriter(string path, int width, int height, int fps)
    {
        AVIFileInit();

        int rc = AVIFileOpen(ref _pfile, path, OF_WRITE | OF_CREATE, 0);
        if (rc != 0)
            throw new InvalidOperationException($"AVIFileOpen 失败 (code {rc})");

        var si = new AVISTREAMINFO
        {
            fccType = FourCC('v', 'i', 'd', 's'),
            fccHandler = 0,
            dwScale = 1,
            dwRate = fps,
            dwQuality = -1,
            dwSampleSize = 0,
            rcFrame = new RECT { left = 0, top = 0, right = width, bottom = height },
            szName = "yayuCapture"
        };

        rc = AVIFileCreateStream(_pfile, out _pstream, ref si);
        if (rc != 0)
        {
            AVIFileRelease(_pfile);
            throw new InvalidOperationException($"AVIFileCreateStream 失败 (code {rc})");
        }

        var bi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = height,        // 正值 = 自底向上 DIB（与 RotateFlip(FlipY) 后的数据一致）
            biPlanes = 1,
            biBitCount = 24,
            biCompression = BI_RGB,
            biSizeImage = width * 3 * height
        };

        // 优先使用 MSVC 压缩；失败则回退无压缩 DIB
        bool compressed = false;
        var opts = new AVICOMPRESSOPTIONS
        {
            fccType = FourCC('v', 'i', 'd', 's'),
            fccHandler = FourCC('M', 'S', 'V', 'C'),
            dwQuality = -1,
            dwFlags = 0
        };
        rc = AVIMakeCompressedStream(out _pcomp, _pstream, ref opts, 0);
        if (rc == 0)
        {
            rc = AVIStreamSetFormat(_pcomp, 0, ref bi, Marshal.SizeOf<BITMAPINFOHEADER>());
            if (rc == 0) compressed = true;
            else AVIMakeCompressedStream(out _pcomp, _pstream, ref opts, 0); // noop guard
        }

        if (!compressed)
        {
            // 回退：无压缩 RGB 流（体积大但一定可用）
            var optsRaw = new AVICOMPRESSOPTIONS
            {
                fccType = FourCC('v', 'i', 'd', 's'),
                fccHandler = 0,
                dwQuality = -1,
                dwFlags = 0
            };
            rc = AVIMakeCompressedStream(out _pcomp, _pstream, ref optsRaw, 0);
            if (rc != 0)
            {
                AVIStreamRelease(_pstream);
                AVIFileRelease(_pfile);
                throw new InvalidOperationException($"AVIMakeCompressedStream 失败 (code {rc})");
            }
            rc = AVIStreamSetFormat(_pcomp, 0, ref bi, Marshal.SizeOf<BITMAPINFOHEADER>());
            if (rc != 0)
            {
                AVIStreamRelease(_pcomp);
                AVIStreamRelease(_pstream);
                AVIFileRelease(_pfile);
                throw new InvalidOperationException($"AVIStreamSetFormat 失败 (code {rc})");
            }
        }

        _initialized = true;
    }

    /// <summary>写入一帧（24bpp RGB 自底向上数据，长度 = width*3*height）</summary>
    public void WriteFrame(IntPtr bits, int size)
    {
        if (!_initialized) return;
        int written, bytes;
        AVIStreamWrite(_pcomp, _frame, 1, bits, size, 0, out written, out bytes);
        _frame++;
    }

    public int FrameCount => _frame;

    public void Dispose()
    {
        if (_pcomp != IntPtr.Zero) { AVIStreamRelease(_pcomp); _pcomp = IntPtr.Zero; }
        if (_pstream != IntPtr.Zero) { AVIStreamRelease(_pstream); _pstream = IntPtr.Zero; }
        if (_pfile != IntPtr.Zero) { AVIFileRelease(_pfile); _pfile = IntPtr.Zero; }
        AVIFileExit();
        _initialized = false;
    }
}
